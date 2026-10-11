// Sortex > Level Editor  (Ctrl/Cmd + Shift + L)
//
// A level editor for the Sort Express prototype, working directly on Sort Express' own JSON format.
//
//  * Board      : add 3-slot / single / wide shelves on a grid that matches the real game's positions,
//                 drag them, set rows, lock, shutter, cargo door and moving (conveyor) rows per shelf.
//  * Items      : pick an item from the palette (every item_*.png in the project), paint it into slots of
//                 the row you are looking at; mark items frozen or mystery.
//  * Checks     : every type must come in multiples of 3, frozen items must be reachable, etc. Board numbers
//                 are compared with the real game's medians for the chosen tier.
//  * Auto-fill  : draw the shelves, let a recipe place the items (types, fill, spread, colour twins, obstacles),
//                 optionally trying several seeds and keeping the one closest to the target win rate.
//  * Simulate   : the same solver + simulated players as Tools/sim, run inside the editor.
//  * Play       : opens the level in Play mode.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Sortex.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Sortex.EditorTools
{
    public sealed class SortexLevelEditor : EditorWindow
    {
        public const string LevelsDir = "Assets/Sortex/Resources/Sortex/Levels";
        public const string ItemsDir = "Assets/Sortex/Resources/Sortex/Items";
        public const string TestLevelKey = "Sortex.TestLevel";
        const string LastLevelKey = "Sortex.Editor.LastLevel";
        const float SW = Board.SlotWidth;
        // the game draws the level's x at half scale (BoardView.XS): the canvas does the same, so it looks like the game
        const float XS = Sortex.Game.BoardView.XS;
        const float SnapX = Board.SlotWidth * 0.5f;
        const float SnapY = 0.25f;

        enum Tool { Select, Shelf3, Single, Wide, Paint, Erase, Frozen, Mystery, MovingRow, Pinata, Egg }

        static readonly string[] ToolNames = { "Select / Move", "+ 3-Shelf", "+ Single", "+ Wide (4)", "Paint", "Erase", "Frozen", "Mystery", "+ Moving row", "Piñata", "Egg" };
        static readonly string[] PinataColors = { "0 red", "1 yellow", "2 green", "3 cyan", "4 blue", "5 pink" };
        int pinataColor;
        static readonly string[] TierNames = { "Normal (ld 0)", "Hard (ld 10)", "Super Hard (ld 20)" };
        static readonly int[] TierTags = { 0, 10, 20 };
        // target win-rate bands per tier (simulated players, no boosters)
        static readonly float[] BandLo = { 0.88f, 0.50f, 0.20f };
        static readonly float[] BandHi = { 1.00f, 0.75f, 0.45f };
        // real game medians, all 2150 levels (Tools/tier_stats.py, Tools/results/tier_stats.json):
        // items, types, shelves, seconds per item, front slots, items per front slot, single shelves, obstacle kinds
        static readonly float[,] RealMedians =
        {
            { 126, 41, 13, 0.92f, 39, 3.27f, 0, 2 },
            { 141, 45, 15, 1.02f, 42, 3.36f, 2, 3 },
            { 153, 50, 16, 1.11f, 45, 3.54f, 2, 2 }
        };
        // the middle half of the real levels of each tier (25th-75th percentile) and the obstacles they use most
        static readonly string[] TierTargets =
        {
            "Real Normal levels (1722): 12-15 shelves, 111-138 items, 36-45 types, 90-145 s. " +
            "Obstacle kinds 1-3; most used: cargo door 24%, frozen 21%, wide 20%, shutter 20%, moving 20%, lock 18%.",
            "Real Hard levels (214): 14-17 shelves, 129-150 items, 40-50 types, 120-175 s. " +
            "Obstacle-heavy: 2-3 kinds; shutter 40%, lock 38%, cargo door 35%, frozen 31%, egg 25%.",
            "Real Super Hard levels (214): 15-17 shelves (half have 2-4 singles), 144-168 items, 47-54 types, 140-215 s, " +
            "3.5 items per front slot. Board-heavy, not obstacle-heavy: 1-3 kinds (13% none); " +
            "shutter 34%, cargo door 27%, lock 23% (~10 matches), piñata 21%, frozen 21%, moving 19%, rotating door 18%, " +
            "wide 16%, mystery 13%, fuse box 7%, egg 2%, iris never."
        };
        // seconds per item used by the three case levels (time rises with the tier, as in the real data)
        static readonly float[] SuggestedSecondsPerItem = { 1.5f, 1.8f, 1.85f };

        // ------------------------------------------------------------------ state
        LevelData level;
        string levelPath;
        bool dirty;
        int version;
        Tool tool = Tool.Select;
        int selected = -1;
        int row;
        string brush;

        float zoom = 62f;
        Vector2 pan;
        bool draggingShelf;
        Vector2 dragOffset;
        bool panning;
        bool strokeActive;
        Vector2 hoverWorld;
        bool needsFrame = true;

        // serialized so work survives script reloads and entering / leaving Play mode
        [SerializeField] List<string> undo = new List<string>();
        [SerializeField] List<string> redo = new List<string>();
        [SerializeField] string savedJson;
        [SerializeField] string savedPath;
        [SerializeField] bool savedDirty;

        // catalog
        List<string> catalog = new List<string>();
        readonly Dictionary<string, string> catalogPaths = new Dictionary<string, string>();
        readonly Dictionary<string, Texture2D> icons = new Dictionary<string, Texture2D>();
        string search = "";
        Vector2 paletteScroll, leftScroll, rightScroll;

        // auto-fill
        [SerializeField] FillRecipe recipe = new FillRecipe();
        bool showFill = true, showHelp;
        int tuneSeeds = 12;

        // simulation (background thread)
        Thread worker;
        volatile bool cancel;
        volatile float progress;
        string workerLabel;
        SimReport report;
        int reportVersion = -1;
        List<KeyValuePair<int, SimReport>> tuneResults;
        int tuneBest = -1;
        string tuneError;
        int simRuns = 300;
        string status;

        GUIStyle badge, bigNumber, wrapLabel, centerLabel;

        // ================================================================== window
        [MenuItem("Sortex/Level Editor %#l", false, 10)]
        public static void Open()
        {
            var w = GetWindow<SortexLevelEditor>("Sortex Level Editor");
            w.minSize = new Vector2(1100, 640);
        }

        /// <summary>Right-click a level .json in the Project window → open it here.</summary>
        [MenuItem("Assets/Open in Sortex Level Editor", false, 20)]
        static void OpenSelected()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            var w = GetWindow<SortexLevelEditor>("Sortex Level Editor");
            w.minSize = new Vector2(1100, 640);
            if (w.ConfirmDiscard()) w.Load(path);
        }

        [MenuItem("Assets/Open in Sortex Level Editor", true)]
        static bool CanOpenSelected()
        {
            return Selection.activeObject != null &&
                   AssetDatabase.GetAssetPath(Selection.activeObject).EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            RefreshCatalog();
            if (recipe == null) recipe = new FillRecipe();
            if (level == null && !string.IsNullOrEmpty(savedJson))
            {
                level = LevelData.FromJson(savedJson);
                foreach (var s in level.shelves) Normalize(s);
                levelPath = string.IsNullOrEmpty(savedPath) ? null : savedPath;
                dirty = savedDirty;
                needsFrame = true;
            }
            if (level == null)
            {
                string last = EditorPrefs.GetString(LastLevelKey, "");
                if (!string.IsNullOrEmpty(last) && File.Exists(last)) Load(last);
                else NewLevel();
            }
        }

        void OnDisable()
        {
            cancel = true;
            if (level != null)
            {
                savedJson = level.ToJson(false);
                savedPath = levelPath;
                savedDirty = dirty;
            }
        }

        void Update()
        {
            if (mysteryQueue.Count > 0) BuildMysteryIcons();
            if (hudWanted != null && hudWanted != hudKey && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling)
                BuildHudTexture();
            if (worker != null)
            {
                Repaint();
                if (!worker.IsAlive)
                {
                    worker = null;
                    OnWorkerDone();
                }
            }
        }

        // ================================================================== level io
        void NewLevel()
        {
            level = new LevelData { name = NextLevelName(), difficultyTag = 0, timeLimit = 100 };
            float[] xs = { -3.13f, 0f, 3.13f };
            for (int r = 0; r < 3; r++)
                foreach (float x in xs)
                    level.shelves.Add(ShelfData.Create(x, 2 - r, 3, 3));
            levelPath = null;
            dirty = false;
            undo.Clear();
            redo.Clear();
            selected = -1;
            row = 0;
            report = null;
            version++;
            needsFrame = true;
            status = "New level \"" + level.name + "\": 9 empty shelves with 3 rows. Paint items or use Auto-fill, then Save.";
        }

        /// <summary>"Level N" with the first N that is not saved yet.</summary>
        static string NextLevelName()
        {
            int n = 1;
            while (File.Exists(LevelsDir + "/Level " + n + ".json")) n++;
            return "Level " + n;
        }

        void Load(string path)
        {
            try
            {
                level = LevelData.FromJson(File.ReadAllText(path));
                if (string.IsNullOrEmpty(level.name) || level.name == "Level") level.name = Path.GetFileNameWithoutExtension(path);
                foreach (var s in level.shelves) Normalize(s);
                levelPath = path;
                dirty = false;
                undo.Clear();
                redo.Clear();
                selected = -1;
                row = 0;
                report = null;
                version++;
                needsFrame = true;
                EditorPrefs.SetString(LastLevelKey, path);
                status = "Opened " + Path.GetFileName(path) +
                         (level.GeneratorItemCount > 0
                             ? "  (item generator: " + level.GeneratorItemCount + " items fill the G slots and the rows behind them when the level starts)"
                             : "");
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Sortex", "Could not read " + path + "\n" + e.Message, "OK");
            }
        }

        static void Normalize(ShelfData s)
        {
            for (int li = 0; li < s.layers.Count; li++)
            {
                var l = s.layers[li];
                if (l.Length != s.width)
                {
                    var n = ShelfData.NewLayer(s.width);
                    for (int k = 0; k < Math.Min(l.Length, s.width); k++) n[k] = l[k] ?? new SlotData();
                    s.layers[li] = n;
                }
                for (int k = 0; k < s.width; k++)
                    if (s.layers[li][k] == null) s.layers[li][k] = new SlotData();
            }
        }

        bool Save(bool saveAs)
        {
            string path = levelPath;
            // a file opened from outside this project (e.g. an original Sort Express level) is never overwritten
            if (saveAs || string.IsNullOrEmpty(path) || !InProject(path))
            {
                Directory.CreateDirectory(LevelsDir);
                string suggested = string.IsNullOrEmpty(level.name) || level.name == "New Level" ? NextLevelName() : Safe(level.name);
                path = EditorUtility.SaveFilePanelInProject("Save level", suggested, "json",
                    "Levels in Resources/Sortex/Levels show up in the game's level select (the Level button).", LevelsDir);
                if (string.IsNullOrEmpty(path)) return false;
                if (string.IsNullOrEmpty(level.name) || level.name == "New Level") level.name = Path.GetFileNameWithoutExtension(path);
            }
            File.WriteAllText(path, level.ToJson());
            EnsureIcons(level);
            AssetDatabase.ImportAsset(ToAssetPath(path));
            levelPath = path;
            dirty = false;
            EditorPrefs.SetString(LastLevelKey, path);
            bool inGame = path.Replace('\\', '/').Contains("Resources/Sortex/Levels");
            status = "Saved " + Path.GetFileName(path) + (inGame ? "  ->  in the game's level select (press Play, then Level)." : "");
            return true;
        }

        static bool InProject(string path)
        {
            try
            {
                string full = Path.GetFullPath(path).Replace('\\', '/');
                string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/').TrimEnd('/');
                return full.StartsWith(assets + "/", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        static string Safe(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Level";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static string ToAssetPath(string path)
        {
            path = path.Replace('\\', '/');
            int i = path.IndexOf("Assets/", StringComparison.Ordinal);
            return i >= 0 ? path.Substring(i) : path;
        }

        /// <summary>The game loads icons from Resources/Sortex/Items: copy every icon the level uses there.</summary>
        void EnsureIcons(LevelData lv)
        {
            Directory.CreateDirectory(ItemsDir);
            bool any = false;
            foreach (var id in lv.ItemIds())
            {
                if (string.IsNullOrEmpty(id) || !id.StartsWith("item_")) continue;
                string dst = ItemsDir + "/" + id + ".png";
                if (File.Exists(dst)) continue;
                string src;
                if (!catalogPaths.TryGetValue(id, out src) || !File.Exists(src)) continue;
                File.Copy(src, dst);
                any = true;
            }
            if (any) AssetDatabase.Refresh();
        }

        void RefreshCatalog()
        {
            catalogPaths.Clear();
            foreach (var guid in AssetDatabase.FindAssets("item_ t:Texture2D"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(p);
                if (!name.StartsWith("item_") || !p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                string existing;
                // prefer the copy the game uses
                if (catalogPaths.TryGetValue(name, out existing) && existing.StartsWith(ItemsDir)) continue;
                catalogPaths[name] = p;
            }
            catalog = catalogPaths.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
            icons.Clear();
        }

        Texture2D Icon(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            Texture2D t;
            if (icons.TryGetValue(id, out t)) return t;
            string p;
            t = catalogPaths.TryGetValue(id, out p) ? AssetDatabase.LoadAssetAtPath<Texture2D>(p) : null;
            icons[id] = t;
            return t;
        }

        // ================================================================== undo
        /// <summary>Call right before changing the level (one undo step per user action).</summary>
        void BeginEdit()
        {
            string snap = level.ToJson(false);
            if (undo.Count == 0 || undo[undo.Count - 1] != snap) undo.Add(snap);
            if (undo.Count > 60) undo.RemoveAt(0);
            redo.Clear();
            Touch();
        }

        void Touch()
        {
            dirty = true;
            version++;
        }

        void DoUndo()
        {
            if (undo.Count == 0) return;
            redo.Add(level.ToJson(false));
            Restore(undo[undo.Count - 1]);
            undo.RemoveAt(undo.Count - 1);
        }

        void DoRedo()
        {
            if (redo.Count == 0) return;
            undo.Add(level.ToJson(false));
            Restore(redo[redo.Count - 1]);
            redo.RemoveAt(redo.Count - 1);
        }

        void Restore(string json)
        {
            level = LevelData.FromJson(json);
            foreach (var s in level.shelves) Normalize(s);
            if (selected >= level.shelves.Count) selected = -1;
            Touch();
        }

        // ================================================================== GUI
        /// <summary>Ends the current GUI pass after a structural change (keeps IMGUI layout passes consistent).</summary>
        void Bail()
        {
            Repaint();
            GUIUtility.ExitGUI();
        }

        void Styles()
        {
            if (badge != null) return;
            badge = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            badge.normal.textColor = Color.white;
            bigNumber = new GUIStyle(EditorStyles.boldLabel) { fontSize = 22 };
            wrapLabel = new GUIStyle(EditorStyles.wordWrappedMiniLabel);
            centerLabel = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            centerLabel.normal.textColor = Color.white;
            centerMini = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            centerMini.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
        }

        void OnGUI()
        {
            if (level == null) NewLevel();
            Styles();
            Shortcuts();

            DrawTopBar();
            EditorGUILayout.BeginHorizontal();
            DrawLeft();
            DrawCenter();
            DrawRight();
            EditorGUILayout.EndHorizontal();
            DrawStatus();
        }

        void Shortcuts()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            bool ctrl = e.control || e.command;
            if (GUIUtility.keyboardControl != 0) return;   // typing in a field
            bool handled = true;
            if (ctrl && e.keyCode == KeyCode.Z && !e.shift) DoUndo();
            else if (ctrl && (e.keyCode == KeyCode.Y || e.keyCode == KeyCode.Z && e.shift)) DoRedo();
            else if (ctrl && e.keyCode == KeyCode.S) Save(false);
            else if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && selected >= 0) DeleteShelf(selected);
            else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9) row = e.keyCode - KeyCode.Alpha1;
            else if (e.keyCode == KeyCode.F) needsFrame = true;
            else if (e.keyCode == KeyCode.Q) tool = Tool.Select;
            else if (e.keyCode == KeyCode.B) tool = Tool.Paint;
            else if (e.keyCode == KeyCode.E) tool = Tool.Erase;
            else handled = false;
            if (handled)
            {
                e.Use();
                Bail();
            }
        }

        void DrawTopBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("New", EditorStyles.toolbarButton, GUILayout.Width(45)))
            {
                if (ConfirmDiscard()) NewLevel();
                Bail();
            }
            if (GUILayout.Button("Open ▾", EditorStyles.toolbarDropDown, GUILayout.Width(60))) OpenMenu();
            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(45))) { Save(false); Bail(); }
            if (GUILayout.Button("Save As…", EditorStyles.toolbarButton, GUILayout.Width(65))) { Save(true); Bail(); }
            GUILayout.Space(10);
            GUILayout.Label((levelPath != null ? Path.GetFileName(levelPath) : "(unsaved)") + (dirty ? " *" : ""), EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            GUI.enabled = undo.Count > 0;
            if (GUILayout.Button("Undo", EditorStyles.toolbarButton, GUILayout.Width(45))) { DoUndo(); Bail(); }
            GUI.enabled = redo.Count > 0;
            if (GUILayout.Button("Redo", EditorStyles.toolbarButton, GUILayout.Width(45))) { DoRedo(); Bail(); }
            GUI.enabled = true;
            GUILayout.Space(10);
            GUI.enabled = worker == null;
            if (GUILayout.Button("Simulate", EditorStyles.toolbarButton, GUILayout.Width(70))) { StartSimulation(); Bail(); }
            GUI.enabled = true;
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.45f, 0.9f, 0.5f);
            if (GUILayout.Button("▶ Play level", EditorStyles.toolbarButton, GUILayout.Width(90))) { PlayLevel(); Bail(); }
            GUI.backgroundColor = old;
            if (GUILayout.Button(new GUIContent("⬆ Publish", "Simulate your saved levels, update the README, build WebGL and push to GitHub (Sortex > 3. Publish to GitHub)."),
                    EditorStyles.toolbarButton, GUILayout.Width(75)))
            {
                if (!dirty || Save(false)) SortexPublish.PublishMenu();
                Bail();
            }
            EditorGUILayout.EndHorizontal();
        }

        bool ConfirmDiscard()
        {
            return !dirty || EditorUtility.DisplayDialog("Sortex", "Discard unsaved changes?", "Discard", "Cancel");
        }

        void OpenMenu()
        {
            var menu = new GenericMenu();
            if (Directory.Exists(LevelsDir))
                foreach (var f in Directory.GetFiles(LevelsDir, "*.json")
                             .OrderBy(f => Path.GetFileNameWithoutExtension(f), Comparer<string>.Create(Sortex.Game.SortexGame.NaturalCompare)))
                {
                    string file = f.Replace('\\', '/');
                    if (Path.GetFileName(file) == "report.json") continue;
                    menu.AddItem(new GUIContent(Path.GetFileNameWithoutExtension(file)), file == levelPath, () => { if (ConfirmDiscard()) Load(file); });
                }
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("From file… (e.g. an original Sort Express level)"), false, () =>
            {
                if (!ConfirmDiscard()) return;
                string p = EditorUtility.OpenFilePanel("Open level", LevelsDir, "json");
                if (!string.IsNullOrEmpty(p)) Load(p);
            });
            menu.AddItem(new GUIContent("Refresh item palette"), false, RefreshCatalog);
            if (levelPath != null && CanDelete(levelPath))
            {
                menu.AddSeparator("");
                string doomed = levelPath;
                menu.AddItem(new GUIContent("Delete \"" + Path.GetFileNameWithoutExtension(doomed) + "\"…"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("Sortex", "Delete " + Path.GetFileName(doomed) + "? It leaves the game menu and, with the next " +
                                                    "Publish, the README table.", "Delete", "Cancel"))
                        DeleteLevel(doomed);
                });
            }
            menu.ShowAsContext();
        }

        void DrawStatus()
        {
            string hint;
            switch (tool)
            {
                case Tool.Select: hint = "Select / Move: click a shelf, drag to move (snaps to the game grid). Del deletes. Middle mouse or Alt+drag pans, wheel zooms, F frames."; break;
                case Tool.Paint: hint = "Paint: click a slot in the current row to place the selected item. Shift+click fills the whole row of that shelf. Right-click erases."; break;
                case Tool.Erase: hint = "Erase: click or drag over slots to empty them."; break;
                case Tool.Frozen: hint = "Frozen: click an item to freeze / unfreeze it. Frozen items never move: the other two copies must be brought to it."; break;
                case Tool.Mystery: hint = "Mystery: click an item to turn it into a stone sculpture (only its outline shows) until a match happens on its shelf or next to it."; break;
                case Tool.Pinata: hint = "Piñata: click a slot to place a piñata of the chosen colour (palette panel) with the selected item inside (or nothing). Three of a colour side by side burst and drop their items. Right-click erases."; break;
                case Tool.Egg: hint = "Dragon egg: click a slot to place an egg. Eggs are sorted like items: three side by side hatch. Right-click erases."; break;
                case Tool.MovingRow: hint = "+ Moving row: click a height to add 5 moving shelves (like level 11). Only the middle three are on screen; the others wait in the pipes and slide in. Set direction and speed in the right panel."; break;
                default: hint = "Click on empty space to add a shelf (it snaps to the game grid). Click an existing shelf to select it."; break;
            }
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(string.IsNullOrEmpty(status) ? hint : status + "      " + hint, wrapLabel);
            EditorGUILayout.EndHorizontal();
        }

        // ================================================================== left column
        void DrawLeft()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(285));
            leftScroll = EditorGUILayout.BeginScrollView(leftScroll);

            showHelp = EditorGUILayout.Foldout(showHelp, "How to use", true);
            if (showHelp)
                EditorGUILayout.HelpBox(
                    "1. Shape the cabinet with the + Shelf tools (or start from New).\n" +
                    "2. Set rows (+ Row / − Row next to the row tabs), lock, shutter, cargo door or moving on a shelf.\n" +
                    "3. Paint items row by row (1-9 switch rows), or Auto-fill with a recipe.\n" +
                    "4. Fix the warnings, then Simulate: aim for the band of the tier.\n" +
                    "5. ▶ Play level to try it. Save keeps it as \"Level N\" and the game's level select lists it.", MessageType.None);

            // level settings
            GUILayout.Label("Level", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            string name = EditorGUILayout.TextField("Name", level.name);
            int tier = Array.IndexOf(TierTags, level.difficultyTag >= 20 ? 20 : level.difficultyTag >= 10 ? 10 : 0);
            int newTier = EditorGUILayout.Popup("Tier", tier, TierNames);
            int time = EditorGUILayout.IntField("Time (s)", level.timeLimit);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                level.name = name;
                level.difficultyTag = TierTags[newTier];
                level.timeLimit = Mathf.Max(10, time);
                tier = newTier;
            }
            var counts = level.TypeCounts();
            int items = ItemsToClear(counts);
            float spi = items > 0 ? (float)level.timeLimit / items : 0f;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(string.Format("{0:0.00} s per item", spi), EditorStyles.miniLabel);
            int suggested = Mathf.RoundToInt(items * SuggestedSecondsPerItem[tier] / 5f) * 5;
            if (items > 0 && GUILayout.Button("Suggest " + suggested + " s", EditorStyles.miniButton, GUILayout.Width(95)))
            {
                BeginEdit();
                level.timeLimit = Mathf.Max(10, suggested);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawStats(counts, tier);
            GUILayout.Space(6);
            DrawWarnings(counts);
            GUILayout.Space(6);
            DrawAutoFill(tier);
            GUILayout.Space(6);
            DrawSimulation(tier);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        /// <summary>Everything that has to be matched: items, eggs, piñatas and what is inside them, generator items.</summary>
        int ItemsToClear(Dictionary<string, int> counts)
        {
            return counts.Values.Sum() + level.PinataCounts().Values.Sum() + level.GeneratorItemCount;
        }

        void DrawStats(Dictionary<string, int> counts, int tier)
        {
            GUILayout.Label("Board", EditorStyles.boldLabel);
            int items = ItemsToClear(counts);
            int singles = level.shelves.Count(s => s.width == 1);
            int wide = level.shelves.Count(s => s.width > 3);
            int maxRows = level.shelves.Count == 0 ? 0 : level.shelves.Max(s => s.layers.Count);
            int frontSlots = 0, frontEmpty = 0, frozen = 0, mystery = 0;
            foreach (var s in level.shelves)
            {
                foreach (var l in s.layers)
                    foreach (var c in l)
                    {
                        if (c.IsItem && c.frozen) frozen++;
                        if (c.IsItem && c.mystery) mystery++;
                    }
                if (s.lockCount > 0) continue;
                var front = s.layers.FirstOrDefault(l => l.Any(c => c.IsItem)) ?? s.layers[0];
                frontSlots += s.width;
                frontEmpty += front.Count(c => !c.IsItem);
            }
            int twins = counts.Keys.GroupBy(LevelGenerator.Model).Where(g => g.Count() > 1).Sum(g => g.Count());
            int allFront = level.shelves.Sum(s => s.width);
            int kinds = ObstacleKinds();

            Row("Shelves", level.shelves.Count + (singles > 0 ? "  (" + singles + " single)" : "") + (wide > 0 ? "  (" + wide + " wide)" : ""),
                RealMedians[tier, 2].ToString("0") + (RealMedians[tier, 6] > 0 ? " (" + RealMedians[tier, 6] + "s)" : ""));
            Row("Items", items.ToString(), RealMedians[tier, 0].ToString("0"));
            Row("Item types", counts.Count.ToString(), RealMedians[tier, 1].ToString("0"));
            Row("Seconds / item", items > 0 ? ((float)level.timeLimit / items).ToString("0.00") : "-", RealMedians[tier, 3].ToString("0.00"));
            Row("Front slots", allFront.ToString(), RealMedians[tier, 4].ToString("0"));
            Row("Items / front slot", allFront > 0 ? ((float)items / allFront).ToString("0.00") : "-", RealMedians[tier, 5].ToString("0.00"));
            Row("Obstacle kinds", kinds.ToString(), RealMedians[tier, 7].ToString("0"));
            Row("Rows (max)", maxRows.ToString(), "");
            Row("Front room", frontSlots > 0 ? frontEmpty + " / " + frontSlots + " empty" : "-", "");
            Row("Colour twins", twins + " types", "");
            Row("Obstacles", string.Format("{0} lock, {1} frozen, {2} mystery, {3} shutter, {4} cargo, {5} moving",
                level.shelves.Count(s => s.lockCount > 0), frozen, mystery,
                level.shelves.Count(s => s.shutter), level.shelves.Count(s => s.cargo), level.shelves.Count(s => s.moving)), "");
            int pinatas = level.PinataCounts().Values.Sum();
            int eggs = level.shelves.Sum(s => s.layers.Sum(l => l.Count(c => c.IsEgg)));
            Row("", string.Format("{0} rotating door, {1} iris, {2} fuse box, {3} piñata, {4} egg",
                level.shelves.Count(s => s.ferris), level.shelves.Count(s => s.iris), level.shelves.Count(s => s.fuseItems.Count > 0), pinatas, eggs), "");
            if (level.generators.Count > 0)
                Row("Generator", level.GeneratorItemCount + " items fill the G slots and the rows behind (original level)", "");
            GUILayout.Label("Right column: real Sort Express median for this tier (all 2150 levels).", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(TierTargets[tier], MessageType.None);
        }

        /// <summary>How many different obstacle kinds the level uses (as counted in Tools/tier_stats.py).</summary>
        int ObstacleKinds()
        {
            bool frozen = false, mystery = false, pinata = false, egg = false;
            foreach (var s in level.shelves)
                foreach (var l in s.layers)
                    foreach (var c in l)
                    {
                        if (c == null) continue;
                        frozen |= c.IsItem && c.frozen;
                        mystery |= c.IsItem && c.mystery;
                        pinata |= c.IsPinata;
                        egg |= c.IsEgg;
                    }
            var flags = new[]
            {
                level.shelves.Any(s => s.lockCount > 0), level.shelves.Any(s => s.moving), frozen,
                level.shelves.Any(s => s.cargo), level.shelves.Any(s => s.shutter), mystery, level.shelves.Any(s => s.width > 3),
                level.shelves.Any(s => s.ferris), pinata, level.shelves.Any(s => s.fuseItems.Count > 0), egg, level.shelves.Any(s => s.iris)
            };
            return flags.Count(f => f);
        }

        static void Row(string label, string value, string real)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.Width(88));
            GUILayout.Label(value, EditorStyles.miniBoldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(real)) GUILayout.Label(real, EditorStyles.centeredGreyMiniLabel, GUILayout.Width(56));
            EditorGUILayout.EndHorizontal();
        }

        List<string> Warnings(Dictionary<string, int> counts)
        {
            var w = new List<string>();
            counts = new Dictionary<string, int>(counts);
            foreach (var g in level.generators)
                foreach (var wave in g.waves)
                    foreach (var e in wave.entries)
                    {
                        string id = e.Key.IsItem || e.Key.IsEgg ? e.Key.iid : null;
                        if (id != null) { int n; counts.TryGetValue(id, out n); counts[id] = n + e.Value; }
                        if (e.Key.IsPinata && !string.IsNullOrEmpty(e.Key.pinataContent))
                        { int n; counts.TryGetValue(e.Key.pinataContent, out n); counts[e.Key.pinataContent] = n + e.Value; }
                    }
            if (counts.Count == 0 && level.PinataCounts().Count == 0) w.Add("No items yet.");
            foreach (var kv in counts.OrderBy(k => k.Key))
                if (kv.Value % 3 != 0) w.Add(Short(kv.Key) + ": " + kv.Value + " (needs a multiple of 3)");
            foreach (var kv in level.PinataCounts().OrderBy(k => k.Key))
                if (kv.Value % 3 != 0) w.Add("Piñata colour " + PinataColors[Mathf.Clamp(kv.Key, 0, 5)] + ": " + kv.Value + " (needs a multiple of 3)");
            for (int si = 0; si < level.shelves.Count; si++)
                foreach (var f in level.shelves[si].fuseItems)
                    if (!counts.ContainsKey(f)) w.Add("Fuse box on shelf " + (si + 1) + " waits for " + Short(f) + ", which is not in the level.");
            // frozen items whose partners can never reach them
            for (int si = 0; si < level.shelves.Count; si++)
            {
                var s = level.shelves[si];
                for (int li = 0; li < s.layers.Count; li++)
                    for (int k = 0; k < s.width; k++)
                    {
                        var c = s.layers[li][k];
                        if (!c.IsItem || !c.frozen) continue;
                        int behind = 0;
                        for (int lj = li + 1; lj < s.layers.Count; lj++) behind += s.layers[lj].Count(x => x.IsItem && x.iid == c.iid);
                        if (behind > 0) w.Add("Frozen " + Short(c.iid) + " on shelf " + (si + 1) + ": a copy is behind it on the same shelf (unsolvable).");
                        if (s.width < 3) w.Add("Frozen " + Short(c.iid) + " is on a single shelf: it can never be matched.");
                        int otherFrozen = 0;
                        foreach (var t in level.shelves)
                            foreach (var l in t.layers)
                                foreach (var x in l)
                                    if (x != c && x.IsItem && x.frozen && x.iid == c.iid) otherFrozen++;
                        if (otherFrozen > 0 && counts[c.iid] <= 3) w.Add("Two frozen " + Short(c.iid) + " items on different shelves (unsolvable).");
                    }
                if (s.shutter && s.ItemCount == 0) w.Add("Shelf " + (si + 1) + ": shutter without items closes at once.");
            }
            int matches = counts.Values.Sum() / 3;
            foreach (var s in level.shelves)
                if (s.lockCount > 0 && s.lockCount > matches - s.ItemCount / 3)
                    w.Add("A lock needs " + s.lockCount + " matches but only " + (matches - s.ItemCount / 3) + " are possible outside it.");
            for (int a = 0; a < level.shelves.Count; a++)
                for (int b = a + 1; b < level.shelves.Count; b++)
                    if (Overlaps(level.shelves[a], level.shelves[b])) w.Add("Shelves " + (a + 1) + " and " + (b + 1) + " overlap.");
            return w;
        }

        void DrawWarnings(Dictionary<string, int> counts)
        {
            var w = Warnings(counts);
            GUILayout.Label("Checks", EditorStyles.boldLabel);
            if (w.Count == 0) EditorGUILayout.HelpBox("All checks passed.", MessageType.Info);
            else EditorGUILayout.HelpBox(string.Join("\n", w.Take(12).ToArray()) + (w.Count > 12 ? "\n… " + (w.Count - 12) + " more" : ""), MessageType.Warning);
        }

        static string Short(string id)
        {
            return id != null && id.StartsWith("item_") ? id.Substring(5) : id;
        }

        void DrawAutoFill(int tier)
        {
            showFill = EditorGUILayout.Foldout(showFill, "Auto-fill (keeps the shelves, deals the items)", true);
            if (!showFill) return;

            // rows filled by hand: the front rows that hold items while every row behind them is still empty
            int handRows = HandFilledRows();
            if (!keepTouched && handRows > 0 && recipe.KeepRows == 0) recipe.KeepRows = handRows;
            int maxRowsAll = level.shelves.Count == 0 ? 1 : level.shelves.Max(s => s.layers.Count);
            var keepNames = new string[maxRowsAll];
            keepNames[0] = "nothing (replace every item)";
            for (int i = 1; i < maxRowsAll; i++) keepNames[i] = i == 1 ? "row 1 (front)" : "rows 1–" + i;
            int keepNow = EditorGUILayout.Popup(new GUIContent("Keep my items in",
                    "Rows you filled by hand stay exactly as they are. Auto-fill completes their types to threes in the rows " +
                    "behind and deals new types only there. 'nothing' replaces every item."),
                Mathf.Clamp(recipe.KeepRows, 0, maxRowsAll - 1), keepNames.Select(n => new GUIContent(n)).ToArray());
            if (keepNow != recipe.KeepRows) { recipe.KeepRows = keepNow; keepTouched = true; }
            var kept = LevelGenerator.KeptCounts(level, recipe.KeepRows);
            int completions = LevelGenerator.Completions(kept);
            if (recipe.KeepRows > 0)
                GUILayout.Label("Kept: " + kept.Values.Sum() + " items, " + kept.Count + " types" +
                                (completions > 0 ? "  ·  +" + completions + " copies complete them to threes" : ""), EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Normal", EditorStyles.miniButtonLeft)) Preset(0);
            if (GUILayout.Button("Hard", EditorStyles.miniButtonMid)) Preset(1);
            if (GUILayout.Button("Super Hard", EditorStyles.miniButtonRight)) Preset(2);
            EditorGUILayout.EndHorizontal();

            int cap = LevelGenerator.Capacity(level, recipe);
            EditorGUILayout.BeginHorizontal();
            recipe.Types = EditorGUILayout.IntSlider(new GUIContent("Item types", "Types in the whole level, the kept ones included."),
                recipe.Types, 1, Mathf.Max(3, Math.Min(120, catalog.Count)));
            if (GUILayout.Button("max", EditorStyles.miniButton, GUILayout.Width(34))) recipe.Types = Math.Max(1, LevelGenerator.MaxTypes(level, recipe));
            EditorGUILayout.EndHorizontal();
            int toDeal = Math.Max(0, recipe.Types - kept.Count) * 3 + completions;
            GUILayout.Label(toDeal + " items to deal" + (recipe.KeepRows > 0 ? " behind the kept rows" : "") + ", room for " + cap + " with these fill ratios",
                toDeal > cap ? EditorStyles.boldLabel : EditorStyles.miniLabel);
            GUI.enabled = recipe.KeepRows == 0;
            recipe.FrontFill = EditorGUILayout.Slider(new GUIContent("Front row fill", "Share of the front row holding items (rest = breathing room). Real game: ~0.80 normal, ~0.68 super hard."), (float)recipe.FrontFill, 0.3f, 1f);
            GUI.enabled = true;
            recipe.BackFill = EditorGUILayout.Slider("Back rows fill", (float)recipe.BackFill, 0.3f, 1f);
            GUILayout.Label("Where the 3 copies of a type go", EditorStyles.miniBoldLabel);
            recipe.SameLayer = EditorGUILayout.Slider("  same row", (float)recipe.SameLayer, 0f, 1f);
            recipe.Adjacent = EditorGUILayout.Slider("  next row", (float)recipe.Adjacent, 0f, 1f);
            recipe.Random = EditorGUILayout.Slider("  anywhere", (float)recipe.Random, 0f, 1f);
            recipe.AllowPairs = EditorGUILayout.Toggle(new GUIContent("Pairs on one shelf", "Two copies may start on the same shelf (easier)."), recipe.AllowPairs);
            recipe.LookAlike = EditorGUILayout.Slider(new GUIContent("Colour twins", "Share of types that have a colour twin on the board (camera red + camera blue)."), (float)recipe.LookAlike, 0f, 1f);
            recipe.Frozen = EditorGUILayout.IntSlider("Frozen items", recipe.Frozen, 0, 12);
            recipe.Mystery = EditorGUILayout.IntSlider("Mystery items", recipe.Mystery, 0, 24);
            EditorGUILayout.BeginHorizontal();
            recipe.Seed = EditorGUILayout.IntField("Seed", recipe.Seed);
            if (GUILayout.Button("rnd", EditorStyles.miniButton, GUILayout.Width(34))) recipe.Seed = UnityEngine.Random.Range(1, 99999);
            EditorGUILayout.EndHorizontal();

            GUI.enabled = worker == null && catalog.Count > 0;
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Fill"))
            {
                if (recipe.KeepRows > 0 || !level.shelves.Any(sh => sh.layers.Any(l => l.Any(c => c != null && c.IsFilled))) ||
                    EditorUtility.DisplayDialog("Auto-fill", "Replace every item on the board? (Ctrl+Z brings them back.)\n\n" +
                                                "To keep the rows you filled by hand, pick them in 'Keep my items in'.", "Replace all", "Cancel"))
                    Fill(recipe.Seed, true);
                Bail();
            }
            tuneSeeds = EditorGUILayout.IntField(tuneSeeds, GUILayout.Width(30));
            if (GUILayout.Button(new GUIContent("Fill & tune", "Tries this many seeds, simulates each and keeps the one closest to the middle of the tier's band.")))
            {
                StartTune(tier);
                Bail();
            }
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            if (catalog.Count == 0) EditorGUILayout.HelpBox("No item_*.png textures found in the project.", MessageType.Error);
            if (!string.IsNullOrEmpty(tuneError)) EditorGUILayout.HelpBox(tuneError, MessageType.Warning);
            if (tuneResults != null && tuneResults.Count > 0)
            {
                GUILayout.Label("Seeds tried (click to use):", EditorStyles.miniBoldLabel);
                foreach (var kv in tuneResults)
                {
                    string txt = "seed " + kv.Key + "   " + (kv.Value.Solvable ? Pct(kv.Value.WinRate) + " win" : "unsolvable") + (kv.Key == tuneBest ? "   ← best" : "");
                    if (GUILayout.Button(txt, EditorStyles.miniButton)) { Fill(kv.Key, true); Bail(); }
                }
            }
        }

        void Preset(int tier)
        {
            var r = recipe;
            switch (tier)
            {
                case 0: r.FrontFill = 0.78; r.BackFill = 0.83; r.SameLayer = 0.75; r.Adjacent = 0.22; r.Random = 0.03; r.AllowPairs = true; r.LookAlike = 0; r.Frozen = 0; r.Mystery = 0; break;
                case 1: r.FrontFill = 0.68; r.BackFill = 0.78; r.SameLayer = 0.60; r.Adjacent = 0.30; r.Random = 0.10; r.AllowPairs = false; r.LookAlike = 0.55; r.Frozen = 2; r.Mystery = 0; break;
                default: r.FrontFill = 0.66; r.BackFill = 0.75; r.SameLayer = 0.50; r.Adjacent = 0.38; r.Random = 0.12; r.AllowPairs = false; r.LookAlike = 0.9; r.Frozen = 2; r.Mystery = 6; break;
            }
            r.Types = Math.Max(1, LevelGenerator.MaxTypes(level, r));
        }

        bool keepTouched;

        /// <summary>How many front rows hold items while every row behind them is empty (rows filled by hand before an
        /// auto-fill); 0 when the board is empty or already filled to the back.</summary>
        int HandFilledRows()
        {
            int maxRows = level.shelves.Count == 0 ? 0 : level.shelves.Max(s => s.layers.Count);
            int last = -1;
            for (int li = 0; li < maxRows; li++)
                if (level.shelves.Any(s => li < s.layers.Count && s.layers[li].Any(c => c != null && c.IsFilled))) last = li;
            if (last < 0 || last >= maxRows - 1) return 0;
            return last + 1;
        }

        void Fill(int seed, bool record)
        {
            var r = recipe.Clone();
            r.Seed = seed;
            var copy = level.Clone();
            var types = LevelGenerator.PickNewTypes(catalog, copy, r, new System.Random(seed));
            string err = LevelGenerator.Fill(copy, r, types);
            if (err != null)
            {
                status = err;
                EditorUtility.DisplayDialog("Auto-fill", err, "OK");
                return;
            }
            if (record) BeginEdit();
            level = copy;
            recipe.Seed = seed;
            status = "Filled with seed " + seed + ": " + types.Count + " new types" + (r.KeepRows > 0 ? " behind your " + (r.KeepRows == 1 ? "front row" : r.KeepRows + " rows") : "") +
                     ". Another seed gives another deal.";
        }

        void DrawSimulation(int tier)
        {
            GUILayout.Label("Simulation", EditorStyles.boldLabel);
            simRuns = EditorGUILayout.IntSlider("Players", simRuns, 50, 1000);
            if (worker != null)
            {
                var r = EditorGUILayout.GetControlRect(false, 18);
                EditorGUI.ProgressBar(r, progress, workerLabel + "  " + Mathf.RoundToInt(progress * 100) + "%");
                if (GUILayout.Button("Cancel")) cancel = true;
                return;
            }
            if (GUILayout.Button("Simulate (" + simRuns + " players)", GUILayout.Height(26))) { StartSimulation(); Bail(); }
            if (report == null)
            {
                GUILayout.Label("Band for this tier: " + Pct(BandLo[tier]) + " - " + Pct(BandHi[tier]) + " win (no boosters).", EditorStyles.miniLabel);
                return;
            }
            if (reportVersion != version) EditorGUILayout.HelpBox("The level changed since this simulation.", MessageType.None);
            if (!report.Solvable)
            {
                EditorGUILayout.HelpBox("No solution found. Check the warnings, add breathing room or reduce the obstacles.", MessageType.Error);
            }
            bool inBand = report.WinRate >= BandLo[tier] - 1e-6 && report.WinRate <= BandHi[tier] + 1e-6;
            var old = GUI.color;
            GUI.color = !report.Solvable ? new Color(1f, 0.5f, 0.5f) : inBand ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.85f, 0.4f);
            GUILayout.Label(Pct(report.WinRate) + " win", bigNumber);
            GUI.color = old;
            GUILayout.Label("Target for " + TierNames[tier] + ": " + Pct(BandLo[tier]) + " - " + Pct(BandHi[tier]) +
                            (inBand ? "  ✔ in band" : report.WinRate > BandHi[tier] ? "  → too easy" : "  → too hard"), EditorStyles.miniBoldLabel);
            Row("Solution", report.Solvable ? (report.SolutionMoves > 0 ? report.SolutionMoves + " moves" : "found") : "none", "");
            Row("Lost to time", Pct(report.Timeout), "");
            Row("Lost: no moves", Pct(report.Stuck), "");
            Row("Win, 2.5x timer", Pct(report.WinRelaxedTimer), "");
            Row("Avg win", report.AvgTimeWin.ToString("0") + " s, " + report.AvgMovesWin.ToString("0") + " moves", "");
            EditorGUILayout.HelpBox("Easier: more front room, fewer types, triples on the same row, fewer obstacles, more time.\n" +
                                    "Harder: the opposite. Obstacle counts are the steepest levers (one more lock step can halve the win rate).", MessageType.None);
        }

        static string Pct(double v)
        {
            return Mathf.RoundToInt((float)v * 100f) + "%";
        }

        // ================================================================== background work
        void StartSimulation()
        {
            if (worker != null) return;
            var snapshot = level.Clone();
            int runs = simRuns;
            int v = version;
            cancel = false;
            progress = 0f;
            workerLabel = "Simulating";
            SimReport result = null;
            worker = new Thread(() =>
            {
                try
                {
                    result = new Simulator().Run(snapshot, new SimSettings { Runs = runs, Seed = 7 }, p => { progress = p; return !cancel; });
                }
                catch (Exception e) { Debug.LogException(e); }
            });
            pendingDone = () =>
            {
                if (result == null || result.Cancelled) { status = "Simulation cancelled."; return; }
                report = result;
                reportVersion = v;
                status = "Simulated " + runs + " players.";
            };
            worker.IsBackground = true;
            worker.Start();
        }

        void StartTune(int tier)
        {
            if (worker != null) return;
            var layout = level.Clone();
            var r0 = recipe.Clone();
            var cat = catalog.ToList();
            int n = Mathf.Clamp(tuneSeeds, 1, 60);
            int start = recipe.Seed;
            float mid = (BandLo[tier] + BandHi[tier]) * 0.5f;
            cancel = false;
            progress = 0f;
            workerLabel = "Trying " + n + " seeds";
            var results = new List<KeyValuePair<int, SimReport>>();
            string error = null;
            worker = new Thread(() =>
            {
                try
                {
                    for (int i = 0; i < n && !cancel; i++)
                    {
                        int seed = start + i;
                        var lv = layout.Clone();
                        var r = r0.Clone();
                        r.Seed = seed;
                        var types = LevelGenerator.PickNewTypes(cat, lv, r, new System.Random(seed));
                        string err = LevelGenerator.Fill(lv, r, types);
                        if (err != null) { error = err; break; }
                        int idx = i;
                        var rep = new Simulator().Run(lv, new SimSettings { Runs = 150, Seed = 7 },
                            p => { progress = (idx + p) / n; return !cancel; });
                        if (rep.Cancelled) break;
                        lock (results) results.Add(new KeyValuePair<int, SimReport>(seed, rep));
                    }
                }
                catch (Exception e) { Debug.LogException(e); }
            });
            pendingDone = () =>
            {
                tuneError = error;
                tuneResults = results;
                tuneBest = -1;
                double best = double.MaxValue;
                foreach (var kv in results)
                {
                    if (!kv.Value.Solvable) continue;
                    double d = Math.Abs(kv.Value.WinRate - mid);
                    if (d < best) { best = d; tuneBest = kv.Key; }
                }
                if (tuneBest >= 0)
                {
                    Fill(tuneBest, true);
                    status = "Kept seed " + tuneBest + " (closest to " + Pct(mid) + "). Press Simulate for a precise number.";
                }
                else if (error == null) status = "No solvable seed found: give the board more room or fewer obstacles.";
            };
            worker.IsBackground = true;
            worker.Start();
        }

        Action pendingDone;

        void OnWorkerDone()
        {
            var a = pendingDone;
            pendingDone = null;
            if (a != null) a();
            Repaint();
        }

        // ================================================================== play
        void PlayLevel()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }
            EnsureIcons(level);
            if (!File.Exists(SortexSetup.ScenePath)) SortexSetup.Setup(false);
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != SortexSetup.ScenePath)
            {
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(SortexSetup.ScenePath);
            }
            SessionState.SetString(TestLevelKey, level.ToJson(false));
            EditorApplication.isPlaying = true;
        }

        // ================================================================== right column
        void DrawRight()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(262));
            rightScroll = EditorGUILayout.BeginScrollView(rightScroll, GUILayout.Height(Mathf.Min(330, position.height * 0.45f)));
            DrawShelfInspector();
            EditorGUILayout.EndScrollView();
            DrawPalette();
            EditorGUILayout.EndVertical();
        }

        void DrawShelfInspector()
        {
            GUILayout.Label("Shelf", EditorStyles.boldLabel);
            if (selected < 0 || selected >= level.shelves.Count)
            {
                GUILayout.Label("Select a shelf on the board (Select tool).", EditorStyles.miniLabel);
                if (level.shelves.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("+ row on all")) { BeginEdit(); foreach (var s in level.shelves) s.SetLayerCount(s.layers.Count + 1); Bail(); }
                    if (GUILayout.Button("- row on all")) { BeginEdit(); foreach (var s in level.shelves) s.SetLayerCount(s.layers.Count - 1); Bail(); }
                    EditorGUILayout.EndHorizontal();
                    if (GUILayout.Button("Clear all items"))
                    {
                        if (EditorUtility.DisplayDialog("Sortex", "Remove every item?", "Clear", "Cancel"))
                        {
                            BeginEdit();
                            foreach (var s in level.shelves) foreach (var l in s.layers) foreach (var c in l) c.Clear();
                        }
                        Bail();
                    }
                }
                return;
            }
            var sh = level.shelves[selected];
            GUILayout.Label("Shelf " + (selected + 1) + "   " + sh.ItemCount + " items", EditorStyles.miniBoldLabel);
            EditorGUI.BeginChangeCheck();
            float x = EditorGUILayout.FloatField("X", sh.x);
            float y = EditorGUILayout.FloatField("Y", sh.y);
            int widthIndex = sh.width == 1 ? 0 : sh.width == 3 ? 1 : Mathf.Clamp(sh.width - 2, 2, 5);
            int newWidthIndex = EditorGUILayout.Popup("Slots", widthIndex, new[] { "1 (single)", "3", "4 (wide)", "5 (wide)", "6 (wide)", "7 (wide)" });
            int rows = EditorGUILayout.IntSlider("Rows", sh.layers.Count, 1, 12);
            int lockCount = EditorGUILayout.IntSlider(new GUIContent("Lock (matches)", "0 = no lock. The shelf opens after this many matches anywhere (level 4+)."), sh.lockCount, 0, 30);
            bool shutter = EditorGUILayout.Toggle(new GUIContent("Shutter", "Closes for good once the shelf is empty (level 41+)."), sh.shutter);
            bool cargo = EditorGUILayout.Toggle(new GUIContent("Cargo door", "Rows behind the front row are hidden (level 31+)."), sh.cargo);
            bool moving = EditorGUILayout.Toggle(new GUIContent("Moving shelf", "Slides sideways with the other moving shelves of its row and loops through pipes at the screen edges (level 11+)."), sh.moving);
            int dir = sh.moveDir;
            float speed = sh.moveSpeed;
            if (sh.moving)
            {
                dir = EditorGUILayout.Popup("  Direction", sh.moveDir > 0 ? 0 : 1, new[] { "→ Right", "← Left" }) == 0 ? 1 : -1;
                speed = EditorGUILayout.Slider(new GUIContent("  Speed", "World units per second. The real game mostly uses 1 (early levels 0.25 - 0.3)."), sh.moveSpeed, 0.1f, 3f);
            }
            bool ferris = EditorGUILayout.Toggle(new GUIContent("Rotating door", "Each row is a stage behind a door; the next one turns in when the current one is cleared (level 81+)."), sh.ferris);
            bool iris = EditorGUILayout.Toggle(new GUIContent("Iris", "Opens and closes after every move; closed it cannot be used (level 211+)."), sh.iris);
            bool irisClosed = sh.irisClosed;
            if (sh.iris) irisClosed = EditorGUILayout.Toggle("  Starts closed", sh.irisClosed);
            if (EditorGUI.EndChangeCheck())
            {
                BeginEdit();
                sh.ferris = ferris;
                sh.iris = iris;
                sh.irisClosed = iris && irisClosed;
                sh.x = x;
                sh.y = y;
                int w = newWidthIndex == 0 ? 1 : newWidthIndex == 1 ? 3 : newWidthIndex + 2;
                if (w != sh.width) sh.SetWidth(w);
                sh.SetLayerCount(rows);
                sh.lockCount = lockCount;
                sh.shutter = shutter;
                sh.cargo = cargo;
                if (moving && !sh.moving)
                {
                    // join the row's motion if other shelves at this height already move
                    var mate = level.shelves.FirstOrDefault(o => o != sh && o.moving && Mathf.Abs(o.y - sh.y) < 0.05f);
                    if (mate != null) { dir = mate.moveDir; speed = mate.moveSpeed; }
                }
                sh.moving = moving;
                // direction and speed belong to the whole moving row
                foreach (var o in level.shelves)
                    if (o == sh || o.moving && sh.moving && Mathf.Abs(o.y - sh.y) < 0.05f)
                    {
                        o.moveDir = dir;
                        o.moveSpeed = Mathf.Round(speed * 100f) / 100f;
                    }
            }
            // fuse box: the items that have to be matched before the shelf opens
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Fuse box", "The shelf stays closed until each of these items has been matched once (level 131+)."), GUILayout.Width(70));
            foreach (var f in sh.fuseItems)
            {
                var ft = Icon(f);
                GUILayout.Box(ft != null ? new GUIContent(ft, Short(f)) : new GUIContent(Short(f)), GUILayout.Width(26), GUILayout.Height(26));
            }
            GUI.enabled = !string.IsNullOrEmpty(brush) && sh.fuseItems.Count < 3 && !sh.fuseItems.Contains(brush ?? "");
            if (GUILayout.Button(new GUIContent("+", "Add the item selected in the palette"), EditorStyles.miniButton, GUILayout.Width(22))) { BeginEdit(); sh.fuseItems.Add(brush); Bail(); }
            GUI.enabled = sh.fuseItems.Count > 0;
            if (GUILayout.Button(new GUIContent("×", "Remove the fuse box"), EditorStyles.miniButton, GUILayout.Width(22))) { BeginEdit(); sh.fuseItems.Clear(); Bail(); }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear items")) { BeginEdit(); foreach (var l in sh.layers) foreach (var c in l) c.Clear(); Bail(); }
            if (GUILayout.Button("Duplicate")) { DuplicateShelf(selected); Bail(); }
            if (GUILayout.Button("Delete")) { DeleteShelf(selected); Bail(); }
            EditorGUILayout.EndHorizontal();
        }

        void DrawPalette()
        {
            GUILayout.Label("Items (" + catalog.Count + ")", EditorStyles.boldLabel);
            if (tool == Tool.Pinata)
            {
                pinataColor = EditorGUILayout.Popup("Piñata colour", pinataColor, PinataColors);
                GUILayout.Label("Inside the piñata: " + (string.IsNullOrEmpty(brush) ? "nothing (pick an item below)" : Short(brush)), EditorStyles.miniLabel);
            }
            EditorGUILayout.BeginHorizontal();
            var tex = Icon(brush);
            GUILayout.Box(tex != null ? new GUIContent(tex) : new GUIContent("none"), GUILayout.Width(44), GUILayout.Height(44));
            EditorGUILayout.BeginVertical();
            GUILayout.Label(brush != null ? Short(brush) : "Pick an item to paint", EditorStyles.miniBoldLabel);
            search = EditorGUILayout.TextField(search, EditorStyles.toolbarSearchField);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();

            var counts = level.TypeCounts();
            var list = string.IsNullOrEmpty(search)
                ? catalog
                : catalog.Where(c => c.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            // used items first, so the counts that need fixing are easy to find
            list = list.OrderBy(c => counts.ContainsKey(c) ? 0 : 1).ThenBy(c => c, StringComparer.Ordinal).ToList();

            const float cell = 48f;
            var area = GUILayoutUtility.GetRect(10, 10, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            int cols = Mathf.Max(1, Mathf.FloorToInt((area.width - 16) / cell));
            int rowsCount = Mathf.CeilToInt(list.Count / (float)cols);
            var content = new Rect(0, 0, cols * cell, rowsCount * cell);
            paletteScroll = GUI.BeginScrollView(area, paletteScroll, content);
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(paletteScroll.y / cell) - 1);
            int lastRow = Mathf.Min(rowsCount - 1, Mathf.CeilToInt((paletteScroll.y + area.height) / cell) + 1);
            for (int r = firstRow; r <= lastRow; r++)
                for (int c = 0; c < cols; c++)
                {
                    int i = r * cols + c;
                    if (i >= list.Count) break;
                    string id = list[i];
                    var rect = new Rect(c * cell + 2, r * cell + 2, cell - 4, cell - 4);
                    EditorGUI.DrawRect(rect, id == brush ? new Color(0.93f, 0.69f, 0.14f, 0.9f) : new Color(0.12f, 0.17f, 0.28f, 1f));
                    var t = Icon(id);
                    if (t != null) GUI.DrawTexture(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), t, ScaleMode.ScaleToFit, true);
                    int n;
                    if (counts.TryGetValue(id, out n))
                    {
                        var b = new Rect(rect.xMax - 18, rect.yMax - 13, 18, 13);
                        EditorGUI.DrawRect(b, n % 3 == 0 ? new Color(0.2f, 0.6f, 0.3f, 0.95f) : new Color(0.85f, 0.2f, 0.2f, 0.95f));
                        GUI.Label(b, n.ToString(), badge);
                    }
                    if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                    {
                        brush = id;
                        if (tool != Tool.Paint) tool = Tool.Paint;
                        Event.current.Use();
                    }
                    if (rect.Contains(Event.current.mousePosition)) GUI.Label(rect, new GUIContent("", Short(id)));
                }
            GUI.EndScrollView();
        }

        // ================================================================== board canvas
        void DrawCenter()
        {
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            var picked = tool;
            for (int i = 0; i < ToolNames.Length; i++)
            {
                bool was = tool == (Tool)i;
                bool now = GUILayout.Toggle(was, ToolNames[i], EditorStyles.toolbarButton);
                if (now && !was) picked = (Tool)i;
            }
            tool = picked;
            GUILayout.FlexibleSpace();
            GUILayout.Label(new GUIContent("Game screen", "Shows what the game's camera shows for this board: the phone screen, the top bar and the boosters, " +
                "and the dashed area where shelves keep their normal size."), EditorStyles.miniLabel);
            int mode = EditorGUILayout.Popup(ScreenMode, ScreenModeNames, EditorStyles.toolbarPopup, GUILayout.Width(150));
            if (mode != ScreenMode) { ScreenMode = mode; needsFrame = true; }
            if (GUILayout.Button("Frame (F)", EditorStyles.toolbarButton)) needsFrame = true;
            EditorGUILayout.EndHorizontal();

            int maxRows = Mathf.Max(1, level.shelves.Count == 0 ? 1 : level.shelves.Max(s => s.layers.Count));
            row = Mathf.Clamp(row, 0, maxRows - 1);
            var labels = new string[maxRows];
            for (int i = 0; i < maxRows; i++) labels[i] = i == 0 ? "Row 1 (front)" : "Row " + (i + 1);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            row = GUILayout.Toolbar(row, labels, EditorStyles.toolbarButton);
            string who = selected >= 0 && selected < level.shelves.Count ? "shelf " + (selected + 1) : "every shelf";
            if (GUILayout.Button(new GUIContent("+ Row", "Add a row behind the last one on " + who + " (select a shelf first to change only that one)."),
                    EditorStyles.toolbarButton, GUILayout.Width(52))) { ChangeRows(1); Bail(); }
            if (GUILayout.Button(new GUIContent("− Row", "Remove the last row of " + who + "."), EditorStyles.toolbarButton, GUILayout.Width(52))) { ChangeRows(-1); Bail(); }
            EditorGUILayout.EndHorizontal();

            var canvas = GUILayoutUtility.GetRect(200, 200, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (needsFrame && canvas.width > 10) FrameBoard(canvas);
            DrawCanvas(canvas);
            HandleCanvasInput(canvas);
            EditorGUILayout.EndVertical();
        }

        /// <summary>+1 / -1 row on the selected shelf (or on every shelf) and shows the new last row.</summary>
        void ChangeRows(int delta)
        {
            if (level.shelves.Count == 0) return;
            BeginEdit();
            var targets = selected >= 0 && selected < level.shelves.Count ? new List<ShelfData> { level.shelves[selected] } : level.shelves;
            foreach (var s in targets) s.SetLayerCount(Mathf.Clamp(s.layers.Count + delta, 1, 12));
            int last = targets.Max(s => s.layers.Count) - 1;
            row = delta > 0 ? last : Mathf.Min(row, last);
            status = (delta > 0 ? "Added a row to " : "Removed a row from ") + (targets.Count == 1 ? "shelf " + (selected + 1) : "every shelf") +
                     ". Row tabs: 1 is the front, higher numbers are further back.";
        }

        Vector2 ToScreen(Rect canvas, float x, float y)
        {
            return new Vector2(canvas.center.x + pan.x + x * XS * zoom, canvas.center.y + pan.y - y * zoom);
        }

        Vector2 ToWorld(Rect canvas, Vector2 p)
        {
            return new Vector2((p.x - canvas.center.x - pan.x) / (zoom * XS), -(p.y - canvas.center.y - pan.y) / zoom);
        }

        Rect ShelfRect(Rect canvas, ShelfData s)
        {
            float w = s.width * SW;
            var tl = ToScreen(canvas, s.x - w * 0.5f, s.y + 0.5f);
            return new Rect(tl.x, tl.y, w * XS * zoom, zoom);
        }

        void FrameBoard(Rect canvas)
        {
            needsFrame = false;
            if (level.shelves.Count == 0)
            {
                pan = Vector2.zero;
                return;
            }
            float minX = level.shelves.Min(s => s.x - s.width * SW * 0.5f), maxX = level.shelves.Max(s => s.x + s.width * SW * 0.5f);
            float minY = level.shelves.Min(s => s.y - 0.5f), maxY = level.shelves.Max(s => s.y + 0.5f);
            // with the game screen shown, frame that (it is a little larger than the shelves)
            Rect frame;
            if (GameFrame(out frame))
            {
                minX = Mathf.Min(minX, frame.xMin / XS);
                maxX = Mathf.Max(maxX, frame.xMax / XS);
                minY = Mathf.Min(minY, frame.yMin);
                maxY = Mathf.Max(maxY, frame.yMax);
            }
            zoom = Mathf.Clamp(Mathf.Min(canvas.width / ((maxX - minX) * XS + 1f), canvas.height / (maxY - minY + 1f)), 20f, 140f);
            pan = new Vector2(-(minX + maxX) * 0.5f * XS * zoom, (minY + maxY) * 0.5f * zoom);
        }

        static readonly Color Bg = new Color(0.07f, 0.1f, 0.18f);
        static readonly Color Interior = new Color(0.12f, 0.19f, 0.31f);

        Color TierRim()
        {
            if (level.difficultyTag >= 20) return new Color(0.95f, 0.55f, 0.48f);
            if (level.difficultyTag >= 10) return new Color(0.71f, 0.54f, 0.94f);
            return new Color(0.6f, 0.78f, 0.97f);
        }

        void DrawCanvas(Rect canvas)
        {
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(canvas, Bg);
            GUI.BeginClip(canvas);
            var local = new Rect(0, 0, canvas.width, canvas.height);

            // grid: thin lines every slot, stronger every 3 slots / every row
            var w0 = ToWorld(local, Vector2.zero);
            var w1 = ToWorld(local, new Vector2(local.width, local.height));
            for (int i = Mathf.FloorToInt(w0.x / SW) - 1; i <= Mathf.CeilToInt(w1.x / SW) + 1; i++)
            {
                float sx = ToScreen(local, i * SW, 0).x;
                EditorGUI.DrawRect(new Rect(sx, 0, 1, local.height), new Color(1, 1, 1, i % 3 == 0 ? 0.07f : 0.025f));
            }
            for (int j = Mathf.FloorToInt(w1.y) - 1; j <= Mathf.CeilToInt(w0.y) + 1; j++)
            {
                float sy = ToScreen(local, 0, j - 0.5f).y;
                EditorGUI.DrawRect(new Rect(0, sy, local.width, 1), new Color(1, 1, 1, 0.06f));
            }

            DrawGameBackground(local);
            var rim = TierRim();
            for (int si = 0; si < level.shelves.Count; si++)
            {
                var s = level.shelves[si];
                var r = ShelfRect(local, s);
                // the game's own shelf (rendered from BaseShelf.prefab): inside now, rim after the items
                var art = ShelfArtRect(r, s);
                var inside = ShelfTexture(s.width, "back");
                if (inside != null) GUI.DrawTexture(art, inside, ScaleMode.StretchToFill, true);
                else
                {
                    float b = Mathf.Max(2f, zoom * 0.06f);
                    EditorGUI.DrawRect(new Rect(r.x - b, r.y - b, r.width + 2 * b, r.height + 2 * b), rim);
                    EditorGUI.DrawRect(r, Interior);
                }
                bool hasRow = row < s.layers.Count;
                for (int k = 0; k < s.width; k++)
                {
                    var cr = new Rect(r.x + k * SW * XS * zoom, r.y, SW * XS * zoom, r.height);
                    if (k > 0) EditorGUI.DrawRect(new Rect(cr.x, cr.y + 2, 1, cr.height - 4), new Color(1, 1, 1, 0.08f));
                    if (!hasRow)
                    {
                        EditorGUI.DrawRect(new Rect(cr.x + 2, cr.y + 2, cr.width - 4, cr.height - 4), new Color(0, 0, 0, 0.35f));
                        continue;
                    }
                    // the row behind, small and dark
                    if (row + 1 < s.layers.Count)
                    {
                        var behind = s.layers[row + 1][k];
                        var bt = SlotIcon(behind);
                        if (bt != null)
                        {
                            GUI.color = new Color(0.35f, 0.4f, 0.55f, 0.9f);
                            GUI.DrawTexture(new Rect(cr.x + cr.width * 0.52f, cr.y + 3, cr.width * 0.45f, cr.height * 0.45f), bt, ScaleMode.ScaleToFit, true);
                            GUI.color = Color.white;
                        }
                    }
                    var c = s.layers[row][k];
                    if (c.IsGenerated)
                    {
                        DrawBorder(new Rect(cr.x + 4, cr.y + 4, cr.width - 8, cr.height - 8), new Color(1f, 1f, 1f, 0.25f), 1);
                        GUI.Label(new Rect(cr.x, cr.y, cr.width, cr.height), "G", centerLabel);
                        continue;
                    }
                    if (c.IsPinata || c.IsEgg)
                    {
                        var sr = new Rect(cr.x + cr.width * 0.08f, cr.y + cr.height * 0.1f, cr.width * 0.84f, cr.height * 0.84f);
                        var st = SlotIcon(c);
                        if (st != null) GUI.DrawTexture(sr, st, ScaleMode.ScaleToFit, true);
                        if (c.IsPinata && !string.IsNullOrEmpty(c.pinataContent))
                        {
                            var content = Icon(c.pinataContent);
                            var ib = new Rect(sr.xMax - sr.width * 0.42f, sr.yMax - sr.height * 0.42f, sr.width * 0.42f, sr.height * 0.42f);
                            EditorGUI.DrawRect(ib, new Color(0f, 0f, 0f, 0.45f));
                            if (content != null) GUI.DrawTexture(ib, content, ScaleMode.ScaleToFit, true);
                        }
                        continue;
                    }
                    if (!c.IsItem) continue;
                    var stone = c.mystery ? MysteryIcon(c.iid) : null;
                    var t = stone != null ? stone : Icon(c.iid);
                    var ir = new Rect(cr.x + cr.width * 0.08f, cr.y + cr.height * 0.1f, cr.width * 0.84f, cr.height * 0.84f);
                    if (t != null) GUI.DrawTexture(ir, t, ScaleMode.ScaleToFit, true);
                    else GUI.Label(ir, Short(c.iid), wrapLabel);
                    if (c.mystery && stone == null) GUI.Label(ir, "?", centerLabel);
                    if (c.frozen)
                    {
                        EditorGUI.DrawRect(ir, new Color(0.55f, 0.85f, 1f, 0.35f));
                        DrawBorder(ir, new Color(0.8f, 0.95f, 1f, 0.9f), 2);
                    }
                }
                var front = ShelfTexture(s.width, "front");
                if (front != null) GUI.DrawTexture(art, front, ScaleMode.StretchToFill, true);
                if (si == selected) DrawBorder(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(0.93f, 0.69f, 0.14f), 3);
                // shelf badges
                var bx = new Rect(r.x + 2, r.y + 2, 0, 14);
                if (s.lockCount > 0)
                {
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, r.height), new Color(0.75f, 0.78f, 0.85f, 0.28f));
                    bx = Badge(bx, "LOCK " + s.lockCount, new Color(0.85f, 0.55f, 0.1f));
                }
                if (s.shutter) bx = Badge(bx, "SHUTTER", new Color(0.9f, 0.45f, 0.1f));
                if (s.cargo) bx = Badge(bx, "CARGO", new Color(0.45f, 0.25f, 0.8f));
                if (s.moving) bx = Badge(bx, (s.moveDir > 0 ? "→ " : "← ") + s.moveSpeed.ToString("0.##"), new Color(0.1f, 0.6f, 0.6f));
                if (s.ferris) bx = Badge(bx, "DOOR " + s.layers.Count, new Color(0.8f, 0.2f, 0.35f));
                if (s.iris) bx = Badge(bx, s.irisClosed ? "IRIS ●" : "IRIS ○", new Color(0.15f, 0.6f, 0.55f));
                if (s.fuseItems.Count > 0)
                {
                    EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, r.height), new Color(0.2f, 0.35f, 0.8f, 0.3f));
                    bx = Badge(bx, "FUSE", new Color(0.2f, 0.4f, 0.9f));
                    for (int fi = 0; fi < s.fuseItems.Count; fi++)
                    {
                        var ft = Icon(s.fuseItems[fi]);
                        var fr = new Rect(bx.x + fi * 16, r.y + 2, 15, 15);
                        EditorGUI.DrawRect(fr, new Color(1f, 1f, 1f, 0.85f));
                        if (ft != null) GUI.DrawTexture(fr, ft, ScaleMode.ScaleToFit, true);
                    }
                }
                var rowsRect = new Rect(r.xMax - 26, r.yMax - 15, 24, 13);
                EditorGUI.DrawRect(rowsRect, new Color(0, 0, 0, 0.55f));
                GUI.Label(rowsRect, "×" + s.layers.Count, badge);
            }

            DrawScreenEdges(local);
            DrawGameScreen(local);

            // ghost of the moving row about to be added
            if (tool == Tool.MovingRow)
            {
                foreach (var g in MovingRowShelves(Snap(hoverWorld).y, 1))
                {
                    bool blocked = level.shelves.Any(s => Overlaps(s, g));
                    DrawBorder(ShelfRect(local, g), blocked ? new Color(1, 0.3f, 0.3f, 0.8f) : new Color(0.4f, 1f, 0.95f, 0.7f), 2);
                }
            }

            // ghost of the shelf about to be added
            int addWidth = AddWidth();
            if (addWidth > 0)
            {
                var p = Snap(hoverWorld);
                var ghost = ShelfData.Create(p.x, p.y, addWidth, 1);
                var gr = ShelfRect(local, ghost);
                bool blocked = level.shelves.Any(s => Overlaps(s, ghost));
                DrawBorder(gr, blocked ? new Color(1, 0.3f, 0.3f, 0.8f) : new Color(1, 1, 1, 0.6f), 2);
            }
            GUI.EndClip();
        }

        // ================================================================== game screen template
        // What the game's camera shows for this board (SortexGame.FitBoard): the phone screen, the level HUD on it (rendered
        // from the game's own LevelHud), and the dashed area where the shelves keep their normal size. The game centres
        // the board between the top bar and the boosters and zooms out until every shelf fits; a board wider than three
        // 3-slot shelves or taller than the screen's rows is shown smaller.
        static readonly string[] ScreenModeNames = { "Off", "9:16 (web link, desktop)", "9:19.5 (phone)" };
        static readonly float[] ScreenAspects = { 0f, 9f / 16f, 9f / 19.5f };
        const string ScreenModeKey = "Sortex.Editor.ScreenMode";
        const float RefWidth = 4.616601f;            // the game's UI reference width (PrefabScreen)
        const float MarginX = 0.2f, MarginY = 0.3f;  // SortexGame.FitBoard
        static readonly float StandardWidth = 3 * 3 * Board.SlotWidth * XS;   // three 3-slot shelves side by side

        static int ScreenMode
        {
            get { return Mathf.Clamp(EditorPrefs.GetInt(ScreenModeKey, 1), 0, ScreenModeNames.Length - 1); }
            set { EditorPrefs.SetInt(ScreenModeKey, value); }
        }

        /// <summary>The shelves' rectangle in game units (x at half scale), like BoardView.Bounds.</summary>
        bool BoardBounds(out Rect bounds)
        {
            bounds = new Rect();
            if (level == null || level.shelves.Count == 0) return false;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            bool moving = false;
            foreach (var s in level.shelves)
            {
                float hw = s.width * SW * XS * 0.5f;
                if (s.moving) moving = true;
                else
                {
                    minX = Mathf.Min(minX, s.x * XS - hw);
                    maxX = Mathf.Max(maxX, s.x * XS + hw);
                }
                minY = Mathf.Min(minY, s.y - 0.5f);
                maxY = Mathf.Max(maxY, s.y + 0.5f);
            }
            if (moving)
            {
                float half = Conveyor.VisibleHalfWidth(level.shelves.Select(s => s.x).ToList(), level.shelves.Select(s => s.width).ToList(),
                    level.shelves.Select(s => s.moving).ToList());
                minX = Mathf.Min(minX, -half * XS);
                maxX = Mathf.Max(maxX, half * XS);
            }
            bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);
            return true;
        }

        struct GameView
        {
            public float Aspect, Size, RefH, Standard;
            public Vector2 Center;
            public Rect Board, Frame, Safe;
        }

        bool ComputeGameView(out GameView v)
        {
            v = new GameView { Aspect = ScreenAspects[ScreenMode] };
            if (v.Aspect <= 0f || !BoardBounds(out v.Board)) return false;
            v.RefH = RefWidth / v.Aspect;
            Sortex.Game.SortexGame.FitBoard(v.Board, v.Aspect, v.RefH, out v.Size, out v.Center);
            v.Frame = new Rect(v.Center.x - v.Size * v.Aspect, v.Center.y - v.Size, 2f * v.Size * v.Aspect, 2f * v.Size);
            // the camera for a standard board (three 3-slot shelves across): shelves inside its board area keep that size
            float avail = Mathf.Max(0.3f, 1f - (Sortex.Game.LevelHud.BoardTop + Sortex.Game.LevelHud.BoardBottom) / v.RefH);
            v.Standard = Mathf.Max(v.RefH * 0.5f, (StandardWidth + 2f * MarginX) / (2f * v.Aspect));
            float hw = v.Standard * v.Aspect - MarginX, hh = v.Standard * avail - MarginY;
            v.Safe = new Rect(v.Board.center.x - hw, v.Board.center.y - hh, 2f * hw, 2f * hh);
            return true;
        }

        bool GameFrame(out Rect frame)
        {
            GameView v;
            frame = new Rect();
            if (!ComputeGameView(out v)) return false;
            frame = v.Frame;
            return true;
        }

        /// <summary>A rectangle in game units (x at half scale) on the canvas.</summary>
        Rect GameToCanvas(Rect local, Rect g)
        {
            var tl = ToScreen(local, g.xMin / XS, g.yMax);
            var br = ToScreen(local, g.xMax / XS, g.yMin);
            return Rect.MinMaxRect(tl.x, tl.y, br.x, br.y);
        }

        /// <summary>The game's background behind the shelves, inside the screen.</summary>
        void DrawGameBackground(Rect local)
        {
            Rect frame;
            if (!GameFrame(out frame)) return;
            var r = GameToCanvas(local, frame);
            var bg = ArtTexture("bg_gradient");
            if (bg != null) GUI.DrawTexture(r, bg, ScaleMode.StretchToFill, false);
            else EditorGUI.DrawRect(r, new Color(0.16f, 0.29f, 0.66f));
        }

        void DrawGameScreen(Rect local)
        {
            GameView v;
            if (!ComputeGameView(out v)) return;
            var fr = GameToCanvas(local, v.Frame);
            // outside the screen: dimmed
            var dim = new Color(0.02f, 0.03f, 0.08f, 0.62f);
            EditorGUI.DrawRect(Rect.MinMaxRect(0, 0, local.width, fr.yMin), dim);
            EditorGUI.DrawRect(Rect.MinMaxRect(0, fr.yMax, local.width, local.height), dim);
            EditorGUI.DrawRect(Rect.MinMaxRect(0, fr.yMin, fr.xMin, fr.yMax), dim);
            EditorGUI.DrawRect(Rect.MinMaxRect(fr.xMax, fr.yMin, local.width, fr.yMax), dim);

            // the level HUD: the game's own (rendered in Update), bands until it is ready
            float k = 2f * v.Size * v.Aspect / RefWidth;     // game units per unit of the UI's reference screen
            float topH = Sortex.Game.LevelHud.BoardTop * k, botH = Sortex.Game.LevelHud.BoardBottom * k;
            var top = GameToCanvas(local, new Rect(v.Frame.xMin, v.Frame.yMax - topH, v.Frame.width, topH));
            var bottom = GameToCanvas(local, new Rect(v.Frame.xMin, v.Frame.yMin, v.Frame.width, botH));
            hudAspect = v.Aspect;
            hudTag = level.difficultyTag;
            hudNumber = LevelNumber();
            hudSeconds = level.timeLimit;
            hudWanted = HudKey(hudAspect, hudTag, hudNumber, hudSeconds);
            if (hudTexture != null && hudKey == hudWanted) GUI.DrawTexture(fr, hudTexture, ScaleMode.StretchToFill, true);
            else
            {
                EditorGUI.DrawRect(top, new Color(0.05f, 0.08f, 0.2f, 0.5f));
                EditorGUI.DrawRect(bottom, new Color(0.05f, 0.08f, 0.2f, 0.5f));
                GUI.Label(top, "top bar: level · timer · pause", centerMini);
                GUI.Label(bottom, "boosters", centerMini);
            }
            var line = new Color(1f, 1f, 1f, 0.28f);
            EditorGUI.DrawRect(new Rect(fr.x, top.yMax, fr.width, 1), line);
            EditorGUI.DrawRect(new Rect(fr.x, bottom.yMin, fr.width, 1), line);
            DrawBorder(new Rect(fr.x - 2, fr.y - 2, fr.width + 4, fr.height + 4), new Color(0.92f, 0.95f, 1f, 0.9f), 2);

            // where shelves keep their normal size
            float pct = v.Standard / v.Size;
            bool fits = pct > 0.985f;
            var safeColor = fits ? new Color(0.45f, 1f, 0.55f, 0.85f) : new Color(1f, 0.62f, 0.2f, 0.95f);
            DrawDashed(GameToCanvas(local, v.Safe), safeColor);
            string what = v.Board.width > v.Safe.width + 0.01f && v.Board.height > v.Safe.height + 0.01f ? "wide and tall"
                : v.Board.width > v.Safe.width + 0.01f ? "wide" : "tall";
            string text = ScreenModeNames[ScreenMode] + ":  " + (fits
                ? "every shelf fits, items at full size" + (pct > 1.02f ? " (" + Mathf.RoundToInt(pct * 100f) + "%, small board)" : "")
                : "the board is too " + what + ": the game zooms out to " + Mathf.RoundToInt(pct * 100f) +
                  "% (smaller items). Keep the shelves inside the dashed area.");
            var size = EditorStyles.miniBoldLabel.CalcSize(new GUIContent(text));
            var lr = new Rect(fr.x, fr.y - 20 >= 2 ? fr.y - 20 : fr.y + 2, size.x + 10, 17);
            EditorGUI.DrawRect(lr, new Color(0f, 0f, 0f, 0.65f));
            var old = GUI.color;
            GUI.color = fits ? new Color(0.75f, 1f, 0.8f) : new Color(1f, 0.8f, 0.5f);
            GUI.Label(new Rect(lr.x + 5, lr.y, lr.width, lr.height), text, EditorStyles.miniBoldLabel);
            GUI.color = old;
        }

        static void DrawDashed(Rect r, Color c)
        {
            const float dash = 7f, gap = 5f;
            for (float x = r.xMin; x < r.xMax; x += dash + gap)
            {
                float w = Mathf.Min(dash, r.xMax - x);
                EditorGUI.DrawRect(new Rect(x, r.yMin, w, 2), c);
                EditorGUI.DrawRect(new Rect(x, r.yMax - 2, w, 2), c);
            }
            for (float y = r.yMin; y < r.yMax; y += dash + gap)
            {
                float h = Mathf.Min(dash, r.yMax - y);
                EditorGUI.DrawRect(new Rect(r.xMin, y, 2, h), c);
                EditorGUI.DrawRect(new Rect(r.xMax - 2, y, 2, h), c);
            }
        }

        int LevelNumber()
        {
            string digits = new string((level.name ?? "").Where(char.IsDigit).ToArray());
            int n;
            return digits.Length > 0 && digits.Length < 6 && int.TryParse(digits, out n) ? n : 1;
        }

        // the HUD picture: the game's LevelHud built in a preview scene and rendered over black and over white, which
        // gives its colours and its transparency
        [NonSerialized] Texture2D hudTexture;
        [NonSerialized] string hudKey, hudWanted;
        float hudAspect;
        int hudTag, hudNumber, hudSeconds;
        GUIStyle centerMini;

        static string HudKey(float aspect, int tag, int number, int seconds)
        {
            return aspect.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + "|" + tag + "|" + number + "|" + seconds;
        }

        void BuildHudTexture()
        {
            hudKey = hudWanted;    // also after a failure: no retry every frame
            Texture2D t = null;
            try { t = RenderHud(hudAspect, hudNumber, hudTag, hudSeconds); }
            catch (Exception e)
            {
                Debug.LogWarning("Sortex: could not draw the game HUD in the editor (" + e.Message + "); showing its outline instead.");
            }
            if (t == null) return;
            if (hudTexture != null) DestroyImmediate(hudTexture);
            hudTexture = t;
            Repaint();
        }

        /// <summary>The game's level HUD (LevelHud) on a screen of the given aspect, transparent around it; null when
        /// nothing could be rendered. Built in a preview scene, rendered over black and over white: the two pictures
        /// give the colours and the transparency.</summary>
        public static Texture2D RenderHud(float aspect, int number, int tag, int seconds, int width = 420)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var holder = new GameObject("SortexEditorHud");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, scene);
            RenderTexture rt = null;
            Texture2D read = null;
            try
            {
                var camGo = new GameObject("Camera");
                camGo.transform.SetParent(holder.transform, false);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.orthographicSize = RefWidth / aspect * 0.5f;
                cam.transform.position = new Vector3(0f, 0f, -10f);
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = 100f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                var hud = new Sortex.Game.LevelHud(holder.transform);
                if (!hud.Ready) return null;
                hud.SetLevel(number, tag);
                hud.SetBoosters(3, 3, 3, 3);
                hud.SetTime(seconds, seconds, false);
                hud.SetVisible(true);
                hud.Follow(cam, aspect);

                int w = width, h = Mathf.RoundToInt(width / aspect);
                rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                read = new Texture2D(w, h, TextureFormat.RGBA32, false);
                var overBlack = RenderOver(cam, rt, read, Color.black);
                var overWhite = RenderOver(cam, rt, read, Color.white);
                var px = new Color32[w * h];
                bool any = false;
                for (int i = 0; i < px.Length; i++)
                {
                    // linear light: over black = c * a, over white = c * a + (1 - a)
                    float br = Mathf.GammaToLinearSpace(overBlack[i].r / 255f), bg = Mathf.GammaToLinearSpace(overBlack[i].g / 255f),
                          bb = Mathf.GammaToLinearSpace(overBlack[i].b / 255f);
                    float wr = Mathf.GammaToLinearSpace(overWhite[i].r / 255f), wg = Mathf.GammaToLinearSpace(overWhite[i].g / 255f),
                          wb = Mathf.GammaToLinearSpace(overWhite[i].b / 255f);
                    float a = Mathf.Clamp01(1f - ((wr - br) + (wg - bg) + (wb - bb)) / 3f);
                    if (a < 0.004f) { px[i] = new Color32(0, 0, 0, 0); continue; }
                    any = true;
                    px[i] = new Color32((byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(br / a)) * 255f),
                                        (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(bg / a)) * 255f),
                                        (byte)Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(bb / a)) * 255f),
                                        (byte)Mathf.RoundToInt(a * 255f));
                }
                if (!any) return null;
                var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                t.SetPixels32(px);
                t.Apply();
                return t;
            }
            finally
            {
                DestroyImmediate(holder);
                EditorSceneManager.ClosePreviewScene(scene);
                if (rt != null) DestroyImmediate(rt);
                if (read != null) DestroyImmediate(read);
            }
        }

        static Color32[] RenderOver(Camera cam, RenderTexture rt, Texture2D read, Color background)
        {
            cam.backgroundColor = background;
            cam.targetTexture = rt;
            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            read.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            cam.targetTexture = null;
            return read.GetPixels32();
        }

        // ================================================================== delete
        static readonly string[] CaseLevelNames = { "Easy", "Medium", "Hard" };

        /// <summary>Own levels in Resources/Sortex/Levels can be deleted; the three case levels and report.json cannot.</summary>
        public static bool CanDelete(string path)
        {
            path = ToAssetPath(path);
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            string dir = (Path.GetDirectoryName(path) ?? "").Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(path);
            return dir == LevelsDir && name != "report" && Array.IndexOf(CaseLevelNames, name) < 0;
        }

        public static bool DeleteLevel(string path)
        {
            path = ToAssetPath(path);
            if (!CanDelete(path) || !File.Exists(path)) return false;
            if (!AssetDatabase.DeleteAsset(path)) return false;
            if (ToAssetPath(EditorPrefs.GetString(LastLevelKey, "")) == path) EditorPrefs.DeleteKey(LastLevelKey);
            foreach (var w in Resources.FindObjectsOfTypeAll<SortexLevelEditor>())
            {
                if (w.levelPath == null || ToAssetPath(w.levelPath) != path) continue;
                w.NewLevel();
                w.status = "Deleted " + Path.GetFileName(path) + ". This is a new, unsaved level.";
                w.Repaint();
            }
            Debug.Log("Sortex: deleted " + path);
            return true;
        }

        /// <summary>With moving shelves: dims what is outside the game screen (inside the pipes).</summary>
        void DrawScreenEdges(Rect local)
        {
            if (!level.shelves.Any(s => s.moving)) return;
            float half = Conveyor.VisibleHalfWidth(level.shelves.Select(s => s.x).ToList(), level.shelves.Select(s => s.width).ToList(),
                level.shelves.Select(s => s.moving).ToList());
            float left = ToScreen(local, -half, 0).x, right = ToScreen(local, half, 0).x;
            var shade = new Color(0.02f, 0.03f, 0.08f, 0.55f);
            if (left > 0) EditorGUI.DrawRect(new Rect(0, 0, left, local.height), shade);
            if (right < local.width) EditorGUI.DrawRect(new Rect(right, 0, local.width - right, local.height), shade);
            var edge = new Color(0.4f, 1f, 0.95f, 0.8f);
            EditorGUI.DrawRect(new Rect(left - 1, 0, 2, local.height), edge);
            EditorGUI.DrawRect(new Rect(right - 1, 0, 2, local.height), edge);
            GUI.Label(new Rect(left + 4, 4, 220, 16), "screen edge: moving shelves enter the pipe here", EditorStyles.miniBoldLabel);
        }

        /// <summary>A moving row as in Sort Express level 11: five 3-slot shelves, the outer two start inside the pipes.</summary>
        List<ShelfData> MovingRowShelves(float y, int rows)
        {
            var list = new List<ShelfData>();
            for (int i = -2; i <= 2; i++)
            {
                var s = ShelfData.Create(i * 3.13f, y, 3, Mathf.Max(1, rows));
                s.moving = true;
                s.moveDir = 1;
                s.moveSpeed = 1f;
                list.Add(s);
            }
            return list;
        }

        readonly Dictionary<string, Texture2D> artTextures = new Dictionary<string, Texture2D>();

        Texture2D ArtTexture(string name)
        {
            Texture2D t;
            if (!artTextures.TryGetValue(name, out t))
            {
                t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sortex/Resources/Sortex/Art/" + name + ".png");
                artTextures[name] = t;
            }
            return t;
        }

        /// <summary>What a slot looks like: item icon, stone sculpture, piñata or egg (null for empty / generated).</summary>
        Texture2D SlotIcon(SlotData c)
        {
            if (c == null) return null;
            if (c.IsPinata) return ArtTexture("pinata_" + Mathf.Clamp(c.pinataColor, 0, 5));
            if (c.IsEgg) return ArtTexture(string.IsNullOrEmpty(c.iid) ? "dragon_egg_0" : c.iid) ?? ArtTexture("dragon_egg_0");
            if (!c.IsItem) return null;
            return c.mystery ? MysteryIcon(c.iid) ?? Icon(c.iid) : Icon(c.iid);
        }

        readonly Dictionary<string, Texture2D> mysteryIcons = new Dictionary<string, Texture2D>();
        readonly HashSet<string> mysteryQueue = new HashSet<string>();

        /// <summary>
        /// The stone sculpture the game shows for a mystery item (same code as the game). Built in Update, not while
        /// the canvas is drawing (it uses the GPU); null until it is ready.
        /// </summary>
        Texture2D MysteryIcon(string id)
        {
            Texture2D t;
            if (mysteryIcons.TryGetValue(id, out t)) return t;
            mysteryQueue.Add(id);
            return null;
        }

        void BuildMysteryIcons()
        {
            foreach (var id in mysteryQueue)
            {
                Texture2D t = null;
                try
                {
                    var src = Icon(id);
                    t = src != null ? Sortex.Game.SortexArt.MysteryTexture(src) : null;
                    if (t != null) t.hideFlags = HideFlags.HideAndDontSave;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Sortex: mystery preview for " + id + " failed: " + e.Message);
                }
                mysteryIcons[id] = t;
            }
            mysteryQueue.Clear();
            Repaint();
        }

        readonly Dictionary<string, Texture2D> shelfTextures = new Dictionary<string, Texture2D>();

        Texture2D ShelfTexture(int width, string layer)
        {
            int[] widths = { 1, 3, 4, 5, 6 };
            int best = 3;
            foreach (int w in widths) if (Mathf.Abs(w - width) < Mathf.Abs(best - width)) best = w;
            string key = best + layer;
            Texture2D t;
            if (!shelfTextures.TryGetValue(key, out t))
            {
                t = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sortex/Resources/Sortex/Art/shelf_w" + best + "_" + layer + ".png");
                shelfTextures[key] = t;
            }
            return t;
        }

        /// <summary>The rendered shelf images are the inner size + 0.15 units on every side (0.3 wider, 1.3 tall).</summary>
        Rect ShelfArtRect(Rect inner, ShelfData s)
        {
            float pad = 0.15f * zoom;
            return new Rect(inner.x - pad, inner.y - pad, inner.width + 2 * pad, inner.height + 2 * pad);
        }

        Rect Badge(Rect at, string text, Color color)
        {
            float w = text.Length * 6.5f + 8;
            var r = new Rect(at.x, at.y, w, at.height);
            EditorGUI.DrawRect(r, color);
            GUI.Label(r, text, badge);
            return new Rect(r.xMax + 3, at.y, 0, at.height);
        }

        static void DrawBorder(Rect r, Color c, float t)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - t, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, t, r.height), c);
            EditorGUI.DrawRect(new Rect(r.xMax - t, r.y, t, r.height), c);
        }

        int AddWidth()
        {
            return tool == Tool.Shelf3 ? 3 : tool == Tool.Single ? 1 : tool == Tool.Wide ? 4 : 0;
        }

        static Vector2 Snap(Vector2 w)
        {
            return new Vector2(Mathf.Round(w.x / SnapX) * SnapX, Mathf.Round(w.y / SnapY) * SnapY);
        }

        static bool Overlaps(ShelfData a, ShelfData b)
        {
            float dx = Mathf.Abs(a.x - b.x), dy = Mathf.Abs(a.y - b.y);
            return dx < (a.width + b.width) * SW * 0.5f - 0.02f && dy < 1f - 0.02f;
        }

        int HitShelf(Vector2 world, out int slot)
        {
            slot = -1;
            for (int i = level.shelves.Count - 1; i >= 0; i--)
            {
                var s = level.shelves[i];
                float hw = s.width * SW * 0.5f;
                if (Mathf.Abs(world.x - s.x) <= hw && Mathf.Abs(world.y - s.y) <= 0.5f)
                {
                    slot = Mathf.Clamp(Mathf.FloorToInt((world.x - (s.x - hw)) / SW), 0, s.width - 1);
                    return i;
                }
            }
            return -1;
        }

        void HandleCanvasInput(Rect canvas)
        {
            int selectedBefore = selected, versionBefore = version;
            HandleCanvasEvent(canvas);
            // selection or board changed in the middle of a GUI pass: restart it so the panels lay out the same way
            if (selected != selectedBefore || version != versionBefore) Bail();
        }

        void HandleCanvasEvent(Rect canvas)
        {
            var e = Event.current;
            bool inside = canvas.Contains(e.mousePosition);
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                if (inside) hoverWorld = ToWorld(canvas, e.mousePosition);
                if (AddWidth() > 0 || tool == Tool.MovingRow) Repaint();
            }

            // pan & zoom
            if (e.type == EventType.ScrollWheel && inside)
            {
                var before = ToWorld(canvas, e.mousePosition);
                zoom = Mathf.Clamp(zoom * (e.delta.y > 0 ? 0.9f : 1.1f), 15f, 200f);
                var after = ToScreen(canvas, before.x, before.y);
                pan += e.mousePosition - after;
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDown && inside && (e.button == 2 || e.button == 0 && e.alt))
            {
                panning = true;
                e.Use();
                return;
            }
            if (panning)
            {
                if (e.type == EventType.MouseDrag) { pan += e.delta; e.Use(); Repaint(); }
                if (e.type == EventType.MouseUp) { panning = false; e.Use(); }
                return;
            }

            if (e.type == EventType.MouseUp)
            {
                draggingShelf = false;
                strokeActive = false;
                return;
            }
            if (!inside) return;
            var world = ToWorld(canvas, e.mousePosition);
            int slot;
            int hit = HitShelf(world, out slot);

            if (e.type == EventType.MouseDown)
            {
                GUIUtility.keyboardControl = 0;
                if (tool == Tool.MovingRow && e.button == 0)
                {
                    int rows = level.shelves.Count > 0 ? Mathf.RoundToInt((float)level.shelves.Average(s => s.layers.Count)) : 3;
                    var rowShelves = MovingRowShelves(Snap(world).y, rows);
                    if (rowShelves.Any(n => level.shelves.Any(s => Overlaps(s, n))))
                    {
                        status = "A moving row needs the whole height free (x -7.8 ... 7.8). Pick an empty height.";
                        e.Use();
                        return;
                    }
                    BeginEdit();
                    level.shelves.AddRange(rowShelves);
                    selected = level.shelves.Count - 3;   // the middle one
                    status = "Added a moving row. Set direction and speed in the right panel; fill it like any shelf.";
                    e.Use();
                    return;
                }
                int addWidth = AddWidth();
                if (addWidth > 0 && e.button == 0)
                {
                    if (hit >= 0) { selected = hit; e.Use(); return; }
                    var p = Snap(world);
                    int rows = level.shelves.Count > 0 ? Mathf.RoundToInt((float)level.shelves.Average(s => s.layers.Count)) : 3;
                    var ns = ShelfData.Create(p.x, p.y, addWidth, Mathf.Max(1, rows));
                    if (level.shelves.Any(s => Overlaps(s, ns))) { status = "There is already a shelf there."; e.Use(); return; }
                    BeginEdit();
                    level.shelves.Add(ns);
                    selected = level.shelves.Count - 1;
                    e.Use();
                    return;
                }
                switch (tool)
                {
                    case Tool.Select:
                        if (e.button != 0) break;
                        selected = hit;
                        if (hit >= 0)
                        {
                            draggingShelf = true;
                            dragOffset = world - new Vector2(level.shelves[hit].x, level.shelves[hit].y);
                            strokeActive = false;
                        }
                        e.Use();
                        break;
                    case Tool.Paint:
                    case Tool.Erase:
                        if (hit < 0 || row >= level.shelves[hit].layers.Count) break;
                        BeginEdit();
                        strokeActive = true;
                        PaintAt(hit, slot, e.button == 1 || tool == Tool.Erase, e.shift);
                        e.Use();
                        break;
                    case Tool.Pinata:
                    case Tool.Egg:
                        if (hit < 0 || row >= level.shelves[hit].layers.Count) break;
                        BeginEdit();
                        var target = level.shelves[hit].layers[row][slot];
                        target.Clear();
                        if (e.button != 1)
                        {
                            var made = tool == Tool.Pinata ? SlotData.Pinata(pinataColor, brush) : SlotData.Egg();
                            target.it = made.it;
                            target.iid = made.iid;
                            target.pinataColor = made.pinataColor;
                            target.pinataContent = made.pinataContent;
                        }
                        e.Use();
                        break;
                    case Tool.Frozen:
                    case Tool.Mystery:
                        if (hit < 0 || row >= level.shelves[hit].layers.Count) break;
                        var c = level.shelves[hit].layers[row][slot];
                        if (!c.IsItem) break;
                        BeginEdit();
                        if (tool == Tool.Frozen) c.frozen = !c.frozen;
                        else c.mystery = !c.mystery;
                        e.Use();
                        break;
                }
            }
            else if (e.type == EventType.MouseDrag && e.button == 0 || e.type == EventType.MouseDrag && e.button == 1)
            {
                if (draggingShelf && selected >= 0)
                {
                    var p = Snap(world - dragOffset);
                    var s = level.shelves[selected];
                    if (Mathf.Abs(p.x - s.x) > 1e-4f || Mathf.Abs(p.y - s.y) > 1e-4f)
                    {
                        if (!strokeActive) { BeginEdit(); strokeActive = true; }
                        s.x = p.x;
                        s.y = p.y;
                        Touch();
                    }
                    e.Use();
                }
                else if (strokeActive && hit >= 0 && row < level.shelves[hit].layers.Count && (tool == Tool.Paint || tool == Tool.Erase))
                {
                    PaintAt(hit, slot, e.button == 1 || tool == Tool.Erase, false);
                    e.Use();
                }
            }
        }

        void PaintAt(int shelf, int slot, bool erase, bool wholeRow)
        {
            var s = level.shelves[shelf];
            int from = wholeRow ? 0 : slot, to = wholeRow ? s.width - 1 : slot;
            for (int k = from; k <= to; k++)
            {
                var c = s.layers[row][k];
                if (erase) c.Clear();
                else if (!string.IsNullOrEmpty(brush))
                {
                    c.it = 1;
                    c.iid = brush;
                }
            }
            if (!erase && string.IsNullOrEmpty(brush)) status = "Pick an item in the palette first.";
            Touch();
        }

        void DeleteShelf(int i)
        {
            if (i < 0 || i >= level.shelves.Count) return;
            BeginEdit();
            level.shelves.RemoveAt(i);
            selected = -1;
        }

        void DuplicateShelf(int i)
        {
            var src = level.shelves[i];
            var copy = src.Clone();
            copy.id = Guid.NewGuid().ToString();
            // first free spot to the right, then below
            for (int step = 1; step < 20; step++)
            {
                copy.x = src.x + step * SnapX * 2;
                if (!level.shelves.Any(s => Overlaps(s, copy))) break;
                copy.x = src.x;
                copy.y = src.y - step;
                if (!level.shelves.Any(s => Overlaps(s, copy))) break;
            }
            BeginEdit();
            level.shelves.Add(copy);
            selected = level.shelves.Count - 1;
        }
    }

    /// <summary>
    /// Deletes own levels on request while Unity is in the background: Logs/SortexDelete.request with one level name per
    /// line ("Level 1"). Only files in Resources/Sortex/Levels, never the case levels. Result in Logs/SortexDelete.log.
    /// </summary>
    [InitializeOnLoad]
    static class SortexLevelRequests
    {
        static double nextPoll;

        static SortexLevelRequests()
        {
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextPoll) return;
            nextPoll = EditorApplication.timeSinceStartup + 2.0;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string logs = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs");
            string request = Path.Combine(logs, "SortexDelete.request");
            if (!File.Exists(request)) return;
            string[] names;
            try
            {
                names = File.ReadAllLines(request);
                File.Delete(request);
            }
            catch (Exception)
            {
                return;
            }
            var log = new System.Text.StringBuilder();
            foreach (var raw in names)
            {
                string name = raw.Trim();
                if (name.Length == 0) continue;
                string path = SortexLevelEditor.LevelsDir + "/" + name + ".json";
                log.Append(SortexLevelEditor.DeleteLevel(path) ? "deleted " : "not deleted ").Append(path).Append('\n');
            }
            File.WriteAllText(Path.Combine(logs, "SortexDelete.log"), log.ToString());
        }
    }
}
