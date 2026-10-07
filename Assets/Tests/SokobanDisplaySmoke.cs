#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using Kuluobishi.Sokoban.Editor;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class SokobanStandaloneSmoke
{
    private IEnumerator WaitDisplay(int width, int height, FullScreenMode mode)
    {
        var end = Time.realtimeSinceStartup + 8f;
        var stable = -1f;
        while (Time.realtimeSinceStartup < end)
        {
            yield return null;
            if (SokobanDisplaySettings.IsSwitching || Screen.width != width || Screen.height != height || Screen.fullScreenMode != mode) stable = -1f;
            else
            {
                if (stable < 0f) stable = Time.realtimeSinceStartup;
                if (Time.realtimeSinceStartup - stable > 0.8f) break;
            }
        }
        Check(!SokobanDisplaySettings.IsSwitching && Screen.width == width && Screen.height == height && Screen.fullScreenMode == mode,
            $"display applied {width}x{height} {mode}; actual={Screen.width}x{Screen.height} {Screen.fullScreenMode}");
    }

    private IEnumerator NativeResize(int width, int height, string stage)
    {
        File.WriteAllText(Path.Combine(folder, "display-stage.txt"), width + "," + height + "," + stage);
        yield return WaitDisplay(Math.Max(SokobanDisplaySettings.MinimumWidth, width), Math.Max(SokobanDisplaySettings.MinimumHeight, height), FullScreenMode.Windowed);
    }

    private void DisplayRectFits(string name)
    {
        var rect = GameObject.Find(name).GetComponent<RectTransform>();
        var corners = new Vector3[4]; rect.GetWorldCorners(corners);
        Check(corners.All(p => p.x >= -2f && p.x <= Screen.width + 2f && p.y >= -2f && p.y <= Screen.height + 2f),
            name + " stays inside the resized viewport");
    }

    private IEnumerator DisplayFlow()
    {
        var args = Environment.GetCommandLineArgs(); var index = Array.IndexOf(args, "-sokobanDisplayPhase");
        var phase = index >= 0 ? args[index + 1] : "1";
        Check(SokobanDisplaySettings.Supported, "Windows display settings are available");
        yield return new WaitForSecondsRealtime(0.5f);
        if (phase == "2")
        {
            var desktop = Screen.currentResolution;
            yield return WaitDisplay(desktop.width, desktop.height, FullScreenMode.FullScreenWindow);
            Check(SokobanDisplaySettings.Borderless && SokobanDisplaySettings.WindowSize == new Vector2Int(1600, 900),
                "restart restores borderless mode and the separate window size");
            SokobanSettingsPanel.Show(); Click("DisplayWindowed");
            yield return WaitDisplay(1600, 900, FullScreenMode.Windowed);
            yield break;
        }
        if (phase == "3")
        {
            yield return WaitDisplay(1600, 900, FullScreenMode.Windowed);
            Check(!SokobanDisplaySettings.Borderless && PlayerPrefs.GetInt(SokobanDisplaySettings.ModeKey, -1) == 0,
                "restart restores windowed mode and the saved client size");
            yield break;
        }
        yield return WaitDisplay(1280, 720, FullScreenMode.Windowed);
        Check(!SokobanDisplaySettings.Borderless, "first launch defaults to a 1280x720 window");
        SokobanSettingsPanel.Show(); yield return Layout("00-settings"); DisplayRectFits("SettingsDialog");
        Check(GameObject.Find("DisplayModeHint") == null, "the settings panel has no display hint text");
        Click("DisplayBorderless"); var resolution = Screen.currentResolution;
        yield return WaitDisplay(resolution.width, resolution.height, FullScreenMode.FullScreenWindow);
        Check(SokobanDisplaySettings.WindowSize == new Vector2Int(1280, 720), "fullscreen does not replace the saved window dimensions");
        yield return Layout("display-settings-fullscreen"); DisplayRectFits("SettingsDialog");
        Click("DisplayWindowed"); yield return WaitDisplay(1280, 720, FullScreenMode.Windowed);
        Click("SettingsBack");
        yield return NativeResize(1600, 900, "large");
        Check(PlayerPrefs.GetInt(SokobanDisplaySettings.WidthKey) == 1600 && PlayerPrefs.GetInt(SokobanDisplaySettings.HeightKey) == 900,
            "a native window resize persists its client dimensions");
        yield return NativeResize(1000, 560, "below-minimum");
        // The external driver must first perform the undersized resize before the clamp is observed.
        var acknowledgement = Path.Combine(folder, "resize-below-minimum.txt");
        var end = Time.realtimeSinceStartup + 8f;
        while (!File.Exists(acknowledgement) && Time.realtimeSinceStartup < end) yield return null;
        Check(File.Exists(acknowledgement), "the native window was resized below the supported minimum");
        yield return WaitDisplay(1280, 720, FullScreenMode.Windowed);
        yield return Layout("display-menu-1280"); DisplayRectFits("MenuCard");
        Click("StartButton"); yield return WaitScene("level");
        yield return Layout("display-levels-1280"); DisplayRectFits("Workspace");
        Click("Level_L001"); Click("StartButton"); yield return WaitScene("game");
        Click("PauseButton"); yield return Layout("display-pause-1280"); DisplayRectFits("PauseDialog");
        SokobanSettingsPanel.Show(); yield return Layout("display-game-settings-1280"); DisplayRectFits("SettingsDialog");
        Click("SettingsBack");
        EnterEditor(); yield return WaitScene("editor");
        var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
        Call(editor, "AddBlankTab");
        var tabs = Get<System.Collections.Generic.List<SokobanEditorTab>>(editor, "tabs");
        var tab = tabs.Last(); var before = JsonUtility.ToJson(tab.Data);
        yield return Layout("display-editor-1280");
        DisplayRectFits("Workspace"); DisplayRectFits("EditViewport"); DisplayRectFits("ImportJson");
        Check(GameObject.Find("EditorSettings") == null, "the editor has no display settings entry");
        yield return NativeResize(1440, 900, "editor-document-resize");
        Check(ReferenceEquals(tabs.Last(), tab) && tab.Dirty && JsonUtility.ToJson(tab.Data) == before,
            "resizing the window keeps the unsaved editor document intact");
        yield return NativeResize(1280, 720, "editor-minimum");
        Click("ImportJson");
        yield return Layout("display-json-1280"); DisplayRectFits("ExchangeDialog"); DisplayRectFits("JsonImportConfirm");
        Click("ExchangeCancel"); Click("AddCategory");
        yield return Layout("display-manager-1280");
        Click("ManagerImportXlsx"); yield return Layout("display-xlsx-1280"); DisplayRectFits("ExchangeDialog");
        yield return NativeResize(1440, 900, "aspect-16-10");
        yield return Layout("display-xlsx-16-10"); DisplayRectFits("Workspace"); DisplayRectFits("ExchangeDialog");
        Click("ExchangeCancel"); Click("ManagerClose");
        yield return NativeResize(1600, 900, "restart-size");
        Call(editor, "ReturnToLevels"); yield return WaitScene("level");
        SokobanGmController.Current.SetOpen(true); Click("GmMenu"); SokobanGmController.Current.SetOpen(false);
        yield return WaitScene("start");
        SokobanEditorSession.Tabs.Clear(); SokobanEditorSession.ActiveIndex = -1;
        SokobanSettingsPanel.Show(); Click("DisplayBorderless"); resolution = Screen.currentResolution;
        yield return WaitDisplay(resolution.width, resolution.height, FullScreenMode.FullScreenWindow);
        Check(PlayerPrefs.GetInt(SokobanDisplaySettings.ModeKey) == 1 && SokobanDisplaySettings.WindowSize == new Vector2Int(1600, 900),
            "fullscreen mode and last window dimensions are stored for the next process");
    }
}
#endif
