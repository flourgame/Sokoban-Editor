using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    internal sealed class SokobanHtmlGenerationSearch
    {
        private readonly SokobanGenerationSettings settings;
        private readonly CancellationToken external;
        private readonly Action<SokobanGenerationProgress> progress;
        private readonly ISet<string> excluded;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly SokobanGenerationProgress stats = new SokobanGenerationProgress();
        private SokobanGenerationResult best;
        private Random random;
        private SokobanHtmlGenerationProfile profile;
        private SokobanHtmlGenerationLayout layout;
        internal SokobanHtmlGenerationSearch(SokobanGenerationSettings parameters, CancellationToken cancellation,
            Action<SokobanGenerationProgress> callback, ISet<string> excludedLayouts)
        { settings = parameters?.Copy(); external = cancellation; progress = callback; excluded = excludedLayouts; }

        internal SokobanGenerationResult Run()
        {
            var error = settings == null ? "缺少生成参数" : settings.Validate();
            if (error.Length > 0) return Finish(SokobanGenerationStatus.Invalid, error);
            random = new Random(settings.seed); profile = SokobanHtmlGenerationProfile.ForSize(settings.width, settings.height);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(external))
            {
                timeout.CancelAfter(settings.budgetSeconds * 1000); var token = timeout.Token;
                try
                {
                    token.ThrowIfCancellationRequested();
                    layout = new SokobanHtmlGenerationLayout(settings.width, settings.height, random, profile.PlacementAttempts);
                    var border = 2 * settings.width + 2 * settings.height - 4;
                    var minimum = (int)Math.Floor(settings.InteriorCells * profile.MinWallRatio) + border;
                    var maximum = (int)Math.Floor(settings.InteriorCells * profile.MaxWallRatio) + border;
                    var priority = (int)Math.Floor(settings.width * settings.height * profile.WallPriority);
                    for (var iteration = 0; iteration < settings.maxCandidates; iteration++)
                    {
                        token.ThrowIfCancellationRequested(); stats.attempts = iteration + 1; stats.mutationAttempts++;
                        Publish("增加墙与箱子/目标");
                        var walls = layout.Count(SokobanHtmlGenerationLayout.Wall);
                        if (walls < minimum)
                        {
                            for (var i = 0; i < Math.Max(1, minimum / 20); i++) AddWall();
                            if (random.NextDouble() < profile.BoxProbability) AddPair();
                        }
                        else if (walls >= maximum) AddPair();
                        else if (iteration < priority || random.NextDouble() < profile.WallProbability)
                        {
                            AddWall(); if (random.NextDouble() < profile.BoxProbability) AddPair();
                        }
                        else
                        {
                            // Source redraws the loop bound on each test, including the final test.
                            for (var i = 0; i < (random.NextDouble() < .3 ? 2 : 1); i++) AddPair();
                        }
                        Publish("求解候选"); token.ThrowIfCancellationRequested();
                        var construction = SokobanHtmlConstructionSolver.Run(layout, profile,
                            Math.Min(settings.candidateSolveMs, Remaining()), token, out var referencePushes);
                        token.ThrowIfCancellationRequested();
                        if (construction != 1)
                        {
                            if (construction == -1) stats.solveUnsolvable++; else stats.solveUnknown++;
                            layout.Load(); stats.rollbacks++; Publish("失败回退"); continue;
                        }
                        layout.Save(); stats.acceptedMutations++; stats.validCandidates++; stats.lastPushes = referencePushes;
                        var candidate = Qualify(referencePushes, token);
                        if (candidate != null)
                        {
                            stats.qualifiedCandidates++;
                            if (stats.firstMatchCandidate == 0) stats.firstMatchCandidate = stats.attempts;
                            if (SokobanHtmlGenerationQuality.Better(candidate.Quality, best?.Quality))
                            { best = candidate; stats.bestCandidate = stats.attempts; stats.bestQuality = candidate.Quality.score; }
                            Publish("已验证匹配关卡"); token.ThrowIfCancellationRequested();
                            if (iteration >= 30 && (SokobanHtmlGenerationQuality.High(candidate.Quality) ||
                                referencePushes >= 20 && iteration >= 40 && layout.Count(5) >= minimum && layout.Count(5) <= maximum &&
                                SokobanHtmlGenerationQuality.Acceptable(candidate.Quality)))
                            {
                                // Reference early-stop returns this current layout, not the tracked best.
                                best = candidate; stats.bestCandidate = stats.attempts; stats.bestQuality = candidate.Quality.score;
                                return Finish(SokobanGenerationStatus.Success, "已生成符合推箱次数与复杂度要求的关卡");
                            }
                        }
                        Publish("保存可解布局");
                    }
                    if (stats.validCandidates == 0 && Remaining() > 1)
                    {
                        Publish("验证仓库备用关卡");
                        layout = SokobanHtmlGenerationFallback.Create(settings.width, settings.height, random, Math.Min(1000, Remaining()));
                        var fallback = Qualify(-1, token);
                        if (fallback != null) { best = fallback; stats.bestCandidate = stats.attempts; stats.bestQuality = fallback.Quality.score; stats.qualifiedCandidates++; }
                    }
                    token.ThrowIfCancellationRequested();
                    return Finish(best != null ? SokobanGenerationStatus.Success : SokobanGenerationStatus.Exhausted,
                        best != null ? "已返回符合要求的最佳候选" : "已达到候选上限，未找到符合推箱次数与复杂度要求的关卡");
                }
                catch (OperationCanceledException)
                {
                    if (external.IsCancellationRequested) { best = null; return Finish(SokobanGenerationStatus.Cancelled, "已取消生成"); }
                    return Finish(best != null ? SokobanGenerationStatus.Success : SokobanGenerationStatus.TimedOut,
                        best != null ? "时间预算已用尽，返回已验证的最佳候选" : "时间预算已用尽，没有匹配关卡");
                }
                catch (Exception ex) { best = null; return Finish(SokobanGenerationStatus.Error, "生成失败：" + ex.Message); }
            }
        }
        private void AddWall()
        { if (layout.InteriorWalls < settings.MaximumWalls) layout.Place(SokobanHtmlGenerationLayout.Wall); }
        private void AddPair()
        {
            if (layout.Count(1) >= settings.MaximumBoxes || layout.Count(6) < 2) return;
            layout.Place(SokobanHtmlGenerationLayout.Box); layout.Place(SokobanHtmlGenerationLayout.Target);
        }
        private SokobanGenerationResult Qualify(int referencePushes, CancellationToken token)
        {
            var level = layout.Snapshot();
            if (!settings.MatchesStructure(level.Boxes.Length, layout.InteriorWalls) || level.Boxes.Length != level.Goals.Length)
            { stats.terrainRejected++; return null; }
            var key = SokobanGenerator.LayoutKey(level);
            if (excluded != null && excluded.Contains(key)) { stats.duplicatesRejected++; return null; }
            // Construction solver reproduces HTML decisions; A* certifies minimum pushes and a real walk/push replay.
            var solution = SokobanSolver.Solve(level, new SokobanSolveOptions { TimeoutMs = Math.Min(settings.candidateSolveMs, Remaining()),
                MaxExpandedNodes = profile.SolverIterations, MaxDiscoveredStates = profile.MemoryNodes }, token);
            token.ThrowIfCancellationRequested();
            if (!solution.IsSolved) { stats.solveUnknown++; return null; }
            stats.lastMoves = solution.Moves.Length; stats.lastPushes = solution.Pushes;
            if (solution.Pushes < settings.minPushes || solution.Pushes > settings.maxPushes) { stats.pushRejected++; return null; }
            var complexity = SokobanDifficultyEvaluator.Evaluate(level, solution, token, budgetMs: Math.Min(1500, Remaining()));
            token.ThrowIfCancellationRequested(); stats.lastDifficulty = complexity.difficulty;
            if (!settings.AnyDifficulty && complexity.difficulty != settings.difficulty) { stats.difficultyRejected++; return null; }
            return new SokobanGenerationResult { Level = level, Solution = solution, Complexity = complexity,
                Quality = SokobanHtmlGenerationQuality.Measure(level, referencePushes < 0 ? solution.Pushes : referencePushes) };
        }
        private int Remaining() => Math.Max(1, settings.budgetSeconds * 1000 - (int)clock.ElapsedMilliseconds);
        private SokobanGenerationResult Finish(SokobanGenerationStatus status, string message)
        {
            var result = status == SokobanGenerationStatus.Success ? best : new SokobanGenerationResult();
            result.Status = status; result.Message = message; result.Settings = settings; result.Statistics = stats;
            stats.elapsedMs = (int)clock.ElapsedMilliseconds; return result;
        }
        private void Publish(string phase)
        { stats.phase = phase; stats.elapsedMs = (int)clock.ElapsedMilliseconds; progress?.Invoke(stats.Copy()); }
    }
}
