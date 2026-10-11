// Draws a Board (Sortex.Core) with SpriteRenderers and animates every change.
//
// The view never decides anything: after each move it is synchronised with the model (Sync) and every
// item view eases towards the place the model says it is. Items that left the board play a pop effect.
//
// Sorting orders: cabinet (-5) < shelf back wall (0) < cargo door (5) < back-row items (10) < shadows (19)
//                 < front items (20) < ice (22) < shelf rim (30) < iris frame (31) < shutter (38)
//                 < iris blades / rotating door (39) < lock door, fuse box (40-42) < texts (41)
//                 < conveyor tunnels (60, 61) < dragged item (100) < fx (110)
//
// Moving shelves (Conveyor): the view slides those shelves (and the items standing on them) every frame and
// writes the new X back into the model, so hit tests and slot positions follow them.
using System.Collections.Generic;
using Sortex.Core;
using UnityEngine;

namespace Sortex.Game
{
    public sealed class BoardView
    {
        // The levels place 3-slot shelves 3.13 apart and rows 1 apart; the game draws x at half that scale, so a
        // 3-slot shelf is about 1.6 x 1 on screen (measured on a screenshot of the game: row pitch / shelf pitch).
        // The model keeps the level's numbers; only the view squeezes x.
        public const float XS = 0.5f;
        const float SW = Board.SlotWidth * XS;
        static float VX(float levelX) { return levelX * XS; }
        const float LipTop = -0.44f;                 // where items stand, relative to the shelf centre
        const float InnerTop = 0.465f;
        const float FrontY = LipTop + SortexArt.ItemSize * 0.5f + 0.005f;
        const float BackY = FrontY + 0.10f;
        const float BackScale = 0.84f;

        sealed class ItemView
        {
            public Item Item;
            public GameObject Go;
            public SpriteRenderer Icon;
            public SpriteRenderer Shadow;
            public SpriteRenderer Ice;
            public Vector3 Target;
            public float TargetScale = 1f;
            public Color TargetTint = Color.white;
            public float Delay;
            public float Speed = 18f;
            public bool ShowsMystery;
            public float Shake;                    // seconds of shake left
            public float Dying = -1f;              // >= 0 while popping
            public int Layer;
            public int Shelf = -1;                 // shelf the item stands on (or is flying to)
            public bool Held;                      // picked up by the player: does not ride the conveyor
        }

        sealed class ShelfView
        {
            public Shelf Shelf;
            public Transform Root;
            public SpriteRenderer Door;
            public TextMesh DoorText;
            public float DoorOpen = -1f;           // >= 0 while the lock door is opening
            public float DoorPunch;
            public SpriteRenderer Shutter;
            public SpriteRenderer ShutterHint;
            public float ShutterDrop = -1f;        // >= 0 while the shutter comes down
            public SpriteRenderer Cargo;
            public SpriteRenderer CargoBadge;
            public TextMesh CargoText;
            public readonly List<SpriteRenderer> Dots = new List<SpriteRenderer>();
            // rotating door (ferris)
            public SpriteRenderer FerrisDoor;
            public readonly List<SpriteRenderer> Lamps = new List<SpriteRenderer>();
            public float FerrisTurn = -1f;         // >= 0 while the door turns to the next stage
            // iris
            public SpriteRenderer IrisBlades;
            public float IrisShown;                // 0 open .. 1 closed
            // fuse box
            public Transform Fuse;
            public readonly List<SpriteRenderer> FuseWindows = new List<SpriteRenderer>();
            public readonly List<SpriteRenderer> FuseIcons = new List<SpriteRenderer>();
            public SpriteRenderer FuseBolt;
            public float FuseOpen = -1f;           // >= 0 while the fuse box bursts open
        }

        sealed class Fx
        {
            public SpriteRenderer Sr;
            public Vector3 Velocity;
            public float Age;
            public float Life;
            public float StartScale;
            public float EndScale;
            public float Spin;
            public float Gravity;
        }

        readonly Transform root;
        readonly Board board;
        readonly int difficultyTag;
        readonly List<ShelfView> shelves = new List<ShelfView>();
        readonly Dictionary<int, ItemView> items = new Dictionary<int, ItemView>();
        readonly List<ItemView> dying = new List<ItemView>();
        readonly List<Fx> fx = new List<Fx>();
        List<ConveyorRow> conveyors = new List<ConveyorRow>();
        float visibleHalf = Conveyor.ScreenHalfWidth;

        public Rect Bounds { get; private set; }
        public bool HasConveyors { get { return conveyors.Count > 0; } }

        public BoardView(Transform parent, Board board, int difficultyTag)
        {
            this.board = board;
            this.difficultyTag = difficultyTag;
            root = new GameObject("Board").transform;
            root.SetParent(parent, false);
            Build();
            Sync(false);
        }

        public void Destroy()
        {
            Object.Destroy(root.gameObject);
        }

        // ------------------------------------------------------------------ geometry
        public static float InnerWidth(Shelf s) { return s.Width * SW; }

