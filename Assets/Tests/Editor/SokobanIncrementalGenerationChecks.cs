using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Kuluobishi.Sokoban;

// Historical class/file name keeps the project check entry stable.
public static class SokobanIncrementalGenerationChecks
{
    private static int checks;
    private static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static void Require(bool condition, string message)
    { checks++; if (!condition) throw new Exception("HTML generation: " + message); }
    /// <summary>与 Require 相同的校验，但不计入断言总数（供进度回调等高频路径使用）。</summary>
    private static void Guard(bool condition, string message)
    { if (!condition) throw new Exception("HTML generation: " + message); }
    private static string Key(SokobanSolverLevel level) => (string)typeof(SokobanGenerator)
        .GetMethod("LayoutKey", Hidden).Invoke(null, new object[] { level });
    public static string Run()
    {
        checks = 0;
        var settings = new SokobanGenerationSettings { width = 6, height = 6, boxCount = 1, wallPercent = 0,
            minPushes = 1, maxPushes = 15, difficulty = 1, seed = 42, budgetSeconds = 10, maxCandidates = 64 };
        var lastAttempt = 0;
        var result = SokobanGenerator.Generate(settings, progress: p => {
            Guard(p.attempts >= lastAttempt && p.attempts <= 64, "monotonic bounded attempts"); lastAttempt = p.attempts;
        });
        Require(result.Status == SokobanGenerationStatus.Success && result.Solution.Pushes == 2 && result.Solution.Moves.Length == 4,
            "push interval excludes walking");
        var stats = result.Statistics;
        Require(stats.attempts == 64 && stats.rollbacks > 0 && stats.acceptedMutations > 0, "source iteration limit and save/rollback");
        Require(stats.bestCandidate >= stats.firstMatchCandidate && stats.bestCandidate <= stats.attempts, "selected candidate provenance");
        Require(result.Quality.evaluatorVersion == "html-quality-3.0" && result.Quality.referencePushes == 2 &&
            Math.Abs(result.Quality.score - 44.733333f) < .0001f && result.Quality.pathDiversity == 0 && result.Quality.solutionEfficiency == .3f,
            "reference evaluator preserves actual StateNode defaults");
        var repeated = SokobanGenerator.Generate(settings);
        Require(Key(result.Level) == Key(repeated.Level) && result.Solution.Moves == repeated.Solution.Moves &&
            result.Quality.score == repeated.Quality.score, "seed reproducibility");
        var excluded = new HashSet<string> { Key(result.Level) };
        var blocked = SokobanGenerator.Generate(settings, excludedLayouts: excluded);
        Require(blocked.Status == SokobanGenerationStatus.Exhausted && blocked.Level == null && blocked.Statistics.duplicatesRejected > 0,
            "exclusion preserves the source construction trajectory");
        Require(excluded.Count == 1, "caller values preserved");
        using (var cancel = new CancellationTokenSource())
        {
            var cancelled = SokobanGenerator.Generate(settings, cancel.Token, p => { if (p.qualifiedCandidates > 0) cancel.Cancel(); });
            Require(cancelled.Status == SokobanGenerationStatus.Cancelled && cancelled.Level == null, "cancel discards completed match");
        }
        var budget = settings.Copy(); budget.budgetSeconds = 1; var delayed = false;
        var timed = SokobanGenerator.Generate(budget, progress: p => {
            if (p.qualifiedCandidates > 0 && !delayed) { delayed = true; Thread.Sleep(1100); }
        });
        Require(timed.Status == SokobanGenerationStatus.Success && timed.Statistics.elapsedMs >= 1000, "timeout returns verified match");
        var impossible = settings.Copy(); impossible.minPushes = impossible.maxPushes = 1000;
        Require(SokobanGenerator.Generate(impossible).Status == SokobanGenerationStatus.Exhausted, "strict interval on exhaustion");
        var assembly = typeof(SokobanGenerator).Assembly;
        Require(assembly.GetType("Kuluobishi.Sokoban.SokobanIncrementalSearch") == null &&
            assembly.GetType("Kuluobishi.Sokoban.SokobanGenerationMutations") == null, "custom search removed");
        var layoutType = assembly.GetType("Kuluobishi.Sokoban.SokobanHtmlGenerationLayout");
        var layout = Activator.CreateInstance(layoutType, Hidden, null, new object[] { 8, 8, new Random(42), 1400 }, null);
        var field = layoutType.GetField("Tiles", Hidden); var original = (int[])((int[])field.GetValue(layout)).Clone();
        var floor = Array.IndexOf(original, 6); ((int[])field.GetValue(layout))[floor] = 5;
        layoutType.GetMethod("Load", Hidden).Invoke(layout, null);
        Require(string.Join(",", (int[])field.GetValue(layout)) == string.Join(",", original), "full saved snapshot restored");
        var snapshot = (SokobanSolverLevel)layoutType.GetMethod("Snapshot", Hidden).Invoke(layout, null);
        snapshot.Walls[floor] = true;
        Require(((int[])field.GetValue(layout))[floor] == 6, "output owns buffers");
        var profileType = assembly.GetType("Kuluobishi.Sokoban.SokobanHtmlGenerationProfile");
        foreach (var size in new[] { 6, 7, 8, 9, 10, 11 })
        {
            var p = profileType.GetMethod("ForSize", Hidden).Invoke(null, new object[] { size, size });
            var maximum = (double)profileType.GetField("MaxWallRatio", Hidden).GetValue(p);
            Require(Math.Abs(maximum - (1.2 + (size - 6) * .2)) < .000001, "source >100% threshold retained");
        }
        return "PASS: " + checks + " HTML checks (source quality, push units, rollback, seed, exclusions, cancellation/deadline, profiles and retired search).";
    }
}
