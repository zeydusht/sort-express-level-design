// Editor helpers for the Sort Express level design prototype.
//
//   Sortex > 1. Setup Project      creates the SortexGame scene, the unlit sprite material and the
//                                  WebGL player settings (also runs once automatically after import)
//   Sortex > 2. Build WebGL (docs) builds the game into <project>/docs, ready for GitHub Pages
//   Sortex > 3. Publish to GitHub  see SortexPublish.cs (simulate own levels, build, commit, push)
//   Sortex > Open Game Scene
//   Sortex > Portrait Game View (9:16)   see SortexGameView.cs (also applied on every Play)
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Sortex.EditorTools
{
    [InitializeOnLoad]
    public static class SortexSetup
    {
        public const string ScenePath = "Assets/Scenes/SortexGame.unity";
        const string MaterialPath = "Assets/Sortex/Resources/Sortex/SpriteUnlit.mat";
        const string AutoKey = "Sortex.AutoSetupDone";

        static SortexSetup()
        {
            EditorApplication.delayCall += AutoSetup;
        }

        static void AutoSetup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            bool missing = !File.Exists(ScenePath) || !File.Exists(MaterialPath);
            if (!missing && SessionState.GetBool(AutoKey, false)) return;
            SessionState.SetBool(AutoKey, true);
            if (missing) Setup(true);
        }

        [MenuItem("Sortex/1. Setup Project", false, 1)]
        static void SetupMenu()
        {
            Setup(true);
            EditorUtility.DisplayDialog("Sortex", "Scene, material and WebGL settings are ready.\n\nPress Play to test, " +
                "then use Sortex > 2. Build WebGL (docs) for GitHub Pages.", "OK");
        }

        [MenuItem("Sortex/Open Game Scene", false, 20)]
        static void OpenScene()
        {
            if (!File.Exists(ScenePath)) Setup(false);
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(ScenePath);
        }

        public static void Setup(bool openScene)
        {
            EnsureMaterial();
            EnsureScene();
            ConfigurePlayer();
            AssetDatabase.SaveAssets();
            if (openScene && !EditorApplication.isPlaying)
            {
                var active = SceneManager.GetActiveScene();
                if (!active.isDirty && active.path != ScenePath) EditorSceneManager.OpenScene(ScenePath);
            }
        }

        static void EnsureMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogWarning("Sortex: no unlit sprite shader found, sprites will use the default material.");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var mat = new Material(shader) { name = "SpriteUnlit" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }

        static void EnsureScene()
        {
            if (!File.Exists(ScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                var previous = SceneManager.GetActiveScene();
                bool additive = previous.IsValid() && !string.IsNullOrEmpty(previous.path);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, additive ? NewSceneMode.Additive : NewSceneMode.Single);
                SceneManager.SetActiveScene(scene);

                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = 8f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.07f, 0.1f, 0.18f);
                camGo.transform.position = new Vector3(0f, 0f, -10f);

                new GameObject("SortexGame").AddComponent<Sortex.Game.SortexGame>();

                EditorSceneManager.SaveScene(scene, ScenePath);
                if (additive && previous.isLoaded && previous != scene)
                {
                    SceneManager.SetActiveScene(previous);
                    EditorSceneManager.CloseScene(scene, true);
                }
                AssetDatabase.Refresh();
            }

            // SortexGame is the first (and only enabled) scene of the build
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) scenes.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void ConfigurePlayer()
        {
            // GitHub Pages does not send Content-Encoding headers: gzip + JS decompression fallback works everywhere.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.defaultWebScreenWidth = 450;
            PlayerSettings.defaultWebScreenHeight = 800;
            PlayerSettings.runInBackground = true;
            PlayerSettings.productName = "Sort Express - Level Design";
            // portrait everywhere, like the phone game
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.defaultScreenWidth = 540;
            PlayerSettings.defaultScreenHeight = 960;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
        }

        [MenuItem("Sortex/2. Build WebGL (docs)", false, 2)]
        static void BuildWebGL()
        {
            BuildDocs(true);
        }

        /// <summary>Builds the WebGL player into &lt;project&gt;/docs (GitHub Pages serves that folder). True on success.</summary>
        public static bool BuildDocs(bool reveal)
        {
            Setup(false);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outDir = Path.Combine(projectRoot, "docs");
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result == BuildResult.Succeeded)
            {
                File.WriteAllText(Path.Combine(outDir, ".nojekyll"), "");
                PatchIndexHtml(Path.Combine(outDir, "index.html"));
                Debug.Log("Sortex: WebGL build ready in " + outDir + " (" + (report.summary.totalSize / (1024 * 1024)) + " MB)");
                if (reveal) EditorUtility.RevealInFinder(outDir);
                return true;
            }
            Debug.LogError("Sortex: WebGL build failed (" + report.summary.result + "). Is the WebGL module installed in Unity Hub?");
            return false;
        }

        // Unity's default template shows a fixed 450 x 800 canvas on desktop browsers, which a laptop window cuts at the
        // top and bottom (the game's top bar and navigation bar). This turns it into a phone-shaped 9:16 window that
        // always fits the browser window, on a dark page without the Unity footer. Phones keep the full-screen canvas.
        const string FitMarker = "/* sortex-fit */";

        public static void PatchIndexHtml(string path)
        {
            if (!File.Exists(path)) return;
            string html = File.ReadAllText(path);
            if (html.Contains(FitMarker)) return;
            var desktop = new System.Text.RegularExpressions.Regex(
                "fullscreenContainer\\.style\\.width = \"\\d+px\";\\s*fullscreenContainer\\.style\\.height = \"\\d+px\";");
            if (!desktop.IsMatch(html))
            {
                Debug.LogWarning("Sortex: index.html has an unexpected layout, left as built.");
                return;
            }
            const string fit = FitMarker + @"
        // Desktop: a phone-shaped (9:16) window that always fits the browser window
        document.querySelector(""#unity-footer"").style.display = ""none"";
        var sortexFit = function () {
          var h = Math.max(320, Math.min(window.innerHeight - 24, 1100));
          var w = Math.round(h * 9 / 16);
          if (w > window.innerWidth - 24) { w = Math.max(180, window.innerWidth - 24); h = Math.round(w * 16 / 9); }
          fullscreenContainer.style.width = w + ""px"";
          fullscreenContainer.style.height = h + ""px"";
        };
        sortexFit();
        window.addEventListener(""resize"", sortexFit);";
            html = desktop.Replace(html, fit, 1);
            html = html.Replace("<title>Unity Web Player | ", "<title>");
            html = html.Replace("</head>",
                "  <style>html, body { background: #15122b; overflow: hidden } .unity-desktop #unity-canvas { border-radius: 14px }</style>\n  </head>");
            File.WriteAllText(path, html);
        }
    }
}
