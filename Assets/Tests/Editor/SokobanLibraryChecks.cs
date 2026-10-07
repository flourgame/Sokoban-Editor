using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using UnityEngine;

public static class SokobanLibraryChecks
{
    private static int checks;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception("Library regression: " + message);
        checks++;
    }
    private static void Reject(Action action, string message)
    {
        var rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Require(rejected, message);
    }

    public static string Run()
    {
        checks = 0;
        SelectionChecks();
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/Codex/library-checks", Guid.NewGuid().ToString("N")));
        var backups = Path.Combine(root, "DeletedBackups");
        var store = new SokobanLibraryStore(root, backups);
        var levels = new List<SokobanLevelDescriptor>();
        var original = new Dictionary<string, string>();
        for (var i = 0; i < 6; i++)
        {
            var id = ((char)('A' + i)).ToString();
            var source = i < 2 ? "BuiltIn" : "Generated";
            var path = Path.Combine(root, source, id, "level.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var data = new SokobanJsonLevel { levelId = id, name = "测试 " + id, terrain = SokobanLevelRepository.BuildEmptyTerrain(8, 8) };
            var json = JsonUtility.ToJson(data, true); File.WriteAllText(path, json); original[id] = json;
            File.WriteAllText(path + ".meta", "fixture-meta-" + id);
            levels.Add(new SokobanLevelDescriptor { LevelId = id, Title = data.name, Source = source, Folder = id, FilePath = path, Order = i });
        }
        levels = store.Apply(levels).ToList();
        Require(!File.Exists(store.CatalogPath), "reading does not create a catalog");
        Require(levels.Take(2).All(l => l.Category == "BuiltIn") && levels.Skip(2).All(l => l.Category == "Generated"), "default categories");
        store.CreateCategory("训练");
        var catalogBefore = File.ReadAllText(store.CatalogPath);
        Reject(() => store.CreateCategory("generated"), "duplicate category ignores case");
        Reject(() => store.CreateCategory("All"), "reserved category rejected");
        Reject(() => store.CreateCategory("bad\nname"), "control characters rejected");
        Require(File.ReadAllText(store.CatalogPath) == catalogBefore, "failed category creation preserves catalog");
        store.Migrate(levels, new[] { "B", "D" }, "训练");
        var reopened = new SokobanLibraryStore(root, backups);
        levels = reopened.Apply(levels).ToList();
        Require(levels.Where(l => l.Category == "训练").Select(l => l.LevelId).SequenceEqual(new[] { "B", "D" }), "migration persists");
        Require(levels[1].Source == "BuiltIn" && levels[3].Source == "Generated", "migration preserves source and ID");
        Require(levels.All(l => File.ReadAllText(l.FilePath) == original[l.LevelId]), "migration preserves every JSON byte");
        reopened.Reorder(levels, new[] { "B", "D" }, new[] { "D", "B" });
        levels = reopened.Apply(levels).ToList();
        Require(levels.Select(l => l.LevelId).SequenceEqual(new[] { "A", "D", "C", "B", "E", "F" }), "filtered order replaces only matching slots");
        Require(levels.Where(l => l.Category != "训练").Select(l => l.LevelId).SequenceEqual(new[] { "A", "C", "E", "F" }), "other categories retain relative order");
        var orderBefore = File.ReadAllText(store.CatalogPath);
        Reject(() => reopened.Reorder(levels, new[] { "B", "D" }, new[] { "B", "B" }), "duplicate order rejected");
        Reject(() => reopened.Migrate(levels, new[] { "Missing" }, "训练"), "stale selection rejected");
        Require(File.ReadAllText(store.CatalogPath) == orderBefore, "invalid batch operations preserve catalog");
        var bundled = new SokobanLibraryStore(Path.Combine(root, "Bundled"), backups, orderBefore);
        Require(bundled.Apply(levels).Select(l => l.LevelId).SequenceEqual(levels.Select(l => l.LevelId)), "bundled catalog applies saved ordering");
        var b = levels.Single(l => l.LevelId == "B"); var d = levels.Single(l => l.LevelId == "D");
        var extra = Path.Combine(Path.GetDirectoryName(b.FilePath), "keep.txt"); File.WriteAllText(extra, "keep unrelated asset");
        reopened.Delete(levels, new[] { "B", "D" });
        Require(!File.Exists(b.FilePath) && !File.Exists(d.FilePath) && !File.Exists(b.FilePath + ".meta"), "delete removes selected JSON and meta");
        Require(File.ReadAllText(extra) == "keep unrelated asset", "delete preserves unrelated assets in same folder");
        Require(Directory.GetFiles(backups, "level.json", SearchOption.AllDirectories).Length == 2 &&
            Directory.GetFiles(backups, "*.meta", SearchOption.AllDirectories).Length == 2, "deletion backups preserve JSON and meta");
        Require(!reopened.Apply(levels).Any(l => l.LevelId == "B" || l.LevelId == "D"), "deleted catalog entries suppress stale resources");
        levels = reopened.Apply(levels).ToList();
        var c = levels.Single(l => l.LevelId == "C"); var e = levels.Single(l => l.LevelId == "E");
        var beforeFailure = File.ReadAllText(store.CatalogPath);
        using (var locked = new FileStream(store.CatalogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => reopened.Delete(levels, new[] { "C", "E" }), "catalog lock aborts batch deletion");
        Require(File.Exists(c.FilePath) && File.Exists(e.FilePath) && File.Exists(c.FilePath + ".meta") && File.Exists(e.FilePath + ".meta"), "failed batch restores all JSON and meta");
        Require(File.ReadAllText(c.FilePath) == original["C"] && File.ReadAllText(e.FilePath) == original["E"] &&
            File.ReadAllText(store.CatalogPath) == beforeFailure, "rollback preserves original bytes and catalog");
        var outsidePath = Path.Combine(Path.GetDirectoryName(root), "outside-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(outsidePath, "outside");
        var outside = new SokobanLevelDescriptor { LevelId = "Outside", Source = "Generated", Category = "Generated", FilePath = outsidePath };
        Reject(() => reopened.Delete(levels.Concat(new[] { outside }).ToList(), new[] { "C", "Outside" }), "out-of-root target rejected before any file move");
        Require(File.ReadAllText(outsidePath) == "outside" && File.Exists(c.FilePath), "out-of-root rejection preserves every file");
        File.WriteAllText(store.CatalogPath, "{ malformed json");
        Reject(() => reopened.Delete(levels, new[] { "C" }), "corrupt catalog blocks mutation");
        Require(File.Exists(c.FilePath), "corrupt catalog does not delete selected file");
        File.WriteAllText(store.CatalogPath, beforeFailure);
        return "PASS: " + checks + " selection, filtered/group ordering, category persistence, bundled reading, deletion scope, backups and batch rollback checks; fixtures only under Temp/Codex.";
    }

    private static void SelectionChecks()
    {
        var visible = new[] { "A", "B", "C", "D", "E", "F" };
        var selection = new SokobanLibrarySelection();
        selection.Click(visible, "B", false, false); Require(selection.Selected.SetEquals(new[] { "B" }), "plain click replaces selection");
        selection.Click(visible, "D", true, false); Require(selection.Selected.SetEquals(new[] { "B", "D" }), "control click adds");
        selection.Click(visible, "F", false, true); Require(selection.Selected.SetEquals(new[] { "D", "E", "F" }), "shift selects anchored range");
        selection.Click(visible, "B", true, true); Require(selection.Selected.SetEquals(new[] { "B", "C", "D", "E", "F" }), "control shift unions range");
        selection.Click(visible, "C", true, false); Require(selection.Selected.SetEquals(new[] { "B", "D", "E", "F" }), "control click removes");
        selection.Retain(new[] { "D", "E", "F" }); Require(selection.Anchor == null && selection.Selected.SetEquals(new[] { "D", "E", "F" }), "hidden anchor cleared");
        selection.Click(new[] { "D", "E", "F" }, "E", false, true); Require(selection.Selected.SetEquals(new[] { "E" }), "shift without anchor selects one");
        selection.SelectAll(visible); Require(selection.Selected.Count == 6, "select all visible");
        selection.Clear(); Require(selection.Selected.Count == 0 && selection.Anchor == null, "clear selection and anchor");
        Require(SokobanLibraryOrdering.MoveGroup(visible, new[] { "A", "C", "E" }, 6).SequenceEqual(new[] { "B", "D", "F", "A", "C", "E" }), "noncontiguous group retains order at end");
        Require(SokobanLibraryOrdering.MoveGroup(visible, new[] { "C", "D" }, 0).SequenceEqual(new[] { "C", "D", "A", "B", "E", "F" }), "group moves to start");
        Require(SokobanLibraryOrdering.MoveGroup(visible, new[] { "C", "D" }, 3).SequenceEqual(visible), "drop inside selected group is unchanged");
    }
}