        public Vector3 SlotPosition(int shelf, int slot, int layer)
        {
            var s = board.Shelves[shelf];
            float x = VX(s.X) - InnerWidth(s) * 0.5f + SW * (slot + 0.5f);
            float y = s.Y + (layer == 0 ? FrontY : BackY);
            return new Vector3(x, y, 0f);
        }

        /// <summary>Shelf and slot under a world point (slot clamped to the shelf), or false.</summary>
        public bool HitShelf(Vector2 p, float tolerance, out int shelf, out int slot)
        {
            shelf = -1;
            slot = -1;
            float best = float.MaxValue;
            for (int i = 0; i < board.Shelves.Count; i++)
            {
                var s = board.Shelves[i];
                if (s.Moving && (Mathf.Abs(p.x) > VX(visibleHalf) || Conveyor.Hidden(s.X, s.Width, visibleHalf))) continue;
                float hw = InnerWidth(s) * 0.5f;
                float dx = Mathf.Max(0f, Mathf.Abs(p.x - VX(s.X)) - hw);
                float dy = Mathf.Max(0f, Mathf.Abs(p.y - s.Y) - 0.5f);
                float d = dx + dy;
                if (d <= tolerance && d < best)
                {
                    best = d;
                    shelf = i;
                    slot = Mathf.Clamp(Mathf.FloorToInt((p.x - (VX(s.X) - hw)) / SW), 0, s.Width - 1);
                }
            }
            return shelf >= 0;
        }

        // ------------------------------------------------------------------ build
        void Build()
        {
            // moving rows: the screen shows a fixed width, the rest of each row waits inside the tunnels
            var xs = new List<float>(); var ys = new List<float>(); var ws = new List<int>();
            var mv = new List<bool>(); var vel = new List<float>();
            foreach (var s in board.Shelves) { xs.Add(s.X); ys.Add(s.Y); ws.Add(s.Width); mv.Add(s.Moving); vel.Add(s.Velocity); }
            visibleHalf = Conveyor.VisibleHalfWidth(xs, ws, mv);
            conveyors = Conveyor.Rows(xs, ys, ws, mv, vel, visibleHalf);
            foreach (var row in conveyors)
                foreach (int si in row.Shelves) board.Shelves[si].X = Conveyor.Wrap(board.Shelves[si].X, row.Loop);

            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var s in board.Shelves)
            {
                float hw = InnerWidth(s) * 0.5f;
                if (!s.Moving)
                {
                    minX = Mathf.Min(minX, VX(s.X) - hw);
                    maxX = Mathf.Max(maxX, VX(s.X) + hw);
                }
                minY = Mathf.Min(minY, s.Y - 0.5f);
                maxY = Mathf.Max(maxY, s.Y + 0.5f);
            }
            if (conveyors.Count > 0)
            {
                minX = Mathf.Min(minX, -VX(visibleHalf));
                maxX = Mathf.Max(maxX, VX(visibleHalf));
            }
            if (board.Shelves.Count == 0) minX = maxX = minY = maxY = 0f;
            Bounds = Rect.MinMaxRect(minX, minY, maxX, maxY);

