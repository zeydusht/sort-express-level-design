// Main controller of the Sort Express level design prototype.
//
//  * Loads the three designed levels (Resources/Sortex/Levels/*.json, Sort Express' own level format)
//    and the simulation report that came with them.
//  * Builds camera, background, board view and HUD from code: the scene only needs this component.
//  * Input: drag & drop (mouse or touch) or tap an item, then tap the target shelf. New Input System.
//  * Rules live in Sortex.Core.Board, the same code the offline solver / simulator uses.
using System.Collections.Generic;
using Sortex.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Sortex.Game
{
    public sealed class SortexGame : MonoBehaviour
    {
        enum State { Menu, Playing, Result }

        static readonly string[] LevelFiles = { "Easy", "Medium", "Hard" };   // the case levels, shown first

        int menuLevels;   // levels listed in the menu (case levels + every level saved in Resources/Sortex/Levels)

        readonly List<LevelData> levels = new List<LevelData>();
        readonly List<Dictionary<string, object>> reports = new List<Dictionary<string, object>>();

        State state;
        int current;
        LevelData level;
        Board board;
        BoardView view;
        SortexHud hud;
        MainMenuView home;      // the game's own home screen (MainScene.prefab), the first thing a visitor sees
        LevelHud levelHud;      // the game's level HUD (LevelSceneUI.prefab): level number, timer, pause, boosters
        PauseDialog pauseDialog;// the game's pause dialog (LevelPauseDialog.prefab)
        WinDialog winDialog;    // the game's level end screens (LevelWinDialog, NoMoreMatchesPanel, LevelLoseDialog)
        OutOfDialog outOfDialog;
        LoseDialog loseDialog;
        bool paused;
        bool levelList;         // the case level list (opened from the Packs tab) instead of the home screen
        int screenFrame = -1;   // frame a screen was opened in: the click that opened it must not also press what is under it
        Camera cam;
        Transform world;
        SpriteRenderer background, shade;
        Vector2Int lastScreen;

        float timeLeft;
        float timeUsed;
        bool timerRunning;
        float endDelay = -1f;
        bool endWin;
        string endReason;
        bool endTimeUp;
        const string TimeUpReason = "Time's up!";
        const string StuckReason = "No moves left!";
        const int Coins = 1000, WinCoins = 20;

        // input
        Item held;
        int heldShelf, heldSlot;
        bool dragging, selected;
        Vector3 pressWorld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // Safety net: the SortexGame scene already contains this component.
            if (SceneManager.GetActiveScene().name != "SortexGame") return;
            if (FindAnyObjectByType<SortexGame>() != null) return;
            new GameObject("SortexGame").AddComponent<SortexGame>();
        }

        // ================================================================== setup
        void Awake()
        {
            Application.targetFrameRate = 60;
            LoadLevels();

            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera");
                cgo.tag = "MainCamera";
                cam = cgo.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SortexArt.Hex("#2A4BA8");
            cam.transform.position = new Vector3(0f, 0f, -10f);

            world = new GameObject("World").transform;
            background = SortexArt.NewSprite("Background", world, SortexArt.BackgroundGradient, -100);
            var white = new Texture2D(4, 4);
            var px = new Color[16];
            for (int i = 0; i < px.Length; i++) px[i] = Color.white;
            white.SetPixels(px);
            white.Apply();
            shade = SortexArt.NewSprite("Shade", world, Sprite.Create(white, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f), -99);
            shade.color = new Color(0.05f, 0.07f, 0.15f, 0.0f);

            hud = new SortexHud(transform);
            home = new MainMenuView(transform);
            home.SetVisible(false);
            home.OnClick("Play", PlayNext);
            home.OnClick("Packs", ShowLevels);
            home.OnClick("Home", () => { });
            foreach (var other in new[] { "Shop", "Leaderboard", "Settings", "Lives", "Coins", "Profile" })
            {
                string label = other;
                home.OnClick(other, () => hud.Toast(label + " is not part of this prototype. Packs lists every level.", Color.white));
            }
            levelHud = new LevelHud(transform);
            levelHud.OnClick("Pause", Pause);
            foreach (var b in LevelHud.BoosterNames)
                levelHud.OnClick(b, () => hud.Toast("Boosters are not part of this prototype: the case is the levels.", Color.white));
            pauseDialog = new PauseDialog(transform);
            pauseDialog.OnClick("Continue", Resume);
            pauseDialog.OnClick("Close", Resume);
            pauseDialog.OnClick("Quit", () => { Resume(); ShowMenu(); });
            foreach (var t in PauseDialog.Toggles)
            {
                string name = t;
                pauseDialog.OnClick(name, () => pauseDialog.Toggle(name));
            }
            winDialog = new WinDialog(transform);
            winDialog.OnClick("Continue", ShowMenu);
            outOfDialog = new OutOfDialog(transform);
            outOfDialog.OnClick("PlayOn", PlayOn);
            outOfDialog.OnClick("Close", ShowLose);
            outOfDialog.OnClick("Coins", () => hud.Toast("The shop is not part of this prototype.", Color.white));
            loseDialog = new LoseDialog(transform);
            loseDialog.OnClick("TryAgain", () => StartLevel(current));
            loseDialog.OnClick("Close", ShowMenu);
            foreach (var b in LoseDialog.BoosterNames)
                loseDialog.OnClick(b, () => hud.Toast("Boosters are not part of this prototype: the case is the levels.", Color.white));
#if UNITY_EDITOR
            // a level sent from Sortex > Level Editor (▶ Play level)
            string test = UnityEditor.SessionState.GetString("Sortex.TestLevel", "");
            if (!string.IsNullOrEmpty(test))
            {
                UnityEditor.SessionState.EraseString("Sortex.TestLevel");
                var lv = LevelData.FromJson(test);
                levels.Add(lv);
                reports.Add(null);
                StartLevel(levels.Count - 1);
                return;
            }
#endif
            ShowMenu();
        }

        void LoadLevels()
        {
            levels.Clear();
            reports.Clear();
            var reportAsset = Resources.Load<TextAsset>(SortexArt.Root + "Levels/report");
            object report = reportAsset != null ? MiniJson.Parse(reportAsset.text) : null;
            foreach (var file in LevelFiles)
            {
                var ta = Resources.Load<TextAsset>(SortexArt.Root + "Levels/" + file);
                if (ta == null)
                {
                    Debug.LogError("Sortex: missing level Resources/Sortex/Levels/" + file + ".json");
                    continue;
                }
                var lv = LevelData.FromJson(ta.text);
                if (string.IsNullOrEmpty(lv.name) || lv.name == "Level") lv.name = file;
                levels.Add(lv);
                reports.Add(MiniJson.Obj(report, file.ToLowerInvariant()));
            }

            // every other level saved with the Level Editor (Level 1, Level 2, ... in natural order)
            var own = new List<TextAsset>();
            foreach (var ta in Resources.LoadAll<TextAsset>(SortexArt.Root + "Levels"))
            {
                if (ta.name == "report" || System.Array.IndexOf(LevelFiles, ta.name) >= 0) continue;
                own.Add(ta);
            }
            own.Sort((a, b) => NaturalCompare(a.name, b.name));
            foreach (var ta in own)
            {
                LevelData lv;
                try { lv = LevelData.FromJson(ta.text); }
                catch (System.Exception e)
                {
                    Debug.LogWarning("Sortex: could not read level " + ta.name + ": " + e.Message);
                    continue;
                }
                if (string.IsNullOrEmpty(lv.name) || lv.name == "Level" || lv.name == "New Level") lv.name = ta.name;
                // the published build only lists levels that can be won; the editor shows everything while designing
                var problems = lv.Problems();
                if (!Application.isEditor && problems.Count > 0)
                {
                    Debug.LogWarning("Sortex: level " + ta.name + " skipped (" + problems[0] + ")");
                    continue;
                }
                levels.Add(lv);
                reports.Add(MiniJson.Obj(report, ta.name.ToLowerInvariant()));   // written by Sortex > 3. Publish
            }
            menuLevels = levels.Count;
        }

        /// <summary>"Level 2" before "Level 10".</summary>
        public static int NaturalCompare(string a, string b)
        {
            int i = 0, j = 0;
            while (i < a.Length && j < b.Length)
            {
                if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                {
                    int si = i, sj = j;
                    while (i < a.Length && char.IsDigit(a[i])) i++;
                    while (j < b.Length && char.IsDigit(b[j])) j++;
                    long na, nb;
                    long.TryParse(a.Substring(si, i - si), out na);
                    long.TryParse(b.Substring(sj, j - sj), out nb);
                    if (na != nb) return na.CompareTo(nb);
                    continue;
                }
                int c = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (c != 0) return c;
                i++;
                j++;
            }
            return (a.Length - i).CompareTo(b.Length - j);
        }

        // ================================================================== screens
        /// <summary>The level the home button plays: the first one not won yet (Easy, Medium, Hard, then the editor's levels).</summary>
        int NextLevel
        {
            get
            {
                int n = Mathf.Max(1, Mathf.Min(levels.Count, menuLevels));
                return Mathf.Clamp(PlayerPrefs.GetInt(ProgressKey, 0), 0, n - 1);
            }
        }

        const string ProgressKey = "Sortex.NextLevel";

        void PlayNext()
        {
            StartLevel(NextLevel);
        }

        void ShowMenu()
        {
            if (!home.Ready)
            {
                ShowLevels();
                return;
            }
            state = State.Menu;
            levelList = false;
            screenFrame = Time.frameCount;
            HideLevelScreens();
            ClearBoard();
            hud.ShowHome();
            int next = NextLevel;
            home.SetLevel(next + 1, levels[next].difficultyTag);
            home.SetWallet(5, 1000);
            home.SetVisible(true);
            FitCamera();
        }

        void ShowLevels()
        {
            state = State.Menu;
            levelList = true;
            home.SetVisible(false);
            HideLevelScreens();
            ClearBoard();
            var cards = new List<LevelCardInfo>();
            for (int i = 0; i < Mathf.Min(levels.Count, menuLevels); i++)
            {
                int index = i;
                var lv = levels[i];
                var b = Board.FromLevel(lv);
                var r = reports[i];
                string sim = r == null ? "" :
                    (MiniJson.Bool(r, "solvable") ? "Solver: solvable" : "Solver: no proof") +
                    "  ·  Simulated win rate " + Mathf.RoundToInt((float)MiniJson.Num(r, "winRate") * 100f) + "%";
                cards.Add(new LevelCardInfo
                {
                    Title = lv.name,
                    Tier = TierLabel(lv),
                    Color = SortexArt.ThemeColor(lv.difficultyTag),
                    Board = b.Shelves.Count + " shelves  ·  " + b.TotalItems + " items  ·  " + lv.timeLimit + " s",
                    Mechanics = MechanicsLabel(b),
                    Sim = sim,
                    Compact = i >= LevelFiles.Length,
                    OnPlay = () => StartLevel(index)
                });
            }
            hud.ShowMenu(cards, home.Ready ? (System.Action)ShowMenu : null);
            FitCamera();
        }

        static string TierLabel(LevelData lv)
        {
            if (lv.difficultyTag >= 20) return "Super Hard tier (ld 20)";
            if (lv.difficultyTag >= 10) return "Hard tier (ld 10)";
            return "Normal tier (ld 0)";
        }

        sealed class Census
        {
            public int Frozen, Mystery, Locks, Singles, Shutters, Cargo, Moving, Ferris, Iris, Fuse, Pinatas, Eggs, Wide, MaxLock;
        }

        static Census Count(Board b)
        {
            var c = new Census();
            foreach (var s in b.Shelves)
            {
                if (s.Locked) { c.Locks++; c.MaxLock = Mathf.Max(c.MaxLock, s.LockCount); }
                if (s.Width == 1) c.Singles++;
                if (s.Width > 3) c.Wide++;
                if (s.Shutter) c.Shutters++;
                if (s.Cargo) c.Cargo++;
                if (s.Moving) c.Moving++;
                if (s.Ferris) c.Ferris++;
                if (s.Iris) c.Iris++;
                if (s.FuseItems.Count > 0) c.Fuse++;
                foreach (var l in s.Layers)
                    foreach (var it in l)
                    {
                        if (it == null) continue;
                        if (it.Frozen) c.Frozen++;
                        if (it.Mystery) c.Mystery++;
                        if (it.Kind == ItemKind.Pinata) c.Pinatas++;
                        if (it.Kind == ItemKind.Egg) c.Eggs++;
                    }
            }
            return c;
        }

        static string MechanicsLabel(Board b)
        {
            var c = Count(b);
            var parts = new List<string>();
            if (c.Singles > 0) parts.Add(c.Singles + " single shelves");
            if (c.Wide > 0) parts.Add(c.Wide + " wide shelves");
            if (c.Frozen > 0) parts.Add(c.Frozen + " frozen items");
            if (c.Locks > 0) parts.Add(c.Locks + " lock");
            if (c.Mystery > 0) parts.Add(c.Mystery + " mystery items");
            if (c.Shutters > 0) parts.Add(c.Shutters + " shutters");
            if (c.Cargo > 0) parts.Add(c.Cargo + " cargo doors");
            if (c.Moving > 0) parts.Add(c.Moving + " moving shelves");
            if (c.Ferris > 0) parts.Add(c.Ferris + " rotating doors");
            if (c.Pinatas > 0) parts.Add(c.Pinatas + " piñatas");
            if (c.Fuse > 0) parts.Add(c.Fuse + " fuse boxes");
            if (c.Eggs > 0) parts.Add(c.Eggs + " dragon eggs");
            if (c.Iris > 0) parts.Add(c.Iris + " irises");
            return parts.Count == 0 ? "No obstacles: learn sort, match and depth" : string.Join("  ·  ", parts.ToArray());
        }

        string HintFor(Board b)
        {
            var c = Count(b);
            var tips = new List<string>();
            if (c.Locks > 0) tips.Add("The lock opens after " + c.MaxLock + " matches.");
            if (c.Fuse > 0) tips.Add("Match the items shown on a fuse box to power it up and open it.");
            if (c.Iris > 0) tips.Add("Irises open and close after every move.");
            if (c.Pinatas > 0) tips.Add("Match three piñatas of a colour: the toys inside drop out.");
            if (c.Eggs > 0) tips.Add("Line up three dragon eggs to hatch them.");
            if (c.Ferris > 0) tips.Add("Rotating doors bring their next stage once the current one is cleared.");
            if (c.Mystery > 0) tips.Add("Stone sculptures turn back into items after a match on their shelf or next to it.");
            if (c.Shutters > 0) tips.Add("Shutters close for good once their shelf is empty.");
            if (c.Cargo > 0) tips.Add("Cargo doors hide the rows behind them.");
            if (c.Moving > 0) tips.Add("Moving shelves slide through the pipes: catch them while they are on screen.");
            if (c.Wide > 0) tips.Add("On wide shelves three side by side match.");
            if (c.Frozen > 0) tips.Add("Frozen items cannot move: bring the other two to them.");
            if (tips.Count > 0) return string.Join("  ", tips.GetRange(0, Mathf.Min(2, tips.Count)).ToArray());
            return "Drag an item onto another shelf. Three of a kind on a shelf pops!  When a row is empty, the row behind it moves forward.";
        }

        /// <summary>Tells the player why a shelf cannot be used right now. False when it can.</summary>
        bool Blocked(int s, bool placing)
        {
            var shelf = board.Shelves[s];
            if (shelf.Locked)
            {
                view.ShakeShelf(s);
                hud.Toast("Locked: " + shelf.LockCount + " more matches to open", SortexArt.LightBlue);
                return true;
            }
            if (shelf.FuseClosed)
            {
                hud.Toast("Fuse box: match the items shown on it to open it", SortexArt.LightBlue);
                return true;
            }
            if (shelf.IrisClosed)
            {
                hud.Toast("The iris is closed: it opens after your next move", SortexArt.LightBlue);
                return true;
            }
            if (placing && shelf.Closed)
            {
                hud.Toast("This shelf is closed", SortexArt.LightBlue);
                return true;
            }
            return false;
        }

        void StartLevel(int index)
        {
            screenFrame = Time.frameCount;
            home.SetVisible(false);
            levelList = false;
            current = Mathf.Clamp(index, 0, levels.Count - 1);
            level = levels[current];
            ClearBoard();
            board = Board.FromLevel(level);
            view = new BoardView(world, board, level.difficultyTag);
            timeLeft = level.timeLimit;
            timeUsed = 0f;
            timerRunning = false;
            endDelay = -1f;
            held = null;
            selected = dragging = false;
            state = State.Playing;
            paused = false;
            pauseDialog.SetVisible(false);
            HideEndDialogs();
            if (levelHud.Ready)
            {
                // the game's own HUD; the uGUI layer only shows toasts and the result card
                hud.ShowHome();
                levelHud.SetLevel(current + 1, level.difficultyTag);
                levelHud.SetBoosters(3, 3, 3, 3);
                levelHud.SetTime(timeLeft, level.timeLimit, false);
                levelHud.SetVisible(true);
            }
            else
                hud.ShowGame(level.name, TierLabel(level).Replace(" tier", "").Replace(" (ld 0)", "").Replace(" (ld 10)", "").Replace(" (ld 20)", ""),
                    SortexArt.ThemeColor(level.difficultyTag), HintFor(board), ShowMenu, () => StartLevel(current));
            hud.SetTimer(timeLeft, false);
            hud.SetProgress(0, board.TotalItems);
            hud.Toast("Timer starts with your first move", Color.white);
            lastScreen = Vector2Int.zero;
            FitCamera();
        }

        void HideLevelScreens()
        {
            paused = false;
            levelHud.SetVisible(false);
            pauseDialog.SetVisible(false);
            HideEndDialogs();
        }

        void HideEndDialogs()
        {
            winDialog.SetVisible(false);
            outOfDialog.SetVisible(false);
            loseDialog.SetVisible(false);
        }

        /// <summary>The level end dialog that is open (null while playing).</summary>
        LevelDialog EndDialog
        {
            get
            {
                if (winDialog.Visible) return winDialog;
                if (outOfDialog.Visible) return outOfDialog;
                if (loseDialog.Visible) return loseDialog;
                return null;
            }
        }

        /// <summary>Out of Time: 60 more seconds, the level goes on. Out of Space needs a booster the prototype has not.</summary>
        void PlayOn()
        {
            if (!endTimeUp)
            {
                hud.Toast("Boosters are not part of this prototype: close this to finish the level.", Color.white);
                return;
            }
            outOfDialog.SetVisible(false);
            screenFrame = Time.frameCount;
            timeLeft += OutOfDialog.ExtraSeconds;
            timerRunning = true;
            endDelay = -1f;
            state = State.Playing;
        }

        void ShowLose()
        {
            outOfDialog.SetVisible(false);
            screenFrame = Time.frameCount;
            if (!loseDialog.Ready)
            {
                LegacyResult(false, endTimeUp ? TimeUpReason : StuckReason);
                return;
            }
            loseDialog.Show();
            loseDialog.Layout(cam, Aspect);
        }

        static float Aspect { get { return Mathf.Max(0.3f, (float)Screen.width / Mathf.Max(1, Screen.height)); } }

        void Pause()
        {
            if (state != State.Playing || paused || !pauseDialog.Ready) return;
            if (held != null) CancelHold();
            paused = true;
            screenFrame = Time.frameCount;
            pauseDialog.SetVisible(true);
            pauseDialog.Layout(cam, Mathf.Max(0.3f, (float)Screen.width / Mathf.Max(1, Screen.height)));
        }

        void Resume()
        {
            paused = false;
            screenFrame = Time.frameCount;
            pauseDialog.SetVisible(false);
        }

        void ClearBoard()
        {
            if (view != null) view.Destroy();
            view = null;
            board = null;
        }

        void Finish(bool win, string reason)
        {
            state = State.Result;
            timerRunning = false;
            screenFrame = Time.frameCount;
            if (held != null) CancelHold();
            if (win && current == NextLevel && current < menuLevels)
            {
                PlayerPrefs.SetInt(ProgressKey, current + 1 < Mathf.Min(levels.Count, menuLevels) ? current + 1 : 0);
                PlayerPrefs.Save();
            }
            endTimeUp = !win && reason == TimeUpReason;
            // the game's own dialogs: Level Completed, or the last chance (Out of Space / Out of Time) and then Level Failed
            LevelDialog dialog = win ? (LevelDialog)winDialog : outOfDialog;
            if (!dialog.Ready)
            {
                LegacyResult(win, reason);
                return;
            }
            if (win) winDialog.Show(WinCoins, timeUsed);
            else outOfDialog.Show(endTimeUp, Coins);
            dialog.Layout(cam, Aspect);
        }

        /// <summary>The prototype's own result card (only when the dialogs' json is missing).</summary>
        void LegacyResult(bool win, string reason)
        {
            int cleared = board.TotalItems - board.RemainingItems;
            if (win)
            {
                string body = "Cleared " + board.TotalItems + " items in " + Mathf.CeilToInt(timeUsed) + " s\n" +
                              board.Moves + " moves  ·  " + board.Matches + " matches";
                bool hasNext = current + 1 < Mathf.Min(levels.Count, menuLevels);
                hud.ShowResult(true, "Level Complete!", body, hasNext ? "Next: " + levels[current + 1].name : "Back to menu",
                    hasNext ? (System.Action)(() => StartLevel(current + 1)) : ShowMenu, () => StartLevel(current), ShowMenu);
            }
            else
            {
                string body = reason + "\nYou cleared " + cleared + " of " + board.TotalItems + " items (" +
                              Mathf.RoundToInt(100f * cleared / Mathf.Max(1, board.TotalItems)) + "%).";
                hud.ShowResult(false, "Level Failed", body, "Try again", () => StartLevel(current), () => StartLevel(current), ShowMenu);
            }
        }

        // ================================================================== frame loop
        void Update()
        {
            float dt = Time.deltaTime;
            FitCamera();   // cheap; keeps the board framed when the browser window or the UI scale changes

            bool freshScreen = Time.frameCount <= screenFrame;
            if (state == State.Menu && !levelList && home.Ready && !freshScreen)
            {
                var p = Pointer.current;
                if (p != null)
                {
                    Vector2 sp = p.position.ReadValue();
                    Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10f));
                    home.Pointer(p.press.wasPressedThisFrame, p.press.isPressed, p.press.wasReleasedThisFrame, wp, dt);
                }
            }
            var end = state == State.Result ? EndDialog : null;
            if (end != null)
            {
                var p = Pointer.current;
                if (p != null && !freshScreen)
                {
                    Vector2 sp = p.position.ReadValue();
                    Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10f));
                    end.Pointer(p.press.wasPressedThisFrame, p.press.isPressed, p.press.wasReleasedThisFrame, wp, dt);
                }
                if (levelHud.Visible) levelHud.SetTime(timeLeft, level.timeLimit, false);
            }
            if (state == State.Playing && paused)
            {
                var p = Pointer.current;
                if (p != null && !freshScreen)
                {
                    Vector2 sp = p.position.ReadValue();
                    Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10f));
                    pauseDialog.Pointer(p.press.wasPressedThisFrame, p.press.isPressed, p.press.wasReleasedThisFrame, wp, dt);
                }
            }
            else if (state == State.Playing)
            {
                bool onHud = false;
                var p = Pointer.current;
                if (p != null && levelHud.Visible && !freshScreen)
                {
                    Vector2 sp = p.position.ReadValue();
                    Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10f));
                    // a press on the pause button or a booster belongs to the HUD, not to the shelves under it
                    onHud = levelHud.Pointer(p.press.wasPressedThisFrame, p.press.isPressed, p.press.wasReleasedThisFrame, wp, dt);
                }
                if (!freshScreen && !onHud && !paused && state == State.Playing) HandleInput();
                if (timerRunning && !paused)
                {
                    timeLeft -= dt;
                    timeUsed += dt;
                    if (timeLeft <= 0f && endDelay < 0f)
                    {
                        timeLeft = 0f;
                        Finish(false, TimeUpReason);
                    }
                }
                if (endDelay >= 0f)
                {
                    endDelay -= dt;
                    if (endDelay < 0f) Finish(endWin, endReason);
                }
                hud.SetTimer(timeLeft, timerRunning);
                if (board != null) hud.SetProgress(board.TotalItems - board.RemainingItems, board.TotalItems);
                if (levelHud.Visible) levelHud.SetTime(timeLeft, level.timeLimit, timerRunning && !paused);
            }
            if (view != null) view.Tick(dt);
            hud.Tick(dt);
        }

        /// <summary>Camera half height and centre for a board between the game's top bar and boosters: at least the
        /// reference screen (4.6166 wide, refH tall), zoomed out until the shelves fit, filling the width.</summary>
        public static void FitBoard(Rect b, float aspect, float refH, out float size, out Vector2 center)
        {
            float avail = Mathf.Max(0.3f, 1f - (LevelHud.BoardTop + LevelHud.BoardBottom) / refH);
            const float marginX = 0.2f, marginY = 0.3f;   // shelf rims and items standing out of the top row
            float sizeW = (b.width + 2f * marginX) / (2f * aspect);
            float sizeH = (b.height + 2f * marginY) / (2f * avail);
            size = Mathf.Max(refH * 0.5f, Mathf.Max(sizeW, sizeH));
            float centerFrac = LevelHud.BoardBottom / refH + avail * 0.5f;
            center = new Vector2(b.center.x, b.center.y + (0.5f - centerFrac) * 2f * size);
        }

        void FitCamera()
        {
            lastScreen = new Vector2Int(Screen.width, Screen.height);
            hud.UpdateScaler();
            if (state == State.Menu && !levelList && home.Ready)
            {
                home.Layout(cam);
                return;
            }
            float aspect = Mathf.Max(0.3f, (float)Screen.width / Mathf.Max(1, Screen.height));
            float size;
            Vector2 center;
            if (view != null && levelHud.Ready && !levelList)
            {
                // like the game: the camera shows at least its reference screen (4.6166 wide) and zooms out until the
                // shelves fit between the top bar and the boosters, filling the width with a thin margin
                FitBoard(view.Bounds, aspect, levelHud.ReferenceHeight(aspect), out size, out center);
            }
            else if (view != null)
            {
                float topFrac = Mathf.Clamp01(hud.TopPixels / Mathf.Max(1, Screen.height));
                float botFrac = Mathf.Clamp01(hud.BottomPixels / Mathf.Max(1, Screen.height));
                float avail = Mathf.Max(0.3f, 1f - topFrac - botFrac);
                var b = view.Bounds;
                const float margin = 0.45f;
                float sizeH = (b.height + 2f * margin) / (2f * avail);
                float sizeW = (b.width + 2f * margin) / (2f * aspect);
                size = Mathf.Max(3f, Mathf.Max(sizeH, sizeW));
                float centerFrac = botFrac + avail * 0.5f;
                center = new Vector2(b.center.x, b.center.y + (0.5f - centerFrac) * 2f * size);
            }
            else
            {
                size = 8f;
                center = Vector2.zero;
            }
            cam.orthographicSize = size;
            cam.transform.position = new Vector3(center.x, center.y, -10f);

            // background covers the whole view
            float h = size * 2f, w = h * aspect;
            if (background.sprite != null)
            {
                var bs = background.sprite.bounds.size;
                // the gradient is stretched to the view so the whole top-to-bottom fade is visible
                background.transform.localScale = new Vector3(w / bs.x * 1.02f, h / bs.y * 1.02f, 1f);
            }
            background.transform.position = new Vector3(center.x, center.y, 0f);
            shade.transform.position = new Vector3(center.x, center.y, 0f);
            shade.transform.localScale = new Vector3(w * 1.05f, h * 1.05f, 1f);
            if (levelHud.Visible) levelHud.Follow(cam, aspect);
            if (pauseDialog.Visible) pauseDialog.Layout(cam, aspect);
            var end = EndDialog;
            if (end != null) end.Layout(cam, aspect);
        }

        // ================================================================== input
        void HandleInput()
        {
            if (endDelay >= 0f) return;
            var p = Pointer.current;
            if (p == null) return;
            Vector2 sp = p.position.ReadValue();
            Vector3 wp = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, 10f));
            wp.z = 0f;

            if (p.press.wasPressedThisFrame) OnPress(wp);
            else if (p.press.isPressed && held != null) OnDrag(wp);
            if (p.press.wasReleasedThisFrame) OnRelease(wp);
        }

        void OnPress(Vector3 wp)
        {
            int s, k;
            if (selected && held != null)
            {
                // second tap of tap-to-move
                if (!view.HitShelf(wp, 0.05f, out s, out k))
                {
                    CancelHold();
                    return;
                }
                if (s == heldShelf)
                {
                    if (k != heldSlot && board.Shelves[s].Front[k] == null)
                    {
                        DropOn(s, k, wp);       // empty slot on the same shelf: slide the item there
                        return;
                    }
                    bool sameItem = k == heldSlot;
                    CancelHold();
                    if (!sameItem) TryPick(s, k, wp);
                    return;
                }
                var target = board.Shelves[s];
                if (target.CanGive && target.Front[k] != null && board.CanPick(s, k))
                {
                    // tapping another item that cannot be a target slot: switch the selection
                    int slot = NearestEmpty(s, wp.x);
                    if (slot < 0)
                    {
                        CancelHold();
                        TryPick(s, k, wp);
                        return;
                    }
                }
                DropOn(s, k, wp);
                return;
            }
            if (view.HitShelf(wp, 0f, out s, out k)) TryPick(s, k, wp);
        }

        void TryPick(int s, int k, Vector3 wp)
        {
            var shelf = board.Shelves[s];
            var it = shelf.Front[k];
            if (it == null) return;
            if (Blocked(s, false)) return;
            if (it.Frozen)
            {
                view.Reject(it);
                hud.Toast("Frozen! Bring the other two here", SortexArt.LightBlue);
                return;
            }
            held = it;
            heldShelf = s;
            heldSlot = k;
            dragging = false;
            selected = false;
            pressWorld = wp;
            if (!timerRunning) timerRunning = true;
            view.Hold(it, view.SlotPosition(s, k, 0) + new Vector3(0f, 0.12f, 0f), false);
        }

        void OnDrag(Vector3 wp)
        {
            if (!dragging && !selected && (wp - pressWorld).sqrMagnitude > 0.15f * 0.15f) dragging = true;
            if (!dragging) return;
            view.Hold(held, wp + DragOffset, true);
        }

        void OnRelease(Vector3 wp)
        {
            if (held == null) return;
            if (!dragging)
            {
                if (!selected) selected = true;     // tap: keep the item lifted, wait for the target tap
                return;
            }
            int s, k;
            var itemPos = wp + DragOffset;
            if (FindDropShelf(itemPos, out s, out k)) DropOn(s, k, itemPos);
            else CancelHold();
        }

        static readonly Vector3 DragOffset = new Vector3(0f, 0.3f, 0f);   // the dragged item floats above the finger

        /// <summary>
        /// Target for a drop: the shelf under the dragged item (what the player sees), else the closest shelf to it,
        /// else the one under the finger. The item's own shelf counts too, so it can slide to another slot.
        /// </summary>
        bool FindDropShelf(Vector3 itemPos, out int s, out int k)
        {
            if (view.HitShelf(itemPos, 0f, out s, out k)) return true;
            if (view.HitShelf(itemPos, 0.35f, out s, out k)) return true;
            return view.HitShelf(itemPos - DragOffset, 0f, out s, out k);
        }

        int NearestEmpty(int s, float x)
        {
            var shelf = board.Shelves[s];
            int best = -1;
            float bd = float.MaxValue;
            for (int i = 0; i < shelf.Width; i++)
            {
                if (shelf.Front[i] != null) continue;
                float d = Mathf.Abs(view.SlotPosition(s, i, 0).x - x);
                if (d < bd)
                {
                    bd = d;
                    best = i;
                }
            }
            return best;
        }

        void DropOn(int s, int k, Vector3 wp)
        {
            var target = board.Shelves[s];
            if (Blocked(s, true))
            {
                CancelHold();
                return;
            }
            int slot;
            if (s == heldShelf)
            {
                // same shelf: only onto an empty slot under the item; anything else puts it back
                slot = k != heldSlot && target.Front[k] == null ? k : -1;
                if (slot < 0)
                {
                    CancelHold();
                    return;
                }
            }
            else slot = target.Front[k] == null ? k : NearestEmpty(s, wp.x);
            if (slot < 0)
            {
                hud.Toast("That shelf is full", SortexArt.LightBlue);
                var it = held;
                CancelHold();
                view.Reject(it);
                return;
            }

            var r = board.Move(heldShelf, heldSlot, s, slot);
            var moved = held;
            held = null;
            selected = dragging = false;
            if (!r.Ok)
            {
                view.Sync(true);
                view.Reject(moved);
                return;
            }
            view.Release(moved);
            view.Land(moved, s, slot, r.Matched);
            view.Sync(true);
            view.PlayResult(r);
            if (r.Matched)
            {
                view.PlayMatch(r.MatchedShelf);
                if (r.Unlocked.Count > 0) hud.Toast("Vault unlocked!", SortexArt.Orange);
                else if (r.FuseOpened.Count > 0) hud.Toast("Fuse box powered up!", SortexArt.Orange);
                else if (r.Released.Count > 0) hud.Toast("Piñata!", SortexArt.Orange);
                else if (r.Revealed.Count > 0) hud.Toast("Mystery revealed!", SortexArt.Orange);
            }
            if (r.Won)
            {
                endWin = true;
                endReason = "win";
                endDelay = 0.7f;
                timerRunning = false;
            }
            else if (r.Stuck)
            {
                endWin = false;
                endReason = StuckReason;
                endDelay = 0.7f;
                timerRunning = false;
            }
        }

        void CancelHold()
        {
            if (held != null) view.Release(held);
            held = null;
            selected = dragging = false;
            view.Sync(true);
        }
    }
}
