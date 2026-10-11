// Effective board of every shipped level after the item generator has dealt its waves (seed 1):
//   mono boardstats.exe <folder with Level*.json>  >  board_stats.jsonl
// One JSON line per level: total items to clear, generated items, front slots / filled, depth.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Sortex.Core;

static class BoardStats
{
    static int Main(string[] a)
    {
        var ci = CultureInfo.InvariantCulture;
        foreach (var f in Directory.GetFiles(a[0], "Level*_0.json").OrderBy(x => x))
        {
            var lv = LevelData.FromJson(File.ReadAllText(f));
            var b = Board.FromLevel(lv, 1);
            int frontSlots = 0, frontFilled = 0, maxDepth = 0, depthSum = 0, backFilled = 0, backSlots = 0;
            foreach (var s in b.Shelves)
            {
                frontSlots += s.Width;
                frontFilled += s.Front.Count(i => i != null);
                maxDepth = Math.Max(maxDepth, s.Layers.Count);
                depthSum += s.Layers.Count;
                for (int li = 1; li < s.Layers.Count; li++)
                {
                    backSlots += s.Width;
                    backFilled += s.Layers[li].Count(i => i != null);
                }
            }
            string n = Path.GetFileNameWithoutExtension(f).Substring(5, 5);
            Console.WriteLine(string.Format(ci,
                "{{\"n\":{0},\"total\":{1},\"generated\":{2},\"frontSlots\":{3},\"frontFilled\":{4},\"maxDepth\":{5},\"avgDepth\":{6:0.###},\"backSlots\":{7},\"backFilled\":{8}}}",
                int.Parse(n), b.TotalItems, b.GeneratedItems, frontSlots, frontFilled, maxDepth,
                b.Shelves.Count > 0 ? (double)depthSum / b.Shelves.Count : 0, backSlots, backFilled));
        }
        return 0;
    }
}
