using System;

namespace Twin.Net
{
    // Objets compatibles JsonUtility. Ils suivent docs/protocole-websocket.md.
    // Les champs absents de ces classes (history, exits, presets, mtbf_obs...) sont ignores.

    [Serializable] public class TypeProbe { public string type; }

    [Serializable] public class StationInfo { public string key; public string name; }
    [Serializable] public class HelloMsg { public string type; public StationInfo[] stations; }

    [Serializable] public class ServerDto { public bool busy; public bool done; public float progress; }

    [Serializable]
    public class StationDto
    {
        public string key;
        public string name;
        public string state;      // marche | attente | bloque | panne
        public float time;
        public int queue;
        public float util;
        public float avail;
        public float blocked;
        public float wait;
        public float downtime;
        public float repair_in;
        public int failures;
        public ServerDto[] servers;
    }

    [Serializable]
    public class ProductDto
    {
        public int id;
        public int st;            // poste concerne
        public string s;          // q (file) | p (traitement) | t (convoyeur)
        public int k;             // rang dans la file ou numero de machine
        public float p;           // progression 0..1 (p et t)
        public bool rej;          // sera rejete au controle
    }

    [Serializable]
    public class KpiDto
    {
        public float t;
        public int produced;
        public int rejected;
        public float reject_pct;
        public float throughput_now;
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
    }

    [Serializable] public class EventDto { public float t; public string text; public string kind; }

    [Serializable]
    public class ConfigDto
    {
        public float[] times;
        public int[] servers;
        public bool adaptive;
        public bool failures;
        public int buffer;
        public int wip_limit;
    }

    [Serializable]
    public class StateMsg
    {
        public string type;
        public float speed;
        public bool paused;
        public string preset;
        public int run_id;
        public ConfigDto config;
        public StationDto[] stations;
        public ProductDto[] products;
        public KpiDto kpi;
        public EventDto[] events;
    }
}
