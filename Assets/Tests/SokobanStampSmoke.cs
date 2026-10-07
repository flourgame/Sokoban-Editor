#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
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
    private IEnumerator StampFlow()
    {
        SokobanStampRepository.RootOverride = Path.Combine(folder, "stamp-data");
        var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        var tabs = Get<List<SokobanEditorTab>>(editor, "tabs");
        Check(tabs.Count == 0 && GameObject.Find("Stamp") != null, "stamp tool available with zero documents");
        Click("Stamp"); yield return null;
        Check(!GameObject.Find("StampUse").GetComponent<Button>().IsInteractable(), "using a preset requires an open document");
        Check(Get<List<SokobanStamp>>(editor, "availableStamps").Count >= 3, "packaged stamp presets discovered in standalone");
        yield return Layout("13-stamp-library");
        Click("StampNew"); var draft = tabs[0]; var id = draft.Data.levelId;
        Check(draft.IsStamp && draft.Dirty && draft.StampMask.Count == 0 && draft.Data.boxes.Length == 0 && draft.Data.player.x == -1, "new stamp is a separate transparent document");
        Check(!GameObject.Find("Play").GetComponent<Button>().IsInteractable() && GameObject.Find("Write").GetComponentInChildren<Text>().text == "保存印章", "stamp document cannot enter gameplay and routes save correctly");
        Click("Write");
        Check(draft.Dirty && !Directory.Exists(SokobanStampRepository.Root) && GameObject.Find("Detail").GetComponent<Text>().text.Contains("绘制"), "empty stamp save fails visibly without losing the draft");
        var name = Get<InputField>(editor, "nameField"); name.text = "自动验收分层印章"; Call(editor, "CommitNameEdit", name.text);
        Call(editor, "SetBrush", SokobanBrush.Wall); Call(editor, "ApplyBrushAt", 0, 0); Call(editor, "ApplyBrushAt", 2, 0);
        Call(editor, "SetBrush", SokobanBrush.Goal); Call(editor, "ApplyBrushAt", 1, 1);
        Call(editor, "SetBrush", SokobanBrush.Box); Call(editor, "ApplyBrushAt", 1, 1);
        Call(editor, "SetBrush", SokobanBrush.Player); Call(editor, "ApplyBrushAt", 2, 2);
        Call(editor, "SetBrush", SokobanBrush.Goal); Call(editor, "ApplyBrushAt", 2, 2);
        Call(editor, "SetBrush", SokobanBrush.Floor); Call(editor, "ApplyBrushAt", 1, 0);
        Check(draft.StampMask.Count == 5 && draft.Data.boxes.Length == 1 && draft.Data.goals.Length == 2 && draft.Data.player.x == 2, "stamp painting supports sparse walls, explicit floor and layered player/box goals");
        Click("StampTransparent"); Call(editor, "ApplyBrushAt", 0, 0);
        Check(!draft.StampMask.Contains(new SokobanGridPoint(0, 0)), "transparent brush removes painted cell");
        Call(editor, "Undo"); Check(draft.StampMask.Count == 5 && draft.Data.terrain[0][0] == '#', "undo restores transparency and terrain together");
        Call(editor, "ApplySize", "1", "1"); Check(draft.Data.size.width == 1 && draft.StampMask.Count == 1, "stamp supports one-cell dimensions and clips out-of-bounds content on resize");
        Call(editor, "Undo"); Check(draft.StampMask.Count == 5 && draft.Data.size.width == 8 && draft.Data.player.x == 2, "resize undo restores original stamp dimensions and layers");
        yield return Layout("14-stamp-designer");
        Click("Write");
        Check(!draft.Dirty && draft.StampSaved && File.Exists(Path.Combine(SokobanStampRepository.Root, id + ".json")), "stamp saves atomically to standalone stamp directory");
        var saved = SokobanStampRepository.Store.ReadAll(out var problems).Single(s => s.stampId == id);
        Check(saved.cells.Length == 5 && saved.name == "自动验收分层印章" && problems.Count == 0, "saved stamp rediscovered with all layers and holes");
        Check(!SokobanLevelRepository.ListAll().Any(d => d.LevelId == id), "saved stamps do not pollute the level list");
        Call(editor, "CloseTab", 0); Check(GameObject.Find("CloseTabDialog") == null && tabs.Count == 0, "clean saved stamp closes without an unsaved prompt");
        Click("Stamp"); Call(editor, "SelectStamp", id); Click("StampEdit");
        draft = tabs[0]; Check(draft.IsStamp && draft.StampMask.Count == 5 && !draft.Dirty, "saved stamp opens for normal editing");
        Call(editor, "SetBrush", SokobanBrush.Wall); Call(editor, "ApplyBrushAt", 4, 4);
        Click("Back"); yield return WaitScene("level"); EnterEditor(); yield return WaitScene("editor");
        editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>(); tabs = Get<List<SokobanEditorTab>>(editor, "tabs"); draft = tabs[0];
        Check(draft.IsStamp && draft.Dirty && draft.StampMask.Count == 6 && draft.UndoStack.Count == 1, "stamp dirty state, mask and undo survive scene changes");
        Call(editor, "CloseTab", 0); Check(GameObject.Find("CloseTabTitle").GetComponent<Text>().text == "印章尚未保存", "dirty stamp close has the proper save/discard/cancel prompt"); Click("CloseTabCancel");
        Call(editor, "Undo"); Click("Write"); Call(editor, "CloseTab", 0);
        Click("EmptyAddLevel"); var levelTab = tabs[0]; levelTab.Data.verifiedMoves = 25;
        Call(editor, "SetCell", 1, 2, new SokobanEditorCell { Box = true, Goal = true });
        var clipboard = Get<List<SokobanClipboardCell>>(editor, "clipboard"); clipboard.Add(new SokobanClipboardCell { WholeCell = true, Cell = new SokobanEditorCell { Box = true } });
        var initial = levelTab.CaptureUndo(); var undoCount = levelTab.UndoStack.Count;
        Click("Stamp"); Call(editor, "SelectStamp", id); Click("StampUse"); Call(editor, "CommitStamp", 1, 1);
        Check(levelTab.Data.verifiedMoves == -1 && levelTab.Dirty && levelTab.UndoStack.Count == undoCount + 1, "stamp is one undo action and invalidates trusted solution cost");
        var hole = (SokobanEditorCell)Call(editor, "GetCell", 1, 2);
        Check(hole.Box && hole.Goal && levelTab.Data.terrain[1][1] == '#' && levelTab.Data.player.x == 3 && levelTab.Data.player.y == 3, "transparent hole preserves target layers while painted cells replace them and move the player");
        Check(!Get<List<Image>>(editor, "cellObjects")[1 + 1 * 8].gameObject.activeSelf, "overwritten player graphic disappears immediately");
        Check(clipboard.Count == 1 && clipboard[0].Cell.Box && Get<bool>(editor, "stampMode"), "stamp brush keeps the ordinary clipboard and stays active");
        var onePlacement = levelTab.CaptureUndo(); Call(editor, "CommitStamp", 4, 1);
        Check(levelTab.Data.player.x == 6 && levelTab.UndoStack.Count == undoCount + 2, "stamp can be placed repeatedly with a single player");
        Call(editor, "Undo"); Check(levelTab.CaptureUndo() == onePlacement, "undo restores a complete previous placement");
        Call(editor, "Undo"); var original = SokobanLevelRepository.Parse(initial);
        Check(levelTab.Data.terrain.SequenceEqual(original.terrain) && levelTab.Data.player.x == original.player.x && levelTab.Data.boxes.Length == original.boxes.Length && levelTab.Data.verifiedMoves == -1, "undo restores the source layout but keeps old verification invalid");
        var beforeRejected = levelTab.CaptureUndo(); var rejectedUndo = levelTab.UndoStack.Count;
        Call(editor, "CommitStamp", 7, 7);
        Check(levelTab.CaptureUndo() == beforeRejected && levelTab.UndoStack.Count == rejectedUndo, "out-of-bounds stamp writes nothing and adds no undo record");
        Call(editor, "TogglePasteMode"); Check(!Get<bool>(editor, "stampMode") && Get<bool>(editor, "pasteMode") && clipboard.Count == 1, "paste and stamp modes are mutually exclusive"); Call(editor, "CancelCurrentState");
        var selection = Get<HashSet<SokobanGridPoint>>(editor, "selection"); selection.Add(new SokobanGridPoint(0, 0)); selection.Add(new SokobanGridPoint(2, 0));
        Click("Stamp"); Click("StampFromSelection"); var selectedDraft = tabs[1];
        Check(selectedDraft.IsStamp && selectedDraft.Data.size.width == 3 && selectedDraft.Data.size.height == 1 && selectedDraft.StampMask.Count == 2, "disconnected selected cells become a sparse stamp without changing the source level");
        Click("Write"); Check(File.Exists(Path.Combine(SokobanStampRepository.Root, selectedDraft.Data.levelId + ".json")), "selected region saves as reusable stamp JSON");
        Call(editor, "CloseTab", 1);
        Click("Stamp"); Click("StampNew"); Call(editor, "CloseTab", 1); Click("CloseTabSave");
        Check(tabs.Count == 2 && tabs[1].Dirty && GameObject.Find("CloseTabMessage").GetComponent<Text>().text.Contains("失败"), "failed stamp save-and-close keeps the document and confirmation");
        Click("CloseTabCancel");
        var quit = typeof(SokobanEditorSession).GetMethod("ConfirmQuit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Check(!(bool)quit.Invoke(null, null) && GameObject.Find("UnsavedQuitTitle").GetComponent<Text>().text.Contains("印章"), "application exit protects both stamps and levels"); Click("UnsavedQuitCancel");
        Call(editor, "CloseTab", 1); Click("CloseTabDiscard");
        Call(editor, "CloseTab", 0); Click("CloseTabDiscard");
        Check(tabs.Count == 0 && GameObject.Find("EditorEmptyHint") != null, "all stamp and level documents can close back to empty workspace");
        yield return null;
    }
}
#endif
