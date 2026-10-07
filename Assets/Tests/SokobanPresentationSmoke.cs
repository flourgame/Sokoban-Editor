#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Kuluobishi.Sokoban;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Play-mode regressions for coordinate ownership and both transition halves.</summary>
public sealed class SokobanPresentationSmoke : MonoBehaviour
{
    private readonly List<string> report = new List<string>();
    private readonly List<string> samples = new List<string>();
    private string folder;
    private GameObject fixtureHost;

    [MenuItem("Sokoban/Run Presentation Checks (Play Mode)")]
    public static void RunMenu()
    {
        if (!EditorApplication.isPlaying) { Debug.LogError("Presentation checks require Play Mode."); return; }
        if (FindObjectOfType<SokobanPresentationSmoke>() != null) return;
        var host = new GameObject("SokobanPresentationChecks");
        DontDestroyOnLoad(host);
        host.AddComponent<SokobanPresentationSmoke>();
    }

    private void Start()
    {
        folder = Path.GetFullPath("docs/verification/presentation");
        Directory.CreateDirectory(folder);
        StartCoroutine(Guard(Run()));
    }

    private IEnumerator Guard(IEnumerator routine)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(routine);
        while (stack.Count > 0)
        {
            object next;
            try
            {
                var current = stack.Peek();
                if (!current.MoveNext()) { stack.Pop(); continue; }
                next = current.Current;
            }
            catch (Exception error)
            {
                report.Add("FAIL: " + error);
                File.WriteAllLines(Path.Combine(folder, "result.txt"), report);
                Debug.LogException(error);
                if (fixtureHost != null) Destroy(fixtureHost);
                Destroy(gameObject);
                yield break;
            }
            // Drive nested iterators through this guard so their failures are recorded.
            var nested = next as IEnumerator;
            if (nested != null) stack.Push(nested);
            else yield return next;
        }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Presentation regression: " + message); }

    private static Vector2 Center(SokobanBoardView view, SokobanLevelRuntime level, SokobanGridPoint point)
    { return new Vector2((point.x + 0.5f - level.Width * 0.5f) * view.CellStride, (level.Height * 0.5f - point.y - 0.5f) * view.CellStride); }

