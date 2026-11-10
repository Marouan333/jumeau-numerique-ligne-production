using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Twin.Net;
using UnityEngine;

namespace Twin.Bench
{
    /// <summary>
    /// Banc d'essai : fait jouer chaque scenario d'optimisation pendant une heure simulee dans la
    /// scene Unity (flux reel du backend) et enregistre les indicateurs mesures.
    /// Lancer avec le menu Twin > Lancer le banc d'essai (le backend doit tourner, de preference
    /// avec SIM_SEED=7 pour des resultats reproductibles).
    /// </summary>
    public sealed class ScenarioBenchmark : MonoBehaviour
    {
        public TwinClient client;
        public float durationSeconds = 3600f;   // duree simulee par scenario
        public float speed = 20f;
        public string outputRelativePath = "../../docs/rapport/data/resultats_unity.json";

        public bool IsRunning { get; private set; }
        public bool IsDone { get; private set; }
        public string Status { get; private set; } = "";

        [Serializable]
        public class Row
        {
            public string scenario;
            public string label;
            public bool failures_enabled;
            public float t;
            public int produced;
            public int rejected;
            public float reject_pct;
            public float throughput_avg;
            public float theoretical;
            public int theoretical_hour;
            public int wip;
            public float avg_wip;
            public float lead_time;
            public float wait_controle;
            public string bottleneck_theoretical;
            public string bottleneck_observed;
            public float downtime_total;
            public int failures;
            public float[] util;
        }

        [Serializable] public class Report { public string measured_with; public string date; public float duration_s; public float speed; public Row[] rows; }

        static readonly string[] Presets = { "initial", "controle_auto", "double_controle", "convoyeur_adaptatif" };
        static readonly string[] Labels = { "Situation initiale", "Contrôle automatisé (8 s)", "2 postes de contrôle", "Convoyeur adaptatif" };

        public void Run()
        {
            if (IsRunning) return;
            IsDone = false;
            StartCoroutine(RunAll());
        }

        IEnumerator RunAll()
        {
            IsRunning = true;
            var rows = new List<Row>();
            // attendre que la connexion au backend soit etablie
            float connectDeadline = Time.realtimeSinceStartup + 30f;
            while ((!client.Connected || client.Latest == null) && Time.realtimeSinceStartup < connectDeadline) yield return null;
            if (!client.Connected)
            {
                Status = "Echec : backend injoignable";
                Debug.LogError("[Bench] " + Status);
                IsRunning = false;
                yield break;
            }
            foreach (bool failures in new[] { true, false })
            {
                for (int i = 0; i < Presets.Length; i++)
                {
                    Status = Labels[i] + (failures ? " (avec pannes)" : " (sans pannes)");
                    Debug.Log("[Bench] " + Status);
                    client.SetSpeed(speed);
                    client.SetFailures(failures);
                    yield return new WaitForSecondsRealtime(0.3f);
                    client.SetPreset(Presets[i]);

                    // attendre la remise a zero du scenario
                    float deadline = Time.realtimeSinceStartup + 15f;
                    while (Time.realtimeSinceStartup < deadline)
                    {
                        var s = client.Latest;
                        if (s != null && s.preset == Presets[i] && s.kpi.t < 30f && s.config.failures == failures) break;
                        yield return null;
                    }

                    // attendre la fin de l'heure simulee
                    float stop = Time.realtimeSinceStartup + 900f;
                    while ((client.Latest == null || client.Latest.kpi.t < durationSeconds) && Time.realtimeSinceStartup < stop)
                        yield return null;

                    rows.Add(ToRow(client.Latest, Presets[i], Labels[i], failures));
                }
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, outputRelativePath));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var report = new Report
            {
                measured_with = "Scene Unity " + Application.unityVersion + " connectee au backend (flux WebSocket reel)",
                date = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                duration_s = durationSeconds,
                speed = speed,
                rows = rows.ToArray(),
            };
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Status = "Termine : " + path;
            Debug.Log("[Bench] " + Status);
            IsRunning = false;
            IsDone = true;
        }

        static Row ToRow(StateMsg s, string key, string label, bool failuresEnabled)
        {
            var row = new Row { scenario = key, label = label, failures_enabled = failuresEnabled };
            if (s == null || s.kpi == null) return row;
            var k = s.kpi;
            row.t = k.t; row.produced = k.produced; row.rejected = k.rejected; row.reject_pct = k.reject_pct;
            row.throughput_avg = k.throughput_avg; row.theoretical = k.theoretical; row.theoretical_hour = k.theoretical_hour;
            row.wip = k.wip; row.avg_wip = k.avg_wip; row.lead_time = k.lead_time; row.wait_controle = k.wait_controle;
            row.bottleneck_theoretical = k.bottleneck_theoretical; row.bottleneck_observed = k.bottleneck_observed;
            row.downtime_total = k.downtime_total; row.failures = k.failures;
            row.util = new float[s.stations.Length];
            for (int i = 0; i < s.stations.Length; i++) row.util[i] = s.stations[i].util;
            return row;
        }
    }
}
