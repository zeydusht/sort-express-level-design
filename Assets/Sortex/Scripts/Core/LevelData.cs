// Level data in Sort Express' own JSON format (keys reverse-engineered from the shipped TextAsset levels
// and the class names in the game's IL2CPP metadata). Original Sort Express level files load as they are
// and are written back with the same keys.
//
//  top level : ld  = difficulty tag (0 normal, 10 hard, 20 super hard)
//              tl  = time limit in seconds
//              s   = shelves
//              ig  = item generators (ItemGenerator / ItemSpawnGroup): pools of items that fill the "it -1" slots
//                    and refill shelves during play
//              nm  = (prototype only) display name
//  shelf     : id, gid        = shelf id, generator group the shelf belongs to
//              pos{x,y}       = centre of the shelf in world units (3-slot shelf = 3.13 wide)
//              sicw           = slot count: 1 single shelf, 3 normal, 4+ wide shelf (MultiShelf)
//              si[]           = layers, front first; si[n].rd[] = slots of that layer
//              lmc            = lock: the shelf stays locked until this many matches are made (LockShelf, level 4)
//              isdp           = cargo door (DispenserShelf, level 31): rows behind the front row are hidden
//              icl            = shutter (ShutterShelf, level 41): once the shelf is empty it closes for good
//              im, mv, ms     = moving shelf (level 11): a row slides sideways and loops through pipes
//              if             = rotating door (FerrisShelf, level 81): the rows are stages behind a door
//              irs            = iris (IrisShelf, level 211): 0 starts open, 1 starts closed; it opens and closes
//              fbi            = fuse box (FuseBoxShelf, level 131): locked until each listed item has been matched
//  slot      : it  = 1 item, 0 empty, -1 filled by the generator, 5 piñata, 7 dragon egg
//              iid = item id, e.g. "item_camera_red" ("pinata", "dragon_egg_0" for the special ones;
//                    eggs are sorted in threes like items and hatch: DragonEggMatchDestroyParticles)
//              pr  = properties: ib = frozen (level 21), imy = mystery (level 61),
//                    pci / pcii = piñata colour (0-5) and the item inside it (level 91)
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Sortex.Core
{
    public sealed class SlotData
    {
        public const int Empty = 0, Normal = 1, Generated = -1, PinataKind = 5, EggKind = 7;

        public int it;
        public string iid;
        public bool frozen;
        public bool mystery;
        public int pinataColor;          // pci
        public string pinataContent;     // pcii: the item that drops out of the piñata (null = nothing)

        public bool IsItem { get { return it == Normal && !string.IsNullOrEmpty(iid); } }
        public bool IsPinata { get { return it == PinataKind; } }
        public bool IsEgg { get { return it == EggKind; } }
        public bool IsGenerated { get { return it == Generated; } }
        /// <summary>Anything that occupies the slot at the start (item, piñata, egg).</summary>
        public bool IsFilled { get { return IsItem || IsPinata || IsEgg; } }

        public SlotData Clone()
        {
            return new SlotData
            {
                it = it, iid = iid, frozen = frozen, mystery = mystery, pinataColor = pinataColor, pinataContent = pinataContent
            };
        }

        public void Clear()
        {
            it = 0;
            iid = null;
            frozen = mystery = false;
            pinataColor = 0;
            pinataContent = null;
        }

        public static SlotData Pinata(int color, string content)
        {
            return new SlotData { it = PinataKind, iid = "pinata", pinataColor = color, pinataContent = content };
        }

        public static SlotData Egg()
        {
            return new SlotData { it = EggKind, iid = "dragon_egg_0" };
        }

        internal static SlotData FromJson(object c)
        {
            var pr = MiniJson.Obj(c, "pr");
            var s = new SlotData
            {
                it = MiniJson.Int(c, "it"),
                iid = MiniJson.Str(c, "iid"),
                frozen = MiniJson.Bool(pr, "ib"),
                mystery = MiniJson.Bool(pr, "imy"),
                pinataColor = MiniJson.Int(pr, "pci")
            };
            var inside = MiniJson.Obj(pr, "pcii");
            if (inside != null && MiniJson.Int(inside, "it") == Normal) s.pinataContent = MiniJson.Str(inside, "iid");
            return s;
        }

        internal Dictionary<string, object> ToJson()
        {
            var cd = new Dictionary<string, object>();
            if (IsItem || IsPinata || IsEgg)
            {
                cd["it"] = (double)it;
                cd["iid"] = iid;
                var pr = new Dictionary<string, object>();
                if (frozen) pr["ib"] = true;
                if (mystery) pr["imy"] = true;
                if (IsPinata)
                {
                    pr["pci"] = (double)pinataColor;
                    pr["pcii"] = string.IsNullOrEmpty(pinataContent)
                        ? new Dictionary<string, object> { { "it", 0.0 } }
                        : new Dictionary<string, object> { { "it", 1.0 }, { "iid", pinataContent } };
                }
                if (pr.Count > 0) cd["pr"] = pr;
            }
            else cd["it"] = IsGenerated ? -1.0 : 0.0;
            return cd;
        }
    }

    /// <summary>
    /// One wave of an item generator (ItemSpawnGroup): what it spawns and how many of each, plus its placement
    /// weights: slw = next to copies in the same row, blw = next to copies in the row in front, rw = anywhere;
    /// ssp = how often the weights are used at all (otherwise anywhere). Waves fill the slots in order, front rows first.
    /// </summary>
    public sealed class SpawnWave
    {
        public readonly List<KeyValuePair<SlotData, int>> entries = new List<KeyValuePair<SlotData, int>>();
        public double ssp = 1, slw = 1, blw = 1, rw = 1;

        public int ItemCount
        {
            get
            {
                // items to clear: normal items and piñatas (plus what is inside them); eggs are obstacles
                int n = 0;
                foreach (var e in entries)
                {
                    if (e.Key.IsFilled) n += e.Value;
                    if (e.Key.IsPinata && !string.IsNullOrEmpty(e.Key.pinataContent)) n += e.Value;
                }
                return n;
            }
        }
    }

    public sealed class GeneratorData
    {
        public string id;
        public readonly List<SpawnWave> waves = new List<SpawnWave>();
        public object raw;      // the original JSON object, written back unchanged
    }

    public sealed class ShelfData
    {
        public string id;
        public string gid = "1";
        public float x;
        public float y;
        public int width = 3;
        public bool shutter;
        public bool cargo;
        public int lockCount;
        public bool moving;          // im: part of a moving (conveyor) row
        public int moveDir = 1;      // +1 right, -1 left
        public float moveSpeed = 1f; // world units per second
        public bool ferris;          // if: rotating door
        public bool iris;            // irs present
        public bool irisClosed;      // irs == 1
        public readonly List<string> fuseItems = new List<string>();   // fbi
        public readonly List<SlotData[]> layers = new List<SlotData[]>();

        public static ShelfData Create(float x, float y, int width, int layerCount)
        {
            var s = new ShelfData { id = Guid.NewGuid().ToString(), x = x, y = y, width = width };
            s.SetLayerCount(Math.Max(1, layerCount));
            return s;
        }

        public ShelfData Clone()
        {
            var s = new ShelfData
            {
                id = id, gid = gid, x = x, y = y, width = width, shutter = shutter, cargo = cargo, lockCount = lockCount,
                moving = moving, moveDir = moveDir, moveSpeed = moveSpeed, ferris = ferris, iris = iris, irisClosed = irisClosed
            };
            s.fuseItems.AddRange(fuseItems);
            foreach (var l in layers)
            {
                var c = new SlotData[l.Length];
                for (int i = 0; i < l.Length; i++) c[i] = l[i] == null ? new SlotData() : l[i].Clone();
                s.layers.Add(c);
            }
            return s;
        }

        /// <summary>Items to clear on this shelf (piñatas, eggs and the items inside piñatas count).</summary>
        public int ItemCount
        {
            get
            {
                int n = 0;
                foreach (var l in layers)
                    foreach (var c in l)
                    {
                        if (c == null) continue;
                        if (c.IsFilled) n++;
                        if (c.IsPinata && !string.IsNullOrEmpty(c.pinataContent)) n++;
                    }
                return n;
            }
        }

        public int GeneratedSlots
        {
            get
            {
                int n = 0;
                foreach (var l in layers) foreach (var c in l) if (c != null && c.IsGenerated) n++;
                return n;
            }
        }

        public void SetLayerCount(int count)
        {
            while (layers.Count < count) layers.Add(NewLayer(width));
            while (layers.Count > count && layers.Count > 1) layers.RemoveAt(layers.Count - 1);
        }

        public void SetWidth(int w)
        {
            w = Math.Max(1, w);
            for (int li = 0; li < layers.Count; li++)
            {
                var old = layers[li];
                var n = NewLayer(w);
                for (int k = 0; k < Math.Min(w, old.Length); k++) n[k] = old[k] ?? new SlotData();
                layers[li] = n;
            }
            width = w;
        }

        public static SlotData[] NewLayer(int w)
        {
            var l = new SlotData[w];
            for (int i = 0; i < w; i++) l[i] = new SlotData();
            return l;
        }
    }

    public sealed class LevelData
    {
        public string name;
        public int difficultyTag;
        public int timeLimit = 120;
        public readonly List<ShelfData> shelves = new List<ShelfData>();
        public readonly List<GeneratorData> generators = new List<GeneratorData>();

        public string DifficultyLabel
        {
            get
            {
                if (difficultyTag >= 20) return "Super Hard";
                if (difficultyTag >= 10) return "Hard";
                return "Normal";
            }
        }

        public LevelData Clone()
        {
            var l = new LevelData { name = name, difficultyTag = difficultyTag, timeLimit = timeLimit };
            foreach (var s in shelves) l.shelves.Add(s.Clone());
            l.generators.AddRange(generators);   // read-only after loading
            return l;
        }

        public static LevelData FromJson(string json)
        {
            object root = MiniJson.Parse(json);
            var level = new LevelData
            {
                name = MiniJson.Str(root, "nm", "Level"),
                difficultyTag = MiniJson.Int(root, "ld"),
                timeLimit = MiniJson.Int(root, "tl", 120)
            };

            var shelves = MiniJson.Arr(root, "s");
            if (shelves != null)
            {
                foreach (object so in shelves)
                {
                    var pos = MiniJson.Obj(so, "pos");
                    var shelf = new ShelfData
                    {
                        id = MiniJson.Str(so, "id", "s" + level.shelves.Count),
                        gid = MiniJson.Str(so, "gid", "1"),
                        x = (float)MiniJson.Num(pos, "x"),
                        y = (float)MiniJson.Num(pos, "y"),
                        width = Math.Max(1, MiniJson.Int(so, "sicw", 3)),
                        shutter = MiniJson.Bool(so, "icl"),
                        cargo = MiniJson.Bool(so, "isdp"),
                        lockCount = MiniJson.Int(so, "lmc"),
                        moving = MiniJson.Bool(so, "im"),
                        ferris = MiniJson.Bool(so, "if")
                    };
                    if (shelf.moving)
                    {
                        // early levels store the velocity in mv (0.3), later ones a direction in mv (+-1) and the speed in ms
                        double mvx = MiniJson.Num(MiniJson.Obj(so, "mv"), "x");
                        shelf.moveDir = mvx < 0 ? -1 : 1;
                        double ms = MiniJson.Num(so, "ms", -1);
                        shelf.moveSpeed = ms >= 0 ? (float)ms : (float)Math.Abs(mvx);
                    }
                    double irs = MiniJson.Num(so, "irs", -1);
                    if (irs >= 0)
                    {
                        shelf.iris = true;
                        shelf.irisClosed = irs >= 1;
                    }
                    var fbi = MiniJson.Arr(so, "fbi");
                    if (fbi != null)
                        foreach (var f in fbi)
                            if (f is string && !string.IsNullOrEmpty((string)f)) shelf.fuseItems.Add((string)f);

                    var layers = MiniJson.Arr(so, "si");
                    if (layers != null)
                    {
                        foreach (object lo in layers)
                        {
                            var rd = MiniJson.Arr(lo, "rd");
                            var slots = new SlotData[shelf.width];
                            for (int k = 0; k < shelf.width; k++)
                                slots[k] = SlotData.FromJson(rd != null && k < rd.Count ? rd[k] : null);
                            shelf.layers.Add(slots);
                        }
                    }
                    if (shelf.layers.Count == 0) shelf.layers.Add(ShelfData.NewLayer(shelf.width));
                    level.shelves.Add(shelf);
                }
            }

            var ig = MiniJson.Arr(root, "ig");
            if (ig != null)
            {
                foreach (object go in ig)
                {
                    var g = new GeneratorData { id = MiniJson.Str(go, "id", "1"), raw = go };
                    var isg = MiniJson.Arr(go, "isg");
                    if (isg != null)
                        foreach (object wo in isg)
                        {
                            var wave = new SpawnWave
                            {
                                ssp = MiniJson.Num(wo, "ssp", 1), slw = MiniJson.Num(wo, "slw", 1),
                                blw = MiniJson.Num(wo, "blw", 1), rw = MiniJson.Num(wo, "rw", 1)
                            };
                            var icd = MiniJson.Arr(wo, "icd");
                            if (icd != null)
                                foreach (object e in icd)
                                {
                                    int count = MiniJson.Int(e, "ic");
                                    if (count > 0) wave.entries.Add(new KeyValuePair<SlotData, int>(SlotData.FromJson(MiniJson.Obj(e, "id2")), count));
                                }
                            g.waves.Add(wave);
                        }
                    level.generators.Add(g);
                }
            }
            return level;
        }

        /// <summary>Writes the level back in Sort Express' JSON format (same keys as the shipped levels).</summary>
        public string ToJson(bool pretty = true)
        {
            var root = new Dictionary<string, object>();
            root["nm"] = name ?? "Level";
            root["as"] = new List<object> { 0.0 };
            root["tid"] = 0.0;
            root["lt"] = 1.0;
            root["ld"] = (double)difficultyTag;
            root["tl"] = (double)timeLimit;
            root["osb"] = false;
            root["sir"] = 1.5625;
            var list = new List<object>();
            foreach (var s in shelves)
            {
                var so = new Dictionary<string, object>();
                so["id"] = string.IsNullOrEmpty(s.id) ? Guid.NewGuid().ToString() : s.id;
                so["gid"] = string.IsNullOrEmpty(s.gid) ? "1" : s.gid;
                so["csb"] = true;
                so["pos"] = new Dictionary<string, object> { { "x", Math.Round(s.x, 4) }, { "y", Math.Round(s.y, 4) } };
                so["im"] = s.moving;
                so["md"] = 0.0;
                so["htp"] = false;
                so["tp"] = new Dictionary<string, object> { { "x", 0.0 }, { "y", 0.0 } };
                so["mv"] = new Dictionary<string, object> { { "x", s.moving ? (double)s.moveDir : 0.0 }, { "y", 0.0 } };
                so["ms"] = s.moving ? Math.Round(s.moveSpeed, 3) : 0.0;
                so["sicw"] = (double)s.width;
                var si = new List<object>();
                foreach (var layer in s.layers)
                {
                    var rd = new List<object>();
                    for (int k = 0; k < s.width; k++)
                    {
                        var c = k < layer.Length ? layer[k] : null;
                        rd.Add(c != null ? c.ToJson() : new Dictionary<string, object> { { "it", 0.0 } });
                    }
                    si.Add(new Dictionary<string, object> { { "rd", rd } });
                }
                so["si"] = si;
                so["isd"] = false;
                so["icn"] = false;
                so["icl"] = s.shutter;
                so["isdp"] = s.cargo;
                so["if"] = s.ferris;
                if (s.iris) so["irs"] = s.irisClosed ? 1.0 : 0.0;
                if (s.fuseItems.Count > 0)
                {
                    var fbi = new List<object>();
                    foreach (var f in s.fuseItems) fbi.Add(f);
                    so["fbi"] = fbi;
                }
                so["lmc"] = (double)s.lockCount;
                so["pd"] = (double)s.layers.Count;
                so["scd"] = new List<object>
                {
                    new Dictionary<string, object> { { "cmd", 0.0 }, { "crd", 2.0 } },
                    new Dictionary<string, object> { { "cmd", 0.0 }, { "crd", 1.0 } },
                    new Dictionary<string, object> { { "cmd", 0.0 }, { "crd", 3.0 } },
                    new Dictionary<string, object> { { "cmd", 0.0 }, { "crd", 0.0 } }
                };
                list.Add(so);
            }
            root["s"] = list;
            var ig = new List<object>();
            foreach (var g in generators) if (g.raw != null) ig.Add(g.raw);
            root["ig"] = ig;
            root["igd"] = null;
            root["sfss"] = false;
            return MiniJson.Serialize(root, pretty);
        }

        /// <summary>Counts of every item type on the shelves (all layers, eggs and piñata contents included, generators not).</summary>
        public Dictionary<string, int> TypeCounts()
        {
            var counts = new Dictionary<string, int>();
            foreach (var s in shelves)
                foreach (var l in s.layers)
                    foreach (var c in l)
                    {
                        if (c == null) continue;
                        if (c.IsItem || c.IsEgg) Add(counts, c.iid);
                        if (c.IsPinata && !string.IsNullOrEmpty(c.pinataContent)) Add(counts, c.pinataContent);
                    }
            return counts;
        }

        /// <summary>Piñatas per colour (they are matched in threes of the same colour).</summary>
        public Dictionary<int, int> PinataCounts()
        {
            var counts = new Dictionary<int, int>();
            foreach (var s in shelves)
                foreach (var l in s.layers)
                    foreach (var c in l)
                    {
                        if (c == null || !c.IsPinata) continue;
                        int n;
                        counts.TryGetValue(c.pinataColor, out n);
                        counts[c.pinataColor] = n + 1;
                    }
            return counts;
        }

        /// <summary>
        /// Reasons the level cannot be won as designed (empty list = playable): no items, a type or a piñata colour
        /// that is not a multiple of 3, a fuse box waiting for an item that is not in the level.
        /// </summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            var counts = TypeCounts();
            foreach (var g in generators)
                foreach (var w in g.waves)
                    foreach (var e in w.entries)
                    {
                        if (e.Key == null) continue;
                        if (e.Key.IsItem || e.Key.IsEgg) Add(counts, e.Key.iid, e.Value);
                        if (e.Key.IsPinata && !string.IsNullOrEmpty(e.Key.pinataContent)) Add(counts, e.Key.pinataContent, e.Value);
                    }
            var pinatas = PinataCounts();
            foreach (var g in generators)
                foreach (var w in g.waves)
                    foreach (var e in w.entries)
                        if (e.Key != null && e.Key.IsPinata)
                        {
                            int n;
                            pinatas.TryGetValue(e.Key.pinataColor, out n);
                            pinatas[e.Key.pinataColor] = n + e.Value;
                        }
            if (counts.Count == 0 && pinatas.Count == 0) problems.Add("no items");
            foreach (var kv in counts)
                if (kv.Value % 3 != 0) problems.Add(kv.Key + ": " + kv.Value + " (not a multiple of 3)");
            foreach (var kv in pinatas)
                if (kv.Value % 3 != 0) problems.Add("piñata colour " + kv.Key + ": " + kv.Value + " (not a multiple of 3)");
            foreach (var s in shelves)
                foreach (var f in s.fuseItems)
                    if (!counts.ContainsKey(f)) problems.Add("fuse box waits for " + f + ", which is not in the level");
            return problems;
        }

        static void Add(Dictionary<string, int> counts, string id, int k)
        {
            int n;
            counts.TryGetValue(id, out n);
            counts[id] = n + k;
        }

        /// <summary>Every item id the level can show: shelves, piñata contents, generator waves and fuse box objectives.</summary>
        public HashSet<string> ItemIds()
        {
            var ids = new HashSet<string>(TypeCounts().Keys);
            foreach (var s in shelves) foreach (var f in s.fuseItems) ids.Add(f);
            foreach (var g in generators)
                foreach (var w in g.waves)
                    foreach (var e in w.entries)
                    {
                        if (e.Key == null) continue;
                        if (e.Key.IsItem || e.Key.IsEgg) ids.Add(e.Key.iid);
                        if (e.Key.IsPinata && !string.IsNullOrEmpty(e.Key.pinataContent)) ids.Add(e.Key.pinataContent);
                    }
            return ids;
        }

        public int GeneratorItemCount
        {
            get
            {
                int n = 0;
                foreach (var g in generators) foreach (var w in g.waves) n += w.ItemCount;
                return n;
            }
        }

        static void Add(Dictionary<string, int> counts, string id)
        {
            int n;
            counts.TryGetValue(id, out n);
            counts[id] = n + 1;
        }

        internal static string Num(double v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
