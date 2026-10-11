// Pure game rules for the Sort Express prototype. No UnityEngine dependency, so the exact same
// rules run inside Unity and inside the offline Monte Carlo simulator (tools/sim).
//
// Rules (confirmed against the live game, or read from the game's data, tutorial pictures and class names):
//  * Each shelf shows its front layer; the layers behind it are visible but not playable.
//  * Drag an item from a shelf's front layer into any empty front slot (also on the same shelf).
//  * Three identical items side by side on the front layer are matched and removed (a 3-slot shelf: all
//    three; a wide shelf: any three neighbours, as in the game's MultiShelf tutorial picture).
//  * When a front layer becomes completely empty, the next layer moves forward.
//  * Locked shelf (lmc = N): cannot be used until N matches have been made anywhere.
//  * Frozen item (ib): cannot be moved, it has to be matched where it stands.
//  * Mystery item (imy): a stone sculpture until a match happens on its shelf or a neighbouring shelf.
//  * Shutter shelf (icl): once it has no items left it closes for good (no more parking there).
//  * Cargo door shelf (isdp, "Dispenser" in the game's code): the rows behind the front row are hidden.
//  * Moving shelves (im): a row slides sideways and loops through pipes (see Conveyor). Same rules; the
//    view only lets the player use the shelves that are on screen.
//  * Rotating door (if, "Ferris"): every row is a stage behind a door; only the current stage is visible.
//    When it is cleared the door turns and the next stage comes in. Lamps show the stages.
//  * Piñata (it 5): matched in threes of the same colour (pci); each one drops the item inside it (pcii)
//    into its slot.
//  * Fuse box (fbi): the shelf is closed until every listed item has been matched once, anywhere.
//  * Iris (irs): opens and closes after every move (irs 1 = starts closed). A closed iris cannot be used.
//  * Dragon egg (it 7): sorted like an item; three eggs side by side hatch and the dragons fly away.
//  * Item generator (ig, "it -1"): at the start its waves (items, gaps, piñatas, eggs...) fill the generated
//    slots and then new rows behind every shelf of its group, row by row, until they are empty.
//    (Level 6 has no generated slot but 146 entries in its generator: they can only go into back rows.)
//  * Win when every item is cleared. Lose when the timer ends or no move is left.
using System;
using System.Collections.Generic;

namespace Sortex.Core
{
    public enum ItemKind { Normal, Pinata, Egg }

    public sealed class Item
    {
        public int Uid;
        public string Type;
        public ItemKind Kind;
        public bool Frozen;
        public bool Mystery;   // true while the identity is hidden
        public bool WasMystery;
        public int PinataColor;
        public string Content;  // piñata: the item inside (null = nothing)

        public bool Movable { get { return !Frozen; } }

        public Item Clone()
        {
            return new Item
            {
                Uid = Uid, Type = Type, Kind = Kind, Frozen = Frozen, Mystery = Mystery, WasMystery = WasMystery,
                PinataColor = PinataColor, Content = Content
            };
        }
    }

    public sealed class Shelf
    {
        public int Index;
        public string Id;
        public string Gid;
        public float X;
        public float Y;
        public int Width;
        public bool Locked;
        public int LockCount;
        public bool Shutter;
        public bool Closed;        // shutter came down: the shelf cannot be used any more
        public bool Cargo;
        public bool Moving;
        public float Velocity;     // moving shelves: signed speed in world units per second
        public bool Ferris;
        public int FerrisStages;   // stages at the start (one lamp each)
        public bool Iris;
        public bool IrisClosed;
        public readonly List<string> FuseItems = new List<string>();
        public readonly List<string> FuseDone = new List<string>();
        public readonly List<Item[]> Layers = new List<Item[]>();
        public readonly List<int> Neighbors = new List<int>();

        public Item[] Front { get { return Layers[0]; } }

        public bool FuseClosed { get { return FuseItems.Count > 0 && FuseDone.Count < FuseItems.Count; } }

        /// <summary>Items can be taken from this shelf.</summary>
        public bool CanGive { get { return !Locked && !FuseClosed && !IrisClosed; } }

