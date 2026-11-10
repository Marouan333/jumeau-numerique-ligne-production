using System.IO;
using Twin.Net;
using Twin.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Twin.EditorTools
{
    /// <summary>
    /// Menu Twin > Construire la scene : cree les materiaux, les textures et toute la scene de la
    /// ligne (sol, convoyeur, palette, bac de rebuts, etiquettes, camera, lumiere) dans la scene ouverte.
    /// Peut etre relance sans danger : l'ancienne ligne est remplacee.
    /// </summary>
    public static class LineSceneSetup
    {
        const string Dir = "Assets/Twin";
        static Shader _lit, _unlit;

        [MenuItem("Twin/Construire la scene")]
        public static void Build()
        {
            EnsureFolders();
            EnsureTextMeshPro();
            _lit = Shader.Find("Universal Render Pipeline/Lit");
            _unlit = Shader.Find("Universal Render Pipeline/Unlit");

            var tex = MakeTextures();
            var mats = MakeMaterials(tex);
            ConfigureRenderPipeline();
            BuildScene(mats);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();
            Debug.Log("[Twin] Scene construite.");
        }

        // ------------------------------------------------------------------ dossiers et TMP
        static void EnsureFolders()
        {
            foreach (var sub in new[] { "", "/Materials", "/Textures", "/Meshes" })
                if (!AssetDatabase.IsValidFolder(Dir + sub))
                {
                    if (sub == "") AssetDatabase.CreateFolder("Assets", "Twin");
                    else AssetDatabase.CreateFolder(Dir, sub.Substring(1));
                }
        }

        static void EnsureTextMeshPro()
        {
            if (TMPro.TMP_Settings.instance == null)
            {
                TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
                AssetDatabase.Refresh();
            }
        }

        // ------------------------------------------------------------------ textures
        sealed class Textures { public Texture2D grid, belt, label; }

        static Textures MakeTextures()
        {
            var t = new Textures();

            // sol : quadrillage tous les 2 m
            var grid = new Texture2D(128, 128, TextureFormat.RGBA32, false);
            var floor = (Color32)LineLayout.Hex("#cfd6de");
            var line = (Color32)LineLayout.Hex("#bcc5d0");
            var px = new Color32[128 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 128; x++)
                    px[y * 128 + x] = (x < 2 || y < 2) ? line : floor;
            grid.SetPixels32(px);
            t.grid = SavePng(grid, "grid", true, TextureWrapMode.Repeat, false);

            // tapis : bande rayee
            var belt = new Texture2D(64, 16, TextureFormat.RGBA32, false);
            var bpx = new Color32[64 * 16];
            var b1 = (Color32)LineLayout.Hex("#2b3139");
            var b2 = (Color32)LineLayout.Hex("#3a414b");
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 64; x++)
                    bpx[y * 64 + x] = x < 8 ? b2 : b1;
            belt.SetPixels32(bpx);
            t.belt = SavePng(belt, "belt", true, TextureWrapMode.Repeat, false);

            // fond arrondi des etiquettes
            const int w = 128, h = 40; const float r = 18f;
            var lab = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var lpx = new Color32[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = Mathf.Max(Mathf.Max(r - x - 0.5f, x + 0.5f - (w - r)), 0f);
                    float dy = Mathf.Max(Mathf.Max(r - y - 0.5f, y + 0.5f - (h - r)), 0f);
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    lpx[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            lab.SetPixels32(lpx);
            t.label = SavePng(lab, "label_bg", false, TextureWrapMode.Clamp, true);
            return t;
        }

        static Texture2D SavePng(Texture2D tex, string name, bool mipmaps, TextureWrapMode wrap, bool alpha)
        {
            string path = Dir + "/Textures/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = wrap;
            imp.mipmapEnabled = mipmaps;
            imp.filterMode = FilterMode.Bilinear;
            imp.anisoLevel = mipmaps ? 8 : 1;
            imp.alphaIsTransparency = alpha;
            imp.textureCompression = TextureImporterCompression.Compressed;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ materiaux
        static Material Lit(string name, string hex, float metallic = 0.2f, float smoothness = 0.4f)
        {
            var m = new Material(_lit) { name = name };
            m.SetColor("_BaseColor", LineLayout.Hex(hex));
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.enableInstancing = true;
            return Save(m);
        }

        static Material Emissive(Material m, string hex, float intensity)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", LineLayout.Hex(hex) * intensity);
            return m;
        }

        static Material Save(Material m)
        {
            string path = Dir + "/Materials/" + m.name + ".mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("ShadowCaster", false);
        }

        static LineMaterials MakeMaterials(Textures tex)
        {
            var lm = ScriptableObject.CreateInstance<LineMaterials>();
            lm.frame = Lit("frame", "#3a4250", 0.6f, 0.6f);
            lm.body = Lit("body", "#dfe4ea", 0.3f, 0.5f);
            lm.accent = Lit("accent", "#1b3a5c", 0.4f, 0.5f);
            lm.dark = Lit("dark", "#1f2933", 0.1f, 0.2f);
            lm.yellow = Lit("yellow", "#f2c230", 0.1f, 0.4f);
            lm.metal = Lit("metal", "#c0c7d0", 0.9f, 0.8f);
            lm.cartonTop = Lit("carton_top", "#c9a26b", 0.0f, 0.3f);
            lm.carton = Lit("carton", "#c9a26b", 0.0f, 0.3f);

            lm.hopper = Lit("hopper", "#7c8796", 0.5f, 0.5f);
            lm.hopper.SetFloat("_Cull", 0f);

            lm.glass = Lit("glass", "#9fd3ff", 0.0f, 0.9f);
            var gc = lm.glass.GetColor("_BaseColor"); gc.a = 0.25f; lm.glass.SetColor("_BaseColor", gc);
            MakeTransparent(lm.glass);

            lm.beam = new Material(_unlit) { name = "beam" };
            lm.beam.SetColor("_BaseColor", new Color(0.22f, 0.74f, 0.97f, 0.55f));
            lm.beam.SetFloat("_Cull", 0f);
            MakeTransparent(lm.beam);
            Save(lm.beam);

            lm.cyan = Emissive(Lit("cyan", "#38bdf8", 0.4f, 0.4f), "#0ea5e9", 0.6f);
            lm.lamp = Emissive(Lit("lamp", "#94a3b8", 0.0f, 0.5f), "#94a3b8", 0.8f);
            lm.progress = Emissive(Lit("progress", "#22c55e", 0.0f, 0.4f), "#22c55e", 0.4f);

            lm.productRaw = Lit("product_raw", "#b7793f", 0.0f, 0.3f);
            lm.productProcessed = Lit("product_processed", "#4f7fe0", 0.0f, 0.4f);
            lm.productChecked = Lit("product_checked", "#2fb67c", 0.0f, 0.4f);
            lm.productRejected = Lit("product_rejected", "#dc2626", 0.0f, 0.4f);

            lm.belt = Lit("belt", "#ffffff", 0.0f, 0.1f);
            lm.belt.SetTexture("_BaseMap", tex.belt);
            lm.belt.SetTextureScale("_BaseMap", new Vector2(LineLayout.ConveyorLength * 1.5f, 1f));
            lm.wood = Lit("wood", "#a0784a", 0.0f, 0.2f);
            lm.woodDark = Lit("wood_deck", "#b88b56", 0.0f, 0.2f);
            lm.bin = Lit("bin", "#d9483b", 0.1f, 0.4f);

            lm.label = LabelMaterial("label", new Color(0.06f, 0.09f, 0.16f, 0.93f), tex.label);
            lm.labelRed = LabelMaterial("label_red", new Color(0.73f, 0.11f, 0.11f, 0.93f), tex.label);

            var floor = Lit("floor", "#ffffff", 0.0f, 0.1f);
            floor.SetTexture("_BaseMap", tex.grid);
            floor.SetTextureScale("_BaseMap", new Vector2(70f, 40f));
            lm.hopperMesh = SaveMesh(LineBuilder.BuildHopperMesh(), "hopper");
            SetBuiltinMeshes(lm);

            string assetPath = Dir + "/LineMaterials.asset";
            if (AssetDatabase.LoadAssetAtPath<LineMaterials>(assetPath) != null) AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.CreateAsset(lm, assetPath);
            floorMaterial = floor;
            // les proprietes modifiees apres creation doivent etre marquees pour etre sauvegardees
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { Dir + "/Materials" }))
                EditorUtility.SetDirty(AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)));
            EditorUtility.SetDirty(lm);
            return lm;
        }

        static Material floorMaterial;

        /// <summary>Renseigne les formes de base dans LineMaterials (sans refaire toute la scene).</summary>
        public static void AssignBuiltinMeshes()
        {
            var lm = AssetDatabase.LoadAssetAtPath<LineMaterials>(Dir + "/LineMaterials.asset");
            if (lm == null) return;
            SetBuiltinMeshes(lm);
            EditorUtility.SetDirty(lm);
            AssetDatabase.SaveAssets();
        }

        static void SetBuiltinMeshes(LineMaterials lm)
        {
            lm.cubeMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            lm.cylinderMesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            lm.sphereMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
            lm.quadMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        }

        static Material LabelMaterial(string name, Color color, Texture2D mask)
        {
            var m = new Material(_unlit) { name = name };
            m.SetTexture("_BaseMap", mask);
            m.SetColor("_BaseColor", color);
            MakeTransparent(m);
            // le fond est toujours dessine avant le texte (sinon le texte peut passer derriere selon l'angle)
            m.renderQueue = 2990;
            return Save(m);
        }

        static Mesh SaveMesh(Mesh mesh, string name)
        {
            string path = Dir + "/Meshes/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ------------------------------------------------------------------ pipeline
        static void ConfigureRenderPipeline()
        {
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urp == null) return;
            urp.shadowDistance = 80f;
            urp.shadowCascadeCount = 2;      // 2 cascades : suffisant pour la ligne et plus leger pour le navigateur
            urp.msaaSampleCount = 2;
            EditorUtility.SetDirty(urp);
        }

        // ------------------------------------------------------------------ scene
        static void BuildScene(LineMaterials m)
        {
            // nettoyage d'une ancienne construction
            foreach (var n in new[] { "Ligne", "TwinClient" })
            {
                var old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }

            var line = new GameObject("Ligne");
            var decor = new GameObject("Decor").transform; decor.SetParent(line.transform, false);

            // sol + marquages
            var floor = LineBuilder.Prim(PrimitiveType.Plane, decor, "Sol", new Vector3(14f, 1f, 8f), Vector3.zero, floorMaterial);
            foreach (float z in new[] { -4.6f, 4.6f })
            {
                var l = LineBuilder.Prim(PrimitiveType.Quad, decor, "Marquage", new Vector3(52f, 0.18f, 1f), new Vector3(2f, 0.02f, z), m.yellow);
                l.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }

            // convoyeur
            float cx = LineLayout.ConveyorStart + LineLayout.ConveyorLength / 2f;
            LineBuilder.Box(decor, "Tapis", LineLayout.ConveyorLength, 0.12f, 1.0f, m.belt, cx, LineLayout.BeltTop - 0.06f, 0f);
            var belt = decor.Find("Tapis").GetComponent<Renderer>();
            foreach (float z in new[] { -0.58f, 0.58f })
                LineBuilder.Box(decor, "Rail", LineLayout.ConveyorLength, 0.22f, 0.12f, m.frame, cx, LineLayout.BeltTop - 0.05f, z);
            for (float x = LineLayout.ConveyorStart + 1; x < LineLayout.ConveyorStart + LineLayout.ConveyorLength; x += 3f)
                foreach (float z in new[] { -0.5f, 0.5f })
                    LineBuilder.Box(decor, "Pied", 0.12f, LineLayout.BeltTop - 0.15f, 0.12f, m.frame, x, (LineLayout.BeltTop - 0.15f) / 2f, z);

            // palette de sortie
            var pallet = new GameObject("Palette").transform;
            pallet.SetParent(line.transform, false);
            pallet.position = LineLayout.PalletPos;
            foreach (float z in new[] { -0.9f, 0f, 0.9f })
                LineBuilder.Box(pallet, "Latte", 2.6f, 0.15f, 0.4f, m.wood, 0, 0.08f, z);
            LineBuilder.Box(pallet, "Plateau", 2.6f, 0.08f, 2.4f, m.woodDark, 0, 0.2f, 0);
            var boxes = new Transform[LineLayout.PalletCapacity];
            for (int k = 0; k < boxes.Length; k++)
            {
                int lx = k % 4, lz = (k / 4) % 3, ly = k / 12;
                var b = LineBuilder.Box(pallet, "Carton " + (k + 1), 0.62f, 0.5f, 0.62f, m.carton, -0.95f + lx * 0.64f, 0.5f + ly * 0.52f, 0.66f - lz * 0.66f);
                b.SetActive(false);
                boxes[k] = b.transform;
            }

            // bac de rebuts
            var bin = new GameObject("Bac de rebuts").transform;
            bin.SetParent(line.transform, false);
            bin.position = LineLayout.BinPos;
            LineBuilder.Box(bin, "Paroi", 1.6f, 1.0f, 0.1f, m.bin, 0, 0.5f, 0.75f);
            LineBuilder.Box(bin, "Paroi", 1.6f, 1.0f, 0.1f, m.bin, 0, 0.5f, -0.75f);
            LineBuilder.Box(bin, "Paroi", 0.1f, 1.0f, 1.5f, m.bin, 0.8f, 0.5f, 0);
            LineBuilder.Box(bin, "Paroi", 0.1f, 1.0f, 1.5f, m.bin, -0.8f, 0.5f, 0);
            LineBuilder.Box(bin, "Fond", 1.6f, 0.1f, 1.5f, m.bin, 0, 0.05f, 0);

            // racines des postes + etiquettes
            var stationRoots = new Transform[4];
            var labels = new WorldLabel[4];
            var labelsRoot = new GameObject("Etiquettes").transform; labelsRoot.SetParent(line.transform, false);
            for (int i = 0; i < 4; i++)
            {
                var go = new GameObject("Poste " + (i + 1));
                go.transform.SetParent(line.transform, false);
                go.transform.position = new Vector3(LineLayout.StationX[i], 0, 0);
                stationRoots[i] = go.transform;
                labels[i] = WorldLabel.Create(labelsRoot, "Etiquette poste " + (i + 1), new Vector3(LineLayout.StationX[i], 5.6f, 0), new Vector2(5.12f, 1.6f), m.label);
            }
            var exitLabel = WorldLabel.Create(labelsRoot, "Etiquette sortie", LineLayout.PalletPos + new Vector3(0, 3.4f, 0), new Vector2(4.2f, 1.1f), m.label);
            var rejectLabel = WorldLabel.Create(labelsRoot, "Etiquette rebuts", LineLayout.BinPos + new Vector3(0, 2.0f, 0), new Vector2(2.6f, 1.0f), m.labelRed);
            exitLabel.Set("Produits finis", "", Color.white);
            rejectLabel.Set("Rebuts", "", Color.white);
            for (int i = 0; i < 4; i++) labels[i].Set(i == 0 ? "Alimentation" : i == 1 ? "Traitement" : i == 2 ? "Contrôle qualité" : "Emballage", "En attente de connexion", LineLayout.Idle);

            var products = new GameObject("Produits").transform; products.SetParent(line.transform, false);

            // client reseau + controleur
            var netGo = new GameObject("TwinClient");
            var client = netGo.AddComponent<TwinClient>();
            var ctrl = line.AddComponent<LineController>();
            ctrl.client = client;
            ctrl.materials = m;
            ctrl.stationRoots = stationRoots;
            ctrl.stationLabels = labels;
            ctrl.exitLabel = exitLabel;
            ctrl.rejectLabel = rejectLabel;
            ctrl.belt = belt;
            ctrl.palletBoxes = boxes;
            ctrl.productsRoot = products;

            // pont avec la page web et banc d'essai des scenarios
            var bridgeGo = new GameObject("LineBridge");
            var bridge = bridgeGo.AddComponent<LineBridge>();
            var bench = bridgeGo.AddComponent<Twin.Bench.ScenarioBenchmark>();
            bench.client = client;

            // les 4 machines par defaut, visibles dans l'editeur
            for (int i = 0; i < 4; i++)
            {
                var u = LineBuilder.BuildUnit(i, i, m, stationRoots[i]);
                u.transform.localPosition = Vector3.zero;
            }

            foreach (var t in decor.GetComponentsInChildren<Transform>())
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);

            SetupCameraAndLight();
        }

        static void SetupCameraAndLight()
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.white;
            RenderSettings.ambientEquatorColor = Color.Lerp(Color.white, LineLayout.Hex("#8a96a8"), 0.5f);
            RenderSettings.ambientGroundColor = LineLayout.Hex("#8a96a8");
            RenderSettings.ambientIntensity = 0.55f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 60f;
            RenderSettings.fogEndDistance = 120f;
            RenderSettings.fogColor = LineLayout.Hex("#e8ecf1");

            var light = Object.FindAnyObjectByType<Light>();
            if (light != null && light.type == LightType.Directional)
            {
                light.transform.position = new Vector3(12, 30, -18);
                light.transform.rotation = Quaternion.LookRotation(-light.transform.position.normalized);
                light.intensity = 1.05f;
                light.color = Color.white;
                light.shadows = LightShadows.Soft;
                light.shadowStrength = 0.9f;
                light.shadowBias = 0.03f;
                light.shadowNormalBias = 0.4f;
            }

            // effets : pas de flou de mouvement ni de vignette (fluidite), bloom discret, tonemapping neutre
            var volume = Object.FindAnyObjectByType<Volume>();
            if (volume != null && volume.sharedProfile != null)
            {
                var profile = volume.sharedProfile;
                if (profile.Has<MotionBlur>()) profile.Remove<MotionBlur>();
                if (profile.Has<Vignette>()) profile.Remove<Vignette>();
                if (profile.TryGet(out Bloom bloom)) { bloom.intensity.Override(0.08f); bloom.threshold.Override(1.2f); }
                if (profile.TryGet(out Tonemapping tm)) tm.mode.Override(TonemappingMode.Neutral);
                EditorUtility.SetDirty(profile);
            }

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = LineLayout.Hex("#e8ecf1");
                cam.fieldOfView = 45f;
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 300f;
                if (cam.GetComponent<OrbitCamera>() == null) cam.gameObject.AddComponent<OrbitCamera>();
                var bridgeObj = GameObject.Find("LineBridge");
                if (bridgeObj != null) bridgeObj.GetComponent<LineBridge>().orbit = cam.GetComponent<OrbitCamera>();
                cam.transform.position = new Vector3(4, 20, -32);
                cam.transform.LookAt(new Vector3(4, 1, 0));
            }
        }
    }
}
