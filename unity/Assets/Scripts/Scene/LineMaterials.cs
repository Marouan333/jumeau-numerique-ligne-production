using UnityEngine;

namespace Twin.Scene
{
    /// <summary>
    /// Materiaux de la ligne. Cree par l'outil editeur "Twin/Construire la scene" et
    /// reference par LineController : ainsi les materiaux et shaders sont inclus dans le build WebGL.
    /// </summary>
    [CreateAssetMenu(menuName = "Twin/Materiaux de la ligne", fileName = "LineMaterials")]
    public sealed class LineMaterials : ScriptableObject
    {
        [Header("Machines")]
        public Material frame;
        public Material body;
        public Material accent;     // bleu marine ENSAM
        public Material dark;
        public Material glass;
        public Material yellow;
        public Material hopper;
        public Material metal;
        public Material cartonTop;
        public Material beam;
        public Material cyan;
        [Header("Voyants (couleur par instance via MaterialPropertyBlock)")]
        public Material lamp;
        public Material progress;
        [Header("Produits")]
        public Material productRaw;
        public Material productProcessed;
        public Material productChecked;
        public Material productRejected;
        public Material carton;
        [Header("Decor")]
        public Material belt;
        public Material wood;
        public Material woodDark;
        public Material bin;
        public Material label;      // fond arrondi des etiquettes (bleu nuit)
        public Material labelRed;   // etiquette des rebuts
        [Header("Mesh")]
        public Mesh hopperMesh;
        [Tooltip("Formes de base : evitent GameObject.CreatePrimitive (colliders non disponibles dans le build WebGL).")]
        public Mesh cubeMesh, cylinderMesh, sphereMesh, quadMesh;
    }
}