        /// <summary>Items can be put on this shelf.</summary>
        public bool CanTake { get { return CanGive && !Closed; } }

        public bool FrontEmpty
        {
            get
            {
                var f = Layers[0];
                for (int i = 0; i < f.Length; i++) if (f[i] != null) return false;
                return true;
            }
        }

        /// <summary>Everything left on the shelf.</summary>
        public int ItemCount
        {
            get
            {
                int n = 0;
                foreach (var l in Layers) foreach (var it in l) if (it != null) n++;
                return n;
            }
        }

        public Shelf Clone()
        {
            var s = new Shelf
            {
                Index = Index, Id = Id, Gid = Gid, X = X, Y = Y, Width = Width, Locked = Locked, LockCount = LockCount,
                Shutter = Shutter, Closed = Closed, Cargo = Cargo, Moving = Moving, Velocity = Velocity,
                Ferris = Ferris, FerrisStages = FerrisStages, Iris = Iris, IrisClosed = IrisClosed
            };
            s.FuseItems.AddRange(FuseItems);
            s.FuseDone.AddRange(FuseDone);
            foreach (var l in Layers)
            {
                var c = new Item[l.Length];
                for (int i = 0; i < l.Length; i++) c[i] = l[i] == null ? null : l[i].Clone();
                s.Layers.Add(c);
            }
            s.Neighbors.AddRange(Neighbors);
            return s;
        }
    }

    public enum MoveFail { None, NoItem, SourceLocked, ItemFrozen, TargetLocked, TargetClosed, TargetOccupied, SameSlot, BadIndex }

    public sealed class MoveResult
    {
        public bool Ok;
        public MoveFail Fail;
        public Item Moved;
        public bool Matched;
        public string MatchedType;
        public int MatchedShelf = -1;
        public readonly List<Item> MatchedItems = new List<Item>();
        public readonly List<int> AdvancedShelves = new List<int>();
        public readonly List<int> Unlocked = new List<int>();
        public readonly List<int> LockChanged = new List<int>();
        public readonly List<Item> Revealed = new List<Item>();
        public readonly List<int> Closed = new List<int>();
        public readonly List<Item> Released = new List<Item>();      // items that dropped out of piñatas
        public readonly List<int> FuseProgress = new List<int>();    // fuse boxes that lit an objective
        public readonly List<int> FuseOpened = new List<int>();
        public readonly List<int> FerrisTurned = new List<int>();
        public bool IrisToggled;
        public readonly List<Item> Painted = new List<Item>();      // painter booster: items that became aliens
        public readonly List<Item> Shuffled = new List<Item>();     // magic hat: items that changed place
        public bool Won;
        public bool Stuck;
    }

    public sealed partial class Board
    {
        public const float SlotWidth = 3.13f / 3f;   // world units, from Sort Express shelf spacing
        public const float ShelfHeight = 1f;

        public readonly List<Shelf> Shelves = new List<Shelf>();
        public int TotalItems;
        public int RemainingItems;
        public int Matches;
        public int Moves;

        /// <summary>Items placed by the level's generators.</summary>
        public int GeneratedItems;

        int nextUid = 1;

        public bool IsWon { get { return RemainingItems == 0; } }

        public static Board FromLevel(LevelData level)
        {
            return FromLevel(level, 1);
        }

