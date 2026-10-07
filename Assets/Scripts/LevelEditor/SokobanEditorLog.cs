using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        // 保留本次运行内的完整记录，切换场景或关闭日志窗口不会丢失；只有清空按钮删除记录。
        private static readonly List<string> operationHistory = new List<string>();
        private RectTransform logOverlay, logContent;
        private ScrollRect logScroll;
        private UnityEngine.UI.Text logStatus;

        private void RecordOperation(string message, SokobanEditorTab tab = null, string contextOverride = null)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            var document = tab ?? Active;
            var context = contextOverride ?? (document?.Data == null ? "编辑器" :
                document.DisplayName.Replace('\n', ' ').Replace('\r', ' ') + " / " + document.Data.levelId);
            RecordBackgroundOperation(message, context);
        }

        // 仅从主线程调用，后台批量任务不依赖某个场景控制器的生命周期。
        internal static void RecordBackgroundOperation(string message, string context = "批量生成")
        {
            operationHistory.Add($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {message}");
            var controller = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
            if (controller != null && controller.logOverlay != null) controller.RefreshOperationLog();
        }

        private void OpenOperationLog()
        {
            if (stampOverlay != null || logOverlay != null || closeTabOverlay != null || solverOverlay != null || generationOverlay != null) return;
            if (managerOverlay != null) CancelManagerDrag();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            boxSelecting = false; moveArmed = false; panning = false; ClearMarquee();
            logOverlay = SokobanUI.Panel(Root, "OperationLogOverlay", SokobanTheme.Background,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            logOverlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            LabelAt(logOverlay, "OperationLogTitle", "操作日志", 38, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Margin, -30f), new Vector2(1300f, 64f));
            var close = ButtonAt(logOverlay, "OperationLogClose", "返回编辑", Vector2.one, Vector2.one,
                new Vector2(-Margin, -30f), new Vector2(210f, 54f), CloseOperationLog);
            var panel = InsetPanel(logOverlay, "OperationLogPanel", SokobanTheme.Panel, Margin, Margin, TopInset, 154f);
            logScroll = SokobanUI.ScrollList(panel, "OperationLogScroll", Vector2.zero, Vector2.one,
                new Vector2(16f, 16f), new Vector2(-16f, -16f), out logContent);
            logScroll.verticalScrollbar.navigation = new Navigation { mode = Navigation.Mode.None };
            logStatus = LabelAt(logOverlay, "OperationLogStatus", "", 21, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(Margin, 96f), new Vector2(1760f, 36f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            var copy = ButtonAt(logOverlay, "OperationLogCopy", "复制", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(Margin, 28f), new Vector2(210f, 54f), CopyOperationLog, SokobanTheme.Accent);
            var clear = ButtonAt(logOverlay, "OperationLogClear", "清空", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(Margin + 234f, 28f), new Vector2(210f, 54f), ClearOperationLog);
            LabelAt(logOverlay, "OperationLogHint", "Esc：返回编辑 · 日志保留本次运行中的操作记录", 20,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, 28f), new Vector2(1150f, 54f),
                TextAnchor.MiddleRight, SokobanTheme.TextSecondary);
            var buttons = new[] { copy, clear, close };
            for (var i = 0; i < buttons.Length; i++)
            {
                buttons[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = buttons[(i + buttons.Length - 1) % buttons.Length],
                    selectOnRight = buttons[(i + 1) % buttons.Length],
                    selectOnUp = buttons[i], selectOnDown = buttons[i]
                };
            }
            UnityEngine.Canvas.ForceUpdateCanvases();
            RefreshOperationLog();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(close.gameObject);
        }

        private void RefreshOperationLog()
        {
            // 立即隐藏旧条目，避免同一帧清空/刷新时旧文字继续参与布局。
            for (var i = logContent.childCount - 1; i >= 0; i--)
            {
                var child = logContent.GetChild(i).gameObject;
                child.SetActive(false); UnityEngine.Object.Destroy(child);
            }
            if (operationHistory.Count == 0) AddOperationLogRow("OperationLogEmpty", "暂无操作记录");
            else
                for (var i = 0; i < operationHistory.Count; i++)
                    AddOperationLogRow("OperationLogEntry_" + i, operationHistory[i]);
            logStatus.text = $"共 {operationHistory.Count} 条记录 · 按时间顺序显示 · 复制会包含全部记录";
            UnityEngine.Canvas.ForceUpdateCanvases();
            logScroll.verticalNormalizedPosition = operationHistory.Count == 0 ? 1f : 0f;
        }

        private void AddOperationLogRow(string name, string text)
        {
            var label = SokobanUI.Text(logContent, name, text, 20, SokobanTheme.TextPrimary, TextAnchor.UpperLeft);
            label.supportRichText = false; label.raycastTarget = false;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var width = Mathf.Max(200f, logContent.rect.width - 24f);
            var settings = label.GetGenerationSettings(new Vector2(width, 0f));
            var height = Mathf.Max(36f, Mathf.Ceil(label.cachedTextGeneratorForLayout.GetPreferredHeight(text, settings) / label.pixelsPerUnit) + 12f);
            var layout = label.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height; layout.minHeight = height;
        }

        private void CopyOperationLog()
        {
            GUIUtility.systemCopyBuffer = string.Join("\n", operationHistory);
            logStatus.text = $"已复制全部 {operationHistory.Count} 条操作记录";
        }

        private void ClearOperationLog()
        {
            operationHistory.Clear();
            RefreshOperationLog();
            logStatus.text = "日志已清空";
        }

        private bool UpdateOperationLog()
        {
            if (logOverlay == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape)) CloseOperationLog();
            return true;
        }

        private void CloseOperationLog()
        {
            if (logOverlay != null)
            {
                logOverlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(logOverlay.gameObject);
            }
            logOverlay = null; logContent = null; logScroll = null; logStatus = null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }
    }
}
