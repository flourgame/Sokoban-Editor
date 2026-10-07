using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    /// <summary>运行期间持有批量任务和结果，关闭面板/离开编辑场景均不取消。</summary>
    public sealed class SokobanBatchGenerationService : MonoBehaviour
    {
        public sealed class Entry
        {
            public SokobanGenerationResult Result;
            public SokobanJsonLevel Level;
            public bool Saved;
            public string SaveError;
        }

        private static SokobanBatchGenerationService instance;
        internal static SokobanBatchGenerationService Existing => instance;
        public static SokobanBatchGenerationService Instance
        {
            get
            {
                if (instance == null)
                    instance = new GameObject("SokobanBatchGenerationService").AddComponent<SokobanBatchGenerationService>();
                return instance;
            }
        }

        public readonly List<Entry> Entries = new List<Entry>();
        public SokobanGenerationSettings Settings { get; private set; }
        public int Requested { get; private set; } = 10;
        public bool Running => task != null;
        public bool Cancelling => cancellation != null && cancellation.IsCancellationRequested;
        public bool Cancelled { get; private set; }
        public string Error { get; private set; }
        public SokobanBatchProgress Progress => Volatile.Read(ref progress);
        public int Succeeded => Entries.Count(e => e.Level != null);
        public int Failed => Entries.Count - Succeeded;
        public bool PendingOpen { get; set; }
        public string Summary => (Running ? (Cancelling ? "正在取消" : "正在批量生成") :
            Cancelled ? "批量生成已取消" : !string.IsNullOrEmpty(Error) ? "批量生成遇到错误" : "批量生成已结束") +
            $" · 成功 {Succeeded} / {Requested} · 未匹配 {Failed}" +
            (!Running && Entries.Count < Requested ? $" · 未处理 {Requested - Entries.Count}" : "");

        private Task<SokobanBatchResult> task;
        private CancellationTokenSource cancellation;
        private readonly ConcurrentQueue<SokobanGenerationResult> queue = new ConcurrentQueue<SokobanGenerationResult>();
        private SokobanBatchProgress progress;

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool StartBatch(SokobanGenerationSettings settings, int count, out string error)
        {
            error = Running ? "已有批量任务正在运行，请等待完成或取消。" : SokobanBatchGenerator.Validate(settings, count);
            if (!string.IsNullOrEmpty(error)) return false;
            DismissNotification(); Entries.Clear(); Cancelled = false; Error = null;
            Settings = settings.Copy(); Requested = count;
            progress = new SokobanBatchProgress { Requested = count };
            cancellation = new CancellationTokenSource(); var token = cancellation.Token;
            var snapshot = Settings.Copy();
            task = Task.Run(() => SokobanBatchGenerator.Generate(snapshot, count, token,
                update => Interlocked.Exchange(ref progress, update), item => queue.Enqueue(item)));
            SokobanEditorSceneController.RecordBackgroundOperation($"开始批量生成 {count} 个：{settings.width}×{settings.height}，" +
                $"箱子 {settings.BoxCountLabel}，推箱 {settings.minPushes}–{settings.maxPushes} 次，复杂度 {settings.DifficultyLabel}，" +
                $"内部墙 {settings.WallPercentLabel}，基础种子 {settings.seed}，每关预算 {settings.budgetSeconds} 秒 / {settings.maxCandidates} 候选");
            return true;
        }

        public void Cancel()
        {
            if (!Running || Cancelling) return;
            cancellation.Cancel();
            SokobanEditorSceneController.RecordBackgroundOperation("请求取消批量生成；保留已完成结果");
        }

        private void DrainResults()
        {
            while (queue.TryDequeue(out var result))
            {
                var entry = new Entry { Result = result };
                if (result.Status == SokobanGenerationStatus.Success)
                    entry.Level = SokobanEditorSceneController.CreateBatchJson(result);
                Entries.Add(entry);
                SokobanEditorSceneController.RecordBackgroundOperation($"第 {Entries.Count}/{Requested} 个：" +
                    (entry.Level != null ? $"成功，种子 {result.Settings.seed}，移动 {result.Solution.Moves.Length} 步 / 推箱 {result.Solution.Pushes} 次" : result.Message));
            }
        }

        private void Update()
        {
            DrainResults();
            if (task == null || !task.IsCompleted) return;
            DrainResults(); // IsCompleted 后再读一次，保证最后一个结果先发布再通知。
            if (task.IsFaulted) Error = task.Exception.GetBaseException().Message;
            else if (task.IsCanceled) Cancelled = true;
            else Cancelled = task.Result.Cancelled;
            task = null; cancellation.Dispose(); cancellation = null;
            SokobanEditorSceneController.RecordBackgroundOperation(Summary + (Error == null ? "" : "：" + Error));
            ShowNotification();
        }

        public string SaveAll()
        {
            if (Running) return "请等待批量生成结束后保存。";
            var saved = 0; var failed = 0;
            foreach (var entry in Entries)
            {
                if (entry.Level == null || entry.Saved) continue;
                var descriptor = new SokobanLevelDescriptor { LevelId = entry.Level.levelId, Title = entry.Level.name,
                    Folder = entry.Level.levelId, Source = "Generated" };
                try
                {
                    // 不覆盖的原子发布阻止覆盖同 ID 文件；整批只刷新一次资产。
                    SokobanLevelRepository.SaveJson(entry.Level, descriptor, refreshAssets: false, overwrite: false);
                    entry.Saved = true; entry.SaveError = null; saved++;
                }
                catch (Exception exception) { entry.SaveError = exception.Message; failed++; }
            }
#if UNITY_EDITOR
            if (saved > 0) UnityEditor.AssetDatabase.Refresh();
#endif
            var message = $"本次保存 {saved} 个 · 已保存 {Entries.Count(e => e.Saved)} 个 · 保存失败 {failed} 个";
            SokobanEditorSceneController.RecordBackgroundOperation(message);
            return message;
        }

        internal void MarkSaved(SokobanJsonLevel data)
        {
            var entry = Entries.FirstOrDefault(e => e.Level != null && e.Level.levelId == data.levelId);
            if (entry != null) { entry.Saved = true; entry.SaveError = null; }
        }

        public void RequestResults()
        {
            DismissNotification(); PendingOpen = true;
            if (SceneManager.GetActiveScene().name != "editor") SceneManager.LoadScene("editor");
        }

        private void ShowNotification()
        {
            SokobanTaskNotification.Show("Batch", Summary +
                (Succeeded > 0 ? "\n结果已保留，可查看并保存。" : "\n可查看原因并调整参数重试。"), RequestResults);
        }
        public void DismissNotification() => SokobanTaskNotification.Dismiss("Batch");

        private void OnDestroy()
        {
            if (instance != this) return;
            if (cancellation != null)
            {
                cancellation.Cancel();
                // 工作者仍可能使用令牌，在结束后释放；回调只有纯 C# 队列写入。
                var source = cancellation;
                task?.ContinueWith(_ => source.Dispose(), TaskScheduler.Default);
            }
            instance = null;
        }
    }
}
