using UnityEngine;

namespace Twin.Scene
{
    /// <summary>
    /// Une machine (un poste peut en avoir plusieurs en parallele). Porte le voyant d'etat,
    /// la barre de progression et les animations propres a chaque type de poste.
    /// </summary>
    public sealed class StationUnit : MonoBehaviour
    {
        public int station;
        public int kind;                 // 0 alimentation, 1 traitement, 2 controle, 3 emballage
        public Renderer lamp;
        public Renderer bar;
        public Transform barTransform;
        public Transform spindle;
        public Transform beam;
        public Transform press;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        MaterialPropertyBlock _lampBlock, _barBlock;

        // etat courant, applique a chaque image dans Animate
        bool _active;
        string _state = "attente";

        void Awake()
        {
            _lampBlock = new MaterialPropertyBlock();
            _barBlock = new MaterialPropertyBlock();
        }

        /// <summary>Applique l'etat recu du backend (appele quand un nouvel etat est affiche).</summary>
        public void Apply(string state, float progress, bool busy, bool done)
        {
            _state = state;
            _active = busy && !done && state != "panne";
            if (barTransform != null)
            {
                var s = barTransform.localScale;
                s.x = Mathf.Max(0.001f, busy ? Mathf.Clamp01(progress) : 0f);
                barTransform.localScale = s;
            }
            if (bar != null)
            {
                var c = done ? LineLayout.Blocked : LineLayout.Running;
                SetColor(bar, _barBlock, c, 0.4f);
            }
        }

        /// <summary>Animation propre a chaque image (voyant clignotant, broche, faisceau, presse).</summary>
        public void Animate(float time, float dt, bool running)
        {
            if (lamp != null)
            {
                float intensity = 0.8f;
                if (_state == "panne") intensity = Mathf.Sin(time * 10f) > 0f ? 1.6f : 0.1f;
                SetColor(lamp, _lampBlock, LineLayout.StateColor(_state), intensity);
            }
            bool active = _active && running;
            if (spindle != null)
            {
                if (active) spindle.Rotate(0f, dt * 12f * Mathf.Rad2Deg, 0f, Space.Self);
                var p = spindle.localPosition;
                p.y = 1.85f + (active ? Mathf.Sin(time * 3f) * 0.12f : 0.2f);
                spindle.localPosition = p;
            }
            if (beam != null)
            {
                beam.gameObject.SetActive(active);
                var p = beam.localPosition;
                p.x = Mathf.Sin(time * 4f) * 0.8f;
                beam.localPosition = p;
            }
            if (press != null)
            {
                var p = press.localPosition;
                p.y = active ? 1.85f + Mathf.Abs(Mathf.Sin(time * 4f)) * 0.3f : 2.1f;
                press.localPosition = p;
            }
        }

        static void SetColor(Renderer r, MaterialPropertyBlock block, Color color, float emission)
        {
            r.GetPropertyBlock(block);
            block.SetColor(BaseColor, color);
            block.SetColor(EmissionColor, color * emission);
            r.SetPropertyBlock(block);
        }
    }
}
