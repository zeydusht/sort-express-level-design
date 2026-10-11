// Auto-fill: puts items into a shelf layout drawn by the designer, from a small recipe.
// C# port of Tools/leveldesign.py (the generator used for the three case levels).
//
// Levers (same as the real game's item generator 'ig': ssp / slw / blw / rw):
//   Types        number of item types (each type = 3 items)
//   FrontFill    share of the front row that holds items (the rest is breathing room)
//   BackFill     share of the rows behind
//   SameLayer / Adjacent / Random   where the 3 copies of a type go: same row, neighbouring rows, anywhere
//   AllowPairs   two copies may start on the same shelf (easier)
//   LookAlike    share of types that have a colour twin on the board (camera red + camera blue ...)
//   Frozen / Mystery   obstacle counts
//   KeepRows     rows from the front the designer filled by hand: they stay as they are, their types are completed to
//                threes in the rows behind, and only the rows behind are generated (0 = replace every item)
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sortex.Core
{
    [Serializable]
    public sealed class FillRecipe
    {
        public int Types = 24;
        public double FrontFill = 0.72;
        public double BackFill = 0.80;
        public double SameLayer = 0.60;
        public double Adjacent = 0.30;
        public double Random = 0.10;
        public bool AllowPairs;
        public double LookAlike = 0.4;
        public int Frozen;
        public int Mystery;
        public int KeepRows;
        public int Seed = 1;

        public FillRecipe Clone() { return (FillRecipe)MemberwiseClone(); }
    }

    public static class LevelGenerator
    {
        struct Cell
        {
            public int Shelf, Layer, Slot;
            public Cell(int s, int l, int k) { Shelf = s; Layer = l; Slot = k; }
        }

        /// <summary>How many items fit into the generated rows (behind the kept ones) with the recipe's fill ratios.</summary>
        public static int Capacity(LevelData level, FillRecipe r)
        {
            int maxLayers = level.shelves.Count == 0 ? 0 : level.shelves.Max(s => s.layers.Count);
            int total = 0;
            for (int li = Math.Max(0, r.KeepRows); li < maxLayers; li++)
            {
                int locked = 0, open = 0;
                foreach (var s in level.shelves)
                {
                    if (li >= s.layers.Count) continue;
                    if (s.lockCount > 0) locked += s.width;
                    else open += s.width;
                }
                total += locked + (int)Math.Round(open * (li == 0 ? r.FrontFill : r.BackFill));
            }
            return total;
        }

        /// <summary>Items in the kept rows by type (an item inside a piñata counts too).</summary>
        public static Dictionary<string, int> KeptCounts(LevelData level, int keepRows)
        {
            var counts = new Dictionary<string, int>();
            foreach (var s in level.shelves)
                for (int li = 0; li < Math.Min(keepRows, s.layers.Count); li++)
                    foreach (var c in s.layers[li])
                    {
                        if (c == null) continue;
                        string id = c.IsItem ? c.iid : c.IsPinata ? c.pinataContent : null;
                        if (string.IsNullOrEmpty(id)) continue;
                        int n;
                        counts.TryGetValue(id, out n);
                        counts[id] = n + 1;
                    }
            return counts;
        }

        /// <summary>Copies the generator adds so every kept type comes in threes.</summary>
        public static int Completions(Dictionary<string, int> kept)
        {
            return kept.Values.Sum(n => (3 - n % 3) % 3);
        }

        /// <summary>The most types the recipe can ask for: the kept ones plus as many new ones as the free room takes.</summary>
        public static int MaxTypes(LevelData level, FillRecipe r)
        {
            var kept = KeptCounts(level, r.KeepRows);
            return kept.Count + Math.Max(0, Capacity(level, r) - Completions(kept)) / 3;
        }

        /// <summary>The new types to deal (recipe.Types counts the kept ones too), none of them already kept.</summary>
        public static List<string> PickNewTypes(IList<string> catalog, LevelData level, FillRecipe r, Random rng)
        {
            var kept = KeptCounts(level, r.KeepRows);
            int n = Math.Max(0, r.Types - kept.Count);
            if (n == 0) return new List<string>();
            return PickTypes(catalog.Where(id => !kept.ContainsKey(id)).ToList(), n, r.LookAlike, rng);
        }

        /// <summary>Picks n item types from the catalog, with roughly lookAlike share in colour-twin groups.</summary>
        public static List<string> PickTypes(IList<string> catalog, int n, double lookAlike, Random rng)
        {
            var byModel = new Dictionary<string, List<string>>();
            foreach (var id in catalog.Distinct())
            {
                string model = Model(id);
                List<string> l;
                if (!byModel.TryGetValue(model, out l)) byModel[model] = l = new List<string>();
                l.Add(id);
            }
            var models = byModel.Keys.ToList();
            Shuffle(models, rng);
            var picked = new List<string>();
            var used = new HashSet<string>();
            int twinsTarget = (int)Math.Round(n * Math.Max(0, Math.Min(1, lookAlike)));
            int twins = 0;
            foreach (var m in models)
            {
                if (twins >= twinsTarget || picked.Count >= n) break;
                var colours = byModel[m].ToList();
                if (colours.Count < 2) continue;
                Shuffle(colours, rng);
                int want = Math.Min(Math.Min(3, colours.Count), Math.Min(twinsTarget - twins, n - picked.Count));
                if (want < 2) want = Math.Min(2, n - picked.Count);
                if (want < 2) break;
                for (int i = 0; i < want; i++) picked.Add(colours[i]);
                twins += want;
                used.Add(m);
            }
            foreach (var m in models)
            {
                if (picked.Count >= n) break;
                if (used.Contains(m)) continue;
                var colours = byModel[m];
                picked.Add(colours[rng.Next(colours.Count)]);
                used.Add(m);
            }
            // tiny catalogs: allow more colours of the same model
            foreach (var id in catalog)
            {
                if (picked.Count >= n) break;
                if (!picked.Contains(id)) picked.Add(id);
            }
            Shuffle(picked, rng);
            return picked;
        }

        public static string Model(string id)
        {
            string s = id.StartsWith("item_") ? id.Substring(5) : id;
            int u = s.LastIndexOf('_');
            return u > 0 ? s.Substring(0, u) : s;
        }

        /// <summary>Fills the layout: every item, or (KeepRows) the rows behind the ones filled by hand, completing the
        /// kept types to threes. Returns null on success, otherwise the reason.</summary>
        public static string Fill(LevelData level, FillRecipe r, IList<string> types)
        {
            var rng = new Random(r.Seed);
            if (level.shelves.Count == 0) return "Add shelves first.";
            int keep = Math.Max(0, r.KeepRows);
            int maxLayers = level.shelves.Max(s => s.layers.Count);
            if (keep >= maxLayers) return "Every row is kept: add a row behind, or keep fewer rows.";
            var kept = KeptCounts(level, keep);
            var missing = kept.Where(kv => kv.Value % 3 != 0).Select(kv => new KeyValuePair<string, int>(kv.Key, 3 - kv.Value % 3)).ToList();
            int nItems = types.Count * 3 + missing.Sum(kv => kv.Value);
            int cap = Capacity(level, r);
            if (cap < nItems)
                return "Not enough room: " + nItems + " items needed" + (missing.Count > 0 ? " (" + missing.Sum(kv => kv.Value) + " of them complete your kept items)" : "") +
                       ", " + cap + " fit with these fill ratios. Add rows or shelves, raise the fill, or use fewer types (max " + MaxTypes(level, r) + ").";

            foreach (var s in level.shelves)
                for (int li = 0; li < s.layers.Count; li++)
                {
                    var l = s.layers[li];
                    for (int k = 0; k < l.Length; k++)
                    {
                        if (l[k] == null) l[k] = new SlotData();
                        if (li >= keep) l[k].Clear();
                    }
                }
            level.generators.Clear();   // an original level's item generator would deal its waves on top

            var byLayer = new List<List<Cell>>();
            for (int li = 0; li < maxLayers; li++) byLayer.Add(new List<Cell>());
            for (int si = 0; si < level.shelves.Count; si++)
            {
                var s = level.shelves[si];
                for (int li = keep; li < s.layers.Count; li++)
                    for (int k = 0; k < s.width; k++) byLayer[li].Add(new Cell(si, li, k));
            }

            // 1. choose the empty slots of every generated row (fill ratio), locked shelves are always full
            var free = new HashSet<Cell>(new CellEq());
            int remaining = nItems;
            for (int li = keep; li < maxLayers; li++)
            {
                var row = byLayer[li];
                var locked = row.Where(c => level.shelves[c.Shelf].lockCount > 0).ToList();
                var open = row.Where(c => level.shelves[c.Shelf].lockCount <= 0).ToList();
                double fill = li == 0 ? r.FrontFill : r.BackFill;
                int target = Math.Min(remaining, locked.Count + (int)Math.Round(open.Count * fill));
                int nEmpty = row.Count - target;
                Shuffle(open, rng);
                var empties = new List<Cell>();
                foreach (var c in open)
                {
                    if (empties.Count >= nEmpty) break;
                    var sh = level.shelves[c.Shelf];
                    bool deeper = c.Layer < sh.layers.Count - 1;
                    if (sh.width == 1 && deeper && remaining - target > 0) continue;   // keep single stacks solid
                    int sameShelf = empties.Count(e => e.Shelf == c.Shelf);
                    if (sh.width > 1 && sameShelf >= sh.width - 1 && li > 0 && remaining - target > 0) continue; // no empty middle row
                    empties.Add(c);
                }
                if (empties.Count < nEmpty)
                {
                    var rest = Enumerable.Reverse(locked).Concat(open).ToList();
                    foreach (var c in rest)
                    {
                        if (empties.Count >= nEmpty) break;
                        if (!empties.Contains(c)) empties.Add(c);
                    }
                }
                foreach (var e in empties) free.Add(e);
                remaining -= row.Count - empties.Count;
            }
            if (remaining > 0) return "Not enough room for " + nItems + " items.";

            var openSlots = new List<List<Cell>>();
            for (int li = 0; li < maxLayers; li++)
            {
                var l = byLayer[li].Where(c => !free.Contains(c)).ToList();
                Shuffle(l, rng);
                openSlots.Add(l);
            }

            // 2a. the copies that complete the kept types: mostly in the row right behind the kept ones, so the player
            //     digs for the third one; sometimes further back
            foreach (var kv in missing)
                for (int i = 0; i < kv.Value; i++)
                {
                    int li = keep;
                    if (rng.NextDouble() < r.Random + r.Adjacent * 0.5) li = keep + rng.Next(maxLayers - keep);
                    while (li < maxLayers && openSlots[li].Count == 0) li++;
                    if (li >= maxLayers) li = Enumerable.Range(keep, maxLayers - keep).FirstOrDefault(x => openSlots[x].Count > 0);
                    if (openSlots[li].Count == 0) return "Ran out of slots.";
                    var c = openSlots[li][openSlots[li].Count - 1];
                    openSlots[li].RemoveAt(openSlots[li].Count - 1);
                    var slot = level.shelves[c.Shelf].layers[c.Layer][c.Slot];
                    slot.it = 1;
                    slot.iid = kv.Key;
                }

            // 2b. place the three copies of every new type
            var order = types.ToList();
            Shuffle(order, rng);
            var placed = new Dictionary<string, List<Cell>>();
            var avail0 = Enumerable.Range(keep, maxLayers - keep).ToList();
            double total = Math.Max(1e-6, r.SameLayer + r.Adjacent + r.Random);
            foreach (var t in order)
            {
                double x = rng.NextDouble() * total;
                string mode = x < r.SameLayer ? "same" : x < r.SameLayer + r.Adjacent ? "adj" : "rand";
                List<Cell> got = null;
                for (int attempt = 0; attempt < 40 && got == null; attempt++)
                {
                    var avail = avail0.Where(li => openSlots[li].Count > 0).ToList();
                    if (avail.Count == 0) break;
                    int[] plan;
                    int a = avail[rng.Next(avail.Count)];
                    if (mode == "same") plan = new[] { a, a, a };
                    else if (mode == "adj")
                        plan = rng.NextDouble() < 0.5 ? new[] { a, a, Math.Min(a + 1, maxLayers - 1) } : new[] { a, Math.Max(a - 1, 0), a };
                    else plan = new[] { avail[rng.Next(avail.Count)], avail[rng.Next(avail.Count)], avail[rng.Next(avail.Count)] };

                    var chosen = new List<Cell>();
                    bool ok = true;
                    foreach (int li in plan)
                    {
                        var avoid = new HashSet<int>();
                        if (!r.AllowPairs) foreach (var c in chosen) avoid.Add(c.Shelf);
                        else
                        {
                            var sameRow = chosen.Where(c => c.Layer == li).ToList();
                            if (sameRow.Count == 2 && sameRow[0].Shelf == sameRow[1].Shelf) avoid.Add(sameRow[0].Shelf);
                        }
                        int idx = openSlots[li].FindIndex(c => !avoid.Contains(c.Shelf));
                        if (idx < 0)
                        {
                            ok = false;
                            break;
                        }
                        chosen.Add(openSlots[li][idx]);
                        openSlots[li].RemoveAt(idx);
                    }
                    if (ok) got = chosen;
                    else foreach (var c in chosen) openSlots[c.Layer].Add(c);
                }
                if (got == null)
                {
                    got = new List<Cell>();
                    for (int li = keep; li < maxLayers && got.Count < 3; li++)
                        while (openSlots[li].Count > 0 && got.Count < 3)
                        {
                            got.Add(openSlots[li][openSlots[li].Count - 1]);
                            openSlots[li].RemoveAt(openSlots[li].Count - 1);
                        }
                    if (got.Count < 3) return "Ran out of slots.";
                }
                foreach (var c in got)
                {
                    var slot = level.shelves[c.Shelf].layers[c.Layer][c.Slot];
                    slot.it = 1;
                    slot.iid = t;
                }
                placed[t] = got;
            }

            // 3. frozen items: front row of open 3+ shelves (the first generated row when rows are kept), both partners
            //    must be able to reach it; only new items, the kept ones stay as the designer left them
            var fronts = Enumerable.Range(0, level.shelves.Count)
                .Where(i => level.shelves[i].width >= 3 && level.shelves[i].lockCount <= 0).ToList();
            Shuffle(fronts, rng);
            int nf = 0;
            foreach (int si in fronts)
            {
                if (nf >= r.Frozen) break;
                var front = FirstFilledLayer(level.shelves[si], keep);
                if (front == null) continue;
                var cands = Enumerable.Range(0, front.Length).Where(k => front[k].IsItem && placed.ContainsKey(front[k].iid)).ToList();
                Shuffle(cands, rng);
                foreach (int k in cands)
                {
                    var partners = placed[front[k].iid].Where(c => !(c.Shelf == si && level.shelves[si].layers[c.Layer] == front && c.Slot == k)).ToList();
                    if (partners.Any(c => c.Shelf == si)) continue;
                    if (partners.Any(c => level.shelves[c.Shelf].layers[c.Layer][c.Slot].frozen)) continue;
                    front[k].frozen = true;
                    nf++;
                    break;
                }
            }

            // 4. mystery items: first two generated rows
            var mcands = new List<SlotData>();
            foreach (var s in level.shelves)
                for (int li = keep; li < Math.Min(keep + 2, s.layers.Count); li++)
                    foreach (var c in s.layers[li])
                        if (c.IsItem && !c.frozen) mcands.Add(c);
            Shuffle(mcands, rng);
            for (int i = 0; i < Math.Min(r.Mystery, mcands.Count); i++) mcands[i].mystery = true;

            // 5. fuse boxes keep their number of objectives, re-picked among the items that are not inside the box
            var used = new HashSet<string>();
            foreach (var s in level.shelves)
            {
                if (s.fuseItems.Count == 0 || keep > 0) continue;   // with kept rows the designer's objectives stay
                int want = Math.Min(3, s.fuseItems.Count);
                var inside = new HashSet<string>(s.layers.SelectMany(l => l).Where(c => c != null && c.IsItem).Select(c => c.iid));
                var cands = types.Where(t => placed.ContainsKey(t) && !inside.Contains(t) && !used.Contains(t)).ToList();
                Shuffle(cands, rng);
                s.fuseItems.Clear();
                foreach (var t in cands.Take(want))
                {
                    s.fuseItems.Add(t);
                    used.Add(t);
                }
            }
            return null;
        }

        static SlotData[] FirstFilledLayer(ShelfData s, int from = 0)
        {
            for (int li = from; li < s.layers.Count; li++)
                if (s.layers[li].Any(c => c != null && c.IsItem)) return s.layers[li];
            return null;
        }

        static void Shuffle<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                var t = list[i];
                list[i] = list[j];
                list[j] = t;
            }
        }

        sealed class CellEq : IEqualityComparer<Cell>
        {
            public bool Equals(Cell a, Cell b) { return a.Shelf == b.Shelf && a.Layer == b.Layer && a.Slot == b.Slot; }
            public int GetHashCode(Cell c) { return (c.Shelf * 397 + c.Layer) * 31 + c.Slot; }
        }
    }
}
