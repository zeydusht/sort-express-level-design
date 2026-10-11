// Renders the home screen (MainMenuView) to a PNG without entering Play mode, to check it at phone size.
//
//   Sortex > Tools > Snapshot Home Screen     -> Logs/SortexSnapshot.png (1080 x 1920, "Level 1")
//   or create Logs/SortexSnapshot.request with "width height level difficultyTag" (works while Unity is in the background)
using System;
using System.IO;
using Sortex.Game;
using UnityEditor;
using UnityEngine;

namespace Sortex.EditorTools
{
    [InitializeOnLoad]
    public static class SortexSnapshot
    {
        static double nextPoll;

        static string Logs { get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs"); } }

        static SortexSnapshot()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string request = Path.Combine(Logs, "SortexSnapshot.request");
            if (!File.Exists(request)) return;
            string[] args;
            try
            {
                args = File.ReadAllText(request).Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                File.Delete(request);
            }
            catch (Exception)
            {
                return;
            }
            int w = args.Length > 0 ? int.Parse(args[0]) : 1080;
            int h = args.Length > 1 ? int.Parse(args[1]) : 1920;
            int level = args.Length > 2 ? int.Parse(args[2]) : 1;
            int tag = args.Length > 3 ? int.Parse(args[3]) : 0;
            Snapshot(w, h, level, tag, Path.Combine(Logs, "SortexSnapshot.png"));
        }

        [MenuItem("Sortex/Tools/Snapshot Home Screen", false, 41)]
        static void SnapshotMenu()
        {
            string path = Path.Combine(Logs, "SortexSnapshot.png");
            Snapshot(1080, 1920, 1, 0, path);
            EditorUtility.RevealInFinder(path);
        }

        public static void Snapshot(int width, int height, int level, int difficultyTag, string path)
        {
            var holder = new GameObject("SortexSnapshot");
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                var camGo = new GameObject("SnapshotCamera");
                camGo.transform.SetParent(holder.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.16f, 0.29f, 0.66f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100f;

                var menu = new MainMenuView(holder.transform);
                if (!menu.Ready) throw new Exception("Resources/Sortex/Menu/menu.json is missing");
                menu.SetLevel(level, difficultyTag);
                menu.SetWallet(5, 1000);
                menu.SetVisible(true);
                menu.Layout(cam, (float)width / height);

                rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = previous;
                cam.targetTexture = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log("Sortex: home screen snapshot written to " + path);
            }
            catch (Exception e)
            {
                Debug.LogError("Sortex: snapshot failed: " + e);
                File.WriteAllText(Path.ChangeExtension(path, ".error.txt"), e.ToString());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(holder);
                if (rt != null) UnityEngine.Object.DestroyImmediate(rt);
                if (tex != null) UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
