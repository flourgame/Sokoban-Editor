using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    /// <summary>本次运行内保存工作区，跨场景返回不会丢失未保存内容和撤销记录。</summary>
    public static class SokobanEditorSession
    {
        internal static readonly List<SokobanEditorTab> Tabs = new List<SokobanEditorTab>();
        internal static int ActiveIndex = -1;
        internal static Vector2 Pan;
        internal static float Zoom = 1f;
        public static bool QuitPending { get => SokobanRuntimeContext.IsQuitConfirmationOpen; private set => SokobanRuntimeContext.IsQuitConfirmationOpen = value; }
        private static bool allowQuit;
        private static GameObject quitCanvas;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Application.wantsToQuit -= ConfirmQuit;
            Tabs.Clear(); ActiveIndex = -1; Pan = Vector2.zero; Zoom = 1f;
            QuitPending = allowQuit = false; quitCanvas = null;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() { Application.wantsToQuit += ConfirmQuit; }

        private static bool ConfirmQuit()
        {
            if (allowQuit) return true;
            var editor = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
            if (editor != null) editor.CaptureSession();
            if (!Tabs.Any(t => t.Dirty)) return true;
            if (QuitPending) return false;
            QuitPending = true;
            var canvas = SokobanUI.CreateCanvas("UnsavedQuitCanvas", 900); quitCanvas = canvas.gameObject;
            UnityEngine.Object.DontDestroyOnLoad(quitCanvas);
            var overlay = SokobanUI.Panel(canvas.transform, "UnsavedQuitOverlay", new Color(0f, 0f, 0f, 0.85f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dialog = SokobanUI.Panel(overlay, "UnsavedQuitDialog", SokobanTheme.Panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-460f, -180f), new Vector2(460f, 180f));
            var title = SokobanUI.Text(dialog, "UnsavedQuitTitle", Tabs.Any(t => t.IsStamp && t.Dirty) ? "有未保存的关卡或印章" : "有未保存的关卡", 34, SokobanTheme.TextPrimary, TextAnchor.MiddleCenter);
            Position(title.rectTransform, new Vector2(0f, 108f), new Vector2(840f, 64f));
            var message = SokobanUI.Text(dialog, "UnsavedQuitMessage", $"{Tabs.Count(t => t.Dirty)} 个文档尚未保存。退出前如何处理？", 22, SokobanTheme.TextSecondary, TextAnchor.MiddleCenter);
            Position(message.rectTransform, new Vector2(0f, 15f), new Vector2(840f, 108f));
            AddButton(dialog, "UnsavedQuitSave", "全部保存并退出", -284f, () =>
            {
                try
                {
                    foreach (var tab in Tabs.Where(t => t.Dirty))
                    {
                        tab.Save();
                    }
                    allowQuit = true; Application.Quit();
                }
                catch (Exception error) { message.text = "保存失败，尚未退出：" + error.Message; }
            });
            AddButton(dialog, "UnsavedQuitDiscard", "丢弃并退出", 0f, () => { allowQuit = true; Application.Quit(); });
            AddButton(dialog, "UnsavedQuitCancel", "取消", 284f, () =>
            { QuitPending = false; quitCanvas.SetActive(false); UnityEngine.Object.Destroy(quitCanvas); quitCanvas = null; });
            return false;
        }
        private static void Position(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
        private static void AddButton(Transform parent, string name, string text, float x, Action click)
        { var button = SokobanUI.Button(parent, name, text, new Vector2(260f, 56f), click); Position(button.GetComponent<RectTransform>(), new Vector2(x, -108f), new Vector2(260f, 56f)); }
    }

    public sealed partial class SokobanEditorSceneController
    {
        internal void CaptureSession()
        {
            if (nameField != null && Data != null) CommitNameEdit(nameField.text);
            SokobanEditorSession.Tabs.Clear(); SokobanEditorSession.Tabs.AddRange(tabs);
            SokobanEditorSession.ActiveIndex = activeIndex;
            SokobanEditorSession.Pan = pan; SokobanEditorSession.Zoom = zoom;
        }
    }
}
