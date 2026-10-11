// Renders the home screen (MainMenuView) or a level with the game's HUD to a PNG without entering Play mode,
// to check them at phone size.
//
//   Sortex > Tools > Snapshot Home Screen     -> Logs/SortexSnapshot.png (1080 x 1920, "Level 1")
//   or create Logs/SortexSnapshot.request (works while Unity is in the background) with
//     "width height level difficultyTag"              the home screen
//     "width height level difficultyTag level:Easy"   Levels/Easy.json on the board with the level HUD
//     "width height level difficultyTag pause:Easy"   the same with the pause dialog open
//     time:Easy (25 s left: the red ring), win:Easy, space:Easy, timeup:Easy, lose:Easy (the level end dialogs)
//     "width height level difficultyTag editorhud"     the HUD picture of the Level Editor's game-screen template
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
            string screen = args.Length > 4 ? args[4] : "menu";
            Snapshot(w, h, level, tag, Path.Combine(Logs, "SortexSnapshot.png"), screen);
        }

        [MenuItem("Sortex/Tools/Snapshot Home Screen", false, 41)]
        static void SnapshotMenu()
        {
            string path = Path.Combine(Logs, "SortexSnapshot.png");
            Snapshot(1080, 1920, 1, 0, path);
            EditorUtility.RevealInFinder(path);
        }

        public static void Snapshot(int width, int height, int level, int difficultyTag, string path, string screen = "menu")
        {
            if (screen == "editorhud")
            {
                // the Level Editor's game-screen template: the HUD picture it draws over the board
                var hud = SortexLevelEditor.RenderHud((float)width / height, level, difficultyTag, 100, width);
                if (hud == null) File.WriteAllText(Path.ChangeExtension(path, ".error.txt"), "editor HUD: nothing rendered");
                else
                {
                    File.WriteAllBytes(path, hud.EncodeToPNG());
                    UnityEngine.Object.DestroyImmediate(hud);
                    Debug.Log("Sortex: editor HUD snapshot written to " + path);
                }
                return;
            }
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

                float aspect = (float)width / height;
                int colon = screen.IndexOf(':');
                if (colon > 0)
                    BuildLevel(holder.transform, cam, aspect, level, difficultyTag, screen.Substring(colon + 1), screen.Substring(0, colon));
                else
                {
                    var menu = new MainMenuView(holder.transform);
                    if (!menu.Ready) throw new Exception("Resources/Sortex/Menu/menu.json is missing");
                    menu.SetLevel(level, difficultyTag);
                    menu.SetWallet(5, 1000);
                    menu.SetVisible(true);
                    menu.Layout(cam, aspect);
                }

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
                Debug.Log("Sortex: " + screen + " snapshot written to " + path);
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

        /// <summary>A level as the game shows it: board, background, level HUD (and the pause dialog).</summary>
        static void BuildLevel(Transform holder, Camera cam, float aspect, int number, int tag, string file, string mode)
        {
            var ta = Resources.Load<TextAsset>(SortexArt.Root + "Levels/" + file);
            if (ta == null) throw new Exception("Resources/Sortex/Levels/" + file + ".json is missing");
            var lv = Sortex.Core.LevelData.FromJson(ta.text);
            var board = Sortex.Core.Board.FromLevel(lv);
            var view = new BoardView(holder, board, lv.difficultyTag);
            var hud = new LevelHud(holder);
            if (!hud.Ready) throw new Exception("Resources/Sortex/Level/level.json is missing");
            hud.SetLevel(number, tag);
            hud.SetBoosters(3, 3, 3, 3);
            hud.SetTime(mode == "time" ? 25f : lv.timeLimit, lv.timeLimit, false);
            hud.SetVisible(true);
            float size;
            Vector2 center;
            SortexGame.FitBoard(view.Bounds, aspect, hud.ReferenceHeight(aspect), out size, out center);
            cam.orthographicSize = size;
            cam.transform.position = new Vector3(center.x, center.y, -10f);
            var bg = SortexArt.NewSprite("Background", holder, SortexArt.BackgroundGradient, -100);
            if (bg.sprite != null)
            {
                var bs = bg.sprite.bounds.size;
                bg.transform.localScale = new Vector3(size * 2f * aspect / bs.x * 1.02f, size * 2f / bs.y * 1.02f, 1f);
            }
            bg.transform.position = new Vector3(center.x, center.y, 0f);
            hud.Follow(cam, aspect);
            if (mode == "pause")
            {
                var dialog = new PauseDialog(holder);
                dialog.SetVisible(true);
                dialog.Layout(cam, aspect);
            }
            LevelDialog end = null;
            if (mode == "win")
            {
                var win = new WinDialog(holder);
                win.Show(20, 84f);
                end = win;
            }
            else if (mode == "space" || mode == "timeup")
            {
                var outOf = new OutOfDialog(holder);
                outOf.Show(mode == "timeup", 1000);
                end = outOf;
            }
            else if (mode == "lose")
            {
                var lose = new LoseDialog(holder);
                lose.Show();
                end = lose;
            }
            if (end != null) end.Layout(cam, aspect);
        }
    }
}