        /// <summary>seed: shuffles the item generators (same seed = same level).</summary>
        public static Board FromLevel(LevelData level, int seed)
        {
            var b = new Board();
            for (int si = 0; si < level.shelves.Count; si++)
            {
                var sd = level.shelves[si];
                var s = new Shelf
                {
                    Index = si,
                    Id = sd.id,
                    Gid = sd.gid,
                    X = sd.x,
                    Y = sd.y,
                    Width = Math.Max(1, sd.width),
                    Locked = sd.lockCount > 0,
                    LockCount = sd.lockCount,
                    Shutter = sd.shutter,
                    Cargo = sd.cargo,
                    Moving = sd.moving,
                    Velocity = sd.moving ? sd.moveDir * sd.moveSpeed : 0f,
                    Ferris = sd.ferris,
                    Iris = sd.iris,
                    IrisClosed = sd.iris && sd.irisClosed
                };
                s.FuseItems.AddRange(sd.fuseItems);
                foreach (var layer in sd.layers)
                {
                    var items = new Item[s.Width];
                    for (int k = 0; k < s.Width && k < layer.Length; k++)
                    {
                        var slot = layer[k];
                        if (slot == null || !slot.IsFilled) continue;
                        items[k] = b.Make(slot);
                        b.TotalItems += ItemsToClear(slot);
                    }
                    s.Layers.Add(items);
                }
                if (s.Layers.Count == 0) s.Layers.Add(new Item[s.Width]);
                b.Shelves.Add(s);
            }

            b.BuildFeeds(level, seed);

            foreach (var s in b.Shelves)
            {
                // Rows the generator added but could not fill are dropped (the level's own rows stay as designed),
                // and a level may start with an empty front layer: bring the first filled layer forward.
                int designed = Math.Max(1, level.shelves[s.Index].layers.Count);
                for (int li = s.Layers.Count - 1; li >= designed; li--)
                {
                    bool empty = true;
                    foreach (var it in s.Layers[li]) if (it != null) { empty = false; break; }
                    if (empty) s.Layers.RemoveAt(li);
                }
                while (s.Layers.Count > 1 && s.FrontEmpty) s.Layers.RemoveAt(0);
                if (s.Shutter && s.ItemCount == 0) s.Closed = true;
                s.FerrisStages = s.Ferris ? s.Layers.Count : 0;
            }
            b.RemainingItems = b.TotalItems;
            b.ComputeNeighbors();
            b.LinkConveyorEnds();
            return b;
        }

        static int ItemsToClear(SlotData s)
        {
            if (s.IsPinata) return string.IsNullOrEmpty(s.pinataContent) ? 1 : 2;
            return s.IsFilled ? 1 : 0;
        }

        Item Make(SlotData c)
        {
            var it = new Item { Uid = nextUid++, Frozen = c.frozen, Mystery = c.mystery, WasMystery = c.mystery };
            if (c.IsPinata)
            {
                it.Kind = ItemKind.Pinata;
                it.PinataColor = c.pinataColor;
                it.Content = string.IsNullOrEmpty(c.pinataContent) ? null : c.pinataContent;
                it.Type = "pinata#" + c.pinataColor;
                it.Mystery = it.WasMystery = false;
            }
            else if (c.IsEgg)
            {
                it.Kind = ItemKind.Egg;
                it.Type = string.IsNullOrEmpty(c.iid) ? "dragon_egg_0" : c.iid;
                it.Mystery = it.WasMystery = false;
            }
            else it.Type = c.iid;
            return it;
        }

        Item MakeNormal(string type)
        {
            return new Item { Uid = nextUid++, Type = type };
        }

