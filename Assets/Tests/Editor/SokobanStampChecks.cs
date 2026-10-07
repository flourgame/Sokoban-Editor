using System;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;

public static class SokobanStampChecks
{
    private static int checks;
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception("Stamp regression: " + reason); checks++; }
    public static string Run()
    {
        checks = 0;
        var stamp = new SokobanStamp { stampId = "Stamp_fixture", name = "分层与透明", width = 3, height = 2,
            cells = new[] { new SokobanStampCell { x = 0, y = 0, goal = true, player = true },
                new SokobanStampCell { x = 2, y = 0, box = true, goal = true }, new SokobanStampCell { x = 0, y = 1, wall = true },
                new SokobanStampCell { x = 2, y = 1 } } };
        Check(SokobanStamp.Validate(stamp).Count == 0, "partial layouts need neither equal box/goal counts nor enclosing walls");
        var parsed = SokobanStamp.Parse(JsonUtility.ToJson(stamp));
        Check(parsed.cells.Length == 4 && parsed.width == 3 && parsed.cells[0].player && parsed.cells[0].goal, "JSON preserves layering and sparse cells");
        Check(!JsonUtility.ToJson(parsed).Contains("levelId") && !JsonUtility.ToJson(parsed).Contains("verifiedMoves"), "stamp uses its own data contract");
        var brush = parsed.AsBrush();
        Check(brush.Count == 4 && !brush.Any(c => c.Dx == 1), "unpainted coordinates remain transparent rather than clearing floor");
        Check(brush.All(c => c.WholeCell) && brush[0].Cell.Player && brush[0].Cell.Goal, "brush keeps whole-cell layered content");
        Check(stamp.Fits(8, 8, 5, 6), "exact edge placement fits");
        Check(!stamp.Fits(8, 8, 6, 6) && !stamp.Fits(8, 8, 5, 7) && !stamp.Fits(8, 8, -1, 0), "out-of-bounds placements are refused as a whole");
        var padded = new SokobanStamp { stampId = "padded", name = "透明边距", width = 8, height = 8,
            cells = new[] { new SokobanStampCell { x = 3, y = 4, box = true }, new SokobanStampCell { x = 2, y = 3, wall = true } } };
        var paddedJson = JsonUtility.ToJson(padded);
        var paddedBrush = padded.AsBrush();
        Check(padded.PlacementAnchor.Equals(new SokobanGridPoint(2, 3)) && paddedBrush[1].Dx == 0 && paddedBrush[1].Dy == 0,
            "cursor anchors to the first painted cell rather than transparent margins or JSON array order");
        Check(paddedBrush[0].Dx == 1 && paddedBrush[0].Dy == 1 && padded.Fits(8, 8, 0, 0) && padded.Fits(8, 8, 6, 6),
            "padded stamp keeps relative geometry and fits at both exact map edges");
        Check(!padded.Fits(8, 8, 7, 6) && !padded.Fits(8, 8, 6, 7), "anchored placement still refuses right and bottom overflow");
        Check(JsonUtility.ToJson(padded) == paddedJson, "brush anchoring does not crop or mutate saved dimensions and coordinates");
        padded.cells = new[] { new SokobanStampCell { x = 4, y = 2, wall = true }, new SokobanStampCell { x = 1, y = 3, box = true } };
        paddedBrush = padded.AsBrush();
        Check(padded.PlacementAnchor.Equals(new SokobanGridPoint(4, 2)) && paddedBrush[1].Dx == -3 && paddedBrush[1].Dy == 1,
            "irregular stamp anchors to a real cell and preserves cells further left on lower rows");
        Check(padded.Fits(8, 8, 3, 0) && !padded.Fits(8, 8, 2, 0), "negative anchored offsets participate in left-edge validation");
        padded.cells = new[] { new SokobanStampCell { x = 1, y = 1 }, new SokobanStampCell { x = 2, y = 1, wall = true } };
        Check(padded.PlacementAnchor.Equals(new SokobanGridPoint(1, 1)) && !padded.AsBrush()[0].Cell.Transparent,
            "explicit floor is painted content and remains a valid anchor rather than transparent padding");
        var tab = parsed.CreateDocument();
        Check(tab.IsStamp && tab.IsSaved && !tab.Dirty && tab.Descriptor == null, "stamp documents are separate from level descriptors");
        Check(tab.StampMask.Count == 4 && tab.Data.terrain[1] == "#.." && tab.Data.player.x == 0, "document creation keeps alpha mask and geometry");
        Check(JsonUtility.ToJson(SokobanStamp.FromDocument(tab)) == JsonUtility.ToJson(parsed), "document roundtrip keeps explicit floors and transparent holes");
        var snapshot = tab.CaptureUndo(); tab.Data.name = "改名"; tab.StampMask.Remove(new SokobanGridPoint(2, 1));
        Check(tab.RestoreUndo(snapshot) && tab.Data.name == "分层与透明" && tab.StampMask.Count == 4, "undo restores name and mask together");
        var empty = new SokobanStamp { stampId = "empty", name = "空白" };
        Check(SokobanStamp.Validate(empty).Any(e => e.Contains("绘制")) && empty.CreateDocument(false).Dirty, "empty drafts are editable but cannot be saved");
        var one = new SokobanStamp { stampId = "one", name = "一格", width = 1, height = 1, cells = new[] { new SokobanStampCell() } };
        Check(SokobanStamp.Validate(one).Count == 0 && one.CreateDocument().Data.size.width == 1, "one-cell floor stamps supported");
        Action<Action, string> throws = (action, reason) => { var failed = false; try { action(); } catch { failed = true; } Check(failed, reason); };
        throws(() => SokobanStamp.Parse("{invalid"), "malformed JSON refused");
        one.stampId = "../level"; Check(SokobanStamp.Validate(one).Count > 0, "unsafe paths rejected"); one.stampId = "one";
        one.name = "line\nbreak"; Check(SokobanStamp.Validate(one).Count > 0, "multiline names refused"); one.name = "一格";
        one.width = 41; Check(SokobanStamp.Validate(one).Count > 0, "size budget enforced"); one.width = 1;
        one.cells[0].x = 1; Check(SokobanStamp.Validate(one).Count > 0, "coordinates validated"); one.cells[0].x = 0;
        one.cells[0].wall = one.cells[0].box = true; Check(SokobanStamp.Validate(one).Count > 0, "objects on walls refused"); one.cells[0].wall = one.cells[0].box = false;
        one.cells = new[] { new SokobanStampCell(), new SokobanStampCell() }; Check(SokobanStamp.Validate(one).Count > 0, "duplicate coordinates refused");
        one.width = 2; one.cells[1].x = 1; one.cells[0].player = one.cells[1].player = true; Check(SokobanStamp.Validate(one).Count > 0, "multiple players refused");
        one.cells = new[] { new SokobanStampCell() }; one.width = 1;
        var root = Path.Combine(Application.dataPath, "../Temp/Codex/stamp-checks", Guid.NewGuid().ToString("N"));
        var bundledSnapshot = JsonUtility.ToJson(parsed);
        var store = new SokobanStampStore(root, () => new[] { bundledSnapshot, "broken bundle" });
        var levels = store.ReadAll(out var problems);
        Check(levels.Count == 1 && problems.Count == 1, "damaged entries do not hide valid presets");
        var path = store.Save(parsed); Check(File.Exists(path) && path.EndsWith(parsed.stampId + ".json"), "safe ID names the file");
        parsed.name = "本地覆盖"; store.Save(parsed);
        Check(store.ReadAll(out problems).Single().name == "本地覆盖", "local edit overrides bundled version by ID");
        Check(Directory.GetFiles(store.Root, "*.tmp-*").Length == 0, "atomic save leaves no temporary files");
        var before = File.ReadAllText(path); parsed.name = "";
        throws(() => store.Save(parsed), "failed validation does not publish");
        Check(File.ReadAllText(path) == before, "failed save preserves previous content");
        File.WriteAllText(Path.Combine(store.Root, "broken.json"), "oops");
        Check(store.ReadAll(out problems).Count == 1 && problems.Count == 2, "invalid local files are skipped and reported");
        var builtins = Resources.LoadAll<TextAsset>(SokobanStampRepository.ResourcePath);
        Check(builtins.Length >= 3 && builtins.All(a => SokobanStamp.Validate(SokobanStamp.Parse(a.text)).Count == 0), "bundled presets are valid and playable as brushes");
        return $"PASS: {checks} stamp assertions (sparse/layered JSON, placement bounds, document/undo, atomic save, packaged fallback and validation).";
    }
}