            foreach (var s in board.Shelves)
            {
                var sv = new ShelfView { Shelf = s };
                sv.Root = new GameObject("Shelf " + s.Index).transform;
                sv.Root.SetParent(root, false);
                sv.Root.localPosition = new Vector3(VX(s.X), s.Y, 0f);
                float iw = InnerWidth(s);

                // cabinet body behind the shelf: one plate per shelf so any shape (cross, diamond...) works
                var plate = SortexArt.NewSliced("Cabinet", sv.Root, SortexArt.Panel, new Vector2(iw + 0.34f, 1.30f), -5);
                plate.transform.localPosition = new Vector3(0f, -0.02f, 0f);
                plate.color = SortexArt.Cabinet;

                // the game's own shelf model (BaseShelf.prefab), pre-rendered: inside behind the items, rim in front
                var inside = SortexArt.NewSprite("Inside", sv.Root, SortexArt.ShelfInside(s.Width), 0);
                var rim = SortexArt.NewSprite("Rim", sv.Root, SortexArt.ShelfRim(s.Width), 30);
                int artWidth = SortexArt.ShelfArtWidth(s.Width);
                if (artWidth != s.Width)
                {
                    float sx = (iw + 0.30f) / (artWidth * SW + 0.30f);
                    inside.transform.localScale = new Vector3(sx, 1f, 1f);
                    rim.transform.localScale = new Vector3(sx, 1f, 1f);
                }

                float innerH = InnerTop - LipTop;
                if (s.Cargo)
                {
                    sv.Cargo = SortexArt.NewSprite("CargoDoor", sv.Root, SortexArt.CargoDoor, 5);
                    sv.Cargo.transform.localPosition = new Vector3(0f, LipTop + innerH * 0.5f, 0f);
                    sv.Cargo.transform.localScale = new Vector3(iw / 1.5625f, innerH, 1f);
                    sv.CargoText = Badge(sv.Root, new Vector3(iw * 0.5f - 0.17f, 0.36f, 0f), new Color(0.43f, 0.29f, 0.84f), out sv.CargoBadge);
                }
                if (s.Shutter)
                {
                    sv.ShutterHint = SortexArt.NewSprite("ShutterHint", sv.Root, SortexArt.ShutterSprite, 31);
                    sv.ShutterHint.transform.localPosition = new Vector3(0f, InnerTop - 0.03f, 0f);
                    sv.ShutterHint.transform.localScale = new Vector3(iw / 1.5625f, 0.06f, 1f);
                    sv.Shutter = SortexArt.NewSprite("Shutter", sv.Root, SortexArt.ShutterSprite, 38);
                    sv.Shutter.transform.localPosition = new Vector3(0f, LipTop + innerH * 0.5f, 0f);
                    sv.Shutter.transform.localScale = new Vector3(iw / 1.5625f, innerH, 1f);
                    sv.Shutter.gameObject.SetActive(false);
                }
                if (s.Locked)
                {
                    sv.Door = SortexArt.NewSprite("LockDoor", sv.Root, SortexArt.LockDoor, 40);
                    sv.Door.transform.localPosition = new Vector3(0f, LipTop + innerH * 0.5f, 0f);
                    sv.Door.transform.localScale = new Vector3(iw / 1.5625f, innerH, 1f);
                    sv.DoorText = SortexArt.NewText("Count", sv.Door.transform, s.LockCount.ToString(), 0.42f, Color.white, 41);
                    sv.DoorText.transform.localScale = new Vector3(1f / sv.Door.transform.localScale.x, 1f / innerH, 1f);
                    sv.DoorText.transform.localPosition = new Vector3(0f, 0.01f, 0f);
                }
                float midY = LipTop + innerH * 0.5f;
                if (s.Ferris)
                {
                    sv.FerrisDoor = SortexArt.NewSprite("RotatingDoor", sv.Root, SortexArt.FerrisDoor, 39);
                    sv.FerrisDoor.transform.localPosition = new Vector3(0f, midY, 0f);
                    SortexArt.Fit(sv.FerrisDoor, iw, innerH);
                    sv.FerrisDoor.color = new Color(1f, 1f, 1f, 0f);
                    int lamps = Mathf.Clamp(s.FerrisStages, 1, 8);
                    float spacing = Mathf.Min(0.36f, (iw - 0.4f) / Mathf.Max(1, lamps));
                    for (int i = 0; i < lamps; i++)
                    {
                        var lamp = SortexArt.NewSprite("Lamp", sv.Root, SortexArt.FerrisLamp(1), 32);
                        lamp.transform.localPosition = new Vector3((i - (lamps - 1) * 0.5f) * spacing, InnerTop + 0.03f, 0f);
                        SortexArt.Fit(lamp, 0.3f, 0.2f);
                        sv.Lamps.Add(lamp);
                    }
                }
                if (s.Iris)
                {
                    var frame = SortexArt.NewSprite("IrisFrame", sv.Root, SortexArt.IrisFrame, 31);
                    frame.transform.localPosition = new Vector3(0f, midY, 0f);
                    SortexArt.Fit(frame, iw + 0.06f, innerH + 0.06f);
                    sv.IrisBlades = SortexArt.NewSprite("IrisBlades", sv.Root, SortexArt.IrisClosed, 39);
                    sv.IrisBlades.transform.localPosition = new Vector3(0f, midY, 0f);
                    SortexArt.Fit(sv.IrisBlades, iw + 0.06f, innerH + 0.06f);
                    sv.IrisShown = s.IrisClosed ? 1f : 0f;
                }
                if (s.FuseItems.Count > 0) BuildFuse(sv, iw, innerH, midY);
                shelves.Add(sv);
            }

            foreach (var row in conveyors) BuildTunnels(row);
        }

        /// <summary>The pipes at both screen edges that moving shelves slide into (MovingShelves1xPipeHorizontal).</summary>
        void BuildTunnels(ConveyorRow row)
        {
            const float mouthCentre = 0.047f;    // pipe_mouth.png: the rim ellipse is 6 px right of the sprite centre
            const float bodyStart = 0.20f;       // where the plain pipe takes over, measured from the rim
            float length = VX(row.Loop) + 2f;
            float y = row.Y - 0.02f;
            for (int side = -1; side <= 1; side += 2)
            {
                var mouth = SortexArt.NewSprite("TunnelMouth", root, SortexArt.PipeMouth, 61);
                mouth.flipX = side > 0;
                mouth.transform.localPosition = new Vector3(side * (VX(visibleHalf) + mouthCentre), y, 0f);
                var body = SortexArt.NewSprite("Tunnel", root, SortexArt.PipeBody, 60);
                float bodyWidth = body.sprite != null ? body.sprite.bounds.size.x : 0.25f;
                body.transform.localScale = new Vector3(length / bodyWidth, 1f, 1f);
                body.transform.localPosition = new Vector3(side * (VX(visibleHalf) + bodyStart + length * 0.5f), y, 0f);
            }
        }

