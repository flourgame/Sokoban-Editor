using System;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using UnityEngine;

public static class SokobanProductChecks
{
    private static int checks;
    private static void Require(bool value, string message) { if (!value) throw new Exception("Product regression: " + message); checks++; }
    public static string Run()
    {
        checks = 0;
        var source = SokobanSolverChecks.Fixture("#######", "#@ $ .#", "#     #", "#######");
        var level = new SokobanLevelRuntime(source); var state = level.CreateInitialState();
        Require(!SokobanSimulation.TryMove(level, state, SokobanDirection.Up).Accepted && state.MoveCount == 0, "wall blocks without counting a move");
        var initial = state.Clone();
        Require(SokobanSimulation.TryMove(level, state, SokobanDirection.Right).Accepted && state.MoveCount == 1 && state.PushCount == 0, "ordinary movement");
        Require(SokobanSimulation.TryMove(level, state, SokobanDirection.Right).Pushed && state.PushCount == 1 && state.Boxes.Contains(new SokobanGridPoint(4, 1)), "box pushing");
        Require(SokobanSimulation.TryMove(level, state, SokobanDirection.Right).Won && state.IsWon && state.MoveCount == 3, "all goals produce victory");
        Require(!initial.IsWon && initial.MoveCount == 0 && initial.Player == level.PlayerStart && initial.Boxes.SetEquals(level.BoxesStart), "undo snapshot remains independent");
        var blockedData = SokobanSolverChecks.Fixture("#######", "#@$$..#", "#######");
        var blocked = new SokobanLevelRuntime(blockedData); var blockedState = blocked.CreateInitialState();
        Require(!SokobanSimulation.TryMove(blocked, blockedState, SokobanDirection.Right).Accepted && blockedState.PushCount == 0, "cannot push a chain of boxes");
        var zero = SokobanSolverChecks.Fixture("#####", "#@* #", "#####"); var zeroState = new SokobanLevelRuntime(zero).CreateInitialState();
        zeroState.RefreshWin(new SokobanLevelRuntime(zero)); Require(zeroState.IsWon && zeroState.MoveCount == 0, "zero-step victory");
        Require(SokobanLevelRepository.BuildEmptyTerrain(int.MaxValue, int.MaxValue).Length == 40, "malformed dimensions cannot allocate unbounded terrain");
        var tab = new Kuluobishi.Sokoban.Editor.SokobanEditorTab();
        for (var i = 0; i < 105; i++) tab.AddUndo(i.ToString(), 100);
        Require(tab.UndoStack.Count == 100 && tab.UndoStack.Pop() == "104" && tab.UndoStack.Last() == "5", "bounded undo retains newest rather than discarding the latest edits");

        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Codex/product-checks", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "progress.json");
        var progress = new SokobanProgressStore(path);
        Require(progress.Find(source) == null, "fresh progress is incomplete");
        Require(progress.RecordWin(source, 10, 4), "normal win persists");
        Require(new SokobanProgressStore(path).Find(source).bestMoves == 10, "progress survives reload");
        Require(progress.RecordWin(source, 15, 5) && progress.Find(source).bestMoves == 10, "worse replay does not replace best score");
        Require(progress.RecordWin(source, 8, 3) && progress.Find(source).bestMoves == 8, "better score replaces best");
        var before = File.ReadAllText(path);
        Require(!progress.RecordWin(source, 0, 0, true) && !progress.RecordWin(source, 0, 0, false, true) && File.ReadAllText(path) == before, "GM and editor preview cannot pollute progress");
        source.name = "renamed"; Require(progress.Find(source).bestMoves == 8, "name-only change preserves score");
        source.player = new SokobanJsonPoint(2, 2); Require(progress.Find(source) == null, "changed layout cannot inherit score");
        var denied = new SokobanProgressStore(root); Require(!denied.RecordWin(source, 7, 3) && denied.LastError != null, "save failure is explicit and controlled");
        File.WriteAllText(Path.Combine(root, "corrupt.json"), "{not json");
        var corrupt = new SokobanProgressStore(Path.Combine(root, "corrupt.json"));
        Require(corrupt.Find(source) == null && corrupt.LastError != null && Directory.GetFiles(root, "corrupt.json.corrupt-*").Length == 1, "corrupt progress recovers with backup");
        Require(corrupt.RecordWin(source, 9, 4) && new SokobanProgressStore(Path.Combine(root, "corrupt.json")).Find(source).bestMoves == 9, "recovered progress can be saved");
        var bundled = new SokobanLevelDescriptor { LevelId = source.levelId, FilePath = Path.Combine(root, "bundle", "level.json"), BundledJson = JsonUtility.ToJson(source), Source = "Generated" };
        Require(SokobanLevelRepository.LoadJson(bundled).name == "renamed", "packaged JSON fallback works without a writable file");
        SokobanLevelRepository.SaveJson(source, bundled, false);
        var newSource = SokobanLevelRepository.Parse(bundled.BundledJson); newSource.name = "local override"; SokobanLevelRepository.SaveJson(newSource, bundled, false);
        Require(SokobanLevelRepository.LoadJson(bundled).name == "local override", "local saved version overrides packaged content");
        var library = new SokobanLibraryStore(root, Path.Combine(root, "backups"));
        var readOnly = new SokobanLevelDescriptor { LevelId = "BundledOnly", FilePath = Path.Combine(root, "readonly", "level.json"), Source = "Generated", BundledJson = JsonUtility.ToJson(new SokobanJsonLevel { levelId = "BundledOnly" }) };
        library.Delete(new[] { readOnly }, new[] { readOnly.LevelId });
        Require(library.Apply(new[] { readOnly }).Count == 0 && !File.Exists(readOnly.FilePath), "packaged-only deletion persists a tombstone without modifying the bundle");
        foreach (var descriptor in SokobanLevelRepository.ListAll().Where(d => d.Source == "BuiltIn"))
        {
            var data = SokobanLevelRepository.LoadJson(descriptor);
            Require(SokobanValidation.Validate(data).Count == 0, descriptor.LevelId + " campaign structure");
            SokobanSolverLevel snapshot; string error;
            Require(SokobanSolverAdapter.TryCreateSnapshot(data, out snapshot, out error) && SokobanSolver.Solve(snapshot, new SokobanSolveOptions { TimeoutMs = 5000 }).IsSolved, descriptor.LevelId + " campaign is solvable");
        }
        return "PASS: " + checks + " product assertions (rules, progress, GM/preview isolation, recovery, packaged content and campaign).";
    }
}
