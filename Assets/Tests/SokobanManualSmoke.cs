#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Current UI documentation and boundary-paste regression, using the smoke runner's isolated data.</summary>
public sealed partial class SokobanStandaloneSmoke
{
    private IEnumerator ManualFlow()
    {
        var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        Click("EmptyAddLevel");
        var tabs = Get<List<SokobanEditorTab>>(editor, "tabs");
        var tab = tabs[0]; var id = tab.Data.levelId;
        tab.Data = SokobanLevelRepository.Parse(JsonUtility.ToJson(SokobanLevelRepository.LoadJson("L001")));
        tab.Data.levelId = id; tab.Data.name = "操作手册示例";
        Call(editor, "SetActiveTab", 0); Click("Write");
        yield return Layout("16-editor-main");
        var selection = Get<HashSet<SokobanGridPoint>>(editor, "selection");
        selection.Add(new SokobanGridPoint(1, 1)); selection.Add(new SokobanGridPoint(2, 1));
        Call(editor, "RefreshSelectionVisual"); Call(editor, "RefreshDetail");
        yield return Layout("17-selection");
        Click("Copy"); Click("Paste");
        var before = JsonUtility.ToJson(tab.Data); var undoCount = tab.UndoStack.Count;
        var clipboard = Get<List<SokobanClipboardCell>>(editor, "clipboard");
        var clipboardCount = clipboard.Count;
        Call(editor, "CommitPaste", tab.Data.size.width - 1, 1);
        Check(JsonUtility.ToJson(tab.Data) == before && tab.UndoStack.Count == undoCount &&
            clipboard.Count == clipboardCount && Get<bool>(editor, "pasteMode"), "out-of-bounds paste is atomic and retains clipboard, mode and undo history");
        yield return Layout("18-paste-rejected");
        Call(editor, "CancelCurrentState");
        selection.Add(new SokobanGridPoint(1, 1)); selection.Add(new SokobanGridPoint(2, 1));
        Call(editor, "CutSelection");
        var cutSnapshot = JsonUtility.ToJson(tab.Data); undoCount = tab.UndoStack.Count;
        clipboard = Get<List<SokobanClipboardCell>>(editor, "clipboard"); clipboardCount = clipboard.Count;
        Call(editor, "CommitPaste", tab.Data.size.width - 1, 1);
        Check(JsonUtility.ToJson(tab.Data) == cutSnapshot && tab.UndoStack.Count == undoCount &&
            clipboard.Count == clipboardCount && Get<bool>(editor, "pasteOneShot"), "rejected cut placement retains pending content and one-shot mode");
        Call(editor, "CancelCurrentState"); Call(editor, "Undo");
        Click("Validate");
        var deadline = Time.realtimeSinceStartup + 15f;
        while (Get<object>(editor, "solverJob") != null && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Get<SokobanSolveResult>(editor, "solverResult").IsSolved, "editor manual example solves");
        yield return Layout("19-solver"); Click("SolverNext"); Click("SolverNext");
        yield return Layout("20-solver-step"); Click("SolverClose"); Click("Write");
        Click("AutoGen"); yield return Layout("21-generation-form");
        var fields = Get<Dictionary<string, InputField>>(editor, "generationFields");
        foreach (var entry in new Dictionary<string,string> { {"Width","6"}, {"Height","6"}, {"Boxes","1"}, {"Walls","0"}, {"MinPushes","1"}, {"MaxPushes","15"}, {"Seed","42"}, {"Budget","5"}, {"Candidates","100"} }) fields[entry.Key].text = entry.Value;
        editor.GetType().GetField("generationDifficultyValue", Fields).SetValue(editor, 0); Call(editor, "RefreshGenerationDifficulty");
        Click("GenerationStart"); deadline = Time.realtimeSinceStartup + 15f;
        while (Get<object>(editor, "generationJob") != null && Time.realtimeSinceStartup < deadline) yield return null;
        Check(Get<SokobanJsonLevel>(editor, "generatedLevel") != null, "single generation produces a reviewable result");
        yield return Layout("22-generation-result"); Click("GenerationAdopt");
        yield return Layout("23-adopted-tab");
        Click("AddCategory");
        Call(editor, "SelectManagerLevel", id, false, false);
        yield return Layout("24-management-selected");
        Click("ManagerTarget"); yield return Layout("25-category-target"); Call(editor, "CloseManagerTarget");
        Click("ManagerDelete"); yield return Layout("26-delete-confirm"); Click("ManagerDeleteCancel"); Click("ManagerClose");
        Call(editor, "CloseTab", tabs.Count - 1); yield return Layout("27-unsaved-tab"); Click("CloseTabCancel");
        Click("Stamp"); yield return Layout("28-stamp-library"); Click("StampClose");
        Call(editor, "AddBlankTab");
        tabs[tabs.Count - 1].Data.boxes = Array.Empty<SokobanJsonPoint>();
        Call(editor, "SetActiveTab", tabs.Count - 1);
        Click("Validate");
        Check(Get<SokobanSolveResult>(editor, "solverResult").Status == SokobanSolveStatus.Invalid, "invalid draft reports a structural error before search");
        yield return Layout("29-invalid-level"); Click("SolverClose");
        Call(editor, "CloseTab", tabs.Count - 1); Click("CloseTabDiscard");
        Click("Category"); yield return Layout("30-category-filter"); Call(editor, "ToggleCategoryPopup");
        Click("OpenLog"); yield return Layout("31-operation-log"); Click("OperationLogClose");
        // Keep the final quit from opening unsaved-document protection in an automated run.
        while (tabs.Count > 0) { Call(editor, "CloseTab", tabs.Count - 1); if (GameObject.Find("CloseTabDiscard") != null) Click("CloseTabDiscard"); }
    }
}
#endif