        /// <summary>Fuse box: a lightning tile and one window per item that has to be matched (FuseBoxObjectiveGroup).</summary>
        void BuildFuse(ShelfView sv, float iw, float innerH, float midY)
        {
            var s = sv.Shelf;
            sv.Fuse = new GameObject("FuseBox").transform;
            sv.Fuse.SetParent(sv.Root, false);
            sv.Fuse.localPosition = new Vector3(0f, midY, 0f);
            var frame = SortexArt.NewSprite("Frame", sv.Fuse, SortexArt.FuseFrame, 40);
            SortexArt.Fit(frame, iw + 0.08f, innerH + 0.1f);
            float h = innerH * 0.86f;
            float boltW = h * 156f / 300f;
            int n = s.FuseItems.Count;
            float avail = iw - boltW - 0.18f;
            float winW = Mathf.Min(h * 150f / 296f, avail / n - 0.04f);
            float x = -iw * 0.5f + 0.06f + boltW * 0.5f;
            sv.FuseBolt = SortexArt.NewSprite("Bolt", sv.Fuse, SortexArt.FuseBolt, 41);
            sv.FuseBolt.transform.localPosition = new Vector3(x, 0f, 0f);
            SortexArt.Fit(sv.FuseBolt, boltW, h);
            float start = x + boltW * 0.5f + 0.06f;
            float step = (iw * 0.5f - 0.06f - start) / n;
            for (int i = 0; i < n; i++)
            {
                float cx = start + step * (i + 0.5f);
                var win = SortexArt.NewSprite("Window", sv.Fuse, SortexArt.FuseWindow(false), 41);
                win.transform.localPosition = new Vector3(cx, 0f, 0f);
                SortexArt.Fit(win, winW, h);
                var icon = SortexArt.NewSprite("Goal", sv.Fuse, SortexArt.Item(s.FuseItems[i]), 42);
                icon.transform.localPosition = new Vector3(cx, -0.04f, 0f);
                float size = Mathf.Min(winW * 0.82f, h * 0.6f);
                icon.transform.localScale = Vector3.one * size / SortexArt.ItemSize;
                sv.FuseWindows.Add(win);
                sv.FuseIcons.Add(icon);
            }
        }

        static TextMesh Badge(Transform parent, Vector3 pos, Color color, out SpriteRenderer bg)
        {
            bg = SortexArt.NewSprite("Badge", parent, SortexArt.Circle, 40);
            bg.transform.localPosition = pos;
            bg.transform.localScale = Vector3.one * 0.26f;
            bg.color = color;
            var t = SortexArt.NewText("Count", parent, "", 0.17f, Color.white, 41);
            t.transform.localPosition = pos + new Vector3(0f, 0.005f, 0f);
            return t;
        }

        ItemView CreateItem(Item it)
        {
            var v = new ItemView { Item = it };
            v.Go = new GameObject("Item " + it.Type);
            v.Go.transform.SetParent(root, false);
            v.Icon = v.Go.AddComponent<SpriteRenderer>();
            if (SortexArt.SpriteMaterial != null) v.Icon.sharedMaterial = SortexArt.SpriteMaterial;
            v.Icon.sprite = SortexArt.ItemSprite(it);
            v.ShowsMystery = it.Mystery;
            v.Shadow = SortexArt.NewSprite("Shadow", v.Go.transform, SortexArt.Shadow, 19);
            v.Shadow.transform.localPosition = new Vector3(0f, -SortexArt.ItemSize * 0.5f + 0.02f, 0f);
            v.Shadow.transform.localScale = new Vector3(0.62f, 0.8f, 1f);   // slots are half as wide as the level spacing
            if (it.Frozen)
            {
                v.Ice = SortexArt.NewSprite("Ice", v.Go.transform, SortexArt.Ice, 22);
                v.Ice.transform.localScale = new Vector3(1.06f, 1.06f, 1f);
            }
            items[it.Uid] = v;
            return v;
        }

