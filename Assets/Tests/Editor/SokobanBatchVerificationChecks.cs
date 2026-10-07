using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Kuluobishi.Sokoban;
using UnityEngine;

public static class SokobanBatchVerificationChecks
{
    private static int checks;
    private static void Require(bool value, string message)
    { if (!value) throw new Exception("Batch verification: " + message); checks++; }
    private static SokobanVerificationInput Input(SokobanJsonLevel data)
    {
        SokobanSolverLevel snapshot; string error;
        return SokobanSolverAdapter.TryCreateSnapshot(data, out snapshot, out error)
            ? new SokobanVerificationInput { Level = snapshot } : new SokobanVerificationInput { Error = error };
    }
    public static string Run()
    {
        checks = 0;
        var solvable = SokobanSolverChecks.Fixture("#######", "#@ $ .#", "#     #", "#######");
        var unsolvable = SokobanSolverChecks.Fixture("#####", "#$@.#", "#   #", "#####");
        var zero = SokobanSolverChecks.Fixture("#####", "#@* #", "#####");
        var invalid = SokobanSolverChecks.Fixture("#####", "#@  #", "#####");
        var json = JsonUtility.ToJson(solvable);
        Require(solvable.verifiedMoves == -1, "new level starts unverified");
        var legacy = SokobanLevelRepository.Parse(json.Replace("\"verifiedMoves\":-1,", ""));
        Require(legacy.verifiedMoves == -1, "legacy missing field uses -1 rather than zero");
        Require(SokobanLevelRepository.Parse("null") == null, "null input compatible");
        var inputs = new[] { Input(solvable), Input(unsolvable), Input(invalid), Input(zero) };
        var callbacks = new List<int>(); var previous = 0f;
        var batch = SokobanBatchVerifier.Run(inputs, new SokobanSolveOptions(), progress: p =>
        { Require(p.Fraction >= previous && p.Fraction <= 1f, "bounded monotonic progress"); previous = p.Fraction; }, completed: (i, _) => callbacks.Add(i));
        Require(batch.Results.Count == 4 && callbacks.SequenceEqual(new[] { 0, 1, 2, 3 }) && !batch.Cancelled && previous == 1f, "all selected entries processed");
        Require(batch.Results[0].IsSolved && batch.Results[1].Status == SokobanSolveStatus.Unsolvable &&
            batch.Results[2].Status == SokobanSolveStatus.Invalid && batch.Results[3].IsSolved && batch.Results[3].Moves.Length == 0,
            "mixed solvable, unsolvable, invalid and zero-step results");
        var replay = new SokobanSolutionPlayback(solvable, batch.Results[0]); while (replay.Next()) { }
        Require(replay.State.IsWon, "complete solved result playback");
        Require(JsonUtility.ToJson(solvable) == json, "worker preserves source document");
        var revision = SokobanVerificationMetadata.Revision(solvable);
        SokobanVerificationMetadata.Apply(solvable, batch.Results[0]);
        Require(solvable.verifiedMoves == batch.Results[0].Moves.Length && solvable.solution.moveCount == solvable.verifiedMoves, "trusted movement count populated");
        Require(SokobanVerificationMetadata.Revision(solvable) == revision, "verification metadata does not change content revision");
        Require(SokobanLevelRepository.Parse(JsonUtility.ToJson(solvable)).verifiedMoves == solvable.verifiedMoves, "verified steps JSON roundtrip");
        SokobanVerificationMetadata.Apply(zero, batch.Results[3]); Require(zero.verifiedMoves == 0, "zero-step victory stays distinct from unverified");
        foreach (var status in new[] { SokobanSolveStatus.Unsolvable, SokobanSolveStatus.Invalid, SokobanSolveStatus.Timeout,
            SokobanSolveStatus.LimitReached, SokobanSolveStatus.Cancelled, SokobanSolveStatus.Error })
        {
            SokobanVerificationMetadata.Apply(solvable, new SokobanSolveResult { Status = status });
            Require(solvable.verifiedMoves == -1 && solvable.solution.status == status.ToString(), "non-solved status remains explicit and untrusted");
        }
        SokobanVerificationMetadata.Apply(solvable, batch.Results[0]);
        solvable.name = "changed";
        Require(SokobanVerificationMetadata.Revision(solvable) != revision, "name edit changes revision");
        SokobanVerificationMetadata.Invalidate(solvable);
        Require(solvable.verifiedMoves == -1 && solvable.solution.status == "Unknown" && solvable.metadata.parMoves == 0, "editing invalidates all costs");
        using (var source = new CancellationTokenSource())
        {
            source.Cancel(); var cancelled = SokobanBatchVerifier.Run(inputs, new SokobanSolveOptions(), source.Token);
            Require(cancelled.Cancelled && cancelled.Results.Count == 0, "cancel before first item");
        }
        using (var source = new CancellationTokenSource())
        {
            var partial = SokobanBatchVerifier.Run(inputs, new SokobanSolveOptions(), source.Token, completed: (i, _) => source.Cancel());
            Require(partial.Cancelled && partial.Results.Count == 1 && partial.Results[0].IsSolved, "cancel preserves completed result");
        }
        using (var source = new CancellationTokenSource())
        {
            var partial = SokobanBatchVerifier.Run(inputs, new SokobanSolveOptions(), source.Token, p =>
            { if (p.CurrentIndex == 1 && p.Completed == 1) source.Cancel(); });
            Require(partial.Cancelled && partial.Results.Count == 2 && partial.Results[1].Status == SokobanSolveStatus.Cancelled,
                "in-flight cancellation publishes cancelled row");
        }
        var limited = SokobanBatchVerifier.Run(new[] { Input(SokobanLevelRepository.LoadJson("L002")) },
            new SokobanSolveOptions { MaxExpandedNodes = 1 });
        Require(limited.Results[0].Status == SokobanSolveStatus.LimitReached, "node limit stays unknown, not unsolvable");
        var rejected = false;
        try { SokobanBatchVerifier.Run(Array.Empty<SokobanVerificationInput>(), new SokobanSolveOptions()); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "empty selection rejected");

        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Codex/verification-data-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var descriptor = new SokobanLevelDescriptor { LevelId = solvable.levelId, FilePath = Path.Combine(root, "level.json") };
        var original = JsonUtility.ToJson(solvable, true); File.WriteAllText(descriptor.FilePath, original);
        SokobanJsonLevel updated; string message;
        Require(SokobanVerificationMetadata.TryPersist(descriptor, original, batch.Results[0], out updated, out message), "automatic verification save succeeds");
        Require(SokobanLevelRepository.LoadJson(descriptor).verifiedMoves == batch.Results[0].Moves.Length && updated.name == "changed", "save preserves source content and steps");
        var historical = new Kuluobishi.Sokoban.Editor.SokobanBatchVerificationService.Entry
            { Saved = true, Updated = updated, SourceRevision = SokobanVerificationMetadata.Revision(updated) };
        var current = SokobanLevelRepository.LoadJson(descriptor);
        Require(historical.MatchesSavedResult(current), "fresh published result matches the saved file");
        SokobanVerificationMetadata.Invalidate(current);
        Require(!historical.MatchesSavedResult(current), "invalidation with identical content cannot reuse historical steps");
        current.name = "edited after batch completion";
        Require(!historical.MatchesSavedResult(current), "edited saved content cannot reuse historical result");
        SokobanVerificationMetadata.Apply(current, batch.Results[0]);
        Require(!historical.MatchesSavedResult(current), "revalidated changed content remains distinct from historical snapshot");
        Require(Directory.GetFiles(root, "*.tmp").Length == 0, "atomic publish cleans temporary file");
        var latest = File.ReadAllText(descriptor.FilePath);
        Require(!SokobanVerificationMetadata.TryPersist(descriptor, original, batch.Results[0], out updated, out message) &&
            File.ReadAllText(descriptor.FilePath) == latest && message.Contains("已改动"), "stale result cannot overwrite newer JSON");
        var edited = SokobanLevelRepository.Parse(latest); edited.name = "user edit during solve"; SokobanVerificationMetadata.Invalidate(edited);
        var editText = JsonUtility.ToJson(edited); File.WriteAllText(descriptor.FilePath, editText);
        Require(!SokobanVerificationMetadata.TryPersist(descriptor, latest, batch.Results[0], out updated, out message) &&
            File.ReadAllText(descriptor.FilePath) == editText, "unsaved-style edit invalidation and conflict protection");
        File.Move(descriptor.FilePath, descriptor.FilePath + ".retained");
        Require(!SokobanVerificationMetadata.TryPersist(descriptor, editText, batch.Results[0], out updated, out message) &&
            !File.Exists(descriptor.FilePath) && message.Contains("删除"), "deleted level is never resurrected");
        return "PASS: " + checks + " verification assertions (mixed results, progress, cancellation, costs, legacy JSON, invalidation, atomic save and conflict protection).";
    }
}
