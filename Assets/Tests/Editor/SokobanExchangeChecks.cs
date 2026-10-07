using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System.Reflection;
using Kuluobishi.Sokoban.Editor;

public static class SokobanExchangeChecks
{
    /// <summary>Run on a fresh editor scene in Unity Play mode, where missing components may be fake-null wrappers.</summary>
    public static string CheckJsonImportUiInPlayMode()
    {
        if (!Application.isPlaying) throw new InvalidOperationException("此项检查须在 Unity Play 模式执行。");
        checks = 0;
        var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        if (editor == null) throw new InvalidOperationException("请先运行 editor 场景。");
        var root = (RectTransform)typeof(SokobanSceneController).GetField("Root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(editor);
        Require(root.GetComponent<CanvasGroup>() == null, "fresh Workspace has no CanvasGroup");
        var import = root.Find("Toolbar/ImportJson").GetComponent<Button>();
        ExecuteEvents.Execute(import.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        var group = root.GetComponent<CanvasGroup>();
        Require(group != null && !group.interactable, "pointer click adds group and blocks underlying Workspace");
        Require(root.Find("ExchangeOverlay/ExchangeDialog/JsonImportText") != null, "JSON input opens in Unity Editor");
        root.Find("ExchangeOverlay/ExchangeDialog/ExchangeCancel").GetComponent<Button>().onClick.Invoke();
        Require(group.interactable, "closing restores Workspace interaction");
        import.onClick.Invoke();
        Require(root.GetComponents<CanvasGroup>().Length == 1 && !group.interactable, "reopening reuses the existing group");
        root.Find("ExchangeOverlay/ExchangeDialog/ExchangeCancel").GetComponent<Button>().onClick.Invoke();
        Require(group.interactable, "second close restores interaction");
        return "Unity Play JSON import UI PASS (" + checks + ")";
    }

    private static int checks;
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Exchange regression: " + message); checks++; }
    private static void Reject(Action action, string message)
    {
        var rejected = false;
        try { action(); } catch (Exception) { rejected = true; }
        Require(rejected, message);
    }
    private static SokobanJsonLevel Fixture() => new SokobanJsonLevel
    {
        levelId = "Existing", name = "中文 & <关卡> \"一\"",
        size = new SokobanJsonSize { width = 6, height = 6 }, terrain = SokobanLevelRepository.BuildEmptyTerrain(6, 6),
        player = new SokobanJsonPoint(1, 2), boxes = new[] { new SokobanJsonPoint(2, 2) }, goals = new[] { new SokobanJsonPoint(4, 2) },
        metadata = new SokobanJsonMetadata { notes = "第一行\n第二行 & < >", author = "测试作者" }
    };

    public static string Run()
    {
        checks = 0;
        var root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/ExchangeChecks"));
        Directory.CreateDirectory(root);
        var level = Fixture(); var json = JsonUtility.ToJson(level);
        var legacy = json.Replace("\"generation\":{", "\"generation\":{\"referenceRepository\":\"https://example.com/legacy\",\"referenceCommit\":\"old-commit\",\"candidate\":12,\"searchAttempts\":34,")
            .Replace("\"solution\":{", "\"solution\":{\"algorithm\":\"Push A*\",\"exploredNodes\":100,\"elapsedMs\":500,");
        var clean = SokobanLevelExchange.ParseImport(legacy);
        Require(JsonUtility.ToJson(clean) == json, "legacy JSON removes diagnostics and preserves all current fields");
        var cleanPath = Path.Combine(root, "clean-level.json");
        var cleanDescriptor = new SokobanLevelDescriptor { LevelId = clean.levelId, Title = clean.name, FilePath = cleanPath, Folder = clean.levelId, Source = "Generated", Category = "Generated" };
        File.WriteAllText(cleanPath, legacy);
        SokobanLevelRepository.SaveJson(clean, cleanDescriptor, false);
        Require(File.ReadAllText(cleanPath) == JsonUtility.ToJson(level, true), "saving legacy data writes the cleaned contract");
        var cleanWorkbook = Path.Combine(root, "clean-contract.xlsx");
        var export = SokobanLevelExchange.ExportRows(new[] { cleanDescriptor });
        SokobanXlsx.Write(cleanWorkbook, export);
        Require(SokobanXlsx.Read(cleanWorkbook)[1][9] == json, "XLSX config JSON uses the same cleaned contract");
        Require(JsonUtility.ToJson(SokobanLevelExchange.ParseRows(SokobanXlsx.Read(cleanWorkbook))[0].Level) == json,
            "clean XLSX import preserves the current contract");
        Require(SokobanLevelExchange.ParseImport(json).terrain.SequenceEqual(level.terrain), "JSON retains layout");
        Reject(() => SokobanLevelExchange.ParseImport("{}"), "empty object rejected instead of creating defaults");
        Reject(() => SokobanLevelExchange.ParseImport("null"), "null rejected");
        Reject(() => SokobanLevelExchange.ParseImport("{bad json}"), "malformed JSON rejected");
        Reject(() => SokobanLevelExchange.ParseImport(json.Replace("\"width\":6", "\"width\":2147483647")), "huge dimensions rejected before allocation");
        var invalid = Fixture(); invalid.boxes[0].x = 0;
        Reject(() => SokobanLevelExchange.ParseImport(JsonUtility.ToJson(invalid)), "box on wall rejected");
        invalid = Fixture(); invalid.schemaVersion = 99;
        Reject(() => SokobanLevelExchange.ParseImport(JsonUtility.ToJson(invalid)), "unsupported schema rejected");
        var table = new List<string[]> { SokobanLevelExchange.Headers.ToArray(),
            new[] { "Existing", level.name, "训练", "2", "6", "6", "1", "0", "-1", json },
            new[] { "Existing", "=不是公式", "训练", "1", "6", "6", "1", "0", "-1", json } };
        var path = Path.Combine(root, "roundtrip.xlsx");
        SokobanXlsx.Write(path, table);
        var read = SokobanXlsx.Read(path);
        Require(read.Count == table.Count && read.SelectMany(r => r).SequenceEqual(table.SelectMany(r => r)), "XLSX preserves every value and multiline JSON");
        var parsed = SokobanLevelExchange.ParseRows(read);
        Require(parsed[0].Level.name == "=不是公式" && parsed[1].Level.name == level.name, "order sorting and name column override");
        Require(parsed.All(r => r.Category == "训练"), "categories retained");
        var simple = SokobanLevelExchange.ParseRows(new[] { new[] { "name", "configJson" }, new[] { "重命名", json } });
        Require(simple.Count == 1 && simple[0].Level.name == "重命名" && simple[0].Category == "Generated", "minimal English headers supported");
        Reject(() => SokobanLevelExchange.ParseRows(new[] { new[] { "名称" }, new[] { "只有名字" } }), "JSON column required");
        Reject(() => SokobanLevelExchange.ParseRows(new[] { new[] { "配置JSON", "json" }, new[] { json, json } }), "duplicate JSON header rejected");
        Reject(() => SokobanLevelExchange.ParseRows(new[] { new[] { "配置JSON", "分类" }, new[] { json, "All" } }), "reserved category rejected");
        Reject(() => SokobanLevelExchange.ParseRows(table.Concat(new[] { new[] { "Bad", "无效", "训练", "3", "", "", "", "", "", "{}" } }).ToList()), "one invalid row rejects entire table");
        var originalBytes = File.ReadAllBytes(path);
        Reject(() => SokobanXlsx.Write(path, new[] { new[] { new string('x', 32768) } }), "Excel cell limit enforced");
        Require(File.ReadAllBytes(path).SequenceEqual(originalBytes), "invalid export preserves existing file");
        Reject(() => SokobanXlsx.Read(Path.Combine(root, "missing.xlsx")), "missing file reported");

        var libraryRoot = Path.Combine(root, Guid.NewGuid().ToString("N"));
        var generatedRoot = Path.Combine(libraryRoot, "2");
        Directory.CreateDirectory(generatedRoot);
        var originalPath = Path.Combine(generatedRoot, "Existing", "level.json");
        Directory.CreateDirectory(Path.GetDirectoryName(originalPath)); File.WriteAllText(originalPath, json);
        var descriptor = new SokobanLevelDescriptor { LevelId = "Existing", Title = level.name, Source = "Generated", Category = "Generated", FilePath = originalPath, Folder = "Existing" };
        var existing = new[] { descriptor };
        var store = new SokobanLibraryStore(libraryRoot, Path.Combine(root, "Backups"));
        store.CreateCategory("保留分类"); var publication = store.Read().publishedCategories;
        var imported = SokobanLevelExchange.SaveNew(parsed, generatedRoot, store, existing);
        Require(imported.Count == 2 && imported.All(d => d.LevelId != "Existing") && imported.Select(d => d.LevelId).Distinct().Count() == 2, "all imports receive unique new IDs");
        Require(File.ReadAllText(originalPath) == json, "existing JSON preserved byte for byte");
        var catalog = store.Read();
        Require(catalog.categories.Contains("保留分类") && catalog.categories.Contains("训练"), "append creates and retains categories");
        Require(catalog.publishedCategories.SequenceEqual(publication), "publication preferences preserved");
        Require(catalog.entries.Where(e => imported.Any(d => d.LevelId == e.levelId)).OrderBy(e => e.order).Select(e => e.levelId).SequenceEqual(imported.Select(d => d.LevelId)), "import order appended to catalog");
        Require(imported.All(d => JsonUtility.FromJson<SokobanJsonLevel>(File.ReadAllText(d.FilePath)).levelId == d.LevelId), "saved JSON IDs match paths");
        var before = File.ReadAllText(store.CatalogPath); var filesBefore = Directory.GetFiles(generatedRoot, "level.json", SearchOption.AllDirectories).Length;
        using (var locked = new FileStream(store.CatalogPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject(() => SokobanLevelExchange.SaveNew(parsed, generatedRoot, store, existing.Concat(imported).ToList()), "locked catalog aborts import");
        Require(File.ReadAllText(store.CatalogPath) == before && Directory.GetFiles(generatedRoot, "level.json", SearchOption.AllDirectories).Length == filesBefore, "failed import rolls back new files and preserves catalog");

        // Fixture generated by an independent Excel writer: shared strings, rich text and sparse cells.
        var independent = Path.Combine(root, "external.xlsx");
        if (File.Exists(independent))
        {
            var external = SokobanLevelExchange.ParseRows(SokobanXlsx.Read(independent));
            Require(external.Count == 1 && external[0].Level.name == "外部工具关卡" && external[0].Category == "外部分类", "independent shared-string workbook imports");
        }
        return "Sokoban exchange checks PASS (" + checks + ")";
    }
}
