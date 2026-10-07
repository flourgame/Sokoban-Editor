using System;
using System.Collections.Generic;

namespace Kuluobishi.Sokoban
{
    // ProgressiveFallbackGenerator.js is called with <=1000 ms by the main HTML flow.
    // With the tier table below the selected tier is only ever 1 (rooms) or 2 (linear);
    // corridor (tier 0) and minimal (tier 3) are unreachable from here.
    internal static class SokobanHtmlGenerationFallback
    {
        internal static SokobanHtmlGenerationLayout Create(int width, int height, Random random, int remainingMs)
        {
            var target = remainingMs < 500 ? .3 : .5;
            var tiers = new[] { .8, .6, .4, .2 }; var tier = 0; var difference = double.PositiveInfinity;
            for (var i = 0; i < tiers.Length; i++) { var d = Math.Abs(tiers[i] - target); if (d < difference) { difference = d; tier = i; } }
            var level = new SokobanHtmlGenerationLayout(width, height, random, 1000);
            var tiles = level.Tiles;
            if (tier == 1)
            {
                var rooms = new List<int[]>(); var maxSize = Math.Min(5, Math.Min(width - 2, height - 2));
                for (var i = 0; i < Math.Min(3, Math.Max(2, width * height / 20)); i++)
                {
                    var rw = (int)(random.NextDouble() * (maxSize - 2)) + 3; var rh = (int)(random.NextDouble() * (maxSize - 2)) + 3;
                    var rx = (int)Math.Floor(random.NextDouble() * (width - rw - 2)) + 1;
                    var ry = (int)Math.Floor(random.NextDouble() * (height - rh - 2)) + 1;
                    for (var y = ry; y < ry + rh; y++) for (var x = rx; x < rx + rw; x++) tiles[y * width + x] = 6;
                    rooms.Add(new[] { rx + rw / 2, ry + rh / 2 });
                }
                for (var i = 0; i < rooms.Count - 1; i++)
                {
                    var a = rooms[i]; var b = rooms[i + 1];
                    for (var x = Math.Min(a[0], b[0]); x <= Math.Max(a[0], b[0]); x++) tiles[a[1] * width + x] = 6;
                    for (var y = Math.Min(a[1], b[1]); y <= Math.Max(a[1], b[1]); y++) tiles[y * width + b[0]] = 6;
                }
            }
            else
            {
                var y = height / 2;
                for (var x = 1; x < width - 1; x++)
                {
                    tiles[y * width + x] = 6;
                    if (random.NextDouble() < .3 && x > 2)
                    { var nextY = y + (random.NextDouble() < .5 ? -1 : 1); if (nextY >= 1 && nextY < height - 1) y = nextY; }
                }
            }
            var floor = new List<int>(); for (var c = 0; c < tiles.Length; c++) if (tiles[c] == 6) floor.Add(c);
            for (var i = floor.Count - 1; i > 0; i--)
            { var j = (int)(random.NextDouble() * (i + 1)); var swap = floor[i]; floor[i] = floor[j]; floor[j] = swap; }
            tiles[floor[0]] = 3;
            var boxes = Math.Min((floor.Count - 1) / 2, tier == 1 ? Math.Max(2, (width + height) / 8) : Math.Max(1, (width + height) / 10));
            for (var i = 0; i < boxes; i++) { tiles[floor[1 + i * 2]] = 1; tiles[floor[2 + i * 2]] = 0; }
            NormalizePlayers(level); return level;
        }
        private static void NormalizePlayers(SokobanHtmlGenerationLayout level)
        {
            // HTML conversion selects the last row-major player, even if a template left two markers.
            var last = Array.LastIndexOf(level.Tiles, 3);
            for (var c = 0; c < last; c++) if (level.Tiles[c] == 3) level.Tiles[c] = 6;
        }
    }
}