        // ------------------------------------------------------------------ sync with the model
        /// <summary>Moves every view to where the model says its item is. animate=false snaps.</summary>
        public void Sync(bool animate)
        {
            var alive = new HashSet<int>();
            for (int si = 0; si < board.Shelves.Count; si++)
            {
                var s = board.Shelves[si];
                var sv = shelves[si];
                for (int li = 0; li < s.Layers.Count; li++)
                {
                    var layer = s.Layers[li];
                    for (int k = 0; k < layer.Length; k++)
                    {
                        var it = layer[k];
                        if (it == null) continue;
                        alive.Add(it.Uid);
                        ItemView v;
                        bool isNew = !items.TryGetValue(it.Uid, out v);
                        // deeper rows / behind a cargo door / later stages of a rotating door
                        bool hidden = li >= 2 || li == 1 && (s.Cargo || s.Ferris);
                        if (hidden)
                        {
                            if (!isNew) v.Go.SetActive(false);
                            continue;
                        }
                        if (isNew) v = CreateItem(it);
                        bool appear = isNew || !v.Go.activeSelf;
                        v.Go.SetActive(true);

                        int oldLayer = v.Layer;
                        v.Layer = li;
                        v.Shelf = si;
                        v.Held = false;
                        v.Target = SlotPosition(si, k, li);
                        v.TargetScale = li == 0 ? 1f : BackScale;
                        v.TargetTint = li == 0 ? Color.white : SortexArt.BackLayerTint;
                        v.Speed = 18f;
                        SetOrder(v, li == 0 ? 20 : 10);
                        v.Shadow.enabled = li == 0;

                        if (appear)
                        {
                            v.Go.transform.localPosition = v.Target;
                            v.Go.transform.localScale = animate ? Vector3.zero : Vector3.one * v.TargetScale;
                            v.Icon.color = v.TargetTint;
                            v.Delay = animate ? 0.22f : 0f;
                        }
                        else if (animate && oldLayer == 1 && li == 0)
                        {
                            v.Delay = 0.16f;                // the row moves forward after the match pop
                            v.Speed = 12f;
                        }

                        if (v.ShowsMystery && !it.Mystery)
                        {
                            v.ShowsMystery = false;          // revealed: the stone breaks and the item appears
                            v.Icon.sprite = SortexArt.Item(it.Type);
                            if (animate)
                            {
                                Shatter(v.Go.transform.localPosition, v.TargetScale);
                                v.Go.transform.localScale = Vector3.one * v.TargetScale * 0.55f;
                            }
                        }
                    }
                }

                // lock door
                if (sv.Door != null && sv.DoorOpen < 0f)
                {
                    if (!s.Locked)
                    {
                        sv.DoorOpen = animate ? 0f : 1f;
                        if (!animate) sv.Door.gameObject.SetActive(false);
                    }
                    else
                    {
                        string txt = s.LockCount.ToString();
                        if (sv.DoorText.text != txt)
                        {
                            sv.DoorText.text = txt;
                            sv.DoorPunch = 0.25f;
                        }
                    }
                }
                // shutter
                if (sv.Shutter != null && s.Closed && !sv.Shutter.gameObject.activeSelf)
                {
                    sv.Shutter.gameObject.SetActive(true);
                    sv.ShutterHint.gameObject.SetActive(false);
                    sv.ShutterDrop = animate ? 0f : 1f;
                }
                // cargo door: number of rows hidden behind it
                if (sv.Cargo != null)
                {
                    int hiddenRows = Mathf.Max(0, s.Layers.Count - 1);
                    sv.CargoText.text = hiddenRows.ToString();
                    bool show = hiddenRows > 0;
                    sv.Cargo.enabled = show;
                    sv.CargoText.gameObject.SetActive(show);
                    sv.CargoBadge.enabled = show;
                }
                UpdateDots(sv);
                UpdateObstacles(sv, animate);
            }

            foreach (var kv in items)
            {
                if (alive.Contains(kv.Key) || kv.Value.Dying >= 0f) continue;
                kv.Value.Dying = 0f;
                SetOrder(kv.Value, 24);
                dying.Add(kv.Value);
            }
            foreach (var d in dying) items.Remove(d.Item.Uid);
        }

        void UpdateObstacles(ShelfView sv, bool animate)
        {
            var s = sv.Shelf;
            // rotating door lamps: done stages broken, the current one lit, the rest off
            if (sv.Lamps.Count > 0)
            {
                int left = s.ItemCount == 0 ? 0 : s.Layers.Count;
                int done = Mathf.Clamp(s.FerrisStages - left, 0, sv.Lamps.Count);
                for (int i = 0; i < sv.Lamps.Count; i++)
                    sv.Lamps[i].sprite = SortexArt.FerrisLamp(i < done ? 2 : i == done && left > 0 ? 0 : 1);
            }
            if (!animate && sv.IrisBlades != null) sv.IrisShown = s.IrisClosed ? 1f : 0f;
            // fuse box objectives
            if (sv.Fuse != null && sv.Fuse.gameObject.activeSelf)
            {
                for (int i = 0; i < sv.FuseWindows.Count; i++)
                {
                    bool done = s.FuseDone.Contains(s.FuseItems[i]);
                    sv.FuseWindows[i].sprite = SortexArt.FuseWindow(done);
                    sv.FuseIcons[i].color = done ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                }
                if (!s.FuseClosed && sv.FuseOpen < 0f)
                {
                    sv.FuseOpen = animate ? 0f : 1f;
                    if (!animate) sv.Fuse.gameObject.SetActive(false);
                    else Burst(sv.Root.localPosition + new Vector3(0f, LipTop + (InnerTop - LipTop) * 0.5f, 0f), new Color(0.55f, 0.85f, 1f), 12);
                }
            }
        }

        void UpdateDots(ShelfView sv)
        {
            // one small dot per row hidden behind the visible back row (cargo shelves show a number instead)
            int hidden = sv.Shelf.Cargo ? 0 : Mathf.Max(0, sv.Shelf.Layers.Count - 2);
            while (sv.Dots.Count < hidden)
            {
                var d = SortexArt.NewSprite("RowDot", sv.Root, SortexArt.Circle, 31);
                d.transform.localScale = Vector3.one * 0.06f;
                d.color = new Color(0.2f, 0.3f, 0.5f, 0.9f);
                sv.Dots.Add(d);
            }
            for (int i = 0; i < sv.Dots.Count; i++)
            {
                sv.Dots[i].enabled = i < hidden;
                float x = (i - (hidden - 1) * 0.5f) * 0.1f;
                sv.Dots[i].transform.localPosition = new Vector3(x, -0.485f, 0f);
            }
        }