        // ================================================================== item generator
        /// <summary>
        /// Generators: the waves fill the generated slots of the group's shelves and then new rows behind them,
        /// row by row across the shelves, until they are empty. Every slot draws an entry with the wave's weights:
        /// a type already in this row (slw), in the row in front (blw) or any type (rw).
        /// </summary>
        void BuildFeeds(LevelData level, int seed)
        {
            var rng = new Random(seed * 7919 + 17);
            foreach (var g in level.generators)
            {
                var group = new List<int>();
                for (int si = 0; si < Shelves.Count; si++)
                    if (level.shelves[si].gid == g.id) group.Add(si);
                if (group.Count == 0) continue;

                var bags = new List<Bag>();
                foreach (var w in g.waves)
                {
                    var bag = new Bag { Wave = w };
                    foreach (var e in w.entries)
                    {
                        string key = Key(e.Key);
                        List<SlotData> list;
                        if (!bag.Entries.TryGetValue(key, out list))
                        {
                            list = new List<SlotData>();
                            bag.Entries[key] = list;
                            bag.Keys.Add(key);
                        }
                        for (int n = 0; n < e.Value; n++) list.Add(e.Key);
                        bag.Left += e.Value;
                    }
                    if (bag.Left > 0) bags.Add(bag);
                }
                if (bags.Count == 0) continue;

                int wave = 0;
                var placed = new List<HashSet<string>>();
                for (int depth = 0; wave < bags.Count && depth < 64; depth++)
                {
                    placed.Add(new HashSet<string>());
                    foreach (int si in group)
                    {
                        if (wave >= bags.Count) break;
                        var s = Shelves[si];
                        var sd = level.shelves[si];
                        bool existing = depth < sd.layers.Count;
                        // a fuse box only gets its own generated slots (no extra rows behind it)
                        if (!existing && s.FuseItems.Count > 0) continue;
                        if (!existing && depth >= s.Layers.Count) s.Layers.Add(new Item[s.Width]);
                        var layer = s.Layers[depth];
                        for (int k = 0; k < s.Width; k++)
                        {
                            if (existing && (k >= sd.layers[depth].Length || sd.layers[depth][k] == null || !sd.layers[depth][k].IsGenerated)) continue;
                            while (wave < bags.Count && bags[wave].Left == 0) wave++;
                            if (wave >= bags.Count) break;
                            var e = Draw(bags[wave], layer, k, depth, placed, s.FuseItems, rng);
                            if (!e.IsFilled) continue;
                            layer[k] = Make(e);
                            placed[depth].Add(Key(e));
                            TotalItems += ItemsToClear(e);
                            GeneratedItems += ItemsToClear(e);
                        }
                    }
                }
                BreakReadyThrees(group, rng);
            }
        }

        /// <summary>
        /// When a wave ends with only one type left it can deal three of a kind side by side: swap one of them
        /// with another generated item of the group, so no row starts as a ready match.
        /// </summary>
        void BreakReadyThrees(List<int> group, Random rng)
        {
            var spots = new List<int[]>();
            foreach (int si in group)
                for (int li = 0; li < Shelves[si].Layers.Count; li++)
                    for (int k = 0; k < Shelves[si].Width; k++)
                        if (Shelves[si].Layers[li][k] != null) spots.Add(new[] { si, li, k });
            if (spots.Count < 4) return;
            foreach (int si in group)
                foreach (var layer in Shelves[si].Layers)
                    for (int i = 0; i + 2 < layer.Length; i++)
                    {
                        if (layer[i] == null || layer[i + 1] == null || layer[i + 2] == null) continue;
                        if (layer[i].Type != layer[i + 1].Type || layer[i + 1].Type != layer[i + 2].Type) continue;
                        for (int attempt = 0; attempt < 40; attempt++)
                        {
                            var o = spots[rng.Next(spots.Count)];
                            var other = Shelves[o[0]].Layers[o[1]];
                            var a = layer[i + 1];
                            var b = other[o[2]];
                            if (b == null || b.Type == a.Type) continue;
                            if (Shelves[o[0]].FuseItems.Contains(a.Type) || Shelves[si].FuseItems.Contains(b.Type)) continue;
                            layer[i + 1] = b;
                            other[o[2]] = a;
                            if (CompletesThree(other, o[2], a.Type) || CompletesThree(layer, i + 1, b.Type))
                            {
                                other[o[2]] = b;    // would make a new three: undo, try another spot
                                layer[i + 1] = a;
                                continue;
                            }
                            break;
                        }
                    }
        }

        sealed class Bag
        {
            public SpawnWave Wave;
            public readonly Dictionary<string, List<SlotData>> Entries = new Dictionary<string, List<SlotData>>();
            public readonly List<string> Keys = new List<string>();
            public int Left;
        }

        static string Key(SlotData e)
        {
            if (e.IsPinata) return "pinata#" + e.pinataColor;
            return e.IsFilled ? e.iid : "";
        }

