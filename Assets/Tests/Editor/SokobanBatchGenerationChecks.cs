using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;

public static class SokobanBatchGenerationChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Batch generation: " + message); checks++; }
    /// <summary>与 Require 相同的校验，但不计入断言总数（供进度回调等高频路径使用）。</summary>
    private static void Guard(bool condition, string message)
    { if (!condition) throw new Exception("Batch generation: " + message); }

    private static SokobanJsonLevel Json(SokobanGenerationResult result)
    {
        return (SokobanJsonLevel)typeof(SokobanEditorSceneController).GetMethod("CreateGeneratedJson",
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { result });
    }

    public static string Run()
    {
        checks = 0;
        var settings = new SokobanGenerationSettings { width = 6, height = 6, boxCount = 1, wallPercent = 0,
            minPushes = 1, maxPushes = 15, difficulty = 1, seed = 42, budgetSeconds = 10, maxCandidates = 140 };
        var before = JsonUtility.ToJson(settings); var keys = new HashSet<string>(); var ids = new HashSet<string>();
        var callbacks = 0; var lastFraction = 0f;
        var result = SokobanBatchGenerator.Generate(settings, 4, progress: p =>
        {
            Guard(p.Fraction >= lastFraction && p.Fraction <= 1f, "monotonic bounded progress");
            lastFraction = p.Fraction;
        }, itemCompleted: _ => callbacks++);
        Require(result.Items.Count == 4 && callbacks == 4 && !result.Cancelled && lastFraction == 1f, "requested count and terminal progress");
        for (var i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i]; var json = Json(item);
            Require(item.Status == SokobanGenerationStatus.Success, "each slot succeeded");
            Require(json.verifiedMoves == item.Solution.Moves.Length, "generated verified moves");
            Require(item.Settings.seed == unchecked(42 + i * 104729), "independent reproducible seed");
            Require(SokobanValidation.Validate(json).Count == 0, "valid generated JSON");
            Require(item.Solution.Pushes >= 1 && item.Solution.Pushes <= 15 && item.Complexity.difficulty == 1, "push and complexity constraints");
            foreach (var box in item.Level.Boxes) Require(Array.IndexOf(item.Level.Goals, box) < 0, "no initial box on goal");
            var copy = (int[])item.Level.Boxes.Clone(); Array.Sort(copy);
            var goals = (int[])item.Level.Goals.Clone(); Array.Sort(goals);
            Require(keys.Add(item.Level.Player + ":" + string.Join(",", copy) + ":" + string.Join(",", goals)), "batch results unique");
            Require(ids.Add(json.levelId), "unique storage IDs");
            var replay = new SokobanSolutionPlayback(json, item.Solution);
            while (replay.Next()) { }
            Require(replay.State.IsWon, "complete solution reaches victory");
            while (replay.Previous()) { }
            Require(replay.Index == 0 && replay.State.Boxes.SetEquals(replay.Level.BoxesStart), "solution rewind");
        }
        Require(before == JsonUtility.ToJson(settings), "caller parameters unchanged");
        var repeated = SokobanBatchGenerator.Generate(settings, 4);
        for (var i = 0; i < 4; i++)
            Require(result.Items[i].Solution.Moves == repeated.Items[i].Solution.Moves &&
                result.Items[i].Level.Player == repeated.Items[i].Level.Player &&
                result.Items[i].Level.Boxes.SequenceEqual(repeated.Items[i].Level.Boxes) &&
                result.Items[i].Level.Goals.SequenceEqual(repeated.Items[i].Level.Goals) &&
                result.Items[i].Level.Walls.SequenceEqual(repeated.Items[i].Level.Walls), "repeatable batch seeds");

        using (var source = new CancellationTokenSource())
        {
            source.Cancel();
            var cancelled = SokobanBatchGenerator.Generate(settings, 4, source.Token);
            Require(cancelled.Cancelled && cancelled.Items.Count == 0, "cancel before first slot");
        }
        using (var source = new CancellationTokenSource())
        {
            var partial = SokobanBatchGenerator.Generate(settings, 4, source.Token, itemCompleted: _ => source.Cancel());
            Require(partial.Cancelled && partial.Items.Count == 1 && partial.Items[0].Status == SokobanGenerationStatus.Success, "cancel retains completed results");
        }
        using (var source = new CancellationTokenSource())
        {
            var partial = SokobanBatchGenerator.Generate(settings, 4, source.Token, p =>
            { if (p.Completed == 1 && p.Current != null) source.Cancel(); });
            Require(partial.Cancelled && partial.Items.Count == 1, "cancel inside next generation");
        }
        var impossible = settings.Copy(); impossible.minPushes = impossible.maxPushes = 1000; impossible.maxCandidates = 1;
        var failed = SokobanBatchGenerator.Generate(impossible, 2);
        Require(failed.Items.Count == 2 && failed.Items.TrueForAll(i => i.Status == SokobanGenerationStatus.Exhausted) && !failed.Cancelled,
            "failure continues without relaxing requirements");
        Require(SokobanBatchGenerator.Validate(settings, 0) != "" && SokobanBatchGenerator.Validate(settings, 101) != "", "quantity bounds");

        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Codex/batch-data-" + Guid.NewGuid().ToString("N")));
        var descriptor = new SokobanLevelDescriptor { Folder = "test", Source = "Generated", FilePath = Path.Combine(root, "level.json") };
        var level = Json(result.Items[0]);
        SokobanLevelRepository.SaveJson(level, descriptor, false, false);
        var bytes = File.ReadAllText(descriptor.FilePath);
        var blocked = false;
        try { level.name = "must not overwrite"; SokobanLevelRepository.SaveJson(level, descriptor, false, false); }
        catch (IOException) { blocked = true; }
        Require(blocked && File.ReadAllText(descriptor.FilePath) == bytes, "new-only save protects existing files");
        Require(SokobanLevelRepository.Parse(bytes).solution.moves == result.Items[0].Solution.Moves, "saved solution/provenance roundtrip");
        return "PASS: " + checks + " batch checks (count, filtering, replay, deduplication, seeds, progress, cancellation, failures, non-overwriting save).";
    }
}
