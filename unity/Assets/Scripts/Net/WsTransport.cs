using System;
using System.Collections.Concurrent;
#if !(UNITY_WEBGL && !UNITY_EDITOR)
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#else
using System.Runtime.InteropServices;
#endif

namespace Twin.Net
{
    public enum WsEventKind { Open, Message, Close, Error }

    public struct WsEvent
    {
        public WsEventKind Kind;
        public string Text;
        public WsEvent(WsEventKind kind, string text) { Kind = kind; Text = text; }
    }

    /// <summary>Transport WebSocket minimal. Les evenements sont lus par TwinClient dans Update.</summary>
    public interface IWsTransport
    {
        bool IsOpen { get; }
        bool TryDequeue(out WsEvent e);
        void Connect(string url);
        void Send(string text);
        void Close();
    }

#if !(UNITY_WEBGL && !UNITY_EDITOR)
    /// <summary>Editeur et applications de bureau : System.Net.WebSockets sur un thread de fond.</summary>
    public sealed class NativeWsTransport : IWsTransport
    {
        readonly ConcurrentQueue<WsEvent> _queue = new ConcurrentQueue<WsEvent>();
        readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        ClientWebSocket _ws;
        CancellationTokenSource _cts;

        public bool IsOpen => _ws != null && _ws.State == WebSocketState.Open;
        public bool TryDequeue(out WsEvent e) => _queue.TryDequeue(out e);

        public void Connect(string url)
        {
            Close();
            _cts = new CancellationTokenSource();
            var ws = new ClientWebSocket();
            _ws = ws;
            var token = _cts.Token;
            Task.Run(() => RunAsync(ws, url, token));
        }

        async Task RunAsync(ClientWebSocket ws, string url, CancellationToken token)
        {
            try
            {
                await ws.ConnectAsync(new Uri(url), token);
                _queue.Enqueue(new WsEvent(WsEventKind.Open, ""));
                var buffer = new byte[64 * 1024];
                var sb = new StringBuilder();
                while (ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    sb.Length = 0;
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (r.MessageType == WebSocketMessageType.Close) return;
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                    } while (!r.EndOfMessage);
                    _queue.Enqueue(new WsEvent(WsEventKind.Message, sb.ToString()));
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                    _queue.Enqueue(new WsEvent(WsEventKind.Error, ex.Message));
            }
            finally
            {
                _queue.Enqueue(new WsEvent(WsEventKind.Close, ""));
            }
        }

        public async void Send(string text)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync();
            try { await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None); }
            catch (Exception) { /* la fermeture est signalee par la boucle de reception */ }
            finally { _sendLock.Release(); }
        }

        public void Close()
        {
            try { _cts?.Cancel(); } catch (Exception) { }
            try { _ws?.Dispose(); } catch (Exception) { }
            _ws = null;
        }
    }
#else
    /// <summary>Build WebGL : WebSocket du navigateur, via Assets/Plugins/WebGL/WsBridge.jslib.</summary>
    public sealed class WebGlWsTransport : IWsTransport
    {
        [DllImport("__Internal")] static extern void WsOpen(string url, string gameObjectName);
        [DllImport("__Internal")] static extern void WsSend(string message);
        [DllImport("__Internal")] static extern void WsClose();

        readonly ConcurrentQueue<WsEvent> _queue = new ConcurrentQueue<WsEvent>();
        readonly string _objectName;
        bool _open;

        public WebGlWsTransport(string gameObjectName) { _objectName = gameObjectName; }

        public bool IsOpen => _open;
        public bool TryDequeue(out WsEvent e) => _queue.TryDequeue(out e);
        public void Connect(string url) { _open = false; WsOpen(url, _objectName); }
        public void Send(string text) { if (_open) WsSend(text); }
        public void Close() { _open = false; WsClose(); }

        // Appeles par TwinClient depuis les messages du navigateur.
        public void PushOpen() { _open = true; _queue.Enqueue(new WsEvent(WsEventKind.Open, "")); }
        public void PushMessage(string text) { _queue.Enqueue(new WsEvent(WsEventKind.Message, text)); }
        public void PushClose(string code) { _open = false; _queue.Enqueue(new WsEvent(WsEventKind.Close, code)); }
        public void PushError(string text) { _queue.Enqueue(new WsEvent(WsEventKind.Error, text)); }
    }
#endif
}