        static void SetOrder(ItemView v, int order)
        {
            v.Icon.sortingOrder = order;
            if (v.Ice != null) v.Ice.sortingOrder = order + 2;
            v.Shadow.sortingOrder = order - 1;
        }

        // ------------------------------------------------------------------ interaction feedback
        public Vector3 ItemPosition(Item it)
        {
            ItemView v;
            return items.TryGetValue(it.Uid, out v) ? v.Go.transform.localPosition : Vector3.zero;
        }

        /// <summary>Lifts an item (drag or tap selection).</summary>
        public void Hold(Item it, Vector3? worldTarget, bool dragging)
        {
            ItemView v;
            if (!items.TryGetValue(it.Uid, out v)) return;
            if (worldTarget.HasValue)
            {
                v.Target = worldTarget.Value;
                v.Held = dragging;                 // a tapped (lifted) item keeps riding a moving shelf
                v.TargetScale = dragging ? 1.15f : 1.08f;
                v.Speed = dragging ? 40f : 20f;
                v.Delay = 0f;
                SetOrder(v, 100);
                v.Shadow.enabled = false;
            }
        }

        public void Release(Item it)
        {
            ItemView v;
            if (!items.TryGetValue(it.Uid, out v)) return;
            v.Speed = 16f;
        }

        public void Reject(Item it)
        {
            ItemView v;
            if (items.TryGetValue(it.Uid, out v)) v.Shake = 0.3f;
        }

        public void ShakeShelf(int shelf)
        {
            var sv = shelves[shelf];
            if (sv.Door != null) sv.DoorPunch = 0.3f;
        }

        /// <summary>Stone chunks of a mystery item flying apart (MysteryItemParticle).</summary>
        void Shatter(Vector3 at, float scale)
        {
            for (int i = 0; i < 7; i++)
            {
                var chunk = SortexArt.NewSprite("Stone", root, SortexArt.StoneChunk(i), 112);
                chunk.transform.localPosition = at + new Vector3(Random.Range(-0.2f, 0.2f), Random.Range(-0.15f, 0.25f), 0f) * scale;
                float a = Random.Range(0.15f, Mathf.PI - 0.15f);
                fx.Add(new Fx
                {
                    Sr = chunk,
                    Velocity = new Vector3(Mathf.Cos(a) * 2.4f, Mathf.Sin(a) * 2.6f + 0.6f, 0f) * Random.Range(0.6f, 1.1f),
                    Gravity = 9f,
                    Life = Random.Range(0.45f, 0.65f),
                    StartScale = Random.Range(0.7f, 1.15f) * scale,
                    EndScale = 0.35f * scale,
                    Spin = Random.Range(-540f, 540f)
                });
            }
            var puff = SortexArt.NewSprite("Dust", root, SortexArt.Glow, 111);
            puff.transform.localPosition = at;
            puff.color = new Color(0.8f, 0.8f, 0.85f, 0.7f);
            fx.Add(new Fx { Sr = puff, Life = 0.35f, StartScale = 0.8f * scale, EndScale = 2.2f * scale });
        }

        public void PlayMatch(int shelf)
        {
            var s = board.Shelves[shelf];
            var c = new Vector3(VX(s.X), s.Y + FrontY, 0f);
            var glow = SortexArt.NewSprite("MatchGlow", root, SortexArt.Glow, 110);
            glow.transform.localPosition = c;
            glow.color = new Color(1f, 0.86f, 0.4f, 0.9f);
            fx.Add(new Fx { Sr = glow, Life = 0.4f, StartScale = 1.2f, EndScale = 3.4f });
            for (int i = 0; i < 9; i++)
            {
                var sp = SortexArt.NewSprite("Sparkle", root, SortexArt.Sparkle, 111);
                sp.transform.localPosition = c + new Vector3(Random.Range(-1.1f, 1.1f), Random.Range(-0.2f, 0.2f), 0f);
                sp.color = i % 3 == 0 ? new Color(1f, 0.95f, 0.6f) : Color.white;
                float a = Random.Range(0f, Mathf.PI * 2f);
                fx.Add(new Fx
                {
                    Sr = sp,
                    Velocity = new Vector3(Mathf.Cos(a), Mathf.Abs(Mathf.Sin(a)) + 0.4f, 0f) * Random.Range(1.2f, 2.6f),
                    Life = Random.Range(0.35f, 0.6f),
                    StartScale = Random.Range(0.25f, 0.45f),
                    EndScale = 0f,
                    Spin = Random.Range(-360f, 360f)
                });
            }
        }

