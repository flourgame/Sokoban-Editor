using System;
using System.Collections.Generic;

namespace Kuluobishi.Sokoban
{
    // Source: HTML_Sokoban/config/default-settings.json, pinned in SokobanGenerationResult.
    internal sealed class SokobanHtmlGenerationProfile
    {
        internal double MinWallRatio = .25, MaxWallRatio = 1.6, WallPriority = .7, WallProbability = .5, BoxProbability = 1;
        internal int SolverIterations = 150000, MemoryNodes = 350000, PlacementAttempts = 1400;
        internal static SokobanHtmlGenerationProfile ForSize(int width, int height)
        {
            var p = new SokobanHtmlGenerationProfile();
            // The webpage uses global defaults for sizes without an exact profile.
            if (width != height || width < 6 || width > 11 || width == 8) return p;
            p.MinWallRatio = .15 + (width - 6) * .05;
            p.MaxWallRatio = 1.2 + (width - 6) * .2;
            p.WallPriority = width == 6 ? .5 : width == 7 ? .6 : .75 + (width - 9) * .05;
            p.WallProbability = .15; p.BoxProbability = width <= 9 ? .30 : width == 10 ? .32 : .35;
            p.SolverIterations = width <= 7 ? 150000 : 200000;
            p.MemoryNodes = width == 6 ? 250000 : width == 7 ? 300000 : width == 11 ? 500000 : 400000;
            p.PlacementAttempts = 1000 + (width - 6) * 200;
            return p;
        }
    }

    // Tile values and row-major weighted selection follow GenerateLevel.js.
    internal sealed class SokobanHtmlGenerationLayout
    {
        internal const int Target = 0, Box = 1, BoxOnTarget = 2, Player = 3, PlayerOnTarget = 4, Wall = 5, Floor = 6;
        internal readonly int Width, Height;
        internal int[] Tiles;
        private int[] saved;
        private readonly Random random;
        private readonly int placementAttempts;
        internal SokobanHtmlGenerationLayout(int width, int height, Random rng, int attempts)
        {
            Width = width; Height = height; random = rng; placementAttempts = attempts;
            Tiles = new int[width * height];
            for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                Tiles[y * width + x] = x == 0 || y == 0 || x == width - 1 || y == height - 1 ? Wall : Floor;
            if (!PlaceOriginal(Player)) throw new InvalidOperationException("无法放置玩家");
            Save();
        }
        internal void Save() => saved = (int[])Tiles.Clone();
        internal void Load() => Tiles = (int[])saved.Clone();
        internal int Count(int tile) { var n = 0; foreach (var t in Tiles) if (t == tile) n++; return n; }
        internal int InteriorWalls => Count(Wall) - (2 * Width + 2 * Height - 4);
        internal static int InteriorWallCount(SokobanSolverLevel level)
        {
            var n = 0;
            for (var y = 1; y < level.Height - 1; y++) for (var x = 1; x < level.Width - 1; x++)
                if (level.Walls[y * level.Width + x]) n++;
            return n;
        }
        private bool PlaceOriginal(int tile)
        {
            for (var attempt = 0; attempt < placementAttempts; attempt++)
            {
                var y = (int)(random.NextDouble() * Height); var x = (int)(random.NextDouble() * Width);
                var c = y * Width + x;
                if (Tiles[c] == Floor) { Tiles[c] = tile; return true; }
            }
            return false;
        }
        internal bool Place(int tile)
        {
            var cells = new List<int>(); var weights = new List<double>(); var total = 0.0;
            for (var y = 1; y < Height - 1; y++) for (var x = 1; x < Width - 1; x++)
            {
                var cell = y * Width + x; if (Tiles[cell] != Floor) continue;
                var weight = Weight(y, x, tile); cells.Add(cell); weights.Add(weight); total += weight;
            }
            if (total <= 0) return PlaceOriginal(tile);
            var value = random.NextDouble() * total;
            for (var i = 0; i < cells.Count; i++)
            {
                value -= weights[i];
                if (value <= 0 || i == cells.Count - 1) { Tiles[cells[i]] = tile; return true; }
            }
            return false;
        }
        internal double Weight(int y, int x, int tile)
        {
            var weight = 1.0;
            if (tile == Box)
            {
                var distance = Math.Abs(y - Height / 2) + Math.Abs(x - Width / 2); var maximum = (Width + Height) / 2;
                weight = distance < maximum * .3 ? .7 : distance > maximum * .7 ? .5 : 1;
                var elements = Around(y, x, 2, 0);
                weight *= elements > 2 ? .3 : elements > 0 ? .7 : 1;
            }
            else if (tile == Wall)
            {
                var border = Math.Min(Math.Min(y - 1, x - 1), Math.Min(Height - 2 - y, Width - 2 - x));
                weight *= border <= 1 ? 1.5 : border >= 3 ? .7 : 1;
                var walls = Around(y, x, 1, 1); weight *= walls >= 3 ? .2 : walls == 1 || walls == 2 ? 1.3 : 1;
                var open = 0; var runs = 0; var last = false;
                foreach (var c in new[] { (y - 1) * Width + x, (y + 1) * Width + x, y * Width + x - 1, y * Width + x + 1 })
                { var connected = Tiles[c] != Wall; if (connected) { open++; if (!last) runs++; } last = connected; }
                if (open <= 2 && runs >= 2) weight *= .1;
                if (Around(y, x, 1, 0) > 0) weight *= .6;
            }
            else if (tile == Target)
            {
                var distance = int.MaxValue;
                for (var c = 0; c < Tiles.Length; c++) if (Tiles[c] == Box)
                    distance = Math.Min(distance, Math.Abs(y - c / Width) + Math.Abs(x - c % Width));
                if (distance != int.MaxValue) weight *= distance < 1 ? .3 : distance <= 4 ? 1 + (4 - distance) * .2 : Math.Max(.4, 1 - (distance - 4) * .1);
                if (Around(y, x, 2, 2) > 0) weight *= .5;
                if (Around(y, x, 1, 1) >= 3) weight *= .3;
            }
            return weight;
        }
        private int Around(int y, int x, int radius, int kind)
        {
            var n = 0;
            for (var yy = Math.Max(0, y - radius); yy <= Math.Min(Height - 1, y + radius); yy++)
            for (var xx = Math.Max(0, x - radius); xx <= Math.Min(Width - 1, x + radius); xx++)
            {
                if (yy == y && xx == x) continue; var t = Tiles[yy * Width + xx];
                if (kind == 1 ? t == Wall : kind == 2 ? t == Target || t == PlayerOnTarget || t == BoxOnTarget : t <= PlayerOnTarget) n++;
            }
            return n;
        }
        internal SokobanSolverLevel Snapshot()
        {
            var walls = new bool[Tiles.Length]; var boxes = new List<int>(); var goals = new List<int>(); var player = -1;
            for (var c = 0; c < Tiles.Length; c++)
            {
                var t = Tiles[c]; walls[c] = t == Wall;
                if (t == Box || t == BoxOnTarget) boxes.Add(c);
                if (t == Target || t == BoxOnTarget || t == PlayerOnTarget) goals.Add(c);
                if (t == Player || t == PlayerOnTarget) player = c;
            }
            return new SokobanSolverLevel { Width = Width, Height = Height, Walls = walls, Boxes = boxes.ToArray(), Goals = goals.ToArray(), Player = player };
        }
    }
}
