// Moving shelves (Sort Express "im", level 11+).
//
// In the real game a moving row is usually 5 three-slot shelves side by side (x = -6.26 ... 6.26) while the
// screen shows three shelves (x = -4.7 ... 4.7). The row slides sideways at "ms" units per second and loops:
// shelves leave through a pipe-like tunnel at one screen edge and come back from the other one
// (GameObject/MovingShelves1xPipeHorizontal.prefab). Shelves inside a tunnel cannot be used.
using System;
using System.Collections.Generic;

namespace Sortex.Core
{
    public sealed class ConveyorRow
    {
        public float Y;
        public float Velocity;              // signed, world units per second
        public float Loop;                  // length after which a shelf comes back to the same place
        public readonly List<int> Shelves = new List<int>();   // indices, left to right at the start
    }

    public static class Conveyor
    {
        /// <summary>Half the width the game camera always shows: three 3-slot shelves.</summary>
        public const float ScreenHalfWidth = 4.695f;

        /// <summary>Half width of the visible area: the screen, or wider if static shelves stick out.</summary>
        public static float VisibleHalfWidth(IList<float> xs, IList<int> widths, IList<bool> moving)
        {
            float half = ScreenHalfWidth;
            for (int i = 0; i < xs.Count; i++)
            {
                if (moving[i]) continue;
                half = Math.Max(half, Math.Abs(xs[i]) + widths[i] * Board.SlotWidth * 0.5f);
            }
            return half;
        }

        /// <summary>Groups the moving shelves into rows (same height, same velocity).</summary>
        public static List<ConveyorRow> Rows(IList<float> xs, IList<float> ys, IList<int> widths, IList<bool> moving,
            IList<float> velocities, float visibleHalf)
        {
            var rows = new List<ConveyorRow>();
            for (int i = 0; i < xs.Count; i++)
            {
                if (!moving[i]) continue;
                ConveyorRow row = null;
                foreach (var r in rows)
                    if (Math.Abs(r.Y - ys[i]) < 0.05f && Math.Abs(r.Velocity - velocities[i]) < 1e-4f) { row = r; break; }
                if (row == null)
                {
                    row = new ConveyorRow { Y = ys[i], Velocity = velocities[i] };
                    rows.Add(row);
                }
                row.Shelves.Add(i);
            }
            foreach (var r in rows)
            {
                r.Shelves.Sort((a, b) => xs[a].CompareTo(xs[b]));
                int n = r.Shelves.Count;
                float widest = 0f;
                foreach (int i in r.Shelves) widest = Math.Max(widest, widths[i] * Board.SlotWidth);
                float spacing = n > 1 ? (xs[r.Shelves[n - 1]] - xs[r.Shelves[0]]) / (n - 1) : widest;
                spacing = Math.Max(spacing, widest * 0.5f);
                // a shelf has to disappear completely before it comes back on the other side
                r.Loop = Math.Max(n * spacing, 2f * visibleHalf + widest);
            }
            return rows;
        }

        /// <summary>Puts x back into the loop [-loop/2, loop/2) around the screen centre.</summary>
        public static float Wrap(float x, float loop)
        {
            float half = loop * 0.5f;
            float d = x + half;
            d -= loop * (float)Math.Floor(d / loop);
            return d - half;
        }

        /// <summary>True when (most of) a shelf at x is inside a tunnel and cannot be used.</summary>
        public static bool Hidden(float x, int width, float visibleHalf)
        {
            return Math.Abs(x) > visibleHalf - width * Board.SlotWidth * 0.25f;
        }
    }
}
