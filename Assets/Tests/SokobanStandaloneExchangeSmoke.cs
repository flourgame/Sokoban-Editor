#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class SokobanStandaloneSmoke
{
    private IEnumerator ExchangeFlow()
    {
        EnterEditor(); yield return WaitScene("editor");
        var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        var tabs = Get<List<SokobanEditorTab>>(editor, "tabs");
        if (tabs.Count == 0) Call(editor, "AddBlankTab");
        var oldTabs = tabs.ToArray();
        var oldJson = oldTabs.Select(t => JsonUtility.ToJson(t.Data)).ToArray();
        Click("ImportJson");
        GameObject.Find("JsonImportText").GetComponent<InputField>().text = "{}";
        Click("JsonImportConfirm");
        Check(GameObject.Find("ExchangeOverlay") != null && tabs.Count == oldTabs.Length, "invalid JSON keeps dialog and existing tabs");
        var data = new SokobanJsonLevel { name = "表格与 JSON 示例", size = new SokobanJsonSize { width = 6, height = 6 },
            terrain = SokobanLevelRepository.BuildEmptyTerrain(6, 6), player = new SokobanJsonPoint(1, 2),
            boxes = new[] { new SokobanJsonPoint(2, 2) }, goals = new[] { new SokobanJsonPoint(4, 2) } };
        var legacyJson = JsonUtility.ToJson(data).Replace("\"generation\":{", "\"generation\":{\"referenceRepository\":\"legacy\",\"referenceCommit\":\"old\",\"candidate\":1,\"searchAttempts\":2,")
            .Replace("\"solution\":{", "\"solution\":{\"algorithm\":\"legacy\",\"exploredNodes\":10,\"elapsedMs\":20,");
        GameObject.Find("JsonImportText").GetComponent<InputField>().text = legacyJson;
        yield return Layout("34-json-import");
        Click("JsonImportConfirm"); yield return null;
        Check(tabs.Count == oldTabs.Length + 1 && tabs.Last().Dirty && tabs.Last().Descriptor == null, "JSON import creates an unsaved new tab");
        Check(oldTabs.Select(t => JsonUtility.ToJson(t.Data)).SequenceEqual(oldJson), "JSON import preserves existing documents");
        Click("CopyJson");
        Check(!GUIUtility.systemCopyBuffer.Contains("referenceRepository") && !GUIUtility.systemCopyBuffer.Contains("elapsedMs"),
            "copy JSON excludes legacy references and search diagnostics");
        Click("Write");
        var saved = tabs.Last(); var originalJson = File.ReadAllText(saved.Descriptor.FilePath);
        Check(!originalJson.Contains("referenceCommit") && !originalJson.Contains("searchAttempts") && !originalJson.Contains("exploredNodes"),
            "saving JSON persists the cleaned contract");
        Click("AddCategory");
        Call(editor, "SelectManagerLevel", saved.Data.levelId, false, false);
        yield return Layout("11-management");
        Click("ManagerExportSelection");
        var xlsxPath = Path.Combine(folder, "exported-levels.xlsx");
        File.WriteAllText(xlsxPath, "existing file");
        GameObject.Find("XlsxPath").GetComponent<InputField>().text = xlsxPath;
        yield return Layout("35-xlsx-export");
        Click("XlsxExportConfirm");
        Check(File.ReadAllText(xlsxPath) == "existing file", "first export confirmation preserves existing workbook");
        Click("XlsxExportConfirm");
        var table = SokobanXlsx.Read(xlsxPath);
        Check(table.Count == 2 && table[1][1] == data.name && table[1][9].Contains("terrain"), "selected XLSX export writes name and full JSON");
        Check(!table[1][9].Contains("referenceRepository") && !table[1][9].Contains("algorithm"), "XLSX JSON excludes removed fields");
        Click("ManagerImportXlsx");
        var badPath = Path.Combine(folder, "invalid-levels.xlsx");
        SokobanXlsx.Write(badPath, new[] { new[] { "配置JSON" }, new[] { "{}" } });
        GameObject.Find("XlsxPath").GetComponent<InputField>().text = badPath;
        Click("XlsxImportPreview");
        Check(!GameObject.Find("XlsxImportConfirm").GetComponent<Button>().interactable, "invalid XLSX disables import confirmation");
        GameObject.Find("XlsxPath").GetComponent<InputField>().text = xlsxPath;
        Click("XlsxImportPreview");
        yield return Layout("36-xlsx-import");
        var beforeCount = SokobanLevelRepository.ListAll().Count;
        Click("XlsxImportConfirm");
        Check(SokobanLevelRepository.ListAll().Count == beforeCount + 1, "XLSX import saves and lists one new level");
        Check(File.ReadAllText(saved.Descriptor.FilePath) == originalJson, "XLSX import preserves original file");
        Click("ManagerOpen"); yield return null;
        Check(tabs.Last().Data.name == data.name && tabs.Last().Data.levelId != saved.Data.levelId, "imported XLSX can be opened with a new ID");
        Click("AddCategory"); Click("ManagerImportXlsx");
        GameObject.Find("XlsxPath").GetComponent<InputField>().text = xlsxPath;
        Click("XlsxImportPreview");
        GameObject.Find("XlsxPath").GetComponent<InputField>().text = xlsxPath + ".missing.xlsx";
        Check(!GameObject.Find("XlsxImportConfirm").GetComponent<Button>().interactable, "changing import path invalidates preview");
        Click("ExchangeCancel"); Click("ManagerClose");
        while (tabs.Count > 0) { Call(editor, "CloseTab", tabs.Count - 1); if (GameObject.Find("CloseTabDiscard") != null) Click("CloseTabDiscard"); }
    }
}
#endif
