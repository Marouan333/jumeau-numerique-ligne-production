using UnityEngine;

namespace Twin.Scene
{
    /// <summary>
    /// Construit les machines et le decor a partir de primitives, comme buildUnit() de la
    /// version Three.js. Utilise a l'execution (nombre de machines variable) et par l'outil editeur.
    /// </summary>
    public static class LineBuilder
    {
        /// <summary>
        /// Renseigne par LineController : pendant l'execution, les formes sont creees avec ces maillages.
        /// GameObject.CreatePrimitive ajoute des colliders (capsule, sphere, mesh) que le build WebGL a supprimes,
        /// ce qui provoque des erreurs dans le navigateur.
        /// </summary>
        public static LineMaterials Shared;

        static Mesh SharedMesh(PrimitiveType type)
        {
            if (Shared == null) return null;
            switch (type)
            {
                case PrimitiveType.Cube: return Shared.cubeMesh;
                case PrimitiveType.Cylinder: return Shared.cylinderMesh;
                case PrimitiveType.Sphere: return Shared.sphereMesh;
                case PrimitiveType.Quad: return Shared.quadMesh;
                default: return null;
            }
        }

        // ------------------------------------------------------------------ primitives
        public static GameObject Prim(PrimitiveType type, Transform parent, string name, Vector3 size, Vector3 pos, Material mat, bool collider = false)
        {
            GameObject go;
            var mesh = Application.isPlaying ? SharedMesh(type) : null;
            if (mesh != null)
            {
                // execution (y compris WebGL) : forme sans collider
                go = new GameObject(name);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            else
            {
                // edition : CreatePrimitive, puis on retire le collider
                go = GameObject.CreatePrimitive(type);
                go.name = name;
                if (!collider)
                {
                    var col = go.GetComponent<Collider>();
                    if (col != null) { if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col); }
                }
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = size;
            return go;
        }

        public static GameObject Box(Transform parent, string name, float w, float h, float d, Material mat, float x, float y, float z)
            => Prim(PrimitiveType.Cube, parent, name, new Vector3(w, h, d), new Vector3(x, y, z), mat);

        // Le cylindre Unity fait 2 de haut pour 0,5 de rayon.
        public static GameObject Cyl(Transform parent, string name, float radius, float height, Material mat, float x, float y, float z)
            => Prim(PrimitiveType.Cylinder, parent, name, new Vector3(radius * 2f, height / 2f, radius * 2f), new Vector3(x, y, z), mat);

        // ------------------------------------------------------------------ machine
        public static StationUnit BuildUnit(int kind, int station, LineMaterials m, Transform parent)
        {
            var root = new GameObject("Machine " + station);
            root.transform.SetParent(parent, false);
            var t = root.transform;
            var unit = root.AddComponent<StationUnit>();
            unit.station = station;
            unit.kind = kind;

            // tunnel : deux flancs + capot, le convoyeur passe au milieu
            Box(t, "Flanc", 2.4f, 2.2f, 0.25f, m.body, 0, 1.1f, 0.85f);
            Box(t, "Flanc", 2.4f, 2.2f, 0.25f, m.body, 0, 1.1f, -0.85f);
            Box(t, "Capot", 2.6f, 0.5f, 2.0f, m.accent, 0, 2.45f, 0);
            Box(t, "Bande", 2.42f, 0.08f, 0.27f, m.yellow, 0, 0.3f, 0.86f);
            Box(t, "Bande", 2.42f, 0.08f, 0.27f, m.yellow, 0, 0.3f, -0.86f);

            if (kind == 0) // alimentation : tremie
            {
                var hopper = new GameObject("Tremie");
                hopper.transform.SetParent(t, false);
                hopper.transform.localPosition = new Vector3(0, 3.3f, 0);
                hopper.AddComponent<MeshFilter>().sharedMesh = m.hopperMesh;
                hopper.AddComponent<MeshRenderer>().sharedMaterial = m.hopper;
                for (int k = 0; k < 4; k++)
                    Box(t, "Produit brut", 0.35f, 0.3f, 0.35f, m.productRaw, -0.3f + (k % 2) * 0.5f, 3.6f + (k / 2) * 0.25f, -0.2f + (k % 2) * 0.3f);
            }
            else if (kind == 1) // traitement : broche
            {
                var spindle = new GameObject("Broche").transform;
                spindle.SetParent(t, false);
                spindle.localPosition = new Vector3(0, 1.85f, 0);
                Cyl(spindle, "Corps", 0.18f, 0.8f, m.frame, 0, 0, 0);
                Cyl(spindle, "Outil", 0.085f, 0.35f, m.metal, 0, -0.55f, 0);
                Box(t, "Moteur", 0.9f, 0.6f, 0.9f, m.frame, 0, 3.0f, 0);
                unit.spindle = spindle;
            }
            else if (kind == 2) // controle qualite : camera + faisceau
            {
                Box(t, "Camera", 0.7f, 0.45f, 0.6f, m.dark, 0, 2.0f, 0);
                Cyl(t, "Objectif", 0.16f, 0.2f, m.glass, 0, 1.72f, 0);
                var beam = Prim(PrimitiveType.Quad, t, "Faisceau", new Vector3(0.12f, 1.6f, 1f), new Vector3(0, 1.25f, 0), m.beam);
                beam.transform.localRotation = Quaternion.Euler(0, 90f, 0);
                unit.beam = beam.transform;
                Box(t, "Rail lumineux", 2.6f, 0.12f, 0.12f, m.cyan, 0, 2.75f, -1.0f);
            }
            else // emballage : cartonneuse
            {
                Box(t, "Carton", 1.4f, 0.9f, 1.4f, m.cartonTop, 0, 3.15f, 0);
                var press = Box(t, "Presse", 1.2f, 0.15f, 1.0f, m.frame, 0, 2.0f, 0);
                unit.press = press.transform;
            }

            // voyant d'etat (cote camera : -Z)
            Cyl(t, "Mat voyant", 0.05f, 1.0f, m.frame, 1.05f, 3.2f, -0.85f);
            var lamp = Prim(PrimitiveType.Sphere, t, "Voyant", Vector3.one * 0.4f, new Vector3(1.05f, 3.8f, -0.85f), m.lamp);
            unit.lamp = lamp.GetComponent<Renderer>();

            // barre de progression (face avant) : pivot a gauche
            Box(t, "Fond barre", 2.0f, 0.16f, 0.04f, m.dark, 0, 2.45f, -1.02f);
            var barPivot = new GameObject("Pivot barre").transform;
            barPivot.SetParent(t, false);
            barPivot.localPosition = new Vector3(-1.0f, 2.45f, -1.03f);
            barPivot.localScale = new Vector3(0.001f, 1f, 1f);
            var bar = Box(barPivot, "Barre", 2.0f, 0.12f, 0.05f, m.progress, 1.0f, 0, 0);
            unit.barTransform = barPivot;
            unit.bar = bar.GetComponent<Renderer>();

            // vitre de protection
            Box(t, "Vitre", 2.2f, 1.0f, 0.04f, m.glass, 0, 1.6f, -0.98f);

            // zone cliquable
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 2.1f, 0);
            col.size = new Vector3(2.8f, 4.2f, 2.2f);
            return unit;
        }

        // ------------------------------------------------------------------ maillage de la tremie
        /// <summary>Tronc de pyramide ouvert (4 faces), comme le CylinderGeometry a 4 segments de Three.js.</summary>
        public static Mesh BuildHopperMesh()
        {
            const float topR = 1.0f, botR = 0.35f, h = 1.2f;
            var verts = new Vector3[8];
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                verts[i] = new Vector3(Mathf.Sin(a) * topR, h / 2f, Mathf.Cos(a) * topR);
                verts[i + 4] = new Vector3(Mathf.Sin(a) * botR, -h / 2f, Mathf.Cos(a) * botR);
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                tris.AddRange(new[] { i, j, i + 4 });
                tris.AddRange(new[] { j, j + 4, i + 4 });
            }
            var mesh = new Mesh { name = "Tremie" };
            mesh.vertices = verts;
            mesh.triangles = tris.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
