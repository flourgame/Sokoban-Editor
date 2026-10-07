using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    /// <summary>批量生成与验证共用通知队列，两个任务同时完成时不会相互遮挡。</summary>
    public sealed class SokobanTaskNotification : MonoBehaviour
    {
        private sealed class Notice { internal string Key, Message; internal Action View; }
        private static SokobanTaskNotification instance;
        private readonly List<Notice> notices = new List<Notice>();
        private Canvas canvas;
        private RectTransform toast;
        public static void Show(string key, string message, Action view)
        {
            if (instance == null) instance = new GameObject("SokobanTaskNotifications").AddComponent<SokobanTaskNotification>();
            instance.notices.RemoveAll(n => n.Key == key);
            instance.notices.Add(new Notice { Key = key, Message = message, View = view });
            instance.Render();
        }
        public static void Dismiss(string key)
        {
            if (instance == null || instance.notices.RemoveAll(n => n.Key == key) == 0) return;
            instance.Render();
        }
        private void Awake() { instance = this; DontDestroyOnLoad(gameObject); }
        private void Render()
        {
            if (canvas != null) { canvas.gameObject.SetActive(false); Destroy(canvas.gameObject); canvas = null; }
            if (notices.Count == 0) return;
            var notice = notices[0];
            canvas = SokobanUI.CreateCanvas("SokobanTaskNotificationCanvas", 500); canvas.transform.SetParent(transform, false);
            toast = SokobanUI.Panel(canvas.transform, notice.Key + "CompletionToast", SokobanTheme.Background,
                new Vector2(1f, 0f), new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            toast.pivot = new Vector2(1f, 0f); toast.anchoredPosition = new Vector2(-36f, 100f); toast.sizeDelta = new Vector2(660f, 174f);
            SokobanUI.Panel(toast, "NotificationAccent", SokobanTheme.Accent, new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -4f), Vector2.zero).GetComponent<Image>().raycastTarget = false;
            var label = SokobanUI.Text(toast, notice.Key + "CompletionMessage", notice.Message, 23, SokobanTheme.TextPrimary, TextAnchor.UpperLeft);
            label.rectTransform.offsetMin = new Vector2(18f, 68f); label.rectTransform.offsetMax = new Vector2(-18f, -14f);
            AddButton(notice.Key + "CompletionView", "查看结果", -174f, () => { Dismiss(notice.Key); notice.View?.Invoke(); }, SokobanTheme.Accent);
            AddButton(notice.Key + "CompletionClose", "关闭提醒", -18f, () => Dismiss(notice.Key), SokobanTheme.Surface);
        }
        private void AddButton(string name, string title, float x, Action action, Color color)
        {
            var button = SokobanUI.Button(toast, name, title, new Vector2(144f, 44f), action, color,
                color == SokobanTheme.Accent ? SokobanTheme.AccentText : SokobanTheme.TextPrimary);
            var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(x, 14f);
        }
        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