        static SlotData Draw(Bag bag, Item[] layer, int k, int depth, List<HashSet<string>> placed, List<string> exclude, Random rng)
        {
            var all = new List<string>();
            foreach (var key in bag.Keys) if (bag.Entries[key].Count > 0) all.Add(key);
            // never hand out a ready-made three in a row, nor a fuse box's own objective items into the box
            var allowed = all.FindAll(key => key == "" || !CompletesThree(layer, k, key) && !exclude.Contains(key));
            if (allowed.Count == 0) allowed = all.FindAll(key => !exclude.Contains(key));
            if (allowed.Count == 0) allowed = all;

            var w = bag.Wave;
            List<string> pick = allowed;
            if (rng.NextDouble() < w.ssp)
            {
                var same = allowed.FindAll(key => key != "" && placed[depth].Contains(key));
                var back = allowed.FindAll(key => key != "" && depth > 0 && placed[depth - 1].Contains(key));
                double ws = same.Count > 0 ? Math.Max(0, w.slw) : 0;
                double wb = back.Count > 0 ? Math.Max(0, w.blw) : 0;
                double wr = Math.Max(0, w.rw);
                double total = ws + wb + wr;
                if (total > 0)
                {
                    double x = rng.NextDouble() * total;
                    pick = x < ws ? same : x < ws + wb ? back : allowed;
                }
            }
            // by remaining copies, so gaps (one key, many copies) spread through the rows like in the game
            int sum = 0;
            foreach (var key in pick) sum += bag.Entries[key].Count;
            int roll = rng.Next(sum);
            string chosen = pick[pick.Count - 1];
            foreach (var key in pick)
            {
                roll -= bag.Entries[key].Count;
                if (roll < 0) { chosen = key; break; }
            }
            var list = bag.Entries[chosen];
            var entry = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            bag.Left--;
            return entry;
        }

        static bool CompletesThree(Item[] layer, int k, string key)
        {
            int left = 0, right = 0;
            for (int i = k - 1; i >= 0 && layer[i] != null && layer[i].Type == key; i--) left++;
            for (int i = k + 1; i < layer.Length && layer[i] != null && layer[i].Type == key; i++) right++;
            return left + right + 1 >= 3;
        }

        // ================================================================== state
        public Board Clone()
        {
            var b = new Board
            {
                TotalItems = TotalItems, RemainingItems = RemainingItems, Matches = Matches, Moves = Moves,
                GeneratedItems = GeneratedItems, nextUid = nextUid
            };
            foreach (var s in Shelves) b.Shelves.Add(s.Clone());
            return b;
        }

        void ComputeNeighbors()
        {
            for (int a = 0; a < Shelves.Count; a++)
            {
                for (int c = a + 1; c < Shelves.Count; c++)
                {
                    var A = Shelves[a];
                    var C = Shelves[c];
                    float halfA = A.Width * SlotWidth * 0.5f;
                    float halfC = C.Width * SlotWidth * 0.5f;
                    float dx = Math.Abs(A.X - C.X);
                    float dy = Math.Abs(A.Y - C.Y);
                    bool sideBySide = dy < 0.3f && dx <= halfA + halfC + 0.25f;
                    float overlap = Math.Min(A.X + halfA, C.X + halfC) - Math.Max(A.X - halfA, C.X - halfC);
                    bool stacked = dy > 0.5f && dy <= ShelfHeight * 1.45f && overlap > 0.1f;
                    if (sideBySide || stacked)
                    {
                        A.Neighbors.Add(c);
                        C.Neighbors.Add(a);
                    }
                }
            }
        }

        /// <summary>Moving rows loop: the last shelf of a row also touches the first one.</summary>
        void LinkConveyorEnds()
        {
            var xs = new List<float>(); var ys = new List<float>(); var ws = new List<int>();
            var mv = new List<bool>(); var vs = new List<float>();
            foreach (var s in Shelves) { xs.Add(s.X); ys.Add(s.Y); ws.Add(s.Width); mv.Add(s.Moving); vs.Add(s.Velocity); }
            float half = Conveyor.VisibleHalfWidth(xs, ws, mv);
            foreach (var row in Conveyor.Rows(xs, ys, ws, mv, vs, half))
            {
                if (row.Shelves.Count < 3) continue;
                int a = row.Shelves[0], c = row.Shelves[row.Shelves.Count - 1];
                if (!Shelves[a].Neighbors.Contains(c)) Shelves[a].Neighbors.Add(c);
                if (!Shelves[c].Neighbors.Contains(a)) Shelves[c].Neighbors.Add(a);
            }
        }

