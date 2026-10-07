using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Threading;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;

public static class SokobanGenerationChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Generation regression: " + message); }
    private static SokobanJsonLevel Json(SokobanGenerationResult result)
    {
        return (SokobanJsonLevel)typeof(SokobanEditorSceneController).GetMethod("CreateGeneratedJson",
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { result });
    }
    private static string Key(SokobanSolverLevel level)
    { return level.Width + ":" + level.Height + ":" + string.Join(",", level.Walls) + ":" + level.Player + ":" + string.Join(",", level.Boxes) + ":" + string.Join(",", level.Goals); }
    private static void CheckCandidate(SokobanGenerationResult result, SokobanGenerationSettings settings)
    {
        Require(result.Status == SokobanGenerationStatus.Success, result.Message);
        var json = Json(result); var before = JsonUtility.ToJson(json);
        Require(SokobanValidation.Validate(json).Count == 0, "structural validation");
        Require(json.size.width == settings.width && json.size.height == settings.height, "dimensions");
        Require(json.verifiedMoves == result.Solution.Moves.Length, "generated result carries trusted verified moves");
        Require(json.boxes.Length == settings.boxCount && json.goals.Length == settings.boxCount, "box and goal counts");
        foreach (var box in result.Level.Boxes)
            Require(Array.IndexOf(result.Level.Goals, box) < 0, "no box starts on any goal");
        Require(result.Solution.Pushes >= settings.minPushes && result.Solution.Pushes <= settings.maxPushes, "exact push filtering");
        Require(result.Complexity.valid && result.Complexity.difficulty == settings.difficulty, "difficulty filtering");
        var wallCount = 0;
        for (var y = 0; y < settings.height; y++)
        for (var x = 0; x < settings.width; x++)
        {
            if (x == 0 || y == 0 || x == settings.width - 1 || y == settings.height - 1)
                Require(json.terrain[y][x] == '#', "perimeter walls");
            else if (json.terrain[y][x] == '#') wallCount++;
        }
        Require(wallCount == settings.WallCount, "exact rounded interior wall count");
        var replay = new SokobanSolutionPlayback(json, result.Solution);
        while (replay.Next()) { }
        Require(replay.State.IsWon, "full solution playback victory");
        Require(replay.State.MoveCount == result.Solution.Moves.Length && replay.State.PushCount == result.Solution.Pushes, "real replay costs");
        while (replay.Previous()) { }
        Require(replay.Index == 0 && replay.State.Boxes.SetEquals(replay.Level.BoxesStart), "rewind generated solution");
        var witness = new SokobanSolutionPlayback(json, new SokobanSolveResult { Status = SokobanSolveStatus.Solved,
            Moves = result.Complexity.witnessMoves, Pushes = CountPushes(result.Complexity.witnessMoves) });
        while (witness.Next()) { }
        Require(witness.State.IsWon, "complexity witness is a complete valid solution");
        Require(before == JsonUtility.ToJson(json), "playback preserves source");
        var restored = SokobanLevelRepository.Parse(before);
        Require(restored.generation.parameters.minPushes == settings.minPushes && restored.generation.seed == settings.seed,
            "generation provenance JSON roundtrip");
        Require(restored.generation.complexity.valid && restored.solution.moves == result.Solution.Moves, "report and solution roundtrip");
        Require(restored.generation.generatorVersion == SokobanGenerationResult.Version &&
            !before.Contains("referenceRepository") && !before.Contains("referenceCommit") &&
            !before.Contains("searchAttempts") && !before.Contains("\"candidate\""), "generation keeps version without provenance or diagnostics");
        Require(restored.generation.quality != null && restored.generation.quality.score == result.Quality.score,
            "layout quality roundtrip");
        SokobanVerificationMetadata.Invalidate(restored);
        Require(restored.verifiedMoves == -1 && restored.generation.quality == null && restored.generation.complexity == null,
            "editing invalidates both layout quality and difficulty");
    }
    private static int CountPushes(string moves)
    { var count = 0; foreach (var c in moves) if (char.IsUpper(c)) count++; return count; }

    public static string Run()
    {
        var output = new StringBuilder();
        var results = new List<SokobanGenerationResult>();
        for (var difficulty = 1; difficulty <= 3; difficulty++)
        {
            var settings = new SokobanGenerationSettings { boxCount = 2, wallPercent = 20, seed = difficulty == 1 ? 20261005 : difficulty == 2 ? 1 : 3,
                difficulty = difficulty, budgetSeconds = 15, maxCandidates = 140 };
            var result = SokobanGenerator.Generate(settings); CheckCandidate(result, settings); results.Add(result);
            var repeated = SokobanGenerator.Generate(settings); CheckCandidate(repeated, settings);
            Require(Key(result.Level) == Key(repeated.Level) && result.Solution.Moves == repeated.Solution.Moves &&
                result.Complexity.score == repeated.Complexity.score, "fixed seed reproducibility");
            output.AppendLine($"{SokobanDifficultyEvaluator.Name(difficulty)}: {result.Solution.Moves.Length} moves, {result.Solution.Pushes} pushes, score {result.Complexity.score:0.0}, candidate {result.Statistics.attempts}");
        }
        var narrow = new SokobanGenerationSettings { width = 6, height = 6, boxCount = 1, wallPercent = 0,
            minPushes = 2, maxPushes = 2, difficulty = 1, seed = 42, maxCandidates = 140, budgetSeconds = 10 };
        var exact = SokobanGenerator.Generate(narrow); CheckCandidate(exact, narrow);
        Require(exact.Solution.Moves.Length > 2, "walking is excluded from the exact push interval");
        output.AppendLine("Exact 2-push interval accepts a 4-move solution.");
        var invalid = new SokobanGenerationSettings { minPushes = 100, maxPushes = 20 };
        Require(SokobanGenerator.Generate(invalid).Status == SokobanGenerationStatus.Invalid, "invalid range rejected");
        invalid = new SokobanGenerationSettings { width = 4 };
        Require(SokobanGenerator.Generate(invalid).Status == SokobanGenerationStatus.Invalid, "invalid size rejected");
        invalid = new SokobanGenerationSettings { width = 5, height = 5, boxCount = 8 };
        Require(SokobanGenerator.Generate(invalid).Status == SokobanGenerationStatus.Invalid, "insufficient floor rejected");
        using (var cancel = new CancellationTokenSource())
        {
            cancel.Cancel();
            Require(SokobanGenerator.Generate(new SokobanGenerationSettings(), cancel.Token).Status == SokobanGenerationStatus.Cancelled,
                "cancellation before generation");
        }
        using (var cancel = new CancellationTokenSource())
        {
            var result = SokobanGenerator.Generate(new SokobanGenerationSettings { seed = 20261005 }, cancel.Token,
                progress => { if (progress.phase == "求解候选") cancel.Cancel(); });
            Require(result.Status == SokobanGenerationStatus.Cancelled && result.Level == null, "in-flight cancellation");
        }
        var exhausted = SokobanGenerator.Generate(new SokobanGenerationSettings { seed = 20261005, minPushes = 1000, maxPushes = 1000, maxCandidates = 1 });
        Require(exhausted.Status == SokobanGenerationStatus.Exhausted && exhausted.Level == null && exhausted.Statistics.attempts == 1, "candidate limit without relaxing constraints");
        var clock = Stopwatch.StartNew();
        var timed = SokobanGenerator.Generate(new SokobanGenerationSettings { width = 8, height = 8, boxCount = 8, wallPercent = 20,
            minPushes = 1000, maxPushes = 1000, maxCandidates = 2000, seed = 42, budgetSeconds = 1, candidateSolveMs = 30000 });
        Require(timed.Status == SokobanGenerationStatus.TimedOut && timed.Level == null && clock.ElapsedMilliseconds < 3000, "total budget enforced");

        var shortLevel = SokobanSolverChecks.Fixture("#######", "#@ $ .#", "#     #", "#######");
        Require(SokobanSolverAdapter.TryCreateSnapshot(shortLevel, out var snapshot, out var error), error);
        var solution = SokobanSolver.Solve(snapshot);
        var shortReport = SokobanDifficultyEvaluator.Evaluate(snapshot, solution, maxProbes: 0);
        var extraWalking = new SokobanSolveResult { Status = SokobanSolveStatus.Solved, Pushes = solution.Pushes,
            Moves = "dududududududududu" + solution.Moves };
        var walkingReport = SokobanDifficultyEvaluator.Evaluate(snapshot, extraWalking, maxProbes: 0);
        Require(shortReport.score == walkingReport.score && shortReport.difficulty == 1, "walking length does not increase complexity");
        var corridor = SokobanSolverChecks.Fixture(new string('#', 40), "#@$" + new string(' ', 35) + ".#", new string('#', 40));
        Require(SokobanSolverAdapter.TryCreateSnapshot(corridor, out snapshot, out error), error);
        solution = SokobanSolver.Solve(snapshot);
        var corridorReport = SokobanDifficultyEvaluator.Evaluate(snapshot, solution);
        Require(solution.Pushes == 36 && corridorReport.difficulty == 1 && corridorReport.score < results[1].Complexity.score,
            "long forced corridor stays simpler than shorter coordinated puzzle");
        var limitedReport = SokobanDifficultyEvaluator.Evaluate(results[1].Level, results[1].Solution, maxProbeNodes: 1);
        Require(limitedReport.unknownBranches > 0, "limited branches remain explicitly unknown");
        var lowered = false;
        for (var seed = 1; seed <= 40 && !lowered; seed++)
        {
            var generated = SokobanGenerator.Generate(new SokobanGenerationSettings { boxCount = 2, wallPercent = 20, seed = seed, difficulty = 2,
                minPushes = 1, maxPushes = 80, budgetSeconds = 10, maxCandidates = 140 });
            if (generated.Status != SokobanGenerationStatus.Success) continue;
            var referenceOnly = SokobanDifficultyEvaluator.Evaluate(generated.Level, generated.Solution, maxProbes: 0);
            var report = generated.Complexity;
            if (report.witnessMoves != generated.Solution.Moves &&
                report.dependencyScore + report.planningScore + report.spaceScore < referenceOnly.dependencyScore + referenceOnly.planningScore + referenceOnly.spaceScore)
            { CheckCandidate(generated, generated.Settings); lowered = true; output.AppendLine("Simpler valid alternative found at seed " + seed + "."); }
        }
        Require(lowered, "alternative solution can lower estimated complexity");
        output.AppendLine("PASS: structural invariants, deterministic generation, replay/witness, JSON, exact interval, invalid inputs, cancellation, candidate/time limits, complexity independent of length.");
        return output.ToString();
    }
}
