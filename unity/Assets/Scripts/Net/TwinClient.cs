using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Twin.Net
{
    /// <summary>
    /// Client du flux WebSocket du backend (voir docs/protocole-websocket.md).
    /// Le backend est la seule source de verite : ce composant ne fait que recevoir
    /// l'etat, le conserver dans un petit tampon et envoyer des commandes.
    /// Les messages arrivent a 10 Hz. TryGetSample renvoie deux etats consecutifs et
    /// un coefficient pour interpoler l'affichage a la cadence de l'ecran.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TwinClient : MonoBehaviour
    {
        [Tooltip("Vide = deduit automatiquement (page web en WebGL, localhost:8000 dans l'editeur).")]
        public string url = "";
        [Tooltip("Retard d'affichage en secondes. Absorbe la variation du reseau (3 a 4 messages a 10 Hz).")]
        [Range(0.05f, 0.5f)] public float renderDelay = 0.15f;
        public bool autoReconnect = true;
        public float reconnectDelay = 2f;

        public event Action<HelloMsg> HelloReceived;
        public event Action<StateMsg> StateReceived;
        public event Action<bool> ConnectionChanged;

        public bool Connected { get; private set; }
        public StateMsg Latest { get; private set; }
        public HelloMsg Hello { get; private set; }

        struct Stamped { public double Time; public StateMsg State; }
        readonly List<Stamped> _buffer = new List<Stamped>(8);
        const int MaxBuffered = 8;

        IWsTransport _transport;
        float _nextConnectAt;
        bool _wantConnection = true;

        // ------------------------------------------------------------------ cycle de vie
        void Awake()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _transport = new WebGlWsTransport(gameObject.name);
#else
            _transport = new NativeWsTransport();
#endif
        }

        void OnEnable() { _wantConnection = true; _nextConnectAt = 0f; }

        void OnDisable()
        {
            _wantConnection = false;
            _transport?.Close();
            SetConnected(false);
        }

        void Update()
        {
            if (_transport == null) return;

            StateMsg newestState = null;
            while (_transport.TryDequeue(out var e))
            {
                switch (e.Kind)
                {
                    case WsEventKind.Open: SetConnected(true); break;
                    case WsEventKind.Close:
                        SetConnected(false);
                        _nextConnectAt = Time.unscaledTime + reconnectDelay;
                        break;
                    case WsEventKind.Error:
                        Debug.LogWarning("[TwinClient] " + e.Text);
                        break;
                    case WsEventKind.Message:
                        newestState = HandleMessage(e.Text) ?? newestState;
                        break;
                }
            }

            if (newestState != null) Push(newestState);

            if (!Connected && _wantConnection && autoReconnect && Time.unscaledTime >= _nextConnectAt)
            {
                _nextConnectAt = Time.unscaledTime + reconnectDelay;
                _transport.Connect(ResolveUrl());
            }
        }

        // ------------------------------------------------------------------ reception
        // Un seul message "state" est decode par image : si plusieurs sont arrives
        // (onglet en arriere-plan, image lente), on garde le plus recent.
        StateMsg HandleMessage(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            TypeProbe probe;
            try { probe = JsonUtility.FromJson<TypeProbe>(json); }
            catch (Exception) { return null; }
            if (probe == null) return null;

            switch (probe.type)
            {
                case "state":
                    return Parse<StateMsg>(json);
                case "hello":
                    var hello = Parse<HelloMsg>(json);
                    if (hello != null) { Hello = hello; HelloReceived?.Invoke(hello); }
                    return null;
                case "error":
                    Debug.LogWarning("[TwinClient] erreur serveur : " + json);
                    return null;
                default:
                    return null; // "compare" : traite par le dashboard web
            }
        }

        static T Parse<T>(string json) where T : class
        {
            try { return JsonUtility.FromJson<T>(json); }
            catch (Exception ex) { Debug.LogWarning("[TwinClient] JSON invalide : " + ex.Message); return null; }
        }

        void Push(StateMsg state)
        {
            Latest = state;
            _buffer.Add(new Stamped { Time = Time.realtimeSinceStartupAsDouble, State = state });
            if (_buffer.Count > MaxBuffered) _buffer.RemoveAt(0);
            StateReceived?.Invoke(state);
        }

        void SetConnected(bool value)
        {
            if (Connected == value) return;
            Connected = value;
            if (!value) _buffer.Clear();
            ConnectionChanged?.Invoke(value);
        }

        // ------------------------------------------------------------------ interpolation
        /// <summary>
        /// Donne les deux etats entre lesquels interpoler maintenant et le coefficient (0 a 1).
        /// Retourne false tant qu'aucun etat n'a ete recu.
        /// </summary>
        public bool TryGetSample(out StateMsg from, out StateMsg to, out float alpha)
        {
            from = to = null; alpha = 0f;
            int n = _buffer.Count;
            if (n == 0) return false;

            double t = Time.realtimeSinceStartupAsDouble - renderDelay;
            if (n == 1 || t <= _buffer[0].Time)
            {
                from = to = _buffer[0].State;
                return true;
            }
            for (int i = 0; i < n - 1; i++)
            {
                double t0 = _buffer[i].Time, t1 = _buffer[i + 1].Time;
                if (t >= t0 && t <= t1)
                {
                    from = _buffer[i].State;
                    to = _buffer[i + 1].State;
                    alpha = (float)((t - t0) / Math.Max(1e-4, t1 - t0));
                    return true;
                }
            }
            from = to = _buffer[n - 1].State; // en retard : on garde le dernier etat
            alpha = 1f;
            return true;
        }

        // ------------------------------------------------------------------ commandes
        static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        static string B(bool v) => v ? "true" : "false";

        void SendRaw(string json) { _transport?.Send(json); }

        public void SetPreset(string name) => SendRaw("{\"cmd\":\"preset\",\"name\":\"" + name + "\"}");
        public void SetTime(int station, float seconds) => SendRaw("{\"cmd\":\"set_time\",\"station\":" + station + ",\"value\":" + F(seconds) + "}");
        public void SetServers(int station, int count) => SendRaw("{\"cmd\":\"set_servers\",\"station\":" + station + ",\"value\":" + count + "}");
        public void SetFailures(bool enabled) => SendRaw("{\"cmd\":\"failures\",\"value\":" + B(enabled) + "}");
        public void SetAdaptive(bool enabled) => SendRaw("{\"cmd\":\"adaptive\",\"value\":" + B(enabled) + "}");
        public void SetSpeed(float speed) => SendRaw("{\"cmd\":\"speed\",\"value\":" + F(speed) + "}");
        public void SetPaused(bool paused) => SendRaw("{\"cmd\":\"pause\",\"value\":" + B(paused) + "}");
        public void ResetRun() => SendRaw("{\"cmd\":\"reset\"}");
        public void BreakStation(int station) => SendRaw("{\"cmd\":\"break\",\"station\":" + station + "}");
        public void RepairStation(int station) => SendRaw("{\"cmd\":\"repair\",\"station\":" + station + "}");

        // ------------------------------------------------------------------ adresse
        string ResolveUrl()
        {
            if (!string.IsNullOrWhiteSpace(url)) return url.Trim();
#if UNITY_WEBGL && !UNITY_EDITOR
            // Page servie par le backend : meme hote, meme port, protocole ws ou wss.
            try
            {
                var page = new Uri(Application.absoluteURL);
                string scheme = page.Scheme == "https" ? "wss" : "ws";
                return scheme + "://" + page.Authority + "/ws";
            }
            catch (Exception) { return "ws://localhost:8000/ws"; }
#else
            // 127.0.0.1 plutot que localhost : evite l'essai IPv6 qui retarde la premiere connexion.
            return "ws://127.0.0.1:8000/ws";
#endif
        }

        // ------------------------------------------------------------------ rappels du navigateur (WebGL)
        // Appeles par WsBridge.jslib via SendMessage.
        public void JsOnOpen(string _)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            (_transport as WebGlWsTransport)?.PushOpen();
#endif
        }
        public void JsOnMessage(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            (_transport as WebGlWsTransport)?.PushMessage(text);
#endif
        }
        public void JsOnClose(string code)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            (_transport as WebGlWsTransport)?.PushClose(code);
#endif
        }
        public void JsOnError(string text)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            (_transport as WebGlWsTransport)?.PushError(text);
#endif
        }
    }
}
