#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kuluobishi.Sokoban;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;

/// <summary>Explicit Play-mode regression using a temporary progress store; never clears player files.</summary>
public sealed class SokobanGmSmoke : MonoBehaviour
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private string folder, originalScene, originalId;
    private bool originalAll, originalDebug, originalPreview, originalOpen, originalExpanded;
    private SokobanProgressStore originalStore;
    private int checks, errors;
    private readonly StringBuilder report = new StringBuilder();
    private static FieldInfo StoreField => typeof(SokobanProgressStore).GetField("current", BindingFlags.Static | BindingFlags.NonPublic);
    public static void Begin(string directory)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("GM regression must start after Play mode is ready.");
        if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A GM regression output directory is required.", nameof(directory));
        var destination = Path.GetFullPath(directory);
        var test = new GameObject("GmSmoke").AddComponent<SokobanGmSmoke>(); test.folder = destination; DontDestroyOnLoad(test.gameObject);
    }
    private IEnumerator Start()
    {
        // Discard stale components restored by an Editor domain reload; tests only run via Begin.
        if (string.IsNullOrWhiteSpace(folder)) { Destroy(gameObject); yield break; }
        Directory.CreateDirectory(folder); originalStore = SokobanProgressStore.Current;
        originalScene = SceneManager.GetActiveScene().name; originalId = SokobanRuntimeContext.SelectedLevelId;
        originalAll = SokobanRuntimeContext.ShowAllLevels; originalOpen = SokobanRuntimeContext.IsGmEnabled;
        originalExpanded = SokobanGmController.Current.IsExpanded;
        originalDebug = SokobanRuntimeContext.IsDebugPlay; originalPreview = SokobanRuntimeContext.IsEditorPreview;
        StoreField.SetValue(null, new SokobanProgressStore(Path.Combine(folder, "progress.json")));
        Application.logMessageReceived += OnLog;
        var flows = new Stack<IEnumerator>(); flows.Push(Flow()); bool success = true;
        while (flows.Count > 0)
        {
            bool next = false; var flow = flows.Peek();
            try { next = flow.MoveNext(); }
            catch (Exception error) { report.AppendLine("FAIL: " + error); success = false; break; }
            if (!next) { flows.Pop(); continue; }
            if (flow.Current is IEnumerator nested) { flows.Push(nested); continue; }
            yield return flow.Current;
        }
        report.AppendLine($"RESULT: {(success && errors == 0 ? "PASS" : "FAIL")}; assertions={checks}; runtimeErrors={errors}; screen={Screen.width}x{Screen.height}");
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString());
        StoreField.SetValue(null, originalStore);
        SokobanRuntimeContext.ShowAllLevels = originalAll; SokobanRuntimeContext.IsDebugPlay = originalDebug;
        SokobanRuntimeContext.IsEditorPreview = originalPreview; SokobanRuntimeContext.SelectedLevelId = originalId;
        SokobanGmController.Current.SetOpen(originalOpen, originalExpanded); SceneManager.LoadScene(originalScene);
        Application.logMessageReceived -= OnLog; Destroy(gameObject);
    }
    private void OnLog(string text, string trace, LogType type)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; report.AppendLine("ERROR: " + text + "\n" + trace); } }
    private void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; report.AppendLine("PASS: " + reason); }
    private static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
    private static object Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    private static void Click(string name)
    {
        var obj = GameObject.Find(name); if (obj == null) throw new Exception("Missing button: " + name);
        var button = obj.GetComponent<Button>(); if (!button.IsInteractable()) throw new Exception("Disabled button: " + name); button.onClick.Invoke();
    }
    private static GameObject Hit(GameObject obj)
    {
        Canvas.ForceUpdateCanvases();
        var rect = obj.GetComponent<RectTransform>(); var results = new List<RaycastResult>();
        var position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, results);
        return results.Count > 0 ? results[0].gameObject : null;
    }
    private IEnumerator Wait(string scene)
    { for (var i = 0; i < 4; i++) yield return null; Check(SceneManager.GetActiveScene().name == scene, "scene " + scene); }
    private IEnumerator Capture(string name)
    {
        yield return null; Canvas.ForceUpdateCanvases();
        CheckWorkspace(name);
        foreach (var text in FindObjectsOfType<Text>())
            if (text.isActiveAndEnabled && text.verticalOverflow == VerticalWrapMode.Truncate && !string.IsNullOrEmpty(text.text))
                Check(text.preferredHeight <= text.rectTransform.rect.height + 2f, name + " text fits: " + text.name);
        typeof(SokobanStandaloneSmoke).GetMethod("CaptureUi", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Path.Combine(folder, name + ".png") });
    }
    private void CheckWorkspace(string reason)
    {
        var workspace = GameObject.Find("Workspace").GetComponent<RectTransform>();
        Check(workspace.localScale == Vector3.one && workspace.anchoredPosition == Vector2.zero, reason + " keeps original workspace scale and position");
    }
    private void Solve()
    {
        var game = FindObjectOfType<SokobanGameplaySceneController>(); Call(game, "ClosePause");
        var source = Get<SokobanLevelRuntime>(game, "level").Source;
        if (!SokobanSolverAdapter.TryCreateSnapshot(source, out var snapshot, out var error)) throw new Exception(error);
        var result = SokobanSolver.Solve(snapshot, new SokobanSolveOptions { TimeoutMs = 5000 });
        if (!result.IsSolved) throw new Exception("Unsolved campaign fixture");
        foreach (var move in result.Moves) Call(game, "TryMove", SokobanSolutionPlayback.ParseDirection(move));
        Check(Get<SokobanState>(game, "state").IsWon, "complete runtime path: " + source.levelId);
    }
    private IEnumerator Flow()
    {
        SokobanRuntimeContext.ShowAllLevels = SokobanRuntimeContext.IsDebugPlay = SokobanRuntimeContext.IsEditorPreview = false;
        SokobanGmController.Current.SetOpen(false); SceneManager.LoadScene("start"); yield return Wait("start");
        Check(GameObject.Find("EditorButton") == null && GameObject.Find("GMPanel") == null, "main menu hides editor and GM by default");
        Click("StartButton"); yield return Wait("level");
        var selector = FindObjectOfType<SokobanLevelSelectSceneController>(); var ids = SokobanCampaign.Ids();
        Check(Get<List<SokobanLevelDescriptor>>(selector, "descriptors").All(d => d.Category == "BuiltIn") && GameObject.Find("EditorButton") == null, "ordinary selection shows only published-category levels without editor entry");
        Check(GameObject.Find("PreviewInfo").GetComponent<Text>().text.IndexOf("分类") < 0 && GameObject.Find("Level_L001").GetComponentInChildren<Text>().text.IndexOf("BuiltIn") < 0, "ordinary names and details hide categories");
        Click("Level_L002"); Check(!GameObject.Find("StartButton").GetComponent<Button>().IsInteractable(), "fresh player's second level is locked");
        Call(selector, "StartSelected"); Check(SceneManager.GetActiveScene().name == "level", "locked level cannot start through its callback");
        yield return Capture("01-player-levels");
        SokobanRuntimeContext.SelectedLevelId = "L002"; SceneManager.LoadScene("game"); yield return Wait("game");
        Check(!FindObjectOfType<SokobanGameplaySceneController>().CanUseGmGameplay && GameObject.Find("Error").GetComponent<Text>().text.Contains("未解锁"), "direct game loading cannot bypass the lock");
        var gm = SokobanGmController.Current; var instance = gm.GetInstanceID(); gm.Toggle(); yield return null;
        var panel = GameObject.Find("GMPanel").GetComponent<RectTransform>();
        Check(SokobanRuntimeContext.IsGmEnabled && !gm.IsExpanded && GameObject.Find("GmCommands") == null, "equals toggle shows only a collapsed dropdown entry");
        Check(panel.anchorMin == Vector2.one && panel.anchorMax == Vector2.one && panel.pivot == Vector2.one && panel.rect.width <= 140f && panel.rect.height <= 44f, "collapsed entry is compact and anchored at the top right");
        Check(Hit(GameObject.Find("GmDropdown"))?.name == "GmDropdown", "collapsed entry accepts pointer events");
        yield return Capture("02-gm-collapsed");
        SceneManager.LoadScene("start"); yield return Wait("start");
        Check(SokobanRuntimeContext.IsGmEnabled && !gm.IsExpanded && gm.GetInstanceID() == instance, "collapsed GM entry persists across scenes");
        yield return Capture("02a-gm-menu");
        Click("GmDropdown"); Click("GmPlayerLevels"); yield return Wait("level");
        Check(SokobanRuntimeContext.IsGmEnabled && gm.IsExpanded && SokobanGmController.Current.GetInstanceID() == instance, "expanded GM persists as the same object across scenes");
        var commands = GameObject.Find("GmCommands").GetComponentsInChildren<Button>();
        Check(commands.Length == 10 && commands.All(b => Mathf.Abs(b.GetComponent<RectTransform>().anchoredPosition.x) < 0.1f), "dropdown commands form one compact column");
        var workspace = GameObject.Find("Workspace").GetComponent<RectTransform>();
        CheckWorkspace("expanded dropdown");
        Check(panel.rect.width <= 260f && panel.rect.height <= 460f && GameObject.Find("GmHint") == null && GameObject.Find("GmTitle") == null && GameObject.Find("GmClose") == null && GameObject.Find("GmStatus") == null, "dropdown removes explanatory text and empty status space");
        Check(Hit(GameObject.Find("Level_L001"))?.transform.IsChildOf(workspace) == true && Hit(GameObject.Find("GmEditor"))?.transform.IsChildOf(panel) == true, "dropdown accepts its own pointer events while uncovered player area stays clickable");
        yield return Capture("02b-gm-expanded");
        Click("GmDropdown"); yield return null;
        Check(!gm.IsExpanded && GameObject.Find("GmCommands") == null && SokobanRuntimeContext.IsGmEnabled, "clicking the top collapses commands and retains the entry");
        CheckWorkspace("collapsed dropdown");
        gm.Toggle(); yield return null; Check(!SokobanRuntimeContext.IsGmEnabled && GameObject.Find("GMPanel") == null, "equals hides the dropdown entry");
        gm.Toggle(); yield return null; Check(!gm.IsExpanded, "equals reopens the compact entry without commands");
        Click("GmDropdown"); yield return null;
        Click("GmAllLevels"); yield return Wait("level"); selector = FindObjectOfType<SokobanLevelSelectSceneController>();
        Check(Get<List<SokobanLevelDescriptor>>(selector, "descriptors").Count == SokobanLevelRepository.ListAll().Count && GameObject.Find("PreviewInfo").GetComponent<Text>().text.Contains("分类"), "GM all-level view exposes every category");
        gm.SetOpen(false); yield return null; Click("Level_L002"); Click("StartButton"); yield return Wait("game");
        Check(SokobanRuntimeContext.IsDebugPlay && GameObject.Find("EditorButton") == null, "GM selected gameplay is marked debug and hides editor entry");
        Solve(); Check(SokobanProgressStore.Current.Find(SokobanLevelRepository.LoadJson("L002")) == null && !SokobanProgressStore.Current.IsUnlocked("L002", ids), "GM browsing victory does not create normal progress");
        gm.SetOpen(true); Click("GmNext"); yield return Wait("game");
        Check(SokobanRuntimeContext.IsGmEnabled && gm.IsExpanded && gm.GetInstanceID() == instance && GameObject.Find("GmForceWin").GetComponent<Button>().IsInteractable(), "GM navigation keeps its dropdown and usable gameplay actions");
        yield return Capture("05-gm-game");
        Click("GmEditor"); yield return Wait("editor");
        Check(SokobanRuntimeContext.IsGmEnabled && gm.IsExpanded && GameObject.Find("EditorEmptyHint") != null && gm.GetInstanceID() == instance, "GM editor entry preserves the expanded dropdown across scene change");
        Check(!SokobanRuntimeContext.IsDebugPlay && !SokobanRuntimeContext.IsEditorPreview, "GM editor entry clears old gameplay context");
        yield return Capture("06-gm-editor");
        Click("GmUnlockAll"); Check(ids.All(id => SokobanProgressStore.Current.IsUnlocked(id, ids)), "GM unlock-all saves all campaign access");
        Check(new SokobanProgressStore(Path.Combine(folder, "progress.json")).IsUnlocked("L003", ids), "GM unlock-all survives reload");
        Click("GmPlayerLevels"); yield return Wait("level"); Click("Level_L002");
        Check(GameObject.Find("StartButton").GetComponent<Button>().IsInteractable(), "player selection refreshes newly unlocked access");
        Click("GmClearData"); Check(SokobanGmController.BlocksInput && GameObject.Find("GmClearBackdrop") != null, "clear confirmation blocks underlying input");
        yield return null; // Let the newly enabled backdrop render before checking its raycast depth.
        Check(Hit(GameObject.Find("Level_L001"))?.name == "GmClearBackdrop", "clear confirmation intercepts pointer events outside the GM panel");
        yield return Capture("03-clear-confirmation");
        Click("GmCancelClear"); Check(SokobanProgressStore.Current.IsUnlocked("L003", ids), "cancel clear preserves unlocks");
        Click("GmClearData"); Click("GmConfirmClear"); yield return Wait("level");
        Check(SokobanRuntimeContext.IsGmEnabled && SokobanProgressStore.Current.IsUnlocked("L001", ids) && !SokobanProgressStore.Current.IsUnlocked("L002", ids), "confirmed clear returns to first-level-only player selection with GM still open");
        gm.SetOpen(false); yield return null; Click("Level_L001"); Click("StartButton"); yield return Wait("game"); Solve();
        Check(SokobanProgressStore.Current.IsUnlocked("L002", ids) && !SokobanProgressStore.Current.IsUnlocked("L003", ids), "normal first victory unlocks exactly the next level");
        Check(GameObject.Find("Next").GetComponentInChildren<Text>().text == "下一关", "victory can continue to the newly unlocked level");
        gm.SetOpen(true); Click("Next"); yield return Wait("game"); Click("GmForceWin");
        Check(SokobanRuntimeContext.IsGmEnabled && SokobanProgressStore.Current.Find(SokobanLevelRepository.LoadJson("L002")) == null && !SokobanProgressStore.Current.IsUnlocked("L003", ids), "forced victory preserves GM opening state and does not unlock the next level");
        Click("WinBack"); yield return Wait("level"); gm.SetOpen(false); yield return null;
        Click("Level_L002"); Click("StartButton"); yield return Wait("game"); Solve();
        Check(SokobanProgressStore.Current.IsUnlocked("L003", ids), "normal second victory unlocks the final level");
        Click("Next"); yield return Wait("game"); Solve(); Click("WinBack"); yield return Wait("level");
        Check(new SokobanProgressStore(Path.Combine(folder, "progress.json")).Find(SokobanLevelRepository.LoadJson("L003"))?.completed == true, "full campaign scores and progression survive reload");
        yield return Capture("04-completed-campaign");
        gm.SetOpen(true); Click("GmClearData"); Click("GmConfirmClear"); yield return Wait("level");
        Check(SokobanProgressStore.Current.Find(SokobanLevelRepository.LoadJson("L001")) == null && !SokobanProgressStore.Current.IsUnlocked("L003", ids), "clear removes actual completed scores and relocks later levels");
    }
}
#endif
