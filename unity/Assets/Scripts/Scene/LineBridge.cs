using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Twin.Scene
{
    /// <summary>
    /// Pont entre la page web (dashboard) et la scene Unity.
    /// Page -> Unity : SendMessage("LineBridge", "FocusStation" | "ResetView", ...).
    /// Unity -> page : window.twinOnStationSelected(index) quand on clique sur une machine.
    /// </summary>
    public sealed class LineBridge : MonoBehaviour
    {
        public OrbitCamera orbit;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void TwinStationClicked(int station);
#endif

        void OnEnable() { if (orbit != null) orbit.StationClicked += OnStationClicked; }
        void OnDisable() { if (orbit != null) orbit.StationClicked -= OnStationClicked; }

        void OnStationClicked(int station)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            TwinStationClicked(station);
#endif
        }

        // Appeles depuis la page (SendMessage ne transporte que des chaines).
        public void FocusStation(string index)
        {
            if (orbit != null && int.TryParse(index, out int i)) orbit.FocusStation(i);
        }

        public void ResetView(string unused)
        {
            if (orbit != null) orbit.ResetView(false);
        }
    }
}
