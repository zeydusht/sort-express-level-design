// The four in-level boosters of Sort Express and the "no match left" check, on the same board model as the moves.
//
//  Vacuum      takes all three copies of one type that shows on a front row, wherever the copies are
//  Magic hat   (Shuffle) deals the movable items again; afterwards several matches are one move away on the front
//              rows: a pair on a shelf with a free slot and the third copy on another front row
//  Painter     turns 9 items (three whole types, at least 4 of them on the front rows) into green aliens: they all
//              match each other, so any three aliens side by side pop
//  Freezer     stops the timer for a while: a timer thing, handled by the game (SortexGame), not by the board
//
// None of them is used by the simulated players (Simulator), so the measured win rates stay "without boosters".
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sortex.Core
{
    public sealed partial class Board
    {
        /// <summary>The type the painter turns items into (Art/painter_alien.png, rendered from the game's alien model).</summary>
        public const string AlienType = "painter_alien";

        struct Spot
        {
            public int Shelf, Layer, Slot;
            public Spot(int s, int l, int k) { Shelf = s; Layer = l; Slot = k; }
        }

        Item At(Spot p) { return Shelves[p.Shelf].Layers[p.Layer][p.Slot]; }
        void Put(Spot p, Item it) { Shelves[p.Shelf].Layers[p.Layer][p.Slot] = it; }

        /// <summary>Every item with its place (all rows, all shelves).</summary>
        IEnumerable<Spot> AllSpots()
        {
            for (int si = 0; si < Shelves.Count; si++)
                for (int li = 0; li < Shelves[si].Layers.Count; li++)
                    for (int k = 0; k < Shelves[si].Width; k++)
                        if (Shelves[si].Layers[li][k] != null) yield return new Spot(si, li, k);
        }

        /// <summary>A type the player can see on a front row (an open shelf, the item not a stone sculpture).</summary>
        bool ShowsOnFront(Spot p)
        {
            var s = Shelves[p.Shelf];
            var it = At(p);
            return p.Layer == 0 && s.CanGive && it.Kind == ItemKind.Normal && !it.Mystery;
        }

        /// <summary>Types that are also inside a piñata: a booster taking their loose copies would leave the hidden one alone.</summary>
        HashSet<string> TypesInPinatas()
        {
            var set = new HashSet<string>();
            foreach (var p in AllSpots())
            {
                var it = At(p);
                if (it.Kind == ItemKind.Pinata && it.Content != null) set.Add(it.Content);
            }
            return set;
        }

        void SettleAll(MoveResult r)
        {
            foreach (var s in Shelves) Settle(s, r);
        }

        void Finish(MoveResult r)
        {
            r.Ok = true;
            r.Won = IsWon;
            r.Stuck = !r.Won && !HasAnyMove();
        }

        // ================================================================== vacuum
        /// <summary>
        /// Takes the three copies of one type shown on a front row: the type with the most copies on the front rows
        /// (ties: random), its front copies first, then the ones nearest the front. Counts as a match (locks, fuse boxes).
        /// False when no type qualifies.
        /// </summary>
        public bool Vacuum(Random rng, MoveResult r)
        {
            var inPinatas = TypesInPinatas();
            var all = AllSpots().ToList();
            var byType = new Dictionary<string, List<Spot>>();
            foreach (var p in all)
            {
                var it = At(p);
                if (it.Kind != ItemKind.Normal || it.Type == null) continue;
                List<Spot> l;
                if (!byType.TryGetValue(it.Type, out l)) byType[it.Type] = l = new List<Spot>();
                l.Add(p);
            }
            string best = null;
            double bestScore = -1;
            foreach (var kv in byType)
            {
                if (kv.Value.Count < 3 || inPinatas.Contains(kv.Key)) continue;
                int front = kv.Value.Count(ShowsOnFront);
                if (front == 0) continue;
                double score = front + rng.NextDouble() * 0.5;
                if (score > bestScore) { bestScore = score; best = kv.Key; }
            }
            if (best == null) return false;

            var take = byType[best].OrderByDescending(p => ShowsOnFront(p) ? 1 : 0).ThenBy(p => p.Layer).Take(3).ToList();
            foreach (var p in take)
            {
                r.MatchedItems.Add(At(p));
                Put(p, null);
            }
            r.Matched = true;
            r.MatchedType = best;
            CountMatch(best, r);
            SettleAll(r);
            Finish(r);
            return true;
        }

        // ================================================================== magic hat
        /// <summary>
        /// Deals the movable items of the open shelves again (frozen items, locked / closed / fuse / rotating-door /
        /// shutter shelves and piñatas stay where they are). Up to four types are dealt as ready matches: two copies on
        /// one shelf next to a free slot, the third on another front row. Some free room goes to the front rows so the
        /// player can move; a board with no free slot at all gets matches that pop straight away instead.
        /// </summary>
        public bool Shuffle(Random rng, MoveResult r)
        {
            var open = new List<int>();
            for (int si = 0; si < Shelves.Count; si++)
            {
                var s = Shelves[si];
                if (s.Locked || s.FuseClosed || s.Closed || s.Ferris || s.Shutter || s.IrisClosed) continue;
                open.Add(si);
            }
            var places = new List<Spot>();
            var pool = new List<Item>();
            foreach (int si in open)
            {
                var s = Shelves[si];
                for (int li = 0; li < s.Layers.Count; li++)
                    for (int k = 0; k < s.Width; k++)
                    {
                        var it = s.Layers[li][k];
                        if (it != null && (it.Frozen || it.Kind == ItemKind.Pinata)) continue;   // stays put
                        places.Add(new Spot(si, li, k));
                        if (it != null) pool.Add(it);
                    }
            }
            if (pool.Count < 2) return false;
            var before = new Dictionary<Item, Spot>();
            foreach (var p in places) if (At(p) != null) before[At(p)] = p;
            foreach (var p in places) Put(p, null);

            var front = places.Where(p => p.Layer == 0).ToList();
            var back = places.Where(p => p.Layer > 0).ToList();
            int freeTotal = places.Count - pool.Count;
            Shuffle(pool, rng);

            // 1. ready matches: pair + free slot on a 3+ shelf, third copy on another shelf's front row
            var byType = pool.Where(it => it.Kind == ItemKind.Normal || it.Kind == ItemKind.Egg).GroupBy(it => it.Type)
                .Where(g => g.Count() >= 3).Select(g => g.Key).ToList();
            Shuffle(byType, rng);
            var frontLeft = new List<Spot>(front);
            Shuffle(frontLeft, rng);
            var keepFree = new List<Spot>();
            int ready = 0, wantReady = Math.Min(4, byType.Count);
            bool popNow = freeTotal == 0;      // nowhere to move: deal threes that pop at once
            foreach (var type in byType)
            {
                if (ready >= wantReady) break;
                var copies = pool.Where(it => it.Type == type).Take(3).ToList();
                if (popNow)
                {
                    // three neighbours on a 3+ shelf front
                    var row = frontLeft.GroupBy(p => p.Shelf).Where(g => Shelves[g.Key].Width >= 3 && g.Count() >= 3)
                        .Select(g => Window(g.OrderBy(p => p.Slot).ToList())).FirstOrDefault(w => w != null);
                    if (row == null) break;
                    for (int i = 0; i < 3; i++) { Put(row[i], copies[i]); frontLeft.Remove(row[i]); pool.Remove(copies[i]); }
                    ready++;
                    continue;
                }
                if (keepFree.Count >= freeTotal) break;
                // a 3+ shelf with three front places left: pair + one place kept free
                var host = frontLeft.GroupBy(p => p.Shelf).Where(g => Shelves[g.Key].Width >= 3 && g.Count() >= 3)
                    .Select(g => Window(g.OrderBy(p => p.Slot).ToList())).FirstOrDefault(w => w != null);
                if (host == null) break;
                var others = frontLeft.Where(p => p.Shelf != host[0].Shelf).ToList();
                if (others.Count == 0) break;
                var third = others[0];
                Put(host[0], copies[0]);
                Put(host[1], copies[1]);
                Put(third, copies[2]);
                keepFree.Add(host[2]);
                foreach (var p in host) frontLeft.Remove(p);
                frontLeft.Remove(third);
                foreach (var c in copies) pool.Remove(c);
                ready++;
            }

            // 2. free room: the kept slots, then up to a quarter of the front rows (never more than the board has)
            int frontFree = Math.Min(freeTotal, Math.Max(keepFree.Count, Math.Min(freeTotal, Math.Max(2, front.Count / 4))));
            var freeFront = new HashSet<Spot>(keepFree);
            foreach (var p in frontLeft)
            {
                if (freeFront.Count >= frontFree) break;
                // never empty a whole front row (the row behind would move up and the shape would change)
                int stillHas = front.Count(q => q.Shelf == p.Shelf && !freeFront.Contains(q)) - 1;
                if (stillHas <= 0) continue;
                freeFront.Add(p);
            }
            var fill = frontLeft.Where(p => !freeFront.Contains(p)).ToList();
            Shuffle(back, rng);
            int backFree = freeTotal - freeFront.Count;
            var backFill = back.Skip(Math.Max(0, backFree)).ToList();
            fill.AddRange(backFill);
            for (int i = 0; i < pool.Count && i < fill.Count; i++) Put(fill[i], pool[i]);
            // more items than places left (the front kept too much room): use the free places, back first
            for (int i = fill.Count; i < pool.Count; i++)
            {
                var spare = places.FirstOrDefault(p => At(p) == null && !keepFree.Contains(p));
                if (At(spare) != null) break;
                Put(spare, pool[i]);
            }

            // 3. no accidental threes in a row (they would sit there unmatched), unless they were meant to pop
            if (!popNow) BreakThrees(open, rng);

            foreach (var kv in before)
            {
                var now = Locate(kv.Key);
                if (now.Shelf != kv.Value.Shelf || now.Layer != kv.Value.Layer || now.Slot != kv.Value.Slot) r.Shuffled.Add(kv.Key);
            }
            if (popNow)
                foreach (int si in open) ResolveMatches(Shelves[si], r);
            SettleAll(r);
            Finish(r);
            return true;
        }

        /// <summary>Three neighbouring places of one shelf's front row (sorted by slot), or null.</summary>
        static List<Spot> Window(List<Spot> sorted)
        {
            for (int i = 0; i + 2 < sorted.Count; i++)
                if (sorted[i + 2].Slot - sorted[i].Slot == 2) return sorted.GetRange(i, 3);
            return null;
        }

        Spot Locate(Item it)
        {
            foreach (var p in AllSpots()) if (At(p) == it) return p;
            return new Spot(-1, -1, -1);
        }

        void BreakThrees(List<int> open, Random rng)
        {
            var spots = new List<Spot>();
            foreach (int si in open)
                for (int li = 0; li < Shelves[si].Layers.Count; li++)
                    for (int k = 0; k < Shelves[si].Width; k++)
                        if (Shelves[si].Layers[li][k] != null && !Shelves[si].Layers[li][k].Frozen) spots.Add(new Spot(si, li, k));
            if (spots.Count < 4) return;
            foreach (int si in open)
                foreach (var layer in Shelves[si].Layers)
                    for (int i = 0; i + 2 < layer.Length; i++)
                    {
                        if (layer[i] == null || layer[i + 1] == null || layer[i + 2] == null) continue;
                        if (layer[i].Type != layer[i + 1].Type || layer[i + 1].Type != layer[i + 2].Type) continue;
                        if (layer[i + 1].Frozen) continue;
                        for (int attempt = 0; attempt < 40; attempt++)
                        {
                            var o = spots[rng.Next(spots.Count)];
                            var other = Shelves[o.Shelf].Layers[o.Layer];
                            var a = layer[i + 1];
                            var b = other[o.Slot];
                            if (b == null || b.Type == a.Type || b.Frozen) continue;
                            layer[i + 1] = b;
                            other[o.Slot] = a;
                            if (CompletesThree(other, o.Slot, a.Type) || CompletesThree(layer, i + 1, b.Type))
                            {
                                other[o.Slot] = b;
                                layer[i + 1] = a;
                                continue;
                            }
                            break;
                        }
                    }
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

        // ================================================================== painter
        /// <summary>
        /// Turns three whole types (9 items) into aliens, picking the types with the most copies on the front rows so
        /// that at least 4 of the 9 show there when the board allows it. Aliens that end up three side by side pop.
        /// </summary>
        public bool Paint(Random rng, MoveResult r)
        {
            var inPinatas = TypesInPinatas();
            var byType = new Dictionary<string, List<Spot>>();
            var hidden = new HashSet<string>();
            foreach (var p in AllSpots())
            {
                var it = At(p);
                if (it.Kind != ItemKind.Normal || it.Type == null || it.Type == AlienType) continue;
                if (it.Mystery) hidden.Add(it.Type);
                List<Spot> l;
                if (!byType.TryGetValue(it.Type, out l)) byType[it.Type] = l = new List<Spot>();
                l.Add(p);
            }
            var cands = byType.Where(kv => kv.Value.Count >= 3 && !inPinatas.Contains(kv.Key) && !hidden.Contains(kv.Key))
                .Select(kv => new
                {
                    Type = kv.Key,
                    Spots = kv.Value.OrderByDescending(p => ShowsOnFront(p) ? 1 : 0).ThenBy(p => p.Layer).Take(3).ToList()
                })
                .Select(c => new { c.Type, c.Spots, Front = c.Spots.Count(ShowsOnFront), Tie = rng.NextDouble() })
                .OrderByDescending(c => c.Front).ThenBy(c => c.Tie).ToList();
            if (cands.Count == 0) return false;
            foreach (var c in cands.Take(3))
                foreach (var p in c.Spots)
                {
                    var it = At(p);
                    it.Type = AlienType;
                    r.Painted.Add(it);
                }
            // aliens three in a row match at once
            foreach (var s in Shelves) ResolveMatches(s, r);
            SettleAll(r);
            Finish(r);
            return true;
        }

        // ================================================================== no match left
        /// <summary>
        /// False when no match can be made any more although items can still be moved: no type has three copies
        /// the player can bring together on the front rows, and no shelf can be emptied to show its next row.
        /// Boards with rows that move on their own (moving shelves, irises, rotating doors) are never judged.
        /// </summary>
        public bool MatchPossible()
        {
            if (IsWon) return true;
            if (!HasAnyMove()) return false;
            foreach (var s in Shelves) if (s.Moving || s.Iris || s.Ferris) return true;

            int free = 0;
            bool host = false;
            foreach (var s in Shelves)
            {
                if (!s.CanTake) continue;
                foreach (var it in s.Front) if (it == null) free++;
                if (s.Width >= 3) host = true;
            }
            if (free == 0) return false;

            // three copies on the front rows (frozen ones can only be the shelf the others come to)
            var movable = new Dictionary<string, int>();
            var frozenAt = new Dictionary<string, HashSet<int>>();
            foreach (var s in Shelves)
            {
                if (!s.CanGive) continue;
                foreach (var it in s.Front)
                {
                    if (it == null) continue;
                    if (it.Frozen)
                    {
                        HashSet<int> set;
                        if (!frozenAt.TryGetValue(it.Type, out set)) frozenAt[it.Type] = set = new HashSet<int>();
                        set.Add(s.Index);
                    }
                    else
                    {
                        int n;
                        movable.TryGetValue(it.Type, out n);
                        movable[it.Type] = n + 1;
                    }
                }
            }
            foreach (var type in movable.Keys.Concat(frozenAt.Keys).Distinct())
            {
                int m;
                movable.TryGetValue(type, out m);
                HashSet<int> fz;
                frozenAt.TryGetValue(type, out fz);
                int frozen = fz == null ? 0 : fz.Count;
                if (frozen > 1) continue;                       // two frozen copies on different shelves never meet
                if (frozen == 1 && m >= 2) return true;
                if (frozen == 0 && m >= 3 && host) return true;
            }

            // a shelf whose front can be moved away to show the next row
            foreach (var s in Shelves)
            {
                if (!s.CanGive || s.Layers.Count < 2) continue;
                bool behind = false;
                for (int li = 1; li < s.Layers.Count && !behind; li++)
                    foreach (var it in s.Layers[li]) if (it != null) { behind = true; break; }
                if (!behind) continue;
                int items = 0, empty = 0;
                bool stuck = false;
                foreach (var it in s.Front)
                {
                    if (it == null) empty++;
                    else if (it.Frozen) stuck = true;
                    else items++;
                }
                if (stuck) continue;
                int room = free - (s.CanTake ? empty : 0);
                if (room >= items) return true;
            }
            return false;
        }
    }
}
