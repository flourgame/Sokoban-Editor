#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Kuluobishi.Sokoban;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Exercise the real settings UI and interruption paths; restore user preferences even on a failed check.</summary>
public sealed partial class SokobanStandaloneSmoke
{
    private Action restoreAnimationSettings;

    private IEnumerator AnimationSettingsFlow()
    {
        var hadPreference = PlayerPrefs.HasKey(SokobanAnimationSettings.PreferenceKey);
        var previousPreference = PlayerPrefs.GetInt(SokobanAnimationSettings.PreferenceKey, 1);
        var hadLowEffects = PlayerPrefs.HasKey("Sokoban.LowEffects");
        var previousLowEffects = PlayerPrefs.GetInt("Sokoban.LowEffects", 0);
        GameObject fixture = null;
        restoreAnimationSettings = () =>
        {
            if (fixture != null) Destroy(fixture);
            SokobanAnimationSettings.SetEnabled(previousPreference != 0);
            if (!hadPreference) PlayerPrefs.DeleteKey(SokobanAnimationSettings.PreferenceKey);
            if (hadLowEffects) PlayerPrefs.SetInt("Sokoban.LowEffects", previousLowEffects);
            else PlayerPrefs.DeleteKey("Sokoban.LowEffects");
            PlayerPrefs.Save();
            SokobanCrtOverlay.RefreshAll();
        };
        PlayerPrefs.SetInt("Sokoban.LowEffects", 0);
        SokobanAnimationSettings.SetEnabled(true);
        SokobanSceneTransition.Load("start");
        yield return WaitScene("start");

        // A large real board exercises camera follow, pushed-box matching and
        // interruption before the first interpolation frame is presented.
        fixture = new GameObject("AnimationRegression", typeof(RectTransform));
        var parent = fixture.GetComponent<RectTransform>(); parent.sizeDelta = new Vector2(540f, 360f);
        var data = new SokobanJsonLevel
        {
            levelId = "AnimationRegression", name = "AnimationRegression",
            size = new SokobanJsonSize { width = 20, height = 5 },
            terrain = new[] { "####################", "#..................#", "#..................#", "#..................#", "####################" },
            player = new SokobanJsonPoint(8, 2), boxes = new[] { new SokobanJsonPoint(9, 2) }, goals = new[] { new SokobanJsonPoint(15, 2) }
        };
        var level = new SokobanLevelRuntime(data); var state = level.CreateInitialState();
        var board = SokobanBoardView.Create(parent, "AnimationBoard", level, state, true, 6, 4);
        var player = board.Board.Find("Entities/Player").GetComponent<RectTransform>();
        var box = board.Board.Find("Entities/Box_9_2").GetComponent<RectTransform>();
        var boxView = box.GetComponent<SokobanEntityView>();
        var beforeCamera = board.Board.anchoredPosition;
        SokobanSimulation.TryMove(level, state, SokobanDirection.Right);
        board.SetState(state, false, SokobanDirection.Right);
        Check(player.anchoredPosition != AnimationCellCenter(board, level, state.Player) &&
            box.anchoredPosition != AnimationCellCenter(board, level, new SokobanGridPoint(10, 2)), "animations on interpolates player and pushed box");
        Check(board.CameraTarget != beforeCamera && board.Board.anchoredPosition == beforeCamera, "animations on smoothly follows instead of snapping the camera");
        Check(board.Board.GetComponentsInChildren<Transform>().Any(t => t.name == "VfxDot"), "animations on emits movement particles");

        SokobanSettingsPanel.Show();
        GameObject.Find("AnimationToggle").GetComponent<Toggle>().isOn = false;
        Check(!SokobanAnimationSettings.Enabled && !GameObject.Find("AnimationToggle").GetComponent<Toggle>().graphic.enabled &&
            PlayerPrefs.GetInt(SokobanAnimationSettings.PreferenceKey, -1) == 0,
            "real animation toggle updates the shared preference and saves it");
        Check(player.anchoredPosition == AnimationCellCenter(board, level, state.Player) &&
            box.anchoredPosition == AnimationCellCenter(board, level, new SokobanGridPoint(10, 2)) &&
            board.Board.anchoredPosition == board.CameraTarget, "disabling during movement immediately completes player, box and camera motion");
        Check(boxView.Body.rectTransform.localScale == Vector3.one &&
            !board.Board.GetComponentsInChildren<Transform>().Any(t => t.name == "VfxDot"), "disabling clears active squash and particles");
        var popup = GameObject.Find("SettingsDialog").GetComponent<SokobanPopupMotion>();
        Check(popup.GetComponent<CanvasGroup>().alpha == 1f && popup.GetComponent<RectTransform>().anchoredPosition == Get<Vector2>(popup, "restPosition"),
            "disabling during popup entry leaves a fully visible usable dialog");

        var clock = SokobanAnimationSettings.VisualTime;
        var shaderClock = Shader.GetGlobalFloat("_SokobanAnimationTime");
        var logo = FindObjectOfType<SokobanLogoMotion>().transform.Find("LogoVisual");
        var logoPosition = logo.localPosition; var logoRotation = logo.localRotation;
        yield return new WaitForSecondsRealtime(0.15f);
        Check(clock == SokobanAnimationSettings.VisualTime && shaderClock == Shader.GetGlobalFloat("_SokobanAnimationTime"), "animations off freezes background, panel and CRT visual time");
        Check(logo.localPosition == logoPosition && logo.localRotation == logoRotation, "animations off keeps the logo stationary");
        Check(Time.timeScale == 1f, "animation setting does not pause gameplay time");

        var button = GameObject.Find("SettingsBack").GetComponent<SokobanButtonFeedback>();
        button.OnPointerEnter(new PointerEventData(EventSystem.current));
        button.OnPointerDown(new PointerEventData(EventSystem.current));
        yield return null;
        var buttonVisual = Get<RectTransform>(button, "rect");
        Check(buttonVisual.anchoredPosition == Get<Vector2>(button, "restPosition") && buttonVisual.localScale == Get<Vector3>(button, "restScale"),
            "animations off preserves button input without lift or press motion");
        button.OnPointerUp(new PointerEventData(EventSystem.current));
        Click("SettingsBack");

        for (var i = 0; i < 5; i++)
        {
            SokobanSimulation.TryMove(level, state, SokobanDirection.Right);
            board.SetState(state, false, SokobanDirection.Right);
        }
        Check(state.IsWon && player.anchoredPosition == AnimationCellCenter(board, level, state.Player) &&
            box.anchoredPosition == AnimationCellCenter(board, level, new SokobanGridPoint(15, 2)) && board.Board.anchoredPosition == board.CameraTarget,
            "animations off still pushes, wins and keeps all visuals at their final grid positions");
        Check(boxView.Body.sprite == Get<Sprite>(boxView, "boxOnGoalSprite") && !board.Board.GetComponentsInChildren<Transform>().Any(t => t.name == "VfxDot"),
            "animations off retains the on-goal appearance without delayed bursts");

        // Drop only the preference cache, preserving listeners, to exercise the
        // same persisted read path used at application startup.
        typeof(SokobanAnimationSettings).GetField("enabledPreference", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, null);
        Check(!SokobanAnimationSettings.Enabled, "animation preference reloads as off from PlayerPrefs");
        SokobanSettingsPanel.Show();
        Check(!GameObject.Find("AnimationToggle").GetComponent<Toggle>().isOn && !GameObject.Find("AnimationToggle").GetComponent<Toggle>().graphic.enabled,
            "reopened settings reflects the saved animation preference");
        CaptureUi(System.IO.Path.Combine(folder, "32-animations-off.png"));
        Click("SettingsBack");
        Destroy(fixture); fixture = null;

        SokobanSceneTransition.Load("level");
        Check(!SokobanSceneTransition.IsRunning, "animations off skips the scene iris");
        yield return WaitScene("level");
        Check(!SokobanAnimationSettings.Enabled && !GameObject.Find("CircleMask").GetComponent<Image>().enabled,
            "animation preference survives scene changes without a blocking mask");
        SokobanSettingsPanel.Show();
        GameObject.Find("AnimationToggle").GetComponent<Toggle>().isOn = true;
        Click("SettingsBack");
        var resumedClock = SokobanAnimationSettings.VisualTime;
        yield return new WaitForSecondsRealtime(0.1f);
        Check(SokobanAnimationSettings.Enabled && SokobanAnimationSettings.VisualTime > resumedClock &&
            PlayerPrefs.GetInt(SokobanAnimationSettings.PreferenceKey, -1) == 1, "turning animation back on resumes visual time and saves the enabled state");
        SokobanSceneTransition.Load("start");
        Check(SokobanSceneTransition.IsRunning, "turning animation back on restores scene transitions");
        SokobanAnimationSettings.SetEnabled(false);
        Check(!GameObject.Find("CircleMask").GetComponent<Image>().enabled, "disabling during a transition removes its mask immediately");
        yield return WaitScene("start");
        Check(!SokobanSceneTransition.IsRunning, "interrupted transition still finishes loading and releases input");
    }

    private static Vector2 AnimationCellCenter(SokobanBoardView view, SokobanLevelRuntime level, SokobanGridPoint point)
    {
        return new Vector2((point.x + 0.5f - level.Width * 0.5f) * view.CellStride,
            (level.Height * 0.5f - point.y - 0.5f) * view.CellStride);
    }
}
#endif
