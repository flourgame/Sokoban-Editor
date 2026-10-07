using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    // Construction decisions follow HTML Solver.js / State.js, including batch ordering,
    // depth limit and frozen goal boxes. Final output is independently certified by Push A*.
    internal static class SokobanHtmlConstructionSolver
    {
        private sealed class Node
        {
            internal int[] Tiles;
            internal int Depth, Heuristic;
            internal long Order;
        }
        private static void StableSort(List<Node> nodes, Func<Node, int> rank)
        {
            // JS Array.sort preserves the current queue order on ties, including earlier sorts.
            for (var i = 0; i < nodes.Count; i++) nodes[i].Order = i;
            nodes.Sort((a, b) => { var c = rank(a).CompareTo(rank(b)); return c != 0 ? c : a.Order.CompareTo(b.Order); });
        }
        internal static int Run(SokobanHtmlGenerationLayout layout, SokobanHtmlGenerationProfile profile,
            int timeoutMs, CancellationToken token, out int pushes)
        {
            pushes = 0; var clock = Stopwatch.StartNew(); var width = layout.Width; var height = layout.Height;
            var start = (int[])layout.Tiles.Clone(); Flood(start, width);
            var queue = new List<Node> { new Node { Tiles = start, Heuristic = Heuristic(start, width) } };
            var seen = new HashSet<string> { Key(start) }; var iterations = 0; var total = 1;
            var offsets = new[] { -width, width, -1, 1 };
            while (iterations < profile.SolverIterations && total < profile.MemoryNodes)
            {
                token.ThrowIfCancellationRequested(); if (clock.ElapsedMilliseconds >= timeoutMs) return 0;
                if (queue.Count == 0) return -1;
                if (iterations % 100 == 0 && queue.Count > 1)
                    StableSort(queue, n => n.Heuristic + n.Depth);
                var batch = Math.Min(50, queue.Count);
                for (var i = 0; i < batch; i++)
                {
                    token.ThrowIfCancellationRequested(); if (clock.ElapsedMilliseconds >= timeoutMs) return 0;
                    iterations++; var parent = queue[0]; queue.RemoveAt(0); if (parent.Depth > 80) continue;
                    var children = new List<Node>();
                    for (var c = 0; c < parent.Tiles.Length; c++)
                    {
                        if (parent.Tiles[c] != 1 && parent.Tiles[c] != 2) continue;
                        foreach (var offset in offsets)
                        {
                            var standing = c - offset; var destination = c + offset;
                            var t = parent.Tiles;
                            if (t[standing] != 3 && t[standing] != 4 || t[destination] == 5 || t[destination] == 1 || t[destination] == 2) continue;
                            var next = (int[])t.Clone();
                            for (var p = 0; p < next.Length; p++) { if (next[p] == 3) next[p] = 6; else if (next[p] == 4) next[p] = 0; }
                            next[destination] = next[destination] == 0 ? 2 : 1;
                            next[c] = next[c] == 2 ? 4 : 3;
                            Flood(next, width);
                            if (Dead(next, width, height) || seen.Contains(Key(next))) continue;
                            var child = new Node { Tiles = next, Depth = parent.Depth + 1, Heuristic = Heuristic(next, width) };
                            children.Add(child);
                            if (Array.IndexOf(next, 1) < 0) { pushes = child.Depth; return 1; }
                        }
                    }
                    StableSort(children, n => n.Heuristic);
                    foreach (var child in children) { seen.Add(Key(child.Tiles)); total++; queue.Add(child); }
                }
            }
            return 0;
        }
        private static string Key(int[] tiles)
        { var chars = new char[tiles.Length]; for (var c = 0; c < chars.Length; c++) chars[c] = (char)('0' + tiles[c]); return new string(chars); }
        private static int Heuristic(int[] tiles, int width)
        {
            var total = 0;
            for (var c = 0; c < tiles.Length; c++) if (tiles[c] == 1)
            {
                var distance = int.MaxValue;
                for (var g = 0; g < tiles.Length; g++) if (tiles[g] == 0)
                    distance = Math.Min(distance, Math.Abs(c / width - g / width) + Math.Abs(c % width - g % width));
                if (distance != int.MaxValue) total += distance;
            }
            return total;
        }
        private static void Flood(int[] tiles, int width)
        {
            var queue = new Queue<int>();
            for (var c = 0; c < tiles.Length; c++) if (tiles[c] == 3 || tiles[c] == 4) queue.Enqueue(c);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                foreach (var next in new[] { c - width, c + width, c - 1, c + 1 })
                    if (next >= 0 && next < tiles.Length && (tiles[next] == 0 || tiles[next] == 6))
                    { tiles[next] = tiles[next] == 0 ? 4 : 3; queue.Enqueue(next); }
            }
        }
        private static bool Corner(int[] t, int c, int width) =>
            (t[c - 1] == 5 || t[c + 1] == 5) && (t[c - width] == 5 || t[c + width] == 5);
        private static bool Dead(int[] t, int width, int height)
        {
            var changed = true;
            while (changed)
            {
                changed = false;
                for (var y = 1; y < height - 1; y++) for (var x = 1; x < width - 1; x++)
                { var c = y * width + x; if (t[c] == 2 && Corner(t, c, width)) { t[c] = 5; changed = true; } }
            }
            for (var y = 1; y < height - 1; y++) for (var x = 1; x < width - 1; x++)
            { var c = y * width + x; if (t[c] == 1 && Corner(t, c, width)) return true; }
            for (var y = 0; y < height - 1; y++) for (var x = 0; x < width - 1; x++)
            {
                var boxes = 0; var walls = 0; var completed = 0; var targets = 0; var c = y * width + x;
                foreach (var p in new[] { c, c + 1, c + width, c + width + 1 })
                { if (t[p] == 1) boxes++; else if (t[p] == 5) walls++; else if (t[p] == 2) completed++; else if (t[p] == 0) targets++; }
                if (boxes + walls + completed == 4 && boxes > 0 && completed + targets < boxes) return true;
            }
            return false;
        }
    }
}
