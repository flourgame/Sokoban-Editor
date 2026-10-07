using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    public sealed class SokobanVerificationInput
    {
        public SokobanSolverLevel Level;
        public string Error;
    }
    public sealed class SokobanBatchVerificationProgress
    {
        public int Requested, Completed, CurrentIndex;
        public float Fraction;
        public long ElapsedMs;
        public SokobanSolveProgress Current;
    }
    public sealed class SokobanBatchVerificationResult
    {
        public bool Cancelled;
        public readonly List<SokobanSolveResult> Results = new List<SokobanSolveResult>();
    }

    /// <summary>只处理主线程已复制的求解快照，顺序验证且逐关发布。</summary>
    public static class SokobanBatchVerifier
    {
        public static SokobanBatchVerificationResult Run(IReadOnlyList<SokobanVerificationInput> inputs,
            SokobanSolveOptions options, CancellationToken token = default,
            Action<SokobanBatchVerificationProgress> progress = null, Action<int, SokobanSolveResult> completed = null)
        {
            if (inputs == null || inputs.Count == 0) throw new ArgumentException("请选择要验证的关卡");
            if (options == null || options.TimeoutMs < 1) throw new ArgumentException("每关验证预算必须大于零");
            var limits = new SokobanSolveOptions { TimeoutMs = options.TimeoutMs,
                MaxExpandedNodes = options.MaxExpandedNodes, MaxDiscoveredStates = options.MaxDiscoveredStates };
            var batch = new SokobanBatchVerificationResult(); var watch = Stopwatch.StartNew();
            for (var index = 0; index < inputs.Count; index++)
            {
                if (token.IsCancellationRequested) break;
                var currentIndex = index;
                progress?.Invoke(new SokobanBatchVerificationProgress { Requested = inputs.Count, Completed = index,
                    CurrentIndex = index, Fraction = (float)index / inputs.Count, ElapsedMs = watch.ElapsedMilliseconds });
                var input = inputs[index];
                var result = !string.IsNullOrEmpty(input.Error) ? new SokobanSolveResult { Status = SokobanSolveStatus.Invalid, Message = input.Error } :
                    SokobanSolver.Solve(input.Level, limits, token, current => progress?.Invoke(new SokobanBatchVerificationProgress
                    { Requested = inputs.Count, Completed = currentIndex, CurrentIndex = currentIndex, Current = current,
                        Fraction = (currentIndex + Math.Min(0.95f, (float)current.ElapsedMs / limits.TimeoutMs)) / inputs.Count,
                        ElapsedMs = watch.ElapsedMilliseconds }));
                batch.Results.Add(result); completed?.Invoke(index, result);
                progress?.Invoke(new SokobanBatchVerificationProgress { Requested = inputs.Count, Completed = index + 1,
                    CurrentIndex = index, Fraction = (float)(index + 1) / inputs.Count, ElapsedMs = watch.ElapsedMilliseconds });
                if (result.Status == SokobanSolveStatus.Cancelled) break;
            }
            batch.Cancelled = token.IsCancellationRequested &&
                (batch.Results.Count < inputs.Count || batch.Results[batch.Results.Count - 1].Status == SokobanSolveStatus.Cancelled);
            return batch;
        }
    }
}
