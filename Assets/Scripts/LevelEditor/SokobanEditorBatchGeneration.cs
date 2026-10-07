using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private void BuildBatchControls(Transform preview)
        {
            var service = SokobanBatchGenerationService.Instance;
            service.DismissNotification();
            if (service.Settings != null)
            {
                var s = service.Settings;
                var keys = new[] { "Width", "Height", "Boxes", "MinPushes", "MaxPushes", "Walls", "Seed", "Budget", "Candidates", "Count" };
                var values = new[] { s.width, s.height, s.boxCount, s.minPushes, s.maxPushes, s.wallPercent, s.seed, s.budgetSeconds, s.maxCandidates, service.Requested };
                for (var i = 0; i < keys.Length; i++) generationFields[keys[i]].text = values[i].ToString();
                generationFields["Boxes"].text = s.AutomaticBoxes ? "" : s.boxCount.ToString();
                generationFields["Walls"].text = s.AutomaticWalls ? "" : s.wallPercent.ToString();
                generationDifficultyValue = s.difficulty; RefreshGenerationDifficulty();
            }
            var track = SokobanUI.Panel(preview, "BatchProgressTrack", SokobanTheme.Field,
                new Vector2(0f, 1f), Vector2.one, new Vector2(24f, -584f), new Vector2(-24f, -562f));
            track.GetComponent<Image>().raycastTarget = false;
            batchProgressFill = SokobanUI.Panel(track, "BatchProgressFill", SokobanTheme.Accent,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            batchProgressFill.GetComponent<Image>().raycastTarget = false;
            RectTransform detailsContent;
            SokobanUI.ScrollList(preview, "BatchDetailsScroll", new Vector2(0f, 1f), Vector2.one,
                new Vector2(24f, -694f), new Vector2(-24f, -598f), out detailsContent);
            generationDetails.rectTransform.SetParent(detailsContent, false);
            generationDetails.rectTransform.sizeDelta = Vector2.zero;
            generationDetails.verticalOverflow = VerticalWrapMode.Overflow;
            generationDetails.raycastTarget = false;
            batchPrevious = ButtonAt(preview, "BatchPreviousResult", "上一个", Vector2.zero, Vector2.zero,
                new Vector2(24f, 108f), new Vector2(132f, 48f), () => SelectBatchResult(batchPreviewIndex - 1));
            batchPreviewLabel = LabelAt(preview, "BatchPreviewIndex", "暂无结果", 20, Vector2.zero, Vector2.zero,
                new Vector2(170f, 108f), new Vector2(240f, 48f), TextAnchor.MiddleCenter);
            batchNext = ButtonAt(preview, "BatchNextResult", "下一个", Vector2.zero, Vector2.zero,
                new Vector2(424f, 108f), new Vector2(132f, 48f), () => SelectBatchResult(batchPreviewIndex + 1));
            batchSave = ButtonAt(preview, "BatchSaveAll", "保存全部结果", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 108f), new Vector2(276f, 48f), SaveBatchResults, SokobanTheme.Accent);
            batchPreviewIndex = -1; batchVisibleEntries = -1;
            batchUiMessage = null;
            generationStatus.text = "填写参数和数量后开始生成。\n启动后可返回编辑或其他页面，结束时会弹出提醒。";
            UpdateBatchGeneration();
        }

        private void StartBatchGeneration()
        {
            var service = SokobanBatchGenerationService.Instance;
            if (service.Running) return;
            int count;
            if (!TryReadGenerationSettings(out var settings) || !ReadGenerationInt("Count", out count))
            { batchUiMessage = generationStatus.text; return; }
            string error;
            if (!service.StartBatch(settings, count, out error))
            { generationStatus.text = batchUiMessage = error; RecordOperation("批量生成参数无效：" + error); return; }
            batchUiMessage = null;
            generationFields["Seed"].text = settings.seed.ToString();
            batchPreviewIndex = -1; batchVisibleEntries = -1; generatedLevel = null; generatedResult = null;
            SokobanUI.DestroyChildren(generationBoard);
            UpdateBatchGeneration();
        }

        private void CancelBatchGeneration()
        { batchUiMessage = null; SokobanBatchGenerationService.Instance.Cancel(); UpdateBatchGeneration(); }

        private void UpdateBatchGeneration()
        {
            var service = SokobanBatchGenerationService.Instance;
            var progress = service.Progress;
            var fraction = progress == null ? 0f : progress.Fraction;
            batchProgressFill.anchorMin = Vector2.zero;
            batchProgressFill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            batchProgressFill.offsetMin = batchProgressFill.offsetMax = Vector2.zero;
            if (service.Settings != null)
            {
                generationStatus.text = service.Summary + $"\n已处理 {service.Entries.Count}/{service.Requested} · 进度 {fraction:P0}" +
                    (progress == null ? "" : $" · 耗时 {progress.ElapsedMs / 1000f:0.0} 秒");
                if (service.Running && progress != null && progress.Current != null)
                    generationStatus.text += $" · 当前第 {Math.Min(service.Requested, progress.Completed + 1)} 个：{progress.Current.phase}，候选 {progress.Current.attempts}";
                if (service.Error != null) generationStatus.text += "\n" + service.Error;
            }
            var successful = service.Entries.Where(e => e.Level != null).ToList();
            if (batchVisibleEntries != service.Entries.Count)
            {
                batchVisibleEntries = service.Entries.Count;
                if (batchPreviewIndex < 0 && successful.Count > 0) SelectBatchResult(0);
            }
            batchPrevious.interactable = batchPreviewIndex > 0;
            batchNext.interactable = batchPreviewIndex >= 0 && batchPreviewIndex + 1 < successful.Count;
            batchSave.interactable = !service.Running && successful.Any(e => !e.Saved);
            batchPreviewLabel.text = successful.Count == 0 ? "暂无结果" : $"第 {batchPreviewIndex + 1} / {successful.Count} 个";
            UpdateGenerationButtons();
            SetButtonLabel(generationStart, service.Running ? "正在生成…" : service.Settings == null ? "开始批量生成" : "重新批量生成");
            SetButtonLabel(generationCancel, "取消整批");
            if (generatedLevel == null && service.Settings != null)
            {
                generationDetails.text = $"基础种子：{service.Settings.seed} · 每关预算 {service.Settings.budgetSeconds} 秒\n" +
                    (service.Running ? "可返回编辑、切换页签或场景，后台任务会继续。" :
                        string.Join("\n", service.Entries.Where(e => e.Level == null).Take(3).Select(e => $"种子 {e.Result.Settings.seed}：{e.Result.Message}")));
            }
            if (batchUiMessage != null) generationStatus.text = batchUiMessage;
        }

        private void SelectBatchResult(int index)
        {
            var items = SokobanBatchGenerationService.Instance.Entries.Where(e => e.Level != null).ToList();
            if (index < 0 || index >= items.Count) return;
            batchPreviewIndex = index; var entry = items[index]; generatedResult = entry.Result; generatedLevel = entry.Level;
            SokobanUI.DestroyChildren(generationBoard); BuildGeneratedPreview();
            generationDetails.text = $"结果 {index + 1} · 种子 {entry.Result.Settings.seed} · 移动 {entry.Result.Solution.Moves.Length} 步 / 推箱 {entry.Result.Solution.Pushes} 次" +
                (entry.Saved ? " · 已保存" : " · 尚未保存") + "\n" + entry.Result.Complexity.explanation;
            if (entry.SaveError != null) generationDetails.text += "\n保存失败：" + entry.SaveError;
            RecordOperation($"预览批量生成结果 {index + 1}：{generatedLevel.levelId}");
        }

        private void SaveBatchResults()
        {
            var service = SokobanBatchGenerationService.Instance;
            var message = service.SaveAll();
            RefreshList();
            foreach (var tab in tabs)
            {
                if (tab.Descriptor != null || tab.Data == null) continue;
                var entry = service.Entries.FirstOrDefault(e => e.Saved && e.Level.levelId == tab.Data.levelId);
                if (entry == null) continue;
                tab.Descriptor = SokobanLevelRepository.ListAll().FirstOrDefault(d => d.LevelId == tab.Data.levelId);
                if (JsonUtility.ToJson(tab.Data) == JsonUtility.ToJson(entry.Level)) tab.Dirty = false;
            }
            RefreshTabs();
            if (batchPreviewIndex >= 0) SelectBatchResult(batchPreviewIndex);
            batchUiMessage = message + (service.Entries.Any(e => e.SaveError != null) ? "\n可重试保存；失败原因见对应结果。" : "\n可在左侧列表和关卡管理中打开已保存结果。");
            UpdateBatchGeneration();
        }

        private void UpdateBatchResultsRequest()
        {
            var service = SokobanBatchGenerationService.Existing;
            if (service == null || !service.PendingOpen || stampOverlay != null || solverOverlay != null || logOverlay != null || closeTabOverlay != null || managerOverlay != null || verificationOverlay != null) return;
            if (generationOverlay != null)
            {
                if (!generationBatchMode) return;
                service.PendingOpen = false; return;
            }
            service.PendingOpen = false; OpenBatchGenerator();
        }
    }
}
