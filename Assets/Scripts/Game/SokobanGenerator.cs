using System;
using System.Collections.Generic;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    [Serializable]
    public sealed class SokobanGenerationSettings
    {
        // Sentinels keep the requested automatic policy in Unity JSON; numeric values remain exact.
        public int width = 8, height = 8, boxCount;
        public int minPushes = 1, maxPushes = 60;
        // Read-only provenance for pre-0.13 JSON. Never interpret old movement limits as pushes.
        public int minMoves, maxMoves;
        public int parameterVersion = 2;
        public int difficulty = (int)SokobanDifficulty.Normal;
        public int wallPercent = -1, seed;
        public int budgetSeconds = 60, maxCandidates = 400, candidateSolveMs = 2000;
        public SokobanGenerationSettings Copy() => (SokobanGenerationSettings)MemberwiseClone();
        public string Validate()
        {
            if (width < 5 || width > 8 || height < 5 || height > 8) return "生成尺寸须为 5–8（包含外围墙）";
            if (boxCount < 0 || boxCount > 8) return "箱子数量须为 1–8，或留空自动";
            if (minMoves != 0 || maxMoves != 0) return "旧参数使用移动步数，请重新填写推箱次数范围";
            if (minPushes < 1 || maxPushes < minPushes || maxPushes > 1000) return "推箱次数须满足 1 ≤ 最少次数 ≤ 最多次数 ≤ 1000";
            if (difficulty < 0 || difficulty > 3) return "请选择不限、简单、普通或困难";
            if (wallPercent < -1 || wallPercent > 45) return "内部墙比例须为 0–45%，或留空自动";
            if (InteriorCells - (AutomaticWalls ? 0 : WallCount) < (AutomaticBoxes ? 1 : boxCount) * 2 + 1)
                return "地面不足，请减少箱子或内部墙";
            if (budgetSeconds < 1 || budgetSeconds > 300) return "时间预算须为 1–300 秒";
            if (maxCandidates < 1 || maxCandidates > 2000) return "候选上限须为 1–2000";
            if (candidateSolveMs < 1 || candidateSolveMs > 30000) return "单候选求解预算须为 1–30000 毫秒";
            return "";
        }
        public bool AutomaticBoxes => boxCount == 0;
        public bool AutomaticWalls => wallPercent == -1;
        public bool AnyDifficulty => difficulty == 0;
        public string DifficultyLabel => AnyDifficulty ? "不限" : SokobanDifficultyEvaluator.Name(difficulty);
        public int InteriorCells => (width - 2) * (height - 2);
        public int WallCount => AutomaticWalls ? -1 : WallsAtPercent(wallPercent);
        public int MaximumBoxes => AutomaticBoxes
            ? Math.Min(8, (InteriorCells - (AutomaticWalls ? 0 : WallCount) - 1) / 2)
            : boxCount;
        public int MaximumWalls => AutomaticWalls
            ? InteriorCells - 2 * (AutomaticBoxes ? 1 : boxCount) - 1 : WallCount;
        public string BoxCountLabel => AutomaticBoxes ? "自动" : boxCount.ToString();
        public string WallPercentLabel => AutomaticWalls ? "自动" : wallPercent + "%";
        private int WallsAtPercent(int percent) => (int)Math.Round(InteriorCells * percent / 100.0, MidpointRounding.AwayFromZero);

        internal bool AllowsStructure(int boxes, int walls) => boxes >= 1 && boxes <= MaximumBoxes && walls >= 0 &&
            walls <= MaximumWalls && InteriorCells - walls >= boxes * 2 + 1;
        internal bool MatchesStructure(int boxes, int walls) => AllowsStructure(boxes, walls) &&
            (AutomaticBoxes || boxes == boxCount) && (AutomaticWalls || walls == WallCount);

        public static bool TryParseStructure(string boxesText, string wallsText, out int boxes, out int walls, out string error)
        {
            boxes = 0; walls = -1; error = "";
            if (!string.IsNullOrWhiteSpace(boxesText) && (!int.TryParse(boxesText, out boxes) || boxes < 1 || boxes > 8))
            { error = "箱子数量须填写 1–8 的整数，或留空自动"; return false; }
            if (!string.IsNullOrWhiteSpace(wallsText) && (!int.TryParse(wallsText, out walls) || walls < 0 || walls > 45))
            { error = "内部墙比例须填写 0–45 的整数，或留空自动"; return false; }
            return true;
        }
    }
    public enum SokobanGenerationStatus { Success, Invalid, Cancelled, TimedOut, Exhausted, Error }
    public sealed class SokobanGenerationProgress
    {
        public int attempts, elapsedMs, lastMoves, lastPushes, lastDifficulty;
        public int terrainRejected, pushRejected, difficultyRejected, solveUnknown, solveUnsolvable, duplicatesRejected;
        public int mutationAttempts, acceptedMutations, rollbacks, validCandidates, qualifiedCandidates;
        public int firstMatchCandidate, bestCandidate;
        public float bestQuality;
        public string phase = "";
        internal SokobanGenerationProgress Copy() => (SokobanGenerationProgress)MemberwiseClone();
    }
    [Serializable]
    public sealed class SokobanGenerationQualityReport
    {
        public string evaluatorVersion = "html-quality-3.0";
        public float score, floorUsage, wallStructure, boxInteraction;
        public float pushComplexity, spatialDistribution, pathDiversity, wallDensity, solutionEfficiency;
        public int referencePushes;
        public int usedFloor, unusedFloor;
    }
    public sealed class SokobanGenerationResult
    {
        public const string Version = "html-incremental-3.1";
        public const string ReferenceRepository = "https://github.com/huanggaole/AutoGenerateSokobanLevel";
        public const string ReferenceCommit = "4ccf418633cf47e153436723c45518ea60f8cfa9";
        public SokobanGenerationStatus Status;
        public string Message = "";
        public SokobanGenerationSettings Settings;
        public SokobanGenerationProgress Statistics;
        public SokobanSolverLevel Level;
        public SokobanSolveResult Solution;
        public SokobanDifficultyReport Complexity;
        public SokobanGenerationQualityReport Quality;
    }
    // C# implementation of the reference HTML construction, placement and ranking rules.
    public static class SokobanGenerator
    {
        public static SokobanGenerationResult Generate(SokobanGenerationSettings parameters,
            CancellationToken cancellation = default, Action<SokobanGenerationProgress> progress = null,
            ISet<string> excludedLayouts = null)
        {
            return new SokobanHtmlGenerationSearch(parameters, cancellation, progress, excludedLayouts).Run();
        }
        private static string StateKey(int[] boxes, int player)
        { var copy = (int[])boxes.Clone(); Array.Sort(copy); return player + ":" + string.Join(",", copy); }
        internal static string LayoutKey(SokobanSolverLevel level)
        {
            var goals = (int[])level.Goals.Clone(); Array.Sort(goals);
            var walls = new char[level.Walls.Length];
            for (var c = 0; c < walls.Length; c++) walls[c] = level.Walls[c] ? '#' : '.';
            return level.Width + "x" + level.Height + ":" + new string(walls) + ":" + string.Join(",", goals) + ":" + StateKey(level.Boxes, level.Player);
        }
    }
}