    private IEnumerator Run()
    {
        yield return BoardRegression();
        while (SokobanSceneTransition.IsRunning) yield return null;
        SokobanSceneTransition.Load("start");
        yield return ObserveTransition("start", "initial-menu");
        CrtRegression();
        Capture("menu");
        yield return Click("StartButton");
        yield return ObserveTransition("level", "menu-to-level");
        var rows = FindObjectsOfType<Button>().Where(b => b.name.StartsWith("Level_")).OrderBy(b => b.name).ToArray();
        Require(rows.Length >= 3, "level selection contains the campaign rows");
        var positions = rows.Select(b => ((RectTransform)b.transform).anchoredPosition).ToArray();
        var eventSystem = EventSystem.current;
        eventSystem.enabled = false; // Prevent the physical cursor cancelling injected hover events.
        for (var i = 0; i < rows.Length; i++)
        {
            ExecuteEvents.Execute(rows[i].gameObject, Pointer(), ExecuteEvents.pointerEnterHandler);
            yield return new WaitForSecondsRealtime(0.22f);
            Require(Vector2.Distance(((RectTransform)rows[i].transform).anchoredPosition, positions[i]) < 0.01f, "hover never changes the layout-owned row position");
            var visual = rows[i].targetGraphic.rectTransform;
            Require(visual != rows[i].transform && visual.anchoredPosition.y > 3f, "row artwork lifts independently of its layout slot");
            ExecuteEvents.Execute(rows[i].gameObject, Pointer(), ExecuteEvents.pointerExitHandler);
        }
        eventSystem.enabled = true;
        report.Add("PASS: all level rows hover without moving, overlapping or losing their lift feedback.");
        yield return Click(rows.First(b => b.name == "Level_L001").name);
        Capture("level-selection");
        yield return Click("StartButton");
        yield return ObserveTransition("game", "level-to-game");
        Capture("game-initial");
        var controller = FindObjectOfType<SokobanGameplaySceneController>();
        controller.OnRestartClicked();
        var tryMove = controller.GetType().GetMethod("TryMove", BindingFlags.Instance | BindingFlags.NonPublic);
        var stateField = controller.GetType().GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        var levelField = controller.GetType().GetField("level", BindingFlags.Instance | BindingFlags.NonPublic);
        var expectedMoves = 0;
        foreach (var direction in new[] { SokobanDirection.Down, SokobanDirection.Down, SokobanDirection.Right, SokobanDirection.Right })
        {
            var boardBeforeMove = FindObjectsOfType<SokobanBoardView>().First(v => v.name == "GameViewport");
            var playerBeforeMove = (RectTransform)boardBeforeMove.Board.Find("Entities/Player");
            var dustOrigin = playerBeforeMove.anchoredPosition;
            tryMove.Invoke(controller, new object[] { direction });
            var state = (SokobanState)stateField.GetValue(controller);
            Require(state.MoveCount == ++expectedMoves, "actual gameplay accepts the intended movement");
            var level = (SokobanLevelRuntime)levelField.GetValue(controller);
            var board = FindObjectsOfType<SokobanBoardView>().First(v => v.name == "GameViewport");
            var root = (RectTransform)board.Board.Find("Entities/Player");
            var dust = board.Board.Find("Entities").GetComponentsInChildren<Image>().Where(i => i.name == "VfxDot").ToArray();
            if (expectedMoves == 1 && PlayerPrefs.GetInt("Sokoban.LowEffects", 0) == 0)
            {
                Require(dust.Length == 3, "walking creates three dust particles");
                foreach (var particle in dust)
                {
                    var offset = particle.rectTransform.anchoredPosition - dustOrigin;
                    Require(Mathf.Abs(particle.color.r - particle.color.g) < 0.001f && Mathf.Abs(particle.color.g - particle.color.b) < 0.001f, "foot dust is neutral grey");
                    Require(offset.y < -board.CellStride * 0.20f && offset.y > -board.CellStride * 0.35f, "dust spawns below the body, at the feet");
                    Require(particle.transform.GetSiblingIndex() < root.GetSiblingIndex(), "dust is drawn behind the character");
                }
                report.Add("PASS: walking dust is grey, at the feet, behind the body.");
            }
            var moveBegan = Time.realtimeSinceStartup;
            var dustCaptured = false;
            while (Time.realtimeSinceStartup - moveBegan < 0.32f)
            {
                yield return null;
                if (expectedMoves == 1 && !dustCaptured && Time.realtimeSinceStartup - moveBegan > 0.04f)
                { Capture("foot-dust"); dustCaptured = true; }
                var body = root.GetComponent<SokobanEntityView>().Body.rectTransform;
                Require(body != root && body.anchoredPosition.magnitude < 3f, "actual player jiggle remains local to the body");
            }
            Require(Vector2.Distance(root.anchoredPosition, Center(board, level, state.Player)) < 0.01f, "actual player settles on the simulation cell");
            foreach (var box in board.Board.Find("Entities").GetComponentsInChildren<SokobanEntityView>())
                if (!box.IsPlayer)
                    Require(state.Boxes.Any(p => Vector2.Distance(((RectTransform)box.transform).anchoredPosition, Center(board, level, p)) < 0.01f), "actual pushed box settles on a simulation cell");
        }
        Capture("game-after-push");
        yield return null;
        yield return null;
        controller.OnUndoClicked();
        yield return new WaitForSecondsRealtime(0.3f);
        controller.OnRestartClicked();
        yield return new WaitForSecondsRealtime(0.3f);
        report.Add("PASS: actual gameplay walk, push, undo and restart keep sprites on simulation cells.");
        controller.OnPauseClicked();
        yield return new WaitForSecondsRealtime(0.5f);
        Capture("pause");
        yield return Click("PauseResume");
        controller.OnMapClicked();
        yield return new WaitForSecondsRealtime(0.5f);
        Capture("map");
        yield return null;
        yield return null;
        controller.OnMapCloseClicked();
        SokobanSettingsPanel.Show();
        yield return new WaitForSecondsRealtime(0.5f);
        Require(FindObjectsOfType<SokobanCrtOverlay>().Length == 1, "settings share the scene CRT pass instead of double-processing the background");
        report.Add("PASS: settings and scene share one CRT pass.");
        Capture("settings");
        yield return null;
        yield return null;
        FindObjectOfType<SokobanSettingsPanel>().Close();
        yield return Click("LevelButton");
        yield return ObserveTransition("level", "game-to-level");
        File.WriteAllLines(Path.Combine(folder, "transition-samples.csv"), samples);
        File.WriteAllLines(Path.Combine(folder, "result.txt"), report);
        Debug.Log("SOKOBAN_PRESENTATION: " + string.Join("\n", report));
        Destroy(gameObject);
    }

