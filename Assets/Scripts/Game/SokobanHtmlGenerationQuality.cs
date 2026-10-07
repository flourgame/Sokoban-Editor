using System;

namespace Kuluobishi.Sokoban
{
    internal static class SokobanHtmlGenerationQuality
    {
        // Actual HTML StateNode objects have neither direction nor boxIndex. Preserve its
        // default direction U and efficiency 0.3 rather than silently changing ranking.
        internal static SokobanGenerationQualityReport Measure(SokobanSolverLevel level, int referencePushes)
        {
            var threshold = (int)Math.Floor(level.Width * level.Height * .35); var count = referencePushes + 1;
            var step = count < threshold * .5 ? .2 : count < threshold ? .5 : count <= threshold * 2
                ? .8 + (double)(count - threshold) / threshold * .2 : count <= threshold * 3 ? 1
                : Math.Max(.6, 1 - (double)(count - threshold * 3) / (threshold * 3) * .4);
            var patterns = count - 2;
            var pattern = patterns <= 0 ? 0 : Math.Max(0, 1.0 / patterns - Math.Min(.5, Math.Max(0, patterns - 2) * .1));
            var pushComplexity = Math.Min(1, step * .7 + pattern * .3);
            var spatial = Distribution(level.Boxes, level) * .4 + Distribution(level.Goals, level) * .4 + Separation(level) * .2;
            var walls = SokobanHtmlGenerationLayout.InteriorWallCount(level);
            var density = (double)walls / ((level.Width - 2) * (level.Height - 2));
            var wallScore = density < .1 ? .3 : density > .8 ? .2 : density <= .6 ? .5 + density / .6 * .5 : Math.Max(.4, 1 - (density - .6) / .2 * .6);
            return new SokobanGenerationQualityReport { referencePushes = referencePushes, pushComplexity = (float)pushComplexity,
                spatialDistribution = (float)spatial, pathDiversity = 0, wallDensity = (float)wallScore, solutionEfficiency = .3f,
                score = (float)(100 * (pushComplexity * .35 + spatial * .25 + wallScore * .12 + .3 * .08)) };
        }
        private static int Distance(int a, int b, int width) => Math.Abs(a / width - b / width) + Math.Abs(a % width - b % width);
        private static double Distribution(int[] cells, SokobanSolverLevel level)
        {
            if (cells.Length <= 1) return 1;
            var sum = 0.0; var pairs = 0;
            for (var i = 0; i < cells.Length; i++) for (var j = i + 1; j < cells.Length; j++) { sum += Distance(cells[i], cells[j], level.Width); pairs++; }
            return Math.Min(1, sum / pairs / ((level.Width + level.Height - 2) * .5));
        }
        private static double Separation(SokobanSolverLevel level)
        {
            if (level.Boxes.Length == 0 || level.Goals.Length == 0) return 0;
            var total = 0.0;
            foreach (var box in level.Boxes)
            { var nearest = int.MaxValue; foreach (var goal in level.Goals) nearest = Math.Min(nearest, Distance(box, goal, level.Width)); total += nearest; }
            var average = total / level.Boxes.Length; var ideal = Math.Max(2, (level.Width + level.Height) / 4);
            return average < 1 ? .2 : average > ideal * 2 ? .3 : Math.Min(1, average / ideal);
        }
        internal static bool Better(SokobanGenerationQualityReport candidate, SokobanGenerationQualityReport best) =>
            best == null || candidate.score > best.score || Math.Abs(candidate.score - best.score) < 10 && candidate.referencePushes > best.referencePushes;
        internal static bool High(SokobanGenerationQualityReport q) => q.score >= 70 && q.pushComplexity >= .6f;
        internal static bool Acceptable(SokobanGenerationQualityReport q) => q.score >= 50 && q.pushComplexity >= .4f;
    }
}