        bool ValidSlot(int shelf, int slot)
        {
            return shelf >= 0 && shelf < Shelves.Count && slot >= 0 && slot < Shelves[shelf].Width;
        }

        // ================================================================== moves
        public bool CanPick(int shelf, int slot)
        {
            if (!ValidSlot(shelf, slot)) return false;
            var s = Shelves[shelf];
            if (!s.CanGive) return false;
            var it = s.Front[slot];
            return it != null && it.Movable;
        }

        public bool CanPlace(int shelf, int slot)
        {
            if (!ValidSlot(shelf, slot)) return false;
            var s = Shelves[shelf];
            return s.CanTake && s.Front[slot] == null;
        }

        public MoveFail Check(int fs, int fslot, int ts, int tslot)
        {
            if (!ValidSlot(fs, fslot) || !ValidSlot(ts, tslot)) return MoveFail.BadIndex;
            if (fs == ts && fslot == tslot) return MoveFail.SameSlot;
            var from = Shelves[fs];
            if (!from.CanGive) return MoveFail.SourceLocked;
            var it = from.Front[fslot];
            if (it == null) return MoveFail.NoItem;
            if (!it.Movable) return MoveFail.ItemFrozen;
            var to = Shelves[ts];
            if (!to.CanGive) return MoveFail.TargetLocked;
            if (to.Closed) return MoveFail.TargetClosed;
            if (to.Front[tslot] != null) return MoveFail.TargetOccupied;
            return MoveFail.None;
        }

        public MoveResult Move(int fs, int fslot, int ts, int tslot)
        {
            var r = new MoveResult { Fail = Check(fs, fslot, ts, tslot) };
            if (r.Fail != MoveFail.None) return r;

            var from = Shelves[fs];
            var to = Shelves[ts];
            var item = from.Front[fslot];
            from.Front[fslot] = null;
            to.Front[tslot] = item;
            r.Ok = true;
            r.Moved = item;
            Moves++;

            ResolveMatches(to, r);
            Settle(from, r);
            if (to != from) Settle(to, r);

            // irises open and close after every move
            foreach (var s in Shelves)
            {
                if (!s.Iris) continue;
                s.IrisClosed = !s.IrisClosed;
                r.IrisToggled = true;
            }

            r.Won = IsWon;
            r.Stuck = !r.Won && !HasAnyMove();
            return r;
        }

        void Settle(Shelf s, MoveResult r)
        {
            Advance(s, r);
            CloseShutter(s, r);
        }

        /// <summary>Matches on a shelf until nothing more matches (piñata contents can complete a new three).</summary>
        void ResolveMatches(Shelf s, MoveResult r)
        {
            for (int guard = 0; guard < 6 && TryMatch(s, r); guard++) { }
        }

        bool TryMatch(Shelf s, MoveResult r)
        {
            var f = s.Front;
            if (s.Width < 3) return false;

            // three identical items side by side (a 3-slot shelf: the whole front row)
            int at = -1;
            for (int i = 0; i + 2 < f.Length && at < 0; i++)
            {
                if (f[i] == null || f[i + 1] == null || f[i + 2] == null) continue;
                if (f[i].Type == f[i + 1].Type && f[i + 1].Type == f[i + 2].Type) at = i;
            }
            if (at < 0) return false;
            string type = f[at].Type;

            for (int i = at; i < at + 3; i++)
            {
                var it = f[i];
                r.MatchedItems.Add(it);
                f[i] = null;
                if (it.Kind == ItemKind.Pinata && it.Content != null)
                {
                    // the piñata bursts and what was inside lands in its slot
                    var drop = MakeNormal(it.Content);
                    f[i] = drop;
                    r.Released.Add(drop);
                }
            }

            r.Matched = true;
            r.MatchedType = type;
            r.MatchedShelf = s.Index;
            CountMatch(type, r);

            Touch(s, r);
            foreach (int n in s.Neighbors) Touch(Shelves[n], r);
            return true;
        }

