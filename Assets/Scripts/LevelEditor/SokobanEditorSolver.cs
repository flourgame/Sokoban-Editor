using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private sealed class SolverJob
        {
            public CancellationTokenSource Cancellation;
            public Task<SokobanSolveResult> Task;
            public SokobanSolveProgress Progress;
        }
        private sealed class StepBlock
        {
            public int First, Count;
            public UnityEngine.UI.Text Label;
        }

        private RectTransform solverOverlay, solverBoard, solverStepContent;
        private UnityEngine.UI.Text solverSummary, solverCurrent;
        private Button solverPrevious, solverNext, solverPlay, solverReset, solverCopy, solverSpeed;
        private ScrollRect solverStepScroll;
        private readonly List<Image> solverCells = new List<Image>();
        private readonly List<Image> solverObjects = new List<Image>();
        private readonly List<StepBlock> solverBlocks = new List<StepBlock>();
        private SolverJob solverJob;
        private SokobanJsonLevel solverSource;
        private SokobanEditorTab solverTab;
        private string solverSourceJson;
        private SokobanLevelRuntime solverLevel;
        private SokobanState solverInitial;
        private SokobanSolveResult solverResult;
        private SokobanSolutionPlayback solverPlayback;
        private bool solverPlaying;
        private float solverNextTick, solverLastProgressUi;
        private int solverSpeedIndex = 1;
        private static readonly float[] SolverIntervals = { 0.9f, 0.45f, 0.18f };

        private void OpenSolver(SokobanJsonLevel source = null, SokobanSolveResult cachedResult = null)
        {
            if ((source == null && (Data == null || IsStampDocument)) || stampOverlay != null || solverOverlay != null || logOverlay != null || closeTabOverlay != null || managerOverlay != null || verificationOverlay != null) return;
            solverTab = source == null ? Active : null;
            solverSourceJson = JsonUtility.ToJson(source ?? Data);
            solverSource = SokobanLevelRepository.Parse(solverSourceJson);
            RecordOperation("开始验证与求解：" + solverSource.name);
            solverResult = null; solverPlayback = null; solverPlaying = false;
            solverLevel = null; solverInitial = null;
            solverBlocks.Clear(); solverCells.Clear(); solverObjects.Clear();

            solverOverlay = SokobanUI.Panel(Root, "SolverOverlay", SokobanTheme.Background,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            LabelAt(solverOverlay, "SolverTitle", "验证与解答 · " + solverSource.name, 38,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -30f), new Vector2(1480f, 64f));
            ButtonAt(solverOverlay, "SolverClose", "返回编辑", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-Margin, -30f), new Vector2(210f, 54f), CloseSolver);
            var boardPanel = InsetPanel(solverOverlay, "SolverBoardPanel", SokobanTheme.BoardBackground,
                Margin, 680f, 116f, BottomInset);
            solverBoard = new GameObject("SolverBoard", typeof(RectTransform)).GetComponent<RectTransform>();
            solverBoard.SetParent(boardPanel, false);
            solverBoard.anchorMin = solverBoard.anchorMax = solverBoard.pivot = new Vector2(0.5f, 0.5f);

            var side = InsetPanel(solverOverlay, "SolverResultPanel", SokobanTheme.Panel, 1276f, Margin, 116f, BottomInset);
            solverSummary = LabelAt(side, "SolverSummary", "正在检查关卡并搜索解答…", 21,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(568f, 124f), TextAnchor.UpperLeft);
            solverCurrent = LabelAt(side, "SolverCurrent", "", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -150f), new Vector2(568f, 62f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            solverStepScroll = SokobanUI.ScrollList(side, "SolverSteps", Vector2.zero, Vector2.one,
                new Vector2(16f, 218f), new Vector2(-16f, -224f), out solverStepContent);

            solverPrevious = ButtonAt(side, "SolverPrevious", "上一步", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(18f, 154f), new Vector2(178f, 50f), PreviousSolverStep);
            solverPlay = ButtonAt(side, "SolverPlay", "播放", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 154f), new Vector2(178f, 50f), ToggleSolverPlay, SokobanTheme.Accent);
            solverNext = ButtonAt(side, "SolverNext", "下一步", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-18f, 154f), new Vector2(178f, 50f), NextSolverStep);
            solverReset = ButtonAt(side, "SolverReset", "回到起点", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(18f, 94f), new Vector2(178f, 50f), ResetSolverPlayback);
            solverCopy = ButtonAt(side, "SolverCopy", "复制完整过程", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-18f, 94f), new Vector2(368f, 50f), CopySolverReport);
            solverSpeed = ButtonAt(side, "SolverSpeed", "速度：正常", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(18f, 34f), new Vector2(178f, 50f), CycleSolverSpeed);
            ButtonAt(side, "SolverReturn", "取消 / 返回编辑", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-18f, 34f), new Vector2(368f, 50f), CloseSolver);
            LabelAt(solverOverlay, "SolverKeys", "空格：播放 / 暂停    ← / →：单步    Home：起点    Esc：返回编辑", 20,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, 28f), new Vector2(1500f, 44f),
                TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            UpdateSolverButtons();
            SetButtonLabel(solverSpeed, "速度：" + new[] { "慢速", "正常", "快速" }[solverSpeedIndex]);

            if (!SokobanSolverAdapter.TryCreateSnapshot(solverSource, out var snapshot, out var error))
            {
                PublishSolverResult(new SokobanSolveResult { Status = SokobanSolveStatus.Invalid, Message = error });
                LabelAt(boardPanel, "SolverInvalidBoard", "关卡数据需要修正\n返回编辑后根据右侧错误提示修改", 28,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 120f), TextAnchor.MiddleCenter);
                return;
            }
            solverLevel = new SokobanLevelRuntime(solverSource);
            solverInitial = solverLevel.CreateInitialState(); solverInitial.RefreshWin(solverLevel);
            BuildSolverBoard(); RefreshSolverBoard();
            if (cachedResult != null) { PublishSolverResult(cachedResult); return; }
            var job = new SolverJob { Cancellation = new CancellationTokenSource() };
            var token = job.Cancellation.Token;
            job.Task = Task.Run(() => SokobanSolver.Solve(snapshot, new SokobanSolveOptions(), token,
                progress => Interlocked.Exchange(ref job.Progress, progress)));
            solverJob = job;
        }

        private void BuildSolverBoard()
        {
            var width = solverLevel.Width; var height = solverLevel.Height;
            var size = Mathf.Min(82f, Mathf.Min(1120f / width, 760f / height)) - 2f;
            var stride = size + 2f;
            solverBoard.sizeDelta = new Vector2(width * stride - 2f, height * stride - 2f);
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var cell = SokobanUI.Panel(solverBoard, $"SolverCell_{x}_{y}", SokobanTheme.BoardFloor,
                    new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                cell.pivot = new Vector2(0f, 1f); cell.anchoredPosition = new Vector2(x * stride, -y * stride);
                cell.sizeDelta = new Vector2(size, size);
                var point = new SokobanGridPoint(x, y);
                SokobanUI.GoalMarker(cell).gameObject.SetActive(solverLevel.Goals.Contains(point));
                var obj = SokobanUI.Panel(cell, "Object", SokobanTheme.Box,
                    new Vector2(0.19f, 0.19f), new Vector2(0.81f, 0.81f), Vector2.zero, Vector2.zero);
                obj.GetComponent<Image>().raycastTarget = false;
                solverCells.Add(cell.GetComponent<Image>()); solverObjects.Add(obj.GetComponent<Image>());
            }
        }
        private void RefreshSolverBoard()
        {
            if (solverLevel == null) return;
            var state = solverPlayback?.State ?? solverInitial;
            for (var y = 0; y < solverLevel.Height; y++)
            for (var x = 0; x < solverLevel.Width; x++)
            {
                var index = y * solverLevel.Width + x; var point = new SokobanGridPoint(x, y);
                solverCells[index].color = solverLevel.IsWall(point) ? SokobanTheme.BoardWall :
                    solverLevel.Goals.Contains(point) ? SokobanTheme.Goal : SokobanTheme.BoardFloor;
                var player = state.Player == point; var box = state.Boxes.Contains(point);
                solverObjects[index].gameObject.SetActive(player || box);
                solverObjects[index].color = player ? SokobanTheme.Player : SokobanTheme.Box;
            }
        }

        private bool UpdateSolverWorkflow()
        {
            if (solverOverlay == null) return false;
            if (solverJob != null)
            {
                if (solverJob.Task.IsCompleted)
                {
                    var job = solverJob; solverJob = null;
                    var result = job.Task.IsFaulted
                        ? new SokobanSolveResult { Status = SokobanSolveStatus.Error, Message = job.Task.Exception.GetBaseException().Message }
                        : job.Task.Result;
                    job.Cancellation.Dispose(); PublishSolverResult(result);
                }
                else if (Time.unscaledTime - solverLastProgressUi >= 0.1f)
                {
                    solverLastProgressUi = Time.unscaledTime;
                    var progress = Volatile.Read(ref solverJob.Progress);
                    if (progress != null) solverSummary.text = $"正在搜索解答…\n已搜索 {progress.ExploredNodes} 个节点\n待搜索 {progress.FrontierNodes} 个节点\n耗时 {progress.ElapsedMs / 1000f:0.0} 秒 · 可随时取消";
                }
            }
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseSolver(); return true; }
            if (Input.GetKeyDown(KeyCode.Space)) ToggleSolverPlay();
            else if (Input.GetKeyDown(KeyCode.LeftArrow)) PreviousSolverStep();
            else if (Input.GetKeyDown(KeyCode.RightArrow)) NextSolverStep();
            else if (Input.GetKeyDown(KeyCode.Home)) ResetSolverPlayback();
            if (solverPlaying && Time.unscaledTime >= solverNextTick)
            {
                solverNextTick = Time.unscaledTime + SolverIntervals[solverSpeedIndex];
                if (solverPlayback.Next()) RecordOperation("自动回放：" + solverPlayback.StepText(solverPlayback.Index - 1));
                else solverPlaying = false;
                if (solverPlayback.Index == solverPlayback.Steps.Count)
                { solverPlaying = false; RecordOperation("自动回放完成：全部箱子已到达目标"); }
                RefreshSolverPlayback();
            }
            return true;
        }

        private void PublishSolverResult(SokobanSolveResult result)
        {
            solverResult = result;
            if (result.IsSolved)
            {
                try { solverPlayback = new SokobanSolutionPlayback(solverSource, result); }
                catch (Exception error)
                { result.Status = SokobanSolveStatus.Error; result.Message = "解答回放校验失败：" + error.Message; solverPlayback = null; }
            }
            solverSummary.text = StatusName(result.Status) + "\n" + (result.IsSolved
                ? $"最少推箱 {result.Pushes} 次 · 移动 {result.Moves.Length} 步\n搜索 {result.ExploredNodes} 个节点 · {result.ElapsedMs} ms\n同推箱数下移动步数最少"
                : $"搜索 {result.ExploredNodes} 个节点 · {result.ElapsedMs} ms");
            if (solverPlayback != null)
            {
                BuildSolverSteps(); RefreshSolverPlayback();
            }
            else
            {
                AddSolverText("SolverFailure", result.Message + "\n\n" +
                    ((result.Status == SokobanSolveStatus.Timeout || result.Status == SokobanSolveStatus.LimitReached)
                    ? "尚未找到解答，不能据此判定无解。" : "返回编辑可调整关卡后再次验证。"), 300f);
                solverCurrent.text = "";
            }
            // Modal editing is blocked, and the exact source revision is checked again before writing metadata.
            if (solverTab != null && ReferenceEquals(Active, solverTab) && JsonUtility.ToJson(Data) == solverSourceJson)
            {
                PushUndo();
                SokobanVerificationMetadata.Apply(Data, result);
                Active.Dirty = true; RefreshTabs();
            }
            UpdateSolverButtons();
            RecordOperation("验证结果（" + solverSource.name + "）：" + StatusName(result.Status) +
                (result.IsSolved ? $"，移动 {result.Moves.Length} 步，推箱 {result.Pushes} 次" : "，" + result.Message) +
                $"，搜索 {result.ExploredNodes} 个节点，耗时 {result.ElapsedMs} ms");
        }

        private UnityEngine.UI.Text AddSolverText(string name, string text, float height)
        {
            var label = SokobanUI.Text(solverStepContent, name, text, 19, SokobanTheme.TextPrimary, TextAnchor.UpperLeft);
            label.verticalOverflow = VerticalWrapMode.Overflow;
            var layout = label.gameObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height; layout.minHeight = height;
            return label;
        }
        private void BuildSolverSteps()
        {
            const int blockSize = 16;
            if (solverPlayback.Steps.Count == 0)
            { AddSolverText("SolverZeroSteps", "0. 初始状态：所有箱子已在目标上。\n无需移动，关卡已完成。", 96f); return; }
            for (var first = 0; first < solverPlayback.Steps.Count; first += blockSize)
            {
                var count = Math.Min(blockSize, solverPlayback.Steps.Count - first);
                var lines = first == 0 ? 1 : 0;
                for (var i = first; i < first + count; i++) lines += solverPlayback.Steps[i].Pushed ? 2 : 1;
                var block = new StepBlock { First = first, Count = count };
                block.Label = AddSolverText("SolverStepBlock_" + first, "", lines * 32f + 12f);
                solverBlocks.Add(block); RefreshStepBlock(block);
            }
        }
        private void RefreshStepBlock(StepBlock block)
        {
            var text = new StringBuilder();
            if (block.First == 0) text.AppendLine("0. 初始状态  玩家 " + solverPlayback.Level.PlayerStart);
            for (var i = block.First; i < block.First + block.Count; i++)
            {
                var current = solverPlayback.Index == i + 1;
                if (current) text.Append("<color=#F0A849>▶ ");
                text.Append(solverPlayback.StepText(i));
                if (current) text.Append("</color>");
                text.AppendLine();
            }
            block.Label.text = text.ToString();
        }
        private void RefreshSolverPlayback()
        {
            if (solverPlayback == null) return;
            RefreshSolverBoard();
            var atEnd = solverPlayback.Index == solverPlayback.Steps.Count;
            solverCurrent.text = $"当前 {solverPlayback.Index} / {solverPlayback.Steps.Count} 步 · 已推箱 {solverPlayback.State.PushCount} 次\n" +
                (atEnd ? "完成：全部箱子已到达目标。" : solverPlayback.Index == 0 ? "起点：尚未执行任何步骤。" :
                "当前动作：向" + SokobanSolutionPlayback.DirectionName(solverPlayback.Steps[solverPlayback.Index - 1].Direction) +
                (solverPlayback.Steps[solverPlayback.Index - 1].Pushed ? "推箱" : "移动"));
            foreach (var block in solverBlocks) RefreshStepBlock(block);
            UnityEngine.Canvas.ForceUpdateCanvases();
            solverStepScroll.verticalNormalizedPosition = 1f - (float)solverPlayback.Index / Math.Max(1, solverPlayback.Steps.Count);
            UpdateSolverButtons();
        }
        private void UpdateSolverButtons()
        {
            if (solverPlay == null) return;
            var ready = solverPlayback != null;
            solverPrevious.interactable = ready && solverPlayback.Index > 0;
            solverNext.interactable = ready && solverPlayback.Index < solverPlayback.Steps.Count;
            solverPlay.interactable = ready && solverPlayback.Steps.Count > 0;
            solverReset.interactable = ready;
            solverCopy.interactable = solverResult != null;
            solverSpeed.interactable = ready && solverPlayback.Steps.Count > 0;
            SetButtonLabel(solverPlay, solverPlaying ? "暂停" : ready && solverPlayback.Index == solverPlayback.Steps.Count && solverPlayback.Steps.Count > 0 ? "重新播放" : "播放");
        }
        private static void SetButtonLabel(Button button, string value)
        {
            var label = button.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
            if (label != null) label.text = value;
        }
        private void ToggleSolverPlay()
        {
            if (solverPlayback == null || solverPlayback.Steps.Count == 0) return;
            if (solverPlayback.Index == solverPlayback.Steps.Count) solverPlayback.Reset();
            solverPlaying = !solverPlaying; solverNextTick = Time.unscaledTime + SolverIntervals[solverSpeedIndex];
            RecordOperation(solverPlaying ? "播放解答" : "暂停解答回放");
            RefreshSolverPlayback();
        }
        private void PreviousSolverStep()
        {
            if (solverPlayback == null) return;
            var previous = solverPlayback.Index;
            solverPlaying = false; solverPlayback.Previous(); RefreshSolverPlayback();
            if (solverPlayback.Index != previous) RecordOperation($"解答上一步：返回第 {solverPlayback.Index} 步");
        }
        private void NextSolverStep()
        {
            if (solverPlayback == null) return;
            var previous = solverPlayback.Index;
            solverPlaying = false; solverPlayback.Next(); RefreshSolverPlayback();
            if (solverPlayback.Index != previous) RecordOperation("解答下一步：" + solverPlayback.StepText(solverPlayback.Index - 1));
        }
        private void ResetSolverPlayback()
        {
            if (solverPlayback == null) return;
            solverPlaying = false; solverPlayback.Reset(); RefreshSolverPlayback();
            RecordOperation("解答回放：回到起点");
        }
        private void CycleSolverSpeed()
        {
            solverSpeedIndex = (solverSpeedIndex + 1) % SolverIntervals.Length;
            SetButtonLabel(solverSpeed, "速度：" + new[] { "慢速", "正常", "快速" }[solverSpeedIndex]);
            solverNextTick = Time.unscaledTime + SolverIntervals[solverSpeedIndex];
            RecordOperation("修改回放速度：" + new[] { "慢速", "正常", "快速" }[solverSpeedIndex]);
        }
        private void CopySolverReport()
        {
            if (solverResult == null) return;
            GUIUtility.systemCopyBuffer = solverPlayback != null ? solverPlayback.FullReport() :
                "关卡：" + solverSource.name + "\n" + StatusName(solverResult.Status) + "\n" + solverResult.Message;
            SetButtonLabel(solverCopy, "已复制完整过程");
            RecordOperation("复制完整求解过程");
        }
        private static string StatusName(SokobanSolveStatus status)
        {
            switch (status)
            {
                case SokobanSolveStatus.Solved: return "验证通过 · 有解";
                case SokobanSolveStatus.Unsolvable: return "验证完成 · 无解";
                case SokobanSolveStatus.Invalid: return "验证失败 · 关卡数据错误";
                case SokobanSolveStatus.Timeout: return "求解超时 · 结果未确定";
                case SokobanSolveStatus.LimitReached: return "搜索达到上限 · 结果未确定";
                case SokobanSolveStatus.Cancelled: return "求解已取消";
                default: return "求解遇到错误";
            }
        }
        private void StopSolverJob()
        {
            if (solverJob == null) return;
            solverJob.Cancellation.Cancel(); solverJob.Cancellation.Dispose(); solverJob = null;
        }
        private void CloseSolver()
        {
            if (solverOverlay != null) RecordOperation(solverJob != null ? "关闭验证面板并取消求解任务" : "关闭验证面板");
            StopSolverJob(); solverPlaying = false;
            if (solverOverlay != null)
            {
                solverOverlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(solverOverlay.gameObject);
            }
            solverOverlay = null; solverPlayback = null; solverResult = null;
            solverBlocks.Clear(); solverCells.Clear(); solverObjects.Clear();
            RefreshDetail();
        }
        private void OnDestroy() { CaptureSession(); StopSolverJob(); StopGenerationJob(); }
    }
}
