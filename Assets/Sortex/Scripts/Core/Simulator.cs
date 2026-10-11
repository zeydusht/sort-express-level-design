// Difficulty estimator shared by the Unity Level Editor and the command line tool (Tools/sim).
//
//  1. Solvability : full-information playouts with noise, then a depth-first search if they fail.
//  2. Players     : a population of imperfect-information players (they see the front row and the row
//                   right behind it, mystery items stay unknown, cargo doors hide their rows) with a
//                   per-move time model. Played N times -> win rate, how players lose, time used.
//
// Pure C#, no UnityEngine: the editor runs it on a background thread.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sortex.Core
{
    public sealed class SimSettings
    {
        public int Runs = 300;
        public int Seed = 7;
        public double RelaxedTimeFactor = 2.5;   // players keep going up to 2.5x the timer (separates board vs time)
        public int ProofPlayouts = 300;
        public int DfsNodeLimit = 20000;
        public double SkillMin = 0.35;
        public double SkillMax = 0.90;
    }

    public sealed class SimReport
    {
        public string Name;
        public int Items, Types, Shelves, BadTypes;
        public bool Solvable;
        public int SolutionMoves = -1;
        public int SolverNodes;
        public int Runs;
        public double WinRate, WinRelaxedTimer, Stuck, Timeout, AvgMovesWin, AvgTimeWin, AvgProgressLoss;
        public int TimeLimit;
        public bool Cancelled;

        public string ToJson()
        {
            Func<double, string> f = v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            return "{" +
                   "\"name\":\"" + (Name ?? "").Replace("\"", "'") + "\"," +
                   "\"items\":" + Items + ",\"types\":" + Types + ",\"shelves\":" + Shelves +
                   ",\"badTypes\":" + BadTypes +
                   ",\"solvable\":" + (Solvable ? "true" : "false") + ",\"solverMoves\":" + SolutionMoves + ",\"solverNodes\":" + SolverNodes +
                   ",\"winRate\":" + f(WinRate) +
                   ",\"winRelaxedTimer\":" + f(WinRelaxedTimer) +
                   ",\"stuck\":" + f(Stuck) +
                   ",\"timeout\":" + f(Timeout) +
                   ",\"avgMovesWin\":" + f(AvgMovesWin) +
                   ",\"avgTimeWin\":" + f(AvgTimeWin) +
                   ",\"avgProgressLoss\":" + f(AvgProgressLoss) +
                   ",\"timeLimit\":" + TimeLimit + "}";
        }
    }

    public sealed class Simulator
    {
        public sealed class Outcome
        {
            public bool Win;
            public string Reason;
            public double Time;
            public int Moves;
            public double Progress;
        }

        bool oracle;
        int nodes;
        HashSet<string> seen;
        int nodeLimit;

        /// <summary>Optional move trace (used by the command line tool).</summary>
        public Action<string> Trace;

        // ================================================================== entry point
        /// <param name="progress">called with 0..1; return false to cancel</param>
        public SimReport Run(LevelData level, SimSettings settings, Func<float, bool> progress = null)
        {
            var board = Board.FromLevel(level);
            var report = new SimReport
            {
                Name = level.name,
                Items = board.TotalItems,
                Shelves = board.Shelves.Count,
                TimeLimit = level.timeLimit,
                Runs = settings.Runs
            };
            var counts = new Dictionary<string, int>();
            foreach (var s in board.Shelves)
                foreach (var l in s.Layers)
                    foreach (var it in l)
                        if (it != null)
                        {
                            int n;
                            counts.TryGetValue(it.Type, out n);
                            counts[it.Type] = n + 1;
                        }
            report.Types = counts.Count;
            report.BadTypes = counts.Count(kv => kv.Value % 3 != 0);

            var rng = new Random(settings.Seed);
            var results = new List<Outcome>();
            var relaxed = new List<bool>();
            for (int i = 0; i < settings.Runs; i++)
            {
                if (progress != null && i % 10 == 0 && !progress(0.9f * i / Math.Max(1, settings.Runs)))
                {
                    report.Cancelled = true;
                    return report;
                }
                double skill = settings.SkillMin + rng.NextDouble() * (settings.SkillMax - settings.SkillMin);
                // played with a relaxed timer; the real timer is applied afterwards, so one run gives both
                // the board difficulty (win with relaxed timer) and the time pressure (win with timer)
                var o = Play(board, level.timeLimit * settings.RelaxedTimeFactor, rng, skill);
                relaxed.Add(o.Win);
                if (o.Time > level.timeLimit)
                {
                    o.Win = false;
                    o.Reason = "time";
                }
                results.Add(o);
            }

            // solvability proof 1: a player cleared it, or full-information playouts
            int proofMoves = results.Where(r => r.Win).Select(r => r.Moves).DefaultIfEmpty(-1).Min();
            if (proofMoves < 0 && relaxed.Any(x => x)) proofMoves = 0;
            var prng = new Random(settings.Seed * 31 + 7);
            oracle = true;
            for (int i = 0; i < settings.ProofPlayouts && proofMoves < 0; i++)
            {
                var o = Play(board, double.MaxValue, prng, 0.6 + 0.4 * prng.NextDouble());
                if (o.Win) proofMoves = o.Moves;
            }
            oracle = false;

            // solvability proof 2: depth-first search (only when the playouts failed)
            nodes = 0;
            if (proofMoves < 0)
            {
                if (progress != null) progress(0.95f);
                var path = new List<int[]>();
                bool solved = false;
                nodeLimit = settings.DfsNodeLimit;
                seen = new HashSet<string>();
                var th = new System.Threading.Thread(() => { solved = Solve(board.Clone(), path); }, 64 * 1024 * 1024);
                th.Start();
                th.Join();
                if (solved) proofMoves = path.Count;
            }
            report.Solvable = proofMoves >= 0;
            report.SolutionMoves = proofMoves;
            report.SolverNodes = nodes;

            int runs = Math.Max(1, results.Count);
            var wins = results.Where(r => r.Win).ToList();
            report.WinRate = wins.Count / (double)runs;
            report.WinRelaxedTimer = relaxed.Count(x => x) / (double)runs;
            report.Stuck = results.Count(r => r.Reason == "stuck") / (double)runs;
            report.Timeout = results.Count(r => r.Reason == "time") / (double)runs;
            report.AvgMovesWin = wins.Count > 0 ? wins.Average(r => r.Moves) : 0;
            report.AvgTimeWin = wins.Count > 0 ? wins.Average(r => r.Time) : 0;
            report.AvgProgressLoss = results.Where(r => !r.Win).Select(r => r.Progress).DefaultIfEmpty(1).Average();
            if (progress != null) progress(1f);
            return report;
        }

        // ================================================================== solver
        static string Key(Board b)
        {
            var sb = new StringBuilder();
            foreach (var s in b.Shelves)
            {
                sb.Append(s.Locked ? 'L' : s.Closed ? 'C' : 'O').Append(s.LockCount)
                  .Append(s.IrisClosed ? 'i' : '-').Append(s.FuseDone.Count).Append('|');
                foreach (var l in s.Layers)
                {
                    var names = l.Where(i => i != null).Select(i => i.Type).OrderBy(t => t, StringComparer.Ordinal);
                    sb.Append(string.Join(",", names.ToArray())).Append(';');
                }
                sb.Append('#');
            }
            return sb.ToString();
        }

        static int Score(Board b, int[] m)
        {
            var from = b.Shelves[m[0]];
            var to = b.Shelves[m[2]];
            string t = from.Front[m[1]].Type;
            int same = to.Front.Count(i => i != null && i.Type == t);
            int other = to.Front.Count(i => i != null && i.Type != t);
            int score = 0;
            if (to.Width >= 3 && same >= 2) score += 100;
            else if (same == 1 && other == 0) score += 30;
            else if (same == 0 && other == 0) score += 5;
            int left = from.Front.Count(i => i != null) - 1;
            if (left == 0) score += 20;
            if (from.Front.Count(i => i != null && i.Type == t) >= 2) score -= 40;
            score -= other * 8;
            return score;
        }

        bool Solve(Board b, List<int[]> path)
        {
            if (b.IsWon) return true;
            if (++nodes > nodeLimit) return false;
            if (!seen.Add(Key(b))) return false;
            var moves = new List<int[]>();
            b.LegalMoves(moves);
            moves.Sort((x, y) => Score(b, y).CompareTo(Score(b, x)));
            foreach (var m in moves)
            {
                var c = b.Clone();
                var r = c.Move(m[0], m[1], m[2], m[3]);
                if (!r.Ok) continue;
                path.Add(m);
                if (Solve(c, path)) return true;
                path.RemoveAt(path.Count - 1);
                if (nodes > nodeLimit) return false;
            }
            return false;
        }

        // ================================================================== player model
        double clock;   // seconds since the start of the game being played (moving shelves)

        /// <summary>The player sees (and can use) the shelf right now: moving shelves inside a tunnel are out of sight.</summary>
        bool OnScreen(Board b, int shelf)
        {
            return oracle || b.OnScreen(shelf, clock);
        }

        string Visible(Board b, int shelf, int slot)
        {
            var it = b.Shelves[shelf].Front[slot];
            if (it == null) return null;
            if (!OnScreen(b, shelf)) return "?off" + it.Uid;
            return it.Mystery && !oracle ? "?" + it.Uid : it.Type;
        }

        bool SeesBehind(Shelf s)
        {
            return oracle || !s.Cargo && !s.Ferris;
        }

        static double Prog(Board b)
        {
            return 1.0 - (double)b.RemainingItems / Math.Max(1, b.TotalItems);
        }

        public Outcome Play(Board start, double timeLimit, Random rng, double skill)
        {
            var b = start.Clone();
            double t = 0;
            var moves = new List<int[]>();
            int guard = 0;
            var recent = new Queue<int>();   // uids moved in the last few moves (players avoid shuffling them again)
            while (true)
            {
                if (b.IsWon) return new Outcome { Win = true, Reason = "win", Time = t, Moves = b.Moves, Progress = 1 };
                if (!b.HasAnyMove()) return new Outcome { Reason = "stuck", Time = t, Moves = b.Moves, Progress = Prog(b) };
                if (t > timeLimit) return new Outcome { Reason = "time", Time = t, Moves = b.Moves, Progress = Prog(b) };
                if (++guard > 2000) return new Outcome { Reason = "loop", Time = t, Moves = b.Moves, Progress = Prog(b) };

                b.LegalMoves(moves);
                clock = t;
                if (b.HasConveyors && !oracle)
                {
                    // only the shelves on screen can be used; when every move needs a shelf that is in a tunnel, wait
                    moves.RemoveAll(m => !OnScreen(b, m[0]) || !OnScreen(b, m[2]));
                    if (moves.Count == 0)
                    {
                        t += 0.5;
                        continue;
                    }
                }

                // what the player can see: front rows (mystery hidden) + the row right behind (not behind cargo doors)
                var visibleBack = new Dictionary<string, int>();
                foreach (var s in b.Shelves)
                {
                    if (!SeesBehind(s) || !OnScreen(b, s.Index)) continue;
                    for (int li = 1; li < s.Layers.Count && (li == 1 || oracle); li++)
                        foreach (var it in s.Layers[li])
                            if (it != null && (!it.Mystery || oracle))
                            {
                                int n;
                                visibleBack.TryGetValue(it.Type, out n);
                                visibleBack[it.Type] = n + 1;
                            }
                }

                var frontCount = new Dictionary<string, int>();
                foreach (var s in b.Shelves)
                {
                    if (!OnScreen(b, s.Index)) continue;
                    foreach (var it in s.Front)
                        if (it != null && (!it.Mystery || oracle))
                        {
                            int n;
                            frontCount.TryGetValue(it.Type, out n);
                            frontCount[it.Type] = n + 1;
                        }
                }

                var scored = new List<KeyValuePair<int[], double>>();
                double best = double.MinValue;
                foreach (var m in moves)
                {
                    string type = Visible(b, m[0], m[1]);
                    var to = b.Shelves[m[2]];
                    var from = b.Shelves[m[0]];
                    int same = 0, other = 0, hidden = 0;
                    for (int k = 0; k < to.Width; k++)
                    {
                        string v = Visible(b, m[2], k);
                        if (v == null) continue;
                        if (v.StartsWith("?")) hidden++;
                        else if (v == type) same++;
                        else other++;
                    }
                    if (to.Width > 3)
                    {
                        // wide shelf: only the neighbours of the target slot count (three side by side match)
                        int run = 0;
                        for (int k = m[3] - 1; k >= 0 && !(m[0] == m[2] && k == m[1]) && Visible(b, m[2], k) == type; k--) run++;
                        for (int k = m[3] + 1; k < to.Width && !(m[0] == m[2] && k == m[1]) && Visible(b, m[2], k) == type; k++) run++;
                        same = Math.Min(run, 2);
                        if (run > 0) other = hidden = 0;
                    }
                    double sc = 0;
                    if (type.StartsWith("?")) sc -= 6;                                   // moving an unknown item
                    else if (to.Width >= 3 && same >= 2) sc += 100;                       // obvious triple
                    else if (same == 1 && other == 0 && hidden == 0) sc += 28;
                    else if (same == 0 && other == 0 && hidden == 0) sc += 6;
                    int fromSame = 0, fromLeft = 0;
                    for (int k = 0; k < from.Width; k++)
                    {
                        if (k == m[1] || from.Front[k] == null) continue;
                        fromLeft++;
                        if (Visible(b, m[0], k) == type) fromSame++;
                    }
                    // digging: the row behind this shelf holds the missing third of a pair that is on screen
                    int useful = 0;
                    if (from.Layers.Count > 1 && SeesBehind(from))
                        foreach (var bi in from.Layers[1])
                            if (bi != null && (!bi.Mystery || oracle) && frontCount.ContainsKey(bi.Type) && frontCount[bi.Type] >= 2) useful++;
                    if (fromLeft == 0) sc += from.Layers.Count > 1 ? 18 + 14 * useful : 4;   // reveals the next row
                    else if (useful > 0) sc += 9 * useful / (fromLeft + 1);                    // working towards it
                    if (fromSame >= 1) sc -= useful > 0 ? 12 : 35;                            // breaking a pair
                    // two-step planning: the moved item is the odd one next to a pair -> it makes room for the third
                    if (from.Width == 3 && fromLeft == 2 && fromSame == 0)
                    {
                        string a = null, c2 = null;
                        for (int k = 0; k < 3; k++)
                        {
                            if (k == m[1]) continue;
                            if (a == null) a = Visible(b, m[0], k);
                            else c2 = Visible(b, m[0], k);
                        }
                        if (a != null && a == c2 && !a.StartsWith("?")) sc += 22;
                    }
                    // preparing a pair on a crowded shelf ([X, Y, _] + X): only worth it when Y can leave
                    if (!type.StartsWith("?") && to.Width == 3 && same == 1 && other == 1 && hidden == 0) sc += 10;
                    if (!type.StartsWith("?") && same == 1 && other == 0 && hidden == 0)
                    {
                        // the third copy is in sight (another front or the row behind): stronger pair
                        int seenElsewhere = 0;
                        for (int q = 0; q < b.Shelves.Count; q++)
                        {
                            if (q == m[0] || q == m[2]) continue;
                            for (int k = 0; k < b.Shelves[q].Width; k++) if (Visible(b, q, k) == type) seenElsewhere++;
                        }
                        if (seenElsewhere > 0 || visibleBack.ContainsKey(type)) sc += 12;
                    }
                    if (recent.Contains(from.Front[m[1]].Uid)) sc -= 25;                  // no ping-pong / shuffling
                    sc -= other * 7 + hidden * 4;
                    scored.Add(new KeyValuePair<int[], double>(m, sc));
                    if (sc > best) best = sc;
                }

                // noisy choice: softmax with a temperature that depends on skill
                double temp = 3.0 + (1.0 - skill) * 10.0;
                double sum = 0;
                var w = new double[scored.Count];
                for (int i = 0; i < scored.Count; i++)
                {
                    w[i] = Math.Exp((scored[i].Value - best) / temp);
                    sum += w[i];
                }
                double pick = rng.NextDouble() * sum;
                int chosen = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    pick -= w[i];
                    if (pick <= 0)
                    {
                        chosen = i;
                        break;
                    }
                }
                var mv = scored[chosen].Key;

                // time model: reaction + search time; search grows with what is on screen
                int visibleTypes = b.Shelves.SelectMany(s => s.Front).Where(i => i != null).Select(i => i.Type).Distinct().Count();
                double think = 0.40 + 0.015 * visibleTypes + (best < 20 ? 0.5 : 0.0);
                t += think * (0.75 + rng.NextDouble() * 0.5) * (1.25 - skill * 0.5);

                if (Trace != null) Trace(string.Format("t={0:0.0} best={1:0} pick={2:0} move {3}.{4}->{5}", t, best, scored[chosen].Value, mv[0], mv[1], mv[2]));
                recent.Enqueue(b.Shelves[mv[0]].Front[mv[1]].Uid);
                if (recent.Count > 4) recent.Dequeue();
                b.Move(mv[0], mv[1], mv[2], mv[3]);
            }
        }
    }
}