        /// <summary>Effects for the newer obstacles after a move (the model already changed; Sync placed the items).</summary>
        public void PlayResult(MoveResult r)
        {
            foreach (var it in r.MatchedItems)
            {
                ItemView v;
                Vector3 at;
                if (items.TryGetValue(it.Uid, out v)) at = v.Go.transform.localPosition;
                else
                {
                    var dv = dying.Find(d => d.Item == it);
                    if (dv == null) continue;
                    at = dv.Go.transform.localPosition;
                }
                if (it.Kind == ItemKind.Pinata) Burst(at, PinataColor(it.PinataColor), 9);
                else if (it.Kind == ItemKind.Egg) Hatch(at);
            }
            foreach (int si in r.FerrisTurned)
                if (si >= 0 && si < shelves.Count && shelves[si].FerrisDoor != null) shelves[si].FerrisTurn = 0f;
        }

        static Color PinataColor(int c)
        {
            switch (c)
            {
                case 0: return new Color(1f, 0.35f, 0.3f);
                case 1: return new Color(1f, 0.9f, 0.3f);
                case 2: return new Color(0.4f, 1f, 0.4f);
                case 3: return new Color(0.35f, 0.9f, 1f);
                case 4: return new Color(0.45f, 0.45f, 1f);
                default: return new Color(1f, 0.4f, 0.85f);
            }
        }

