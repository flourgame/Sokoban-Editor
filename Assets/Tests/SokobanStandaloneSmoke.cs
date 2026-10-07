#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>开发构建专用；只有显式传入 -sokobanSmokeDir 才执行，使用隔离的数据目录。</summary>
public sealed partial class SokobanStandaloneSmoke : MonoBehaviour
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private string folder;
    private int checks, errors;
    private readonly StringBuilder report = new StringBuilder();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Install()
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-sokobanSmokeDir");
        if (Application.isEditor || index < 0 || index + 1 >= args.Length) return;
        var runner = new GameObject("StandaloneSmoke").AddComponent<SokobanStandaloneSmoke>();
        runner.folder = Path.GetFullPath(args[index + 1]); DontDestroyOnLoad(runner.gameObject);
    }
    private IEnumerator Start()
    {
        Application.runInBackground = true; Directory.CreateDirectory(folder);
        SokobanLevelRepository.PlayerDataRootOverride = Path.Combine(folder, "user-data");
        typeof(SokobanProgressStore).GetField("current", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new SokobanProgressStore(Path.Combine(folder, "progress.json")));
        Application.logMessageReceived += OnLog;
        var flows = new Stack<IEnumerator>(); flows.Push(Flow());
        while (flows.Count > 0)
        {
            bool next = false; var flow = flows.Peek();
            try { next = flow.MoveNext(); }
            catch (Exception error) { report.AppendLine("FAIL: " + error); Finish(false); yield break; }
            if (!next) { flows.Pop(); continue; }
            if (flow.Current is IEnumerator nested) { flows.Push(nested); continue; }
            yield return flow.Current;
        }
        Finish(errors == 0);
    }
    private void OnLog(string condition, string stack, LogType type)
    { if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert) { errors++; report.AppendLine("ERROR: " + condition + "\n" + stack); } }
    private void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; report.AppendLine("PASS: " + message); }
    private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, Fields).GetValue(instance);
    private static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Fields).Invoke(instance, args);
    private static void Click(string name)
    {
        var go = GameObject.Find(name); if (go == null) throw new Exception("Missing button " + name);
        var button = go.GetComponent<Button>(); if (button == null || !button.IsInteractable()) throw new Exception("Inactive button " + name);
        button.onClick.Invoke();
    }
    private static void EnterEditor()
    {
        SokobanGmController.Current.SetOpen(true); Click("GmEditor"); SokobanGmController.Current.SetOpen(false);
    }
    private IEnumerator WaitScene(string name)
    {
        var deadline = Time.realtimeSinceStartup + 15f;
        do { yield return null; }
        while ((SceneManager.GetActiveScene().name != name || SokobanSceneTransition.IsRunning) && Time.realtimeSinceStartup < deadline);
        Check(SceneManager.GetActiveScene().name == name && !SokobanSceneTransition.IsRunning, "scene " + name);
        yield return new WaitForSecondsRealtime(0.25f);
    }
    private IEnumerator Layout(string page)
    {
        yield return new WaitForSecondsRealtime(0.3f);
        UnityEngine.Canvas.ForceUpdateCanvases();
        foreach (var label in UnityEngine.Object.FindObjectsOfType<Text>())
        {
            if (!label.isActiveAndEnabled || label.GetComponentInParent<InputField>() != null || label.GetComponentsInParent<Canvas>().Any(c => !c.enabled) || label.verticalOverflow != VerticalWrapMode.Truncate || string.IsNullOrEmpty(label.text)) continue;
            if (label.preferredHeight > label.rectTransform.rect.height + 2f)
                throw new Exception(page + ": text clipped: " + label.name + " preferred=" + label.preferredHeight + " height=" + label.rectTransform.rect.height);
        }
        CaptureUi(Path.Combine(folder, page + ".png"));
        yield return null;
    }
    // Hidden Windows players may skip presentation. Render UI to a texture so screenshots
    // still contain the actual canvas geometry rather than a black, unpresented back buffer.
    private static void CaptureUi(string destination)
    {
        var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c => c.enabled && c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var cameraObject = new GameObject("SmokeCaptureCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>(); camera.enabled = false;
        camera.orthographic = true; camera.orthographicSize = 5f; camera.nearClipPlane = 0.1f; camera.farClipPlane = 20f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = SokobanTheme.Background;
        var target = RenderTexture.GetTemporary(Screen.width, Screen.height, 24);
        camera.targetTexture = target; var previous = RenderTexture.active;
        var texture = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
        try
        {
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f; }
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); texture.Apply();
            File.WriteAllBytes(destination, texture.EncodeToPNG());
        }
        finally
        {
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
            Canvas.ForceUpdateCanvases(); RenderTexture.active = previous; camera.targetTexture = null;
            RenderTexture.ReleaseTemporary(target); UnityEngine.Object.Destroy(texture); UnityEngine.Object.Destroy(cameraObject);
        }
    }
    private void SolveGameplay()
    {
        var game = UnityEngine.Object.FindObjectOfType<SokobanGameplaySceneController>();
        Call(game, "ClosePause");
        var level = Get<SokobanLevelRuntime>(game, "level");
        SokobanSolverLevel snapshot; string error;
        if (!SokobanSolverAdapter.TryCreateSnapshot(level.Source, out snapshot, out error)) throw new Exception(error);
        var solved = SokobanSolver.Solve(snapshot, new SokobanSolveOptions { TimeoutMs = 5000 });
        if (!solved.IsSolved) throw new Exception("Campaign could not be solved");
        foreach (var move in solved.Moves) Call(game, "TryMove", SokobanSolutionPlayback.ParseDirection(move));
        Check(Get<SokobanState>(game, "state").IsWon, level.LevelId + " runtime complete path");
    }
    private IEnumerator Flow()
    {
        yield return new WaitForSecondsRealtime(0.5f);
        if (Environment.GetCommandLineArgs().Contains("-sokobanExchangeOnly"))
        { yield return ExchangeFlow(); yield break; }
        Check(SceneManager.GetActiveScene().name == "start", "entry scene is the main menu"); yield return Layout("01-menu");
        SokobanSettingsPanel.Show(); yield return Layout("00-settings"); Click("SettingsBack");
        Click("StartButton"); yield return WaitScene("level"); yield return Layout("02-levels");
        Click("Level_L001"); Click("StartButton"); yield return WaitScene("game");
        var game = UnityEngine.Object.FindObjectOfType<SokobanGameplaySceneController>(); Call(game, "ClosePause");
        yield return Layout("03-game");
        Click("PauseButton"); var pausedTime = Get<float>(game, "elapsedSeconds");
        Call(game, "TryMove", SokobanDirection.Right); Call(game, "UndoMove");
        yield return new WaitForSecondsRealtime(0.15f);
        Check(Get<SokobanState>(game, "state").MoveCount == 0 && Get<float>(game, "elapsedSeconds") == pausedTime, "pause blocks moves/undo and freezes the timer");
        Check(!GameObject.Find("UndoButton").GetComponent<Button>().IsInteractable(), "pause isolates underlying controls"); yield return Layout("04-pause");
        Click("PauseResume"); Call(game, "TryMove", SokobanDirection.Down); Call(game, "UndoMove");
        Check(Get<SokobanState>(game, "state").MoveCount == 0, "runtime undo restores state");
        Click("MapButton"); Call(game, "TryMove", SokobanDirection.Right);
        Check(Get<SokobanState>(game, "state").MoveCount == 0, "map blocks gameplay input"); yield return Layout("05-map"); Click("MapClose");
        SolveGameplay(); yield return Layout("06-win");
        Check(SokobanProgressStore.Current.Find(SokobanLevelRepository.LoadJson("L001")).bestMoves == 5, "normal victory persists best movement count");
        Click("Next"); yield return WaitScene("game");
        Check(SokobanRuntimeContext.SelectedLevelId == "L002", "next campaign level is L002"); SolveGameplay();
        Click("Next"); yield return WaitScene("game");
        Check(SokobanRuntimeContext.SelectedLevelId == "L003", "next campaign level is L003"); SolveGameplay();
        var campaignIds = SokobanCampaign.Ids();
        var atLastCampaignLevel = campaignIds.IndexOf(SokobanRuntimeContext.SelectedLevelId) == campaignIds.Count - 1;
        Check(GameObject.Find("Next").GetComponentInChildren<Text>().text == (atLastCampaignLevel ? "返回选关" : "下一关"),
            "next button reflects whether a further campaign level exists");
        Click("WinBack"); yield return WaitScene("level");
        Check(GameObject.Find("Level_L001").GetComponentInChildren<Text>().text.Contains("已完成"), "selection displays saved completion/best score");
        var invalid = SokobanLevelRepository.ListAll().FirstOrDefault(d => SokobanValidation.Validate(SokobanLevelRepository.LoadJson(d)).Count > 0);
        if (invalid != null)
        {
            SokobanGmController.Current.SetOpen(true); Click("GmAllLevels"); SokobanGmController.Current.SetOpen(false);
            yield return WaitScene("level");
            Click("Level_" + invalid.LevelId);
            Check(!GameObject.Find("StartButton").GetComponent<Button>().IsInteractable(), "invalid draft cannot enter ordinary gameplay");
            SokobanGmController.Current.SetOpen(true); Click("GmPlayerLevels"); SokobanGmController.Current.SetOpen(false);
            yield return WaitScene("level");
        }
        Click("Level_L002"); Click("StartButton"); yield return WaitScene("game");
        game = UnityEngine.Object.FindObjectOfType<SokobanGameplaySceneController>(); Call(game, "ClosePause");
        SokobanGmController.Current.SetOpen(true); Click("GmForceWin"); SokobanGmController.Current.SetOpen(false);
        Check(GameObject.Find("WinTitle").GetComponent<Text>().text == "GM 完成" && SokobanProgressStore.Current.Find(SokobanLevelRepository.LoadJson("L002")).bestMoves == 12, "GM result is marked and never replaces normal score");
        Click("WinBack"); yield return WaitScene("level");
        SokobanGmController.Current.SetOpen(true); yield return Layout("15-gm-menu"); SokobanGmController.Current.SetOpen(false);
        EnterEditor(); yield return WaitScene("editor");
        var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        Check(GameObject.Find("EditorEmptyHint") != null, "editor supports zero tabs"); yield return Layout("07-editor-empty");
        Click("EmptyAddLevel"); var tabs = Get<List<SokobanEditorTab>>(editor, "tabs"); var tab = tabs[0]; var id = tab.Data.levelId;
        tab.Data = SokobanLevelRepository.Parse(JsonUtility.ToJson(SokobanLevelRepository.LoadJson("L001"))); tab.Data.levelId = id;
        Call(editor, "SetActiveTab", 0);
        var field = Get<InputField>(editor, "nameField"); field.text = "跨页面保留的未保存关卡"; Call(editor, "CommitNameEdit", field.text);
        tab.AddUndo(JsonUtility.ToJson(tab.Data), 100);
        editor.GetType().GetField("zoom", Fields).SetValue(editor, 1.4f);
        editor.GetType().GetField("pan", Fields).SetValue(editor, new Vector2(32f, -18f));
        Click("Back"); yield return WaitScene("level"); EnterEditor(); yield return WaitScene("editor");
        editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>(); tabs = Get<List<SokobanEditorTab>>(editor, "tabs"); tab = tabs[0];
        Check(tabs.Count == 1 && tab.Dirty && tab.Data.name == "跨页面保留的未保存关卡" && tab.UndoStack.Count == 2 && Get<float>(editor, "zoom") == 1.4f, "scene roundtrip preserves dirty tabs, document, undo and view");
        Call(editor, "CloseTab", 0); Check(GameObject.Find("CloseTabDialog") != null, "dirty tab close asks for confirmation"); Click("CloseTabCancel");
        var quit = typeof(SokobanEditorSession).GetMethod("ConfirmQuit", BindingFlags.Static | BindingFlags.NonPublic);
        Check(!(bool)quit.Invoke(null, null) && SokobanEditorSession.QuitPending, "application exit protects unsaved documents"); yield return Layout("08-unsaved-quit"); Click("UnsavedQuitCancel");
        Click("Write"); Check(!tab.Dirty && File.Exists(tab.Descriptor.FilePath) && tab.Descriptor.FilePath.StartsWith(SokobanLevelRepository.LevelRoot, StringComparison.OrdinalIgnoreCase), "standalone editor saves in isolated writable user data");
        Check(SokobanLevelRepository.LoadJson(tab.Descriptor).verifiedMoves == -1, "unverified saved JSON has -1");
        Click("Play"); yield return WaitScene("game"); SolveGameplay();
        Check(GameObject.Find("WinStats").GetComponent<Text>().text.Contains("试玩") && SokobanProgressStore.Current.Find(tab.Data) == null, "editor preview is isolated from ordinary player progress");
        Click("Next"); yield return WaitScene("editor"); editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        Click("BatchValidate"); Call(editor, "SelectVerificationLevel", id, false, false); Click("VerificationStart");
        var verification = SokobanBatchVerificationService.Instance; var instance = verification.GetInstanceID();
        Click("VerificationClose"); Click("Back"); yield return WaitScene("level");
        var timeout = Time.realtimeSinceStartup + 10f;
        while (verification.Running && Time.realtimeSinceStartup < timeout) yield return null;
        Check(!verification.Running && verification.Completed == 1 && verification.Solved == 1 && verification.Unsaved == 0 && verification.Progress.Fraction == 1f && instance == SokobanBatchVerificationService.Instance.GetInstanceID(), "verification survives scene change and persists complete results");
        Check(SokobanLevelRepository.LoadJson(tab.Descriptor).verifiedMoves == 5, "standalone verification writes trusted cost");
        Check(GameObject.Find("VerificationCompletionToast") != null, "verification completion notice appears in the current scene");
        Click("VerificationCompletionView"); yield return WaitScene("editor"); yield return null; yield return Layout("09-verification");
        Click("VerificationCopy"); Check(GUIUtility.systemCopyBuffer.Contains("解关步数 5"), "verification report copies the completed batch");
        Click("VerificationClose"); editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        Click("BatchGenerate"); var fields = Get<Dictionary<string, InputField>>(editor, "generationFields");
        var values = new Dictionary<string, string> { { "Width", "6" }, { "Height", "6" }, { "Boxes", "1" }, { "MinPushes", "1" }, { "MaxPushes", "15" }, { "Walls", "0" }, { "Seed", "42" }, { "Budget", "5" }, { "Candidates", "100" }, { "Count", "2" } };
        foreach (var value in values) fields[value.Key].text = value.Value;
        editor.GetType().GetField("generationDifficultyValue", Fields).SetValue(editor, 1); Call(editor, "RefreshGenerationDifficulty");
        Click("GenerationStart"); Click("GenerationClose"); Click("Back"); yield return WaitScene("level");
        var generation = SokobanBatchGenerationService.Instance; timeout = Time.realtimeSinceStartup + 15f;
        while (generation.Running && Time.realtimeSinceStartup < timeout) yield return null;
        Check(!generation.Running && generation.Succeeded == 2 && generation.Progress.Fraction == 1f, "batch generation completes outside editor");
        Click("BatchCompletionView"); yield return WaitScene("editor"); yield return null; yield return Layout("10-generation");
        Click("BatchSaveAll"); Check(generation.Entries.All(e => e.Saved && SokobanLevelRepository.LoadJson(e.Level.levelId)?.verifiedMoves >= 0), "standalone generated levels save and are discoverable");
        Click("GenerationClose"); Click("AddCategory"); yield return null; yield return Layout("11-management");
        Click("ManagerClose"); Click("OpenLog"); yield return null; yield return Layout("12-log"); Click("OperationLogCopy");
        Check(GUIUtility.systemCopyBuffer.Contains("开始批量验证") && GUIUtility.systemCopyBuffer.Contains("开始批量生成"), "operation log includes both workflows"); Click("OperationLogClose");
        var store = SokobanLevelRepository.Library; var all = SokobanLevelRepository.ListAll();
        store.CreateCategory("自动验收"); store.Migrate(all, new[] { id }, "自动验收");
        Check(SokobanLevelRepository.ListAll().First(d => d.LevelId == id).Category == "自动验收", "standalone category migration persists");
        all = SokobanLevelRepository.ListAll(); store.Delete(all, new[] { id });
        Check(!SokobanLevelRepository.ListAll().Any(d => d.LevelId == id), "standalone deletion persists and removes the saved level");
        editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        Call(editor, "CloseTab", 0); yield return null;
        yield return StampFlow();
        yield return ManualFlow();
        yield return AnimationSettingsFlow();
        yield return ExchangeFlow();
    }
    private void Finish(bool success)
    {
        restoreAnimationSettings?.Invoke();
        restoreAnimationSettings = null;
        Application.logMessageReceived -= OnLog;
        report.AppendLine($"RESULT: {(success ? "PASS" : "FAIL")}; assertions={checks}; runtimeErrors={errors}; screen={Screen.width}x{Screen.height}; Unity={Application.unityVersion}");
        File.WriteAllText(Path.Combine(folder, "result.txt"), report.ToString());
        Debug.Log(report.ToString()); Application.Quit(success ? 0 : 1);
    }
}
#endif
