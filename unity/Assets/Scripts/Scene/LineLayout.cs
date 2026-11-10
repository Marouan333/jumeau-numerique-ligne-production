using Twin.Net;
using UnityEngine;

namespace Twin.Scene
{
    /// <summary>
    /// Disposition de la ligne, reprise de la scene Three.js d'origine (frontend/js/scene.js).
    /// Axe Z inverse par rapport a Three.js (Unity est main gauche) : la camera regarde la
    /// ligne depuis -Z, l'image obtenue est la meme.
    /// </summary>
    public static class LineLayout
    {
        public static readonly float[] StationX = { -18f, -6f, 6f, 18f };
        public const float BeltTop = 1.0f;     // hauteur du dessus du convoyeur
        public const float QueueGap = 0.8f;    // espacement des produits en file
        public const float UnitGap = 2.6f;     // ecart entre machines paralleles
        public const float ConveyorStart = -22f;
        public const float ConveyorLength = 46f;
        public static readonly Vector3 PalletPos = new Vector3(26.5f, 0f, 0f);
        public static readonly Vector3 BinPos = new Vector3(8.4f, 0f, 3.4f); // STATION_X[2] + 2.4
        public const int PalletCapacity = 24;

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // Couleurs d'etat (identiques au dashboard) : marche, attente, bloque, panne.
        public static readonly Color Running = Hex("#22c55e");
        public static readonly Color Idle = Hex("#94a3b8");
        public static readonly Color Blocked = Hex("#f59e0b");
        public static readonly Color Failed = Hex("#ef4444");

        public static Color StateColor(string state)
        {
            switch (state)
            {
                case "marche": return Running;
                case "bloque": return Blocked;
                case "panne": return Failed;
                default: return Idle;
            }
        }

        public static string StateText(string state)
        {
            switch (state)
            {
                case "marche": return "En marche";
                case "bloque": return "Bloqué";
                case "panne": return "EN PANNE";
                default: return "En attente";
            }
        }

        public static float UnitZ(int index, int count) => -((index - (count - 1) / 2f) * UnitGap);

        /// <summary>Position cible d'un produit selon son etat, comme _target() de la version Three.js.</summary>
        public static Vector3 ProductTarget(ProductDto p, StateMsg state, int[] unitCounts)
        {
            int st = Mathf.Clamp(p.st, 0, 3);
            float x0 = StationX[st];
            float y = BeltTop + 0.21f;
            if (p.s == "q") return new Vector3(x0 - 2.2f - p.k * QueueGap, y, 0f);
            if (p.s == "p")
            {
                int n = unitCounts != null && st < unitCounts.Length ? Mathf.Max(1, unitCounts[st]) : 1;
                return new Vector3(x0 - 0.9f + 1.8f * p.p, y, UnitZ(p.k, n));
            }
            // convoyeur : de la sortie du poste precedent vers le bout de la file du poste st
            int prev = Mathf.Max(0, st - 1);
            float from = StationX[prev] + 1.6f;
            int queue = state.stations != null && st < state.stations.Length ? state.stations[st].queue : 0;
            float to = x0 - 2.2f - queue * QueueGap;
            return new Vector3(Mathf.Lerp(from, to, p.p), y, 0f);
        }

        public static Color ProductColorIndex(int st) => st == 2 ? Hex("#4f7fe0") : st >= 3 ? Hex("#2fb67c") : Hex("#b7793f");
    }
}