        /// <summary>Confetti (piñata) or sparks (fuse box).</summary>
        void Burst(Vector3 at, Color color, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var sp = SortexArt.NewSprite("Confetti", root, i % 2 == 0 ? SortexArt.Sparkle : SortexArt.Circle, 112);
                sp.transform.localPosition = at;
                sp.color = i % 3 == 0 ? Color.white : Color.Lerp(color, new Color(Random.value, Random.value, Random.value), 0.35f);
                float a = Random.Range(0.1f, Mathf.PI - 0.1f);
                fx.Add(new Fx
                {
                    Sr = sp,
                    Velocity = new Vector3(Mathf.Cos(a) * 2.6f, Mathf.Sin(a) * 3f + 0.5f, 0f) * Random.Range(0.6f, 1.1f),
                    Gravity = 8f,
                    Life = Random.Range(0.45f, 0.7f),
                    StartScale = Random.Range(0.12f, 0.22f),
                    EndScale = 0.05f,
                    Spin = Random.Range(-600f, 600f)
                });
            }
        }

        /// <summary>A dragon egg hatches: shell pieces fall, a glow rises (the dragon flies off).</summary>
        void Hatch(Vector3 at)
        {
            for (int i = 0; i < 6; i++)
            {
                var shell = SortexArt.NewSprite("Shell", root, SortexArt.StoneChunk(i), 112);
                shell.color = new Color(0.95f, 0.45f, 0.85f);
                shell.transform.localPosition = at;
                float a = Random.Range(0.2f, Mathf.PI - 0.2f);
                fx.Add(new Fx
                {
                    Sr = shell,
                    Velocity = new Vector3(Mathf.Cos(a) * 2f, Mathf.Sin(a) * 2.2f, 0f),
                    Gravity = 9f, Life = 0.55f, StartScale = 0.8f, EndScale = 0.3f, Spin = Random.Range(-500f, 500f)
                });
            }
            var glow = SortexArt.NewSprite("Dragon", root, SortexArt.Glow, 113);
            glow.transform.localPosition = at;
            glow.color = new Color(1f, 0.6f, 0.95f, 0.9f);
            fx.Add(new Fx { Sr = glow, Velocity = new Vector3(0.6f, 4f, 0f), Life = 0.7f, StartScale = 0.9f, EndScale = 0.3f });
        }

        // ------------------------------------------------------------------ per-frame animation
        public void Tick(float dt)
        {
            MoveConveyors(dt);
            foreach (var v in items.Values) Animate(v, dt);

            for (int i = dying.Count - 1; i >= 0; i--)
            {
                var v = dying[i];
                v.Dying += dt;
                float t = v.Dying / 0.28f;
                float s = t < 0.35f ? Mathf.Lerp(1f, 1.25f, t / 0.35f) : Mathf.Lerp(1.25f, 0f, (t - 0.35f) / 0.65f);
                v.Go.transform.localScale = Vector3.one * Mathf.Max(0f, s);
                if (t >= 1f)
                {
                    Object.Destroy(v.Go);
                    dying.RemoveAt(i);
                }
            }

            foreach (var sv in shelves)
            {
                AnimateObstacles(sv, dt);
                if (sv.Shutter != null && sv.ShutterDrop >= 0f && sv.ShutterDrop < 1f)
                {
                    sv.ShutterDrop = Mathf.Min(1f, sv.ShutterDrop + dt / 0.35f);
                    float innerH = InnerTop - LipTop;
                    float h = innerH * sv.ShutterDrop;
                    var sc = sv.Shutter.transform.localScale;
                    sc.y = Mathf.Max(0.001f, h);
                    sv.Shutter.transform.localScale = sc;
                    sv.Shutter.transform.localPosition = new Vector3(0f, InnerTop - h * 0.5f, 0f);
                }
                if (sv.Door == null) continue;
                if (sv.DoorPunch > 0f)
                {
                    sv.DoorPunch -= dt;
                    float p = Mathf.Max(0f, sv.DoorPunch);
                    sv.DoorText.transform.localScale = new Vector3(1f / sv.Door.transform.localScale.x,
                        1f / sv.Door.transform.localScale.y, 1f) * (1f + p * 1.6f);
                    sv.DoorText.color = Color.Lerp(Color.white, SortexArt.Orange, p * 4f);
                }
                if (sv.DoorOpen >= 0f && sv.Door.gameObject.activeSelf)
                {
                    sv.DoorOpen += dt / 0.45f;
                    float t = Mathf.Clamp01(sv.DoorOpen);
                    var lp = sv.Door.transform.localPosition;
                    lp.y = LipTop + (InnerTop - LipTop) * 0.5f + t * t * 1.1f;
                    sv.Door.transform.localPosition = lp;
                    var c = Color.white;
                    c.a = 1f - t;
                    sv.Door.color = c;
                    var tc = sv.DoorText.color;
                    tc.a = 1f - t;
                    sv.DoorText.color = tc;
                    if (t >= 1f) sv.Door.gameObject.SetActive(false);
                }
            }

            for (int i = fx.Count - 1; i >= 0; i--)
            {
                var f = fx[i];
                f.Age += dt;
                float t = f.Age / f.Life;
                if (t >= 1f)
                {
                    Object.Destroy(f.Sr.gameObject);
                    fx.RemoveAt(i);
                    continue;
                }
                f.Sr.transform.localPosition += f.Velocity * dt;
                if (f.Gravity > 0f) f.Velocity.y -= f.Gravity * dt;
                else f.Velocity *= 1f - 2.5f * dt;
                f.Sr.transform.localScale = Vector3.one * Mathf.Lerp(f.StartScale, f.EndScale, t);
                f.Sr.transform.Rotate(0f, 0f, f.Spin * dt);
                var c = f.Sr.color;
                c.a = 1f - t * t;
                f.Sr.color = c;
            }
        }

        void AnimateObstacles(ShelfView sv, float dt)
        {
            if (sv.FerrisDoor != null && sv.FerrisTurn >= 0f)
            {
                // the door swings shut, the stage turns, the door opens on the next stage
                sv.FerrisTurn += dt / 0.8f;
                float t = Mathf.Clamp01(sv.FerrisTurn);
                float a = t < 0.3f ? t / 0.3f : t < 0.65f ? 1f : 1f - (t - 0.65f) / 0.35f;
                sv.FerrisDoor.color = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
                if (t >= 1f) sv.FerrisTurn = -1f;
            }
            if (sv.IrisBlades != null)
            {
                float target = sv.Shelf.IrisClosed ? 1f : 0f;
                sv.IrisShown = Mathf.MoveTowards(sv.IrisShown, target, dt / 0.25f);
                sv.IrisBlades.color = new Color(1f, 1f, 1f, sv.IrisShown);
                sv.IrisBlades.enabled = sv.IrisShown > 0.01f;
            }
            if (sv.Fuse != null && sv.FuseOpen >= 0f && sv.Fuse.gameObject.activeSelf)
            {
                sv.FuseOpen += dt / 0.4f;
                float t = Mathf.Clamp01(sv.FuseOpen);
                sv.Fuse.localScale = Vector3.one * (1f + 0.25f * t);
                foreach (var r in sv.Fuse.GetComponentsInChildren<SpriteRenderer>())
                {
                    var c = r.color;
                    c.a = 1f - t;
                    r.color = c;
                }
                if (t >= 1f) sv.Fuse.gameObject.SetActive(false);
            }
        }

        /// <summary>Slides the moving rows; shelves that leave one tunnel come back out of the other.</summary>
        void MoveConveyors(float dt)
        {
            foreach (var row in conveyors)
                foreach (int si in row.Shelves)
                {
                    var s = board.Shelves[si];
                    float nx = Conveyor.Wrap(s.X + row.Velocity * dt, row.Loop);
                    float dx = VX(nx - s.X);
                    s.X = nx;
                    shelves[si].Root.localPosition = new Vector3(VX(nx), s.Y, 0f);
                    if (dx == 0f) continue;
                    foreach (var v in items.Values)
                    {
                        if (v.Shelf != si || v.Held) continue;
                        v.Target.x += dx;
                        var p = v.Go.transform.localPosition;
                        p.x += dx;
                        v.Go.transform.localPosition = p;
                    }
                }
        }

        static void Animate(ItemView v, float dt)
        {
            if (!v.Go.activeSelf) return;
            var tr = v.Go.transform;
            if (v.Delay > 0f)
            {
                v.Delay -= dt;
                return;
            }
            float k = 1f - Mathf.Exp(-v.Speed * dt);
            var pos = Vector3.Lerp(tr.localPosition, v.Target, k);
            float scale = Mathf.Lerp(tr.localScale.y, v.TargetScale, Mathf.Min(1f, k * 1.2f));

            if (v.Shake > 0f)
            {
                v.Shake -= dt;
                pos.x = v.Target.x + Mathf.Sin(v.Shake * 60f) * 0.06f * (v.Shake / 0.3f);
            }

            var tint = Color.Lerp(v.Icon.color, v.TargetTint, k);
            tr.localPosition = pos;
            tr.localScale = new Vector3(scale, scale, 1f);
            v.Icon.color = tint;
        }
    }
}
