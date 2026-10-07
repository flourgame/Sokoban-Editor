using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed class SokobanBatchVerificationService : MonoBehaviour
    {
        public sealed class Entry
        {
            public SokobanLevelDescriptor Descriptor;
            public string OriginalJson, SourceRevision, SaveMessage;
            public SokobanJsonLevel Source, Updated;
            public SokobanVerificationInput Input;
            public SokobanSolveResult Result;
            public bool Saved;

            public bool MatchesSavedResult(SokobanJsonLevel current) => Saved && Updated != null && current != null &&
                SokobanVerificationMetadata.Revision(current) == SourceRevision && current.verifiedMoves == Updated.verifiedMoves &&
                JsonUtility.ToJson(current.solution) == JsonUtility.ToJson(Updated.solution);
        }
        private sealed class CompletedItem { internal int Index; internal SokobanSolveResult Result; }
        private static SokobanBatchVerificationService instance;
        internal static SokobanBatchVerificationService Existing => instance;
        public static SokobanBatchVerificationService Instance
        {
            get
            {
                if (instance == null) instance = new GameObject("SokobanBatchVerificationService").AddComponent<SokobanBatchVerificationService>();
                return instance;
            }
        }
        public readonly List<Entry> Entries = new List<Entry>();
        public bool Running => task != null;
        public bool Cancelling => cancellation != null && cancellation.IsCancellationRequested;
        public bool Cancelled { get; private set; }
        public string Error { get; private set; }
        public int TimeoutSeconds { get; private set; } = 30;
        public int Revision { get; private set; }
        public bool PendingOpen { get; set; }
        public SokobanBatchVerificationProgress Progress => Volatile.Read(ref progress);
        public int Completed => Entries.Count(e => e.Result != null);
        public int Solved => Entries.Count(e => e.Result != null && e.Result.IsSolved);
        public int Unsolvable => Entries.Count(e => e.Result != null && e.Result.Status == SokobanSolveStatus.Unsolvable);
        public int Unsaved => Entries.Count(e => e.Result != null && !e.Saved);
        public string Summary => (Running ? Cancelling ? "正在取消批量验证" : "正在批量验证" :
            Cancelled ? "批量验证已取消" : Error != null ? "批量验证遇到错误" : "批量验证已结束") +
            $" · 已处理 {Completed}/{Entries.Count} · 有解 {Solved} · 无解 {Unsolvable}";
        private Task<SokobanBatchVerificationResult> task;
        private CancellationTokenSource cancellation;
        private readonly ConcurrentQueue<CompletedItem> queue = new ConcurrentQueue<CompletedItem>();
        private SokobanBatchVerificationProgress progress;
        private bool assetsDirty;

        private void Awake() { if (instance != null && instance != this) { Destroy(gameObject); return; } instance = this; DontDestroyOnLoad(gameObject); }

        public bool StartBatch(IEnumerable<SokobanLevelDescriptor> selected, int timeoutSeconds, out string error)
        {
            var descriptors = (selected ?? Enumerable.Empty<SokobanLevelDescriptor>()).GroupBy(d => d.LevelId).Select(g => g.First()).ToList();
            error = Running ? "已有批量验证任务正在运行" : descriptors.Count == 0 ? "请选择要验证的关卡" :
                timeoutSeconds < 1 || timeoutSeconds > 300 ? "每关验证预算须为 1–300 秒" : null;
            if (error != null) return false;
            DismissNotification(); Entries.Clear(); TimeoutSeconds = timeoutSeconds; Error = null; Cancelled = false; assetsDirty = false;
            foreach (var descriptor in descriptors)
            {
                var entry = new Entry { Descriptor = descriptor, Input = new SokobanVerificationInput() };
                try
                {
                    SokobanLevelRepository.PrepareWritableCopy(descriptor);
                    entry.OriginalJson = SokobanLevelRepository.ReadSavedText(descriptor);
                    entry.Source = SokobanLevelRepository.Parse(entry.OriginalJson);
                    if (entry.Source == null || entry.Source.levelId != descriptor.LevelId) throw new IOException("关卡读取失败或 ID 不一致");
                    entry.SourceRevision = SokobanVerificationMetadata.Revision(entry.Source);
                    if (!SokobanSolverAdapter.TryCreateSnapshot(entry.Source, out entry.Input.Level, out var validationError)) entry.Input.Error = validationError;
                }
                catch (Exception exception) { entry.Input.Error = "读取失败：" + exception.Message; }
                Entries.Add(entry);
            }
            progress = new SokobanBatchVerificationProgress { Requested = Entries.Count };
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            var inputs = Entries.Select(e => e.Input).ToArray(); var options = new SokobanSolveOptions { TimeoutMs = timeoutSeconds * 1000 };
            task = Task.Run(() => SokobanBatchVerifier.Run(inputs, options, token,
                update => Interlocked.Exchange(ref progress, update), (index, result) => queue.Enqueue(new CompletedItem { Index = index, Result = result })));
            Revision++;
            SokobanEditorSceneController.RecordBackgroundOperation($"开始批量验证 {Entries.Count} 个已保存关卡，每关预算 {timeoutSeconds} 秒；" +
                string.Join("、", Entries.Select(e => e.Descriptor.LevelId)), "批量验证");
            return true;
        }

        public void Cancel()
        {
            if (!Running || Cancelling) return;
            cancellation.Cancel(); SokobanEditorSceneController.RecordBackgroundOperation("请求取消批量验证，保留已完成结果", "批量验证");
        }

        private void Drain()
        {
            while (queue.TryDequeue(out var completed))
            {
                var entry = Entries[completed.Index]; entry.Result = completed.Result;
                if (entry.Result.IsSolved)
                {
                    try { new SokobanSolutionPlayback(entry.Source, entry.Result); }
                    catch (Exception error) { entry.Result = new SokobanSolveResult { Status = SokobanSolveStatus.Error, Message = "解答回放校验失败：" + error.Message }; }
                }
                if (entry.Source != null && entry.OriginalJson != null)
                {
                    entry.Saved = SokobanVerificationMetadata.TryPersist(entry.Descriptor, entry.OriginalJson, entry.Result, out entry.Updated, out entry.SaveMessage);
                    if (entry.Saved)
                    {
                        assetsDirty |= Path.GetFullPath(entry.Descriptor.FilePath).StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                        var controller = UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>();
                        if (controller != null) controller.AcceptBatchVerification(entry);
                    }
                }
                else entry.SaveMessage = "源文件无法读取，结果未写入";
                Revision++;
                SokobanEditorSceneController.RecordBackgroundOperation($"{entry.Descriptor.DisplayTitle} / {entry.Descriptor.LevelId}：" +
                    SokobanEditorSceneController.VerificationStatusName(entry.Result.Status) +
                    (entry.Result.IsSolved ? $"，移动 {entry.Result.Moves.Length} 步 / 推箱 {entry.Result.Pushes} 次" : "，" + entry.Result.Message) +
                    "；" + entry.SaveMessage, "批量验证");
            }
        }

        private void Update()
        {
            Drain();
            if (task == null || !task.IsCompleted) return;
            Drain();
            if (task.IsFaulted) Error = task.Exception.GetBaseException().Message;
            else if (task.IsCanceled) Cancelled = true;
            else Cancelled = task.Result.Cancelled;
            task = null; cancellation.Dispose(); cancellation = null; Revision++;
#if UNITY_EDITOR
            if (assetsDirty) UnityEditor.AssetDatabase.Refresh();
#endif
            SokobanEditorSceneController.RecordBackgroundOperation(Summary + $" · 未写入 {Unsaved} 个", "批量验证");
            SokobanTaskNotification.Show("Verification", Summary + $"\n结果已保留 · 未写入 {Unsaved} 个，可查看详情。", RequestResults);
        }

        public void RequestResults()
        {
            DismissNotification(); PendingOpen = true;
            if (SceneManager.GetActiveScene().name != "editor") SceneManager.LoadScene("editor");
        }
        public void DismissNotification() => SokobanTaskNotification.Dismiss("Verification");
        private void OnDestroy()
        {
            if (instance != this) return;
            if (cancellation != null)
            {
                cancellation.Cancel(); var source = cancellation;
                task?.ContinueWith(_ => source.Dispose(), TaskScheduler.Default);
            }
            instance = null;
        }
    }
}
