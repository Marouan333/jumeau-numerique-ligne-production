using System.IO;
using Twin.Bench;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Twin.EditorTools
{
    /// <summary>Menus Twin : banc d'essai des scenarios et build WebGL du jumeau numerique.</summary>
    [InitializeOnLoad]
    public static class TwinMenu
    {
        const string BenchFlag = "Twin.RunBenchOnPlay";

        static TwinMenu()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        // ------------------------------------------------------------------ banc d'essai
        [MenuItem("Twin/Lancer le banc d'essai")]
        public static void RunBenchmark()
        {
            SessionState.SetBool(BenchFlag, true);
            EditorApplication.EnterPlaymode();
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(BenchFlag, false)) return;
            SessionState.SetBool(BenchFlag, false);
            var bench = Object.FindAnyObjectByType<ScenarioBenchmark>();
            if (bench == null) { Debug.LogError("[Twin] ScenarioBenchmark introuvable : lancez Twin > Construire la scene."); return; }
            bench.Run();
        }

        // ------------------------------------------------------------------ build WebGL
        [MenuItem("Twin/Construire WebGL")]
        public static void BuildWebGl()
        {
            // differe l'execution pour rendre la main a l'appelant (la compilation WebGL est longue)
            EditorApplication.delayCall += () => DoBuildWebGl();
        }

        /// <summary>Point d'entree en ligne de commande : Unity -batchmode -buildTarget WebGL -executeMethod Twin.EditorTools.TwinMenu.BuildWebGlBatch</summary>
        public static void BuildWebGlBatch()
        {
            LineSceneSetup.AssignBuiltinMeshes();
            bool ok = DoBuildWebGl();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        static bool DoBuildWebGl()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../frontend/unity"));
            Directory.CreateDirectory(outDir);

            PlayerSettings.productName = "JumeauNumerique";
            PlayerSettings.companyName = "ENSAM Rabat";
            PlayerSettings.runInBackground = true;
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;      // fonctionne sans en-tetes Content-Encoding
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.WebGL.template = "APPLICATION:Minimal";
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/SampleScene.unity", true) };

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            Debug.Log("[Twin] Build WebGL vers " + outDir);
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            string line = "[Twin] Build WebGL : " + s.result + " | " + (s.totalSize / (1024f * 1024f)).ToString("0.0") + " Mo | " + s.totalTime.TotalSeconds.ToString("0") + " s | erreurs " + s.totalErrors;
            Debug.Log(line);
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "twin_build_result.txt"), line);
            return s.result == BuildResult.Succeeded;
        }
    }
}