        /// <summary>A set of three is gone: counts it, opens locks one step, lights fuse box objectives.</summary>
        void CountMatch(string type, MoveResult r)
        {
            Matches++;
            RemainingItems -= 3;
            foreach (var other in Shelves)
            {
                if (other.Locked)
                {
                    other.LockCount--;
                    r.LockChanged.Add(other.Index);
                    if (other.LockCount <= 0)
                    {
                        other.LockCount = 0;
                        other.Locked = false;
                        r.Unlocked.Add(other.Index);
                    }
                }
                if (other.FuseClosed && other.FuseItems.Contains(type) && !other.FuseDone.Contains(type))
                {
                    other.FuseDone.Add(type);
                    r.FuseProgress.Add(other.Index);
                    if (!other.FuseClosed) r.FuseOpened.Add(other.Index);
                }
            }
        }

        /// <summary>A match on or next to a shelf reveals its mystery items.</summary>
        static void Touch(Shelf s, MoveResult r)
        {
            foreach (var it in s.Front)
            {
                if (it == null || !it.Mystery) continue;
                it.Mystery = false;
                r.Revealed.Add(it);
            }
        }

        static void Advance(Shelf s, MoveResult r)
        {
            bool advanced = false;
            while (s.Layers.Count > 1 && s.FrontEmpty)
            {
                s.Layers.RemoveAt(0);
                advanced = true;
            }
            if (!advanced) return;
            if (!r.AdvancedShelves.Contains(s.Index)) r.AdvancedShelves.Add(s.Index);
            if (s.Ferris && !r.FerrisTurned.Contains(s.Index)) r.FerrisTurned.Add(s.Index);
        }

        static void CloseShutter(Shelf s, MoveResult r)
        {
            if (!s.Shutter || s.Closed || s.ItemCount > 0) return;
            s.Closed = true;
            r.Closed.Add(s.Index);
        }

        /// <summary>
        /// True when a useful move exists: an item to another shelf, or within its own shelf when that can change
        /// something (wide shelves match neighbours; every move turns the irises).
        /// </summary>
        public bool HasAnyMove()
        {
            bool iris = false;
            foreach (var s in Shelves) iris |= s.Iris;

            int movableShelves = 0, firstMovable = -1;
            for (int i = 0; i < Shelves.Count; i++)
            {
                var s = Shelves[i];
                if (!s.CanGive) continue;
                bool any = false;
                foreach (var it in s.Front) if (it != null && it.Movable) { any = true; break; }
                if (!any) continue;
                if (movableShelves == 0) firstMovable = i;
                movableShelves++;
                if ((s.Width > 3 || iris) && s.CanTake && Array.IndexOf(s.Front, null) >= 0) return true;
            }
            if (movableShelves == 0) return false;

            for (int i = 0; i < Shelves.Count; i++)
            {
                var s = Shelves[i];
                if (!s.CanTake || Array.IndexOf(s.Front, null) < 0) continue;
                // an empty slot is useful if some movable item lives on another shelf
                if (movableShelves > 1 || firstMovable != i) return true;
            }
            return false;
        }

        /// <summary>Enumerates every legal move to a different shelf (used by the simulator).</summary>
        public void LegalMoves(List<int[]> into)
        {
            into.Clear();
            for (int a = 0; a < Shelves.Count; a++)
            {
                var A = Shelves[a];
                if (!A.CanGive) continue;
                for (int i = 0; i < A.Width; i++)
                {
                    var it = A.Front[i];
                    if (it == null || !it.Movable) continue;
                    for (int c = 0; c < Shelves.Count; c++)
                    {
                        var C = Shelves[c];
                        // on a wide shelf the slot matters (three neighbours match), so it also counts within the shelf
                        bool wide = C.Width > 3;
                        if (c == a && !wide || !C.CanTake) continue;
                        for (int j = 0; j < C.Width; j++)
                        {
                            if (C.Front[j] != null) continue;
                            into.Add(new[] { a, i, c, j });
                            if (!wide) break; // slots on a 3-slot shelf are equivalent
                        }
                    }
                }
            }
        }
    }
}
