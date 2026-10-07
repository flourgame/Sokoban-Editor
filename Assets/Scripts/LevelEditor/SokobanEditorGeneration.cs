using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private sealed class GenerationJob
        {
            internal CancellationTokenSource Cancellation;
            internal Task<SokobanGenerationResult> Task;
            internal SokobanGenerationProgress Progress;
        }
        private RectTransform generationOverlay, generationBoard;
        private UnityEngine.UI.Text generationStatus, generationDetails;
        private readonly Dictionary<string, InputField> generationFields = new Dictionary<string, InputField>();
        private Button generationStart, generationCancel, generationAdopt, generationReplay;
        private Dropdown generationDifficulty;
        private int generationDifficultyValue = 2;
        private GenerationJob generationJob;
        private SokobanGenerationResult generatedResult;
        private SokobanJsonLevel generatedLevel;
        private float generationProgressTick;
        private bool generationBatchMode;
        private int batchPreviewIndex = -1, batchVisibleEntries = -1;
        private RectTransform batchProgressFill;
        private UnityEngine.UI.Text batchPreviewLabel;
        private Button batchPrevious, batchNext, batchSave;
        private string batchUiMessage;

        private void OpenGenerator() => OpenGenerationPanel(false);
        private void OpenBatchGenerator() => OpenGenerationPanel(true);
        private void OpenGenerationPanel(bool batch)
        {
            if (stampOverlay != null || generationOverlay != null || solverOverlay != null || logOverlay != null || closeTabOverlay != null || managerOverlay != null || verificationOverlay != null) return;
            generationBatchMode = batch;
            RecordOperation(batch ? "打开批量生成面板" : "打开自动生成面板");
            generationFields.Clear(); generatedResult = null; generatedLevel = null;
            generationOverlay = SokobanUI.Panel(Root, "GenerationOverlay", SokobanTheme.Background,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            LabelAt(generationOverlay, "GenerationTitle", batch ? "批量生成关卡" : "自动生成关卡", 38, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Margin, -30f), new Vector2(1300f, 64f));
            ButtonAt(generationOverlay, "GenerationClose", "返回编辑", Vector2.one, Vector2.one,
                new Vector2(-Margin, -30f), new Vector2(210f, 54f), CloseGenerator);
            var parameters = InsetPanel(generationOverlay, "GenerationParameters", SokobanTheme.Panel, Margin, 1264f, 116f, BottomInset);
            AddGenerationField(parameters, "Width", "宽度（含外围墙）", "8", 0, 0);
            AddGenerationField(parameters, "Height", "高度（含外围墙）", "8", 1, 0);
            AddGenerationField(parameters, "Boxes", "箱子数（留空自动）", "", 0, 1);
            LabelAt(parameters, "GenerationDifficultyLabel", "生成难度", 20, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(318f, -126f), new Vector2(276f, 32f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            generationDifficulty = CreateGenerationDifficultyDropdown(parameters);
            RefreshGenerationDifficulty();
            AddGenerationField(parameters, "MinPushes", "最少推箱次数", "1", 0, 2);
            AddGenerationField(parameters, "MaxPushes", "最多推箱次数", "60", 1, 2);
            AddGenerationField(parameters, "Walls", "内部墙 %（留空自动）", "", 0, 3);
            AddGenerationField(parameters, "Seed", "种子（留空自动）", "", 1, 3);
            AddGenerationField(parameters, "Budget", batch ? "每关时间预算（秒）" : "时间预算（秒）", "60", 0, 4);
            AddGenerationField(parameters, "Candidates", batch ? "每关候选数量上限" : "候选数量上限", "400", 1, 4);
            if (batch) AddGenerationField(parameters, "Count", "生成数量（1–100）", "10", 0, 5);
            LabelAt(parameters, "GenerationHelp", "箱子数、墙比例留空自动；填写后精确匹配。\n尺寸 5–8 · 箱子 1–8 · 填写墙比例 0–45%\n次数只统计推箱，走路不计；复杂度独立评估。", 20,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, batch ? -644f : -548f), new Vector2(572f, 126f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            generationStart = ButtonAt(parameters, "GenerationStart", "开始生成", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 32f), new Vector2(276f, 54f), batch ? (Action)StartBatchGeneration : StartGeneration, SokobanTheme.Accent);
            generationCancel = ButtonAt(parameters, "GenerationCancel", "取消生成", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 32f), new Vector2(276f, 54f), batch ? (Action)CancelBatchGeneration : CancelGeneration);

            var preview = InsetPanel(generationOverlay, "GenerationPreview", SokobanTheme.Panel, 692f, Margin, 116f, BottomInset);
            generationBoard = SokobanUI.Panel(preview, "GenerationBoard", SokobanTheme.BoardBackground,
                new Vector2(0f, 1f), Vector2.one, new Vector2(20f, -436f), new Vector2(-20f, -20f));
            generationStatus = LabelAt(preview, "GenerationStatus", "填写参数后开始生成。\n成功后可查看完整解答，再采用到新页签。", 23,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -454f), new Vector2(1126f, 96f), TextAnchor.UpperLeft);
            generationDetails = LabelAt(preview, "GenerationDetails", "", 19, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, batch ? -598f : -558f), new Vector2(1126f, batch ? 152f : 220f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            generationReplay = ButtonAt(preview, "GenerationReplay", "查看 / 播放完整解答", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(24f, 32f), new Vector2(368f, 54f), () => OpenSolver(generatedLevel, generatedResult.Solution));
            generationAdopt = ButtonAt(preview, "GenerationAdopt", "采用关卡", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-24f, 32f), new Vector2(276f, 54f), AdoptGeneratedLevel, SokobanTheme.Accent);
            if (batch) BuildBatchControls(preview);
            UpdateGenerationButtons();
        }
        private void RefreshGenerationDifficulty()
        {
            generationDifficulty.SetValueWithoutNotify(generationDifficultyValue);
            generationDifficulty.RefreshShownValue();
        }
        private Dropdown CreateGenerationDifficultyDropdown(Transform parent)
        {
            var rect = SokobanUI.Panel(parent, "GenerationDifficulty", SokobanTheme.Field,
                new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
            rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = new Vector2(318f, -166f); rect.sizeDelta = new Vector2(276f, 46f);
            var dropdown = rect.gameObject.AddComponent<Dropdown>(); dropdown.targetGraphic = rect.GetComponent<Image>();
            var caption = SokobanUI.Text(rect, "Label", "", 22, SokobanTheme.TextPrimary);
            caption.rectTransform.offsetMax = new Vector2(-44f, -4f); caption.raycastTarget = false;
            var arrow = SokobanUI.Text(rect, "Arrow", "▼", 18, SokobanTheme.TextSecondary, TextAnchor.MiddleCenter);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0f); arrow.rectTransform.anchorMax = Vector2.one;
            arrow.rectTransform.offsetMin = new Vector2(-38f, 0f); arrow.rectTransform.offsetMax = new Vector2(-6f, 0f); arrow.raycastTarget = false;
            var template = SokobanUI.Panel(rect, "Template", SokobanTheme.PanelAlt, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, Vector2.zero);
            template.pivot = new Vector2(0.5f, 1f); template.anchoredPosition = new Vector2(0f, -4f); template.sizeDelta = new Vector2(0f, 184f);
            var viewport = SokobanUI.Panel(template, "Viewport", Color.clear, Vector2.zero, Vector2.one, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            viewport.gameObject.AddComponent<RectMask2D>();
            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>(); content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 44f);
            var item = SokobanUI.Panel(content, "Item", SokobanTheme.Surface, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
            item.pivot = new Vector2(0.5f, 1f); item.anchoredPosition = Vector2.zero; item.sizeDelta = new Vector2(0f, 44f);
            var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = item.GetComponent<Image>();
            var selected = SokobanUI.Panel(item, "Selected", SokobanTheme.Accent, new Vector2(0f, 0.2f), new Vector2(0f, 0.8f), new Vector2(4f, 0f), new Vector2(7f, 0f));
            selected.GetComponent<Image>().raycastTarget = false; toggle.graphic = selected.GetComponent<Image>();
            var itemText = SokobanUI.Text(item, "ItemLabel", "", 22, SokobanTheme.TextPrimary); itemText.raycastTarget = false;
            itemText.rectTransform.offsetMin = new Vector2(16f, 4f);
            var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.content = content;
            scroll.horizontal = false; scroll.vertical = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            dropdown.template = template; dropdown.captionText = caption; dropdown.itemText = itemText;
            dropdown.options = new List<Dropdown.OptionData> { new Dropdown.OptionData("不限"), new Dropdown.OptionData("简单"),
                new Dropdown.OptionData("普通"), new Dropdown.OptionData("困难") };
            template.gameObject.SetActive(false);
            dropdown.onValueChanged.AddListener(value =>
            {
                generationDifficultyValue = value;
                RecordOperation("修改生成难度：" + dropdown.options[value].text);
            });
            return dropdown;
        }
        private void AddGenerationField(Transform parent, string key, string label, string value, int column, int row)
        {
            var x = 24f + column * 294f; var y = -24f - row * 102f;
            LabelAt(parent, "GenerationLabel" + key, label, 20, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(276f, 32f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            var placeholder = key == "Seed" ? "自动种子" : key == "Boxes" || key == "Walls" ? "自动决定" : "整数";
            var input = SokobanUI.InputField(parent, "Generation" + key, value, placeholder, new Vector2(276f, 46f));
            var rect = input.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y - 40f); input.characterLimit = 11;
            input.contentType = InputField.ContentType.IntegerNumber; generationFields[key] = input;
            var previous = value;
            input.onEndEdit.AddListener(current =>
            {
                if (current == previous) return;
                RecordOperation("修改生成参数：" + label + " = " + (string.IsNullOrWhiteSpace(current) ? "自动" : current));
                previous = current;
            });
        }
        private void StartGeneration()
        {
            if (generationJob != null) return;
            if (!TryReadGenerationSettings(out var settings)) return;
            RecordOperation($"开始生成：{settings.width} × {settings.height}，箱子 {settings.BoxCountLabel}，推箱 {settings.minPushes}–{settings.maxPushes} 次，" +
                $"难度 {settings.DifficultyLabel}，内部墙 {settings.WallPercentLabel}，种子 {settings.seed}，预算 {settings.budgetSeconds} 秒，候选上限 {settings.maxCandidates}");
            generatedLevel = null; generatedResult = null; SokobanUI.DestroyChildren(generationBoard);
            generationDetails.text = "实际种子：" + settings.seed + "\n正在寻找符合推箱次数与复杂度要求的关卡…";
            generationStatus.text = "正在生成… 可随时取消。";
            var job = new GenerationJob { Cancellation = new CancellationTokenSource() };
            var token = job.Cancellation.Token;
            job.Task = Task.Run(() => SokobanGenerator.Generate(settings, token,
                progress => Interlocked.Exchange(ref job.Progress, progress)));
            generationJob = job; UpdateGenerationButtons();
        }
        // Both forms read, validate and interpret optional fields through this one path.
        private bool TryReadGenerationSettings(out SokobanGenerationSettings settings)
        {
            settings = new SokobanGenerationSettings { difficulty = generationDifficultyValue };
            if (!ReadGenerationInt("Width", out settings.width) || !ReadGenerationInt("Height", out settings.height) ||
                !ReadGenerationInt("MinPushes", out settings.minPushes) || !ReadGenerationInt("MaxPushes", out settings.maxPushes) ||
                !ReadGenerationInt("Budget", out settings.budgetSeconds) || !ReadGenerationInt("Candidates", out settings.maxCandidates)) return false;
            if (!SokobanGenerationSettings.TryParseStructure(generationFields["Boxes"].text, generationFields["Walls"].text,
                out settings.boxCount, out settings.wallPercent, out var error))
            { generationStatus.text = error; RecordOperation("生成参数无效：" + error); return false; }
            if (string.IsNullOrWhiteSpace(generationFields["Seed"].text)) settings.seed = Guid.NewGuid().GetHashCode();
            else if (!ReadGenerationInt("Seed", out settings.seed)) return false;
            error = settings.Validate();
            if (!string.IsNullOrEmpty(error)) { generationStatus.text = error; RecordOperation("生成参数无效：" + error); return false; }
            return true;
        }
        private bool ReadGenerationInt(string key, out int value)
        {
            if (int.TryParse(generationFields[key].text, out value)) return true;
            generationStatus.text = "参数需要填写有效整数：" + generationFields[key].transform.parent.Find("GenerationLabel" + key).GetComponent<UnityEngine.UI.Text>().text;
            RecordOperation("生成参数无效：" + generationStatus.text);
            return false;
        }
        private bool UpdateGenerationWorkflow()
        {
            if (generationOverlay == null) return false;
            if (generationBatchMode)
            {
                UpdateBatchGeneration();
                if (Input.GetKeyDown(KeyCode.Escape)) CloseGenerator();
                return true;
            }
            if (generationJob != null)
            {
                if (generationJob.Task.IsCompleted)
                {
                    var job = generationJob; generationJob = null;
                    var result = job.Task.IsFaulted ? new SokobanGenerationResult { Status = SokobanGenerationStatus.Error,
                        Message = job.Task.Exception.GetBaseException().Message } : job.Task.Result;
                    job.Cancellation.Dispose(); PublishGeneratedResult(result);
                }
                else if (Time.unscaledTime - generationProgressTick >= 0.1f)
                {
                    generationProgressTick = Time.unscaledTime;
                    var progress = Volatile.Read(ref generationJob.Progress);
                    if (progress != null) generationStatus.text = $"{progress.phase} · 已尝试 {progress.attempts} 次 · 匹配 {progress.qualifiedCandidates} 个\n" +
                        $"耗时 {progress.elapsedMs / 1000f:0.0} 秒 · 最近解答 {progress.lastPushes} 推" +
                        (progress.lastDifficulty > 0 ? " / " + SokobanDifficultyEvaluator.Name(progress.lastDifficulty) : "");
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape)) CloseGenerator();
            return true;
        }
        private void PublishGeneratedResult(SokobanGenerationResult result)
        {
            generatedResult = result;
            var stats = result.Statistics;
            generationStatus.text = result.Message + (stats == null ? "" : $"\n尝试 {stats.attempts} 次 · 耗时 {stats.elapsedMs / 1000f:0.0} 秒");
            if (result.Status == SokobanGenerationStatus.Success)
            {
                generatedLevel = CreateGeneratedJson(result); BuildGeneratedPreview();
                generationStatus.text = $"已生成：推箱 {result.Solution.Pushes} 次 / 移动 {result.Solution.Moves.Length} 步 · 箱子 {result.Level.Boxes.Length} 个 · 内部墙 {SokobanHtmlGenerationLayout.InteriorWallCount(result.Level)} 格\n" +
                    $"尝试 {stats.attempts} 次 · 耗时 {stats.elapsedMs / 1000f:0.0} 秒";
                generationDetails.text = $"种子 {result.Settings.seed} · 最佳候选 {stats.bestCandidate} / 总尝试 {stats.attempts}" +
                    (result.Quality == null ? "" : $" · 布局质量 {result.Quality.score:0.0}/100") + "\n" + result.Complexity.explanation;
                SetButtonLabel(generationStart, "重新生成");
            }
            else if (stats != null)
                generationDetails.text = $"种子 {result.Settings.seed} · 可解候选 {stats.validCandidates} · 修改通过 {stats.acceptedMutations}\n结构未通过 {stats.terrainRejected} · 推箱次数未匹配 {stats.pushRejected}\n" +
                    $"难度不匹配 {stats.difficultyRejected} · 求解未完成 {stats.solveUnknown} · 未找到解 {stats.solveUnsolvable} · 重复候选 {stats.duplicatesRejected}\n没有匹配结果；当前编辑关卡保持原样。";
            UpdateGenerationButtons();
            RecordOperation("自动生成结果：" + generationStatus.text);
        }
        private static SokobanJsonLevel CreateGeneratedJson(SokobanGenerationResult result)
        {
            var level = result.Level; var settings = result.Settings;
            var rows = new string[level.Height];
            for (var y = 0; y < level.Height; y++)
            {
                var row = new char[level.Width];
                for (var x = 0; x < level.Width; x++) row[x] = level.Walls[y * level.Width + x] ? '#' : '.';
                rows[y] = new string(row);
            }
            var boxes = new SokobanJsonPoint[level.Boxes.Length]; var goals = new SokobanJsonPoint[level.Goals.Length];
            for (var b = 0; b < boxes.Length; b++) boxes[b] = new SokobanJsonPoint(level.Boxes[b] % level.Width, level.Boxes[b] / level.Width);
            for (var g = 0; g < goals.Length; g++) goals[g] = new SokobanJsonPoint(level.Goals[g] % level.Width, level.Goals[g] / level.Width);
            var solution = result.Solution;
            return new SokobanJsonLevel { levelId = "Generated_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6),
                name = "生成关卡 · " + SokobanDifficultyEvaluator.Name(result.Complexity.difficulty),
                verifiedMoves = solution.Moves.Length,
                size = new SokobanJsonSize { width = level.Width, height = level.Height }, terrain = rows, boxes = boxes, goals = goals,
                player = new SokobanJsonPoint(level.Player % level.Width, level.Player / level.Width),
                metadata = new SokobanJsonMetadata { difficulty = result.Complexity.difficulty, parMoves = solution.Moves.Length,
                    parPushes = solution.Pushes, tags = new[] { "generated" }, notes = "复杂度估计；" + result.Complexity.evaluatorVersion },
                generation = new SokobanJsonGeneration { isGenerated = true, seed = settings.seed, generatorVersion = SokobanGenerationResult.Version,
                    parameters = settings.Copy(), complexity = result.Complexity, quality = result.Quality },
                solution = new SokobanJsonSolution { status = "Solved", moves = solution.Moves,
                    pushes = solution.Pushes, moveCount = solution.Moves.Length } };
        }
        private void BuildGeneratedPreview()
        {
            var level = new SokobanLevelRuntime(generatedLevel);
            var stride = Mathf.Min(72f, Mathf.Min(1080f / level.Width, 400f / level.Height));
            var board = new GameObject("PreviewGrid", typeof(RectTransform)).GetComponent<RectTransform>(); board.SetParent(generationBoard, false);
            board.anchorMin = board.anchorMax = board.pivot = new Vector2(0.5f, 0.5f);
            board.sizeDelta = new Vector2(level.Width * stride, level.Height * stride);
            for (var y = 0; y < level.Height; y++)
            for (var x = 0; x < level.Width; x++)
            {
                var point = new SokobanGridPoint(x, y);
                var cell = SokobanUI.Panel(board, "PreviewCell", level.IsWall(point) ? SokobanTheme.BoardWall :
                    level.Goals.Contains(point) ? SokobanTheme.Goal : SokobanTheme.BoardFloor,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                cell.pivot = new Vector2(0f, 1f); cell.anchoredPosition = new Vector2(x * stride, -y * stride); cell.sizeDelta = Vector2.one * (stride - 2f);
                if (level.Goals.Contains(point)) SokobanUI.GoalMarker(cell);
                if (level.PlayerStart == point || level.BoxesStart.Contains(point))
                    SokobanUI.Panel(cell, "Object", level.PlayerStart == point ? SokobanTheme.Player : SokobanTheme.Box,
                        new Vector2(0.19f, 0.19f), new Vector2(0.81f, 0.81f), Vector2.zero, Vector2.zero);
            }
        }
        private void UpdateGenerationButtons()
        {
            var running = generationBatchMode ? SokobanBatchGenerationService.Instance.Running : generationJob != null;
            var ready = generatedLevel != null;
            foreach (var field in generationFields.Values) field.interactable = !running;
            generationDifficulty.interactable = !running; generationStart.interactable = !running;
            generationCancel.interactable = running && (generationBatchMode ? !SokobanBatchGenerationService.Instance.Cancelling : !generationJob.Cancellation.IsCancellationRequested);
            generationReplay.interactable = ready; generationAdopt.interactable = ready;
        }
        private void CancelGeneration()
        {
            if (generationJob == null) return;
            RecordOperation("请求取消自动生成");
            generationJob.Cancellation.Cancel(); generationStatus.text = "正在取消生成…"; UpdateGenerationButtons();
        }
        private void StopGenerationJob()
        {
            if (generationJob == null) return;
            generationJob.Cancellation.Cancel(); generationJob.Cancellation.Dispose(); generationJob = null;
        }
        private void CloseGenerator()
        {
            if (generationOverlay != null) RecordOperation(generationBatchMode ? "离开批量生成页面（任务和结果保留）" :
                generationJob != null ? "关闭自动生成面板并取消任务" : "关闭自动生成面板");
            if (!generationBatchMode) StopGenerationJob();
            if (generationOverlay != null) { generationOverlay.gameObject.SetActive(false); UnityEngine.Object.Destroy(generationOverlay.gameObject); }
            generationOverlay = null; generatedLevel = null; generatedResult = null; generationFields.Clear();
        }
        private void AdoptGeneratedLevel()
        {
            if (generatedLevel == null || generationJob != null) return;
            var data = SokobanLevelRepository.Parse(JsonUtility.ToJson(generatedLevel));
            var saved = generationBatchMode && SokobanBatchGenerationService.Instance.Entries.Exists(e => e.Level != null && e.Level.levelId == data.levelId && e.Saved);
            CloseGenerator();
            var existing = tabs.FindIndex(t => t.Data.levelId == data.levelId);
            if (existing >= 0) { SetActiveTab(existing); return; }
            var descriptor = saved ? SokobanLevelRepository.ListAll().FirstOrDefault(d => d.LevelId == data.levelId) : null;
            tabs.Add(new SokobanEditorTab { Data = data, Descriptor = descriptor, Dirty = !saved }); SetActiveTab(tabs.Count - 1);
            SetDetail("已采用生成关卡到新页签；点击“保存”保存到 Generated。\n" + data.generation.complexity.explanation);
        }
        // CreateGeneratedJson 是 private static；本别名把它以 internal 暴露给 SokobanBatchGenerationService。
        internal static SokobanJsonLevel CreateBatchJson(SokobanGenerationResult result) => CreateGeneratedJson(result);
    }
}
