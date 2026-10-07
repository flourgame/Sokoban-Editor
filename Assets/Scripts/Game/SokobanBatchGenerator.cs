using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    public sealed class SokobanBatchProgress
    {
        public int Requested, Completed, Succeeded, Failed;
        public long ElapsedMs;
        public float Fraction;
        public SokobanGenerationProgress Current;
    }

    public sealed class SokobanBatchResult
    {
        public int Requested;
        public bool Cancelled;
        public readonly List<SokobanGenerationResult> Items = new List<SokobanGenerationResult>();
    }

    /// <summary>顺序生成，整个批次共用布局去重集合；每关独立预算，不占用 Unity 主线程。</summary>
    public static class SokobanBatchGenerator
    {
        public static string Validate(SokobanGenerationSettings settings, int count)
        {
            if (count < 1 || count > 100) return "生成数量须为 1–100";
            return settings == null ? "缺少生成参数" : settings.Validate();
        }

        public static SokobanBatchResult Generate(SokobanGenerationSettings parameters, int count,
            CancellationToken token = default, Action<SokobanBatchProgress> progress = null,
            Action<SokobanGenerationResult> itemCompleted = null)
        {
            var settings = parameters?.Copy();
            var error = Validate(settings, count);
            if (!string.IsNullOrEmpty(error)) throw new ArgumentException(error);
            var result = new SokobanBatchResult { Requested = count };
            var layouts = new HashSet<string>();
            var clock = Stopwatch.StartNew(); var succeeded = 0;
            for (var index = 0; index < count; index++)
            {
                if (token.IsCancellationRequested) break;
                var itemSettings = settings.Copy();
                itemSettings.seed = unchecked(settings.seed + index * 104729);
                var completed = index; var successCount = succeeded;
                progress?.Invoke(new SokobanBatchProgress { Requested = count, Completed = completed,
                    Succeeded = successCount, Failed = completed - successCount, ElapsedMs = clock.ElapsedMilliseconds,
                    Fraction = (float)completed / count });
                var item = SokobanGenerator.Generate(itemSettings, token, current =>
                {
                    var within = Math.Max((float)current.elapsedMs / (settings.budgetSeconds * 1000),
                        (float)current.attempts / settings.maxCandidates);
                    progress?.Invoke(new SokobanBatchProgress { Requested = count, Completed = completed,
                        Succeeded = successCount, Failed = completed - successCount, Current = current,
                        ElapsedMs = clock.ElapsedMilliseconds, Fraction = (completed + Math.Min(0.95f, within)) / count });
                }, layouts);
                // 正在取消的一关不计作失败；已完成结果仍交付。
                if (item.Status == SokobanGenerationStatus.Cancelled) break;
                result.Items.Add(item);
                if (item.Status == SokobanGenerationStatus.Success)
                { succeeded++; layouts.Add(SokobanGenerator.LayoutKey(item.Level)); }
                itemCompleted?.Invoke(item);
                progress?.Invoke(new SokobanBatchProgress { Requested = count, Completed = result.Items.Count,
                    Succeeded = succeeded, Failed = result.Items.Count - succeeded, ElapsedMs = clock.ElapsedMilliseconds,
                    Fraction = (float)result.Items.Count / count });
            }
            result.Cancelled = token.IsCancellationRequested && result.Items.Count < count;
            return result;
        }
    }
}
