using TMPro;
using UnityEngine;

namespace Twin.Scene
{
    /// <summary>Etiquette 3D qui fait toujours face a la camera (nom du poste, compteurs).</summary>
    public sealed class WorldLabel : MonoBehaviour
    {
        public TextMeshPro text;
        public Renderer background;
        string _lastKey = "";
        Transform _cam;
        public static WorldLabel Create(Transform parent, string name, Vector3 position, Vector2 size, Material backgroundMaterial)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;
            var label = root.AddComponent<WorldLabel>();

            var bg = LineBuilder.Prim(PrimitiveType.Quad, root.transform, "Fond", new Vector3(size.x, size.y, 1f), Vector3.zero, backgroundMaterial);
            label.background = bg.GetComponent<Renderer>();

            var textGo = new GameObject("Texte");
            textGo.transform.SetParent(root.transform, false);
            textGo.transform.localPosition = new Vector3(0, 0, -0.01f);
            var tmp = textGo.AddComponent<TextMeshPro>();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.rectTransform.sizeDelta = new Vector2(size.x, size.y);
            tmp.fontSize = size.y * 3.0f;
            tmp.color = Color.white;
            label.text = tmp;

            // Ordre de dessin explicite : les etiquettes passent apres tous les autres objets transparents
            // (vitres, faisceau) et le texte apres son fond, quel que soit l'angle ou la distance de la camera.
            var bgRenderer = label.background as MeshRenderer;
            if (bgRenderer != null) bgRenderer.sortingOrder = 100;
            var textRenderer = textGo.GetComponent<MeshRenderer>();
            if (textRenderer != null) textRenderer.sortingOrder = 101;
            return label;
        }

        /// <summary>Met a jour le texte seulement s'il change (evite de regenerer le maillage a chaque image).</summary>
        public void Set(string title, string subtitle, Color subtitleColor)
        {
            string key = title + "|" + subtitle + "|" + subtitleColor.GetHashCode();
            if (key == _lastKey) return;
            _lastKey = key;
            string hex = ColorUtility.ToHtmlStringRGB(subtitleColor);
            text.text = string.IsNullOrEmpty(subtitle)
                ? "<b>" + title + "</b>"
                : "<b>" + title + "</b>\n<size=72%><color=#" + hex + ">" + subtitle + "</color></size>";
        }

        void Awake()
        {
            // meme regle pour les etiquettes deja presentes dans la scene sauvegardee
            if (background != null) background.sortingOrder = 100;
            if (text != null && text.TryGetComponent<MeshRenderer>(out var r)) r.sortingOrder = 101;
        }

        void LateUpdate()
        {
            if (_cam == null)
            {
                var c = Camera.main;
                if (c == null) return;
                _cam = c.transform;
            }
            transform.rotation = Quaternion.LookRotation(_cam.forward, _cam.up);
        }
    }
}