    private void CrtRegression()
    {
        var overlays = FindObjectsOfType<SokobanCrtOverlay>();
        Require(overlays.Length == 1, "one CRT pass in the scene");
        var crt = overlays[0];
        var image = crt.GetComponent<Image>();
        Require(!image.raycastTarget && image.canvas.sortingOrder == 4000, "CRT is above popups and below the scene-transition mask without blocking input");
        Require(image.material.shader.isSupported && !ShaderUtil.ShaderHasError(image.material.shader), "CRT shader compiles on the active GPU");
        Require(EventSystem.current.GetComponent<StandaloneInputModule>().inputOverride is SokobanCrtInput, "pointer module uses CRT coordinate correction");
        var originalIntensity = PlayerPrefs.GetFloat("Sokoban.CRTIntensity", 0.22f);
        var lowKeyExisted = PlayerPrefs.HasKey("Sokoban.LowEffects");
        var originalLow = PlayerPrefs.GetInt("Sokoban.LowEffects", 0);
        try
        {
            crt.SetIntensity(0f);
            var cornerPoint = new Vector2(Screen.width * 0.08f, Screen.height * 0.08f);
            Require(!image.enabled && SokobanCrtOverlay.ScreenToSource(cornerPoint) == cornerPoint, "zero intensity restores an unwarped image and input");
            PlayerPrefs.SetInt("Sokoban.LowEffects", 0);
            crt.SetIntensity(0.22f);
            Require(Mathf.Abs(Screen.height / image.material.GetFloat("_Scanlines") - 6f) < 0.01f, "scanlines keep a six-pixel interval");
            Require(Vector2.Distance(SokobanCrtOverlay.ScreenToSource(cornerPoint), cornerPoint) > 0.5f, "curved UI still corrects pointer position at the screen edge");

            // Locate the rendered button by numerically inverting the image mapping,
            // then use the actual scene raycasters on the corrected pointer.
            var button = FindObjectsOfType<Button>().First(b => b.name == "StartButton");
            var rect = (RectTransform)button.transform;
            var source = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var visible = source;
            for (var i = 0; i < 20; i++) visible += (source - SokobanCrtOverlay.ScreenToSource(visible)) * 0.8f;
            var pointer = new PointerEventData(EventSystem.current) { position = SokobanCrtOverlay.ScreenToSource(visible) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Require(hits.Count > 0 && hits[0].gameObject.GetComponentInParent<Button>() == button, "visible curved button maps to its actual click target");
            PlayerPrefs.SetInt("Sokoban.LowEffects", 1);
            crt.SetIntensity(0.22f);
            Require(image.material.GetFloat("_Chromatic") == 0f && image.material.GetFloat("_NoiseAmount") == 0f && image.material.GetFloat("_Curvature") < 0.0075f, "low effects retains reduced curvature and skips noise/chromatic samples");
        }
        finally
        {
            if (lowKeyExisted) PlayerPrefs.SetInt("Sokoban.LowEffects", originalLow);
            else PlayerPrefs.DeleteKey("Sokoban.LowEffects");
            crt.SetIntensity(originalIntensity);
        }
        report.Add("PASS: full-screen CRT, shader support, curved pointer raycast, zero strength and low-effects settings.");
    }

    private IEnumerator BoardRegression()
    {
        fixtureHost = new GameObject("PresentationFixture", typeof(RectTransform));
        var parent = fixtureHost.GetComponent<RectTransform>(); parent.sizeDelta = new Vector2(1000f, 700f);
        var data = new SokobanJsonLevel { levelId = "PresentationOnly", name = "Regression", size = new SokobanJsonSize { width = 9, height = 5 },
            terrain = new[] { "#########", "#.......#", "#.......#", "#.......#", "#########" },
            player = new SokobanJsonPoint(2, 1), boxes = new[] { new SokobanJsonPoint(4, 2), new SokobanJsonPoint(3, 1) },
            goals = new[] { new SokobanJsonPoint(4, 1), new SokobanJsonPoint(7, 3) } };
        var level = new SokobanLevelRuntime(data); var state = level.CreateInitialState();
        var view = SokobanBoardView.Create(parent, "RegressionBoard", level, state);
        var player = (RectTransform)view.Board.Find("Entities/Player");
        var stationary = (RectTransform)view.Board.Find("Entities/Box_4_2");
        var pushed = (RectTransform)view.Board.Find("Entities/Box_3_1");
        var origin = player.anchoredPosition; var stationaryPosition = stationary.anchoredPosition;
        Require(SokobanSimulation.TryMove(level, state, SokobanDirection.Right).Accepted, "push fixture move is accepted");
        state.Boxes.Clear(); state.Boxes.Add(new SokobanGridPoint(4, 1)); state.Boxes.Add(new SokobanGridPoint(4, 2));
        view.SetState(state, false, SokobanDirection.Right);
        var goalCenter = Center(view, level, new SokobanGridPoint(4, 1));
        var burst = new List<RectTransform>();
        foreach (Transform child in view.Board.Find("Entities"))
            if (child.name == "VfxDot" && Vector2.Distance(((RectTransform)child).anchoredPosition, goalCenter) < 0.01f)
                burst.Add((RectTransform)child);
        var peakBurstRadius = 0f;
        Require(Vector2.Distance(player.anchoredPosition, origin) < 0.01f, "a move starts at its rendered position");
        var previousX = origin.x;
        var moveBegan = Time.realtimeSinceStartup;
        var refreshChecked = false;
        while (Time.realtimeSinceStartup - moveBegan < 0.32f)
        {
            yield return null;
            Require(player.anchoredPosition.x >= previousX - 0.01f, "player never snaps back to its initial cell");
            Require(Vector2.Distance(stationary.anchoredPosition, stationaryPosition) < 0.01f, "stationary box identity survives target enumeration order");
            Require(Mathf.Abs(pushed.anchoredPosition.y - Center(view, level, new SokobanGridPoint(3, 1)).y) < 0.01f, "pushed box does not trade places with its neighbour");
            previousX = player.anchoredPosition.x;
            foreach (var dot in burst) if (dot != null) peakBurstRadius = Mathf.Max(peakBurstRadius, Vector2.Distance(dot.anchoredPosition, goalCenter));
            if (!refreshChecked)
            {
                refreshChecked = true;
                var before = player.anchoredPosition;
                view.SetState(state);
                Require(Vector2.Distance(player.anchoredPosition, before) < 0.01f, "same-state refresh does not restart movement");
            }
        }
        if (PlayerPrefs.GetInt("Sokoban.LowEffects", 0) == 0)
        {
            Require(burst.Count == 6 && peakBurstRadius > 20f && peakBurstRadius <= 54.1f, "goal particles expand at three times the old radius");
            report.Add("PASS: goal burst expands beyond the old 18-unit radius, within the new 24-54 envelope.");
        }
        Require(Vector2.Distance(player.anchoredPosition, Center(view, level, state.Player)) < 0.01f, "player ends on target cell");
        var beforeRapid = state.Clone();
        SokobanSimulation.TryMove(level, state, SokobanDirection.Down);
        view.SetState(state, false, SokobanDirection.Down);
        yield return null;
        var midway = player.anchoredPosition;
        view.SetState(beforeRapid, false);
        Require(Vector2.Distance(midway, player.anchoredPosition) < 0.01f, "mid-animation undo is position-continuous");
        yield return new WaitForSecondsRealtime(0.3f);
        state = level.CreateInitialState(); view.SetState(state, true);
        yield return new WaitForSecondsRealtime(0.3f);
        Require(Vector2.Distance(player.anchoredPosition, Center(view, level, state.Player)) < 0.01f, "restart cancels old movement");
        Require(player.GetComponent<SokobanEntityView>().Body.rectTransform.anchoredPosition == Vector2.zero, "restart cancels body offset");
        Require(player.GetComponent<SokobanEntityView>().Body.transform != player, "body animation has a separate transform");
        SokobanSimulation.TryMove(level, state, SokobanDirection.Right); view.SetState(state, false, SokobanDirection.Right);
        parent.sizeDelta = new Vector2(500f, 320f);
        yield return new WaitForSecondsRealtime(0.3f);
        Require(Vector2.Distance(player.anchoredPosition, Center(view, level, state.Player)) < 0.01f, "resizing during movement preserves the final cell");
        report.Add("PASS: frame-by-frame movement, idle refresh, box identity, interrupted undo, restart and resize.");
        Destroy(fixtureHost); fixtureHost = null;
    }

    private IEnumerator ObserveTransition(string expectedScene, string label)
    {
        var began = Time.realtimeSinceStartup;
        var newSceneSamples = 0; var openingMin = float.MaxValue; var openingMax = float.MinValue;
        var closeCapture = false; var openCapture = false;
        do
        {
            var transition = FindObjectOfType<SokobanSceneTransition>();
            var mask = transition != null ? transition.GetComponentInChildren<Image>() : null;
            var radius = mask != null ? mask.material.GetFloat("_Radius") : -999f;
            var scene = SceneManager.GetActiveScene().name;
            samples.Add(label + "," + scene + "," + Time.frameCount + "," + radius.ToString("F5", System.Globalization.CultureInfo.InvariantCulture));
            if (scene == expectedScene && SokobanSceneTransition.IsRunning)
            {
                newSceneSamples++; openingMin = Mathf.Min(openingMin, radius); openingMax = Mathf.Max(openingMax, radius);
                if (!openCapture && radius > 0.15f && radius < 0.65f) { Capture(label + "-enter"); openCapture = true; }
            }
            else if (!closeCapture && radius > 0.15f && radius < 0.65f)
            { Capture(label + "-leave"); closeCapture = true; }
            Require(Time.realtimeSinceStartup - began < 15f, "transition finishes without waiting for editor focus");
            yield return null;
        } while (SokobanSceneTransition.IsRunning || SceneManager.GetActiveScene().name != expectedScene);
        Require(newSceneSamples >= 3 && openingMin < 0.1f && openingMax > 0.7f, "new scene has a visible black-to-open entry animation: " + label);
        report.Add("PASS: " + label + " entry frames=" + newSceneSamples + "; radius " + openingMin.ToString("F2") + " -> " + openingMax.ToString("F2"));
        yield return new WaitForSecondsRealtime(0.15f);
    }

    private static PointerEventData Pointer() => new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };

    private IEnumerator Click(string name)
    {
        var button = FindObjectsOfType<Button>().FirstOrDefault(b => b.name == name && b.gameObject.activeInHierarchy);
        Require(button != null && button.IsInteractable(), "button can be clicked: " + name);
        var pointer = Pointer();
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerEnterHandler);
        yield return new WaitForSecondsRealtime(0.15f);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerDownHandler);
        yield return null;
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerUpHandler);
        ExecuteEvents.Execute(button.gameObject, pointer, ExecuteEvents.pointerClickHandler);
    }

    private void Capture(string name) { ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + ".png")); }
}
#endif
