using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    public enum SokobanDifficulty { Simple = 1, Normal = 2, Hard = 3 }

    [Serializable]
    public sealed class SokobanDifficultyReport
    {
        public string evaluatorVersion = "complexity-1.0";
        public bool valid;
        public int difficulty;
        public float score, dependencyScore, planningScore, spaceScore, trapScore;
        public int dependencyEdges, dependencyChain, temporaryExcursions, goalDepartures, boxRevisits;
        public int sharedCriticalCells, destinationBlockPairs, solvedBranches, deadBranches, unknownBranches;
        public string witnessMoves = "";
        public string explanation = "";
    }

    public static class SokobanDifficultyEvaluator
    {
        private sealed class State
        {
            internal int Player, PrefixLength, Box, Direction;
            internal int[] Boxes;
        }
        private sealed class Trace
        {
            internal SokobanDifficultyReport Report = new SokobanDifficultyReport();
            internal List<State> States = new List<State>();
        }
        public static string Name(int difficulty) => difficulty == 1 ? "简单" : difficulty == 2 ? "普通" : "困难";
        private static float Clamp(float value) => Math.Min(1f, Math.Max(0f, value));

        public static SokobanDifficultyReport Evaluate(SokobanSolverLevel level, SokobanSolveResult solution,
            CancellationToken cancellation = default, int budgetMs = 1500, int maxProbes = 12, int maxProbeNodes = 2500)
        {
            if (solution == null || !solution.IsSolved) throw new ArgumentException("复杂度评估需要已求解关卡");
            var clock = Stopwatch.StartNew(); var grid = new SokobanGenerationGrid(level);
            var critical = grid.CriticalCells();
            var reference = Analyze(grid, critical, solution.Moves, cancellation);
            var best = reference.Report;
            var solved = 0; var dead = 0; var unknown = 0; var probes = 0;
            var visited = new HashSet<string>();
            var samples = Math.Min(4, reference.States.Count);
            for (var sample = 0; sample < samples; sample++)
            {
                cancellation.ThrowIfCancellationRequested();
                var index = samples == 1 ? 0 : sample * (reference.States.Count - 1) / (samples - 1);
                var state = reference.States[index]; var alternatives = grid.Pushes(state.Player, state.Boxes);
                var checkedHere = 0;
                foreach (var push in alternatives)
                {
                    if (push.Box == state.Box && push.Direction == state.Direction) continue;
                    if (probes >= maxProbes || checkedHere >= 3 || clock.ElapsedMilliseconds >= budgetMs) break;
                    var key = state.PrefixLength + ":" + push.Box + ":" + push.Direction;
                    if (!visited.Add(key)) continue;
                    probes++; checkedHere++;
                    var boxes = (int[])state.Boxes.Clone(); boxes[push.Box] = push.To;
                    var remaining = Math.Max(1, budgetMs - (int)clock.ElapsedMilliseconds);
                    var suffix = SokobanSolver.Solve(grid.Snapshot(push.Player, boxes), new SokobanSolveOptions
                    { TimeoutMs = Math.Min(100, remaining), MaxExpandedNodes = Math.Max(1, maxProbeNodes),
                        MaxDiscoveredStates = Math.Max(2, maxProbeNodes * 2) }, cancellation);
                    if (suffix.Status == SokobanSolveStatus.Cancelled) cancellation.ThrowIfCancellationRequested();
                    if (suffix.IsSolved)
                    {
                        solved++;
                        var moves = solution.Moves.Substring(0, state.PrefixLength) + push.Moves + suffix.Moves;
                        var alternative = Analyze(grid, critical, moves, cancellation).Report;
                        if (CoreScore(alternative) < CoreScore(best)) best = alternative;
                    }
                    else if (suffix.Status == SokobanSolveStatus.Unsolvable) dead++;
                    else unknown++;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            best.solvedBranches = solved; best.deadBranches = dead; best.unknownBranches = unknown;
            best.trapScore = solved + dead == 0 ? 0 : (float)dead / (solved + dead);
            best.score = CoreScore(best) + 15f * best.trapScore;
            best.difficulty = best.score < 25f ? 1 : best.score < 50f ? 2 : 3; best.valid = true;
            best.explanation = $"复杂度估计：{Name(best.difficulty)}（{best.score:0.0}/100）\n" +
                $"分项：依赖 {35f * best.dependencyScore:0.0}/35 · 腾挪 {30f * best.planningScore:0.0}/30 · 空间 {20f * best.spaceScore:0.0}/20 · 陷阱 {15f * best.trapScore:0.0}/15\n" +
                $"阻挡依赖 {best.dependencyEdges} 对 · 依赖链 {best.dependencyChain} 个箱子\n" +
                $"临时腾挪 {best.temporaryExcursions} 次 · 搬离目标 {best.goalDepartures} 次 · 重新处理箱子 {best.boxRevisits} 次\n" +
                $"共用关键格 {best.sharedCriticalCells} 个 · 推入格阻挡 {best.destinationBlockPairs} 对\n" +
                $"替代分支：可解 {solved} / 无解 {dead} / 未确定 {unknown}\n" +
                (best.witnessMoves == solution.Moves ? "按参考解答评估；尚未找到更简单的替代解。" : "已找到复杂度更低的替代解，按较低分数评估。") +
                "（有限探查，需试玩校准）";
            return best;
        }
        private static float CoreScore(SokobanDifficultyReport report) =>
            35f * report.dependencyScore + 30f * report.planningScore + 20f * report.spaceScore;

        private static Trace Analyze(SokobanGenerationGrid grid, bool[] critical, string moves, CancellationToken cancellation)
        {
            var trace = new Trace(); var report = trace.Report; report.witnessMoves = moves;
            var boxes = (int[])grid.Level.Boxes.Clone(); var player = grid.Level.Player;
            var goals = new HashSet<int>(grid.Level.Goals);
            var dependencies = new HashSet<int>(); var blockedPairs = new HashSet<int>();
            var usage = new Dictionary<int, HashSet<int>>();
            var awayFrom = new int[boxes.Length];
            for (var b = 0; b < boxes.Length; b++) awayFrom[b] = -1;
            var lastBox = -1; var usedBoxes = new HashSet<int>();
            for (var i = 0; i < moves.Length; i++)
            {
                cancellation.ThrowIfCancellationRequested();
                var d = Array.IndexOf(SokobanGenerationGrid.Directions, char.ToLowerInvariant(moves[i]));
                if (d < 0) throw new ArgumentException("解答包含非法方向");
                var next = grid.Neighbors[player, d];
                if (!grid.Floor(next)) throw new ArgumentException("解答移动到墙");
                var b = Array.IndexOf(boxes, next);
                if (b >= 0)
                {
                    if (!char.IsUpper(moves[i])) throw new ArgumentException("解答推箱标记不一致");
                    var to = grid.Neighbors[next, d];
                    if (!grid.Floor(to) || Array.IndexOf(boxes, to) >= 0) throw new ArgumentException("解答推箱非法");
                    trace.States.Add(new State { Player = player, Boxes = (int[])boxes.Clone(), PrefixLength = i, Box = b, Direction = d });
                    ObserveDependencies(grid, player, boxes, dependencies, blockedPairs);
                    var before = grid.GoalDistance[next]; var after = grid.GoalDistance[to];
                    if (before >= 0 && after > before && awayFrom[b] < 0) awayFrom[b] = before;
                    if (awayFrom[b] >= 0 && after <= awayFrom[b]) { report.temporaryExcursions++; awayFrom[b] = -1; }
                    if (goals.Contains(next) && !goals.Contains(to)) report.goalDepartures++;
                    if (lastBox != b && usedBoxes.Contains(b)) report.boxRevisits++;
                    usedBoxes.Add(b); lastBox = b;
                    ObserveUsage(usage, critical, next, b); ObserveUsage(usage, critical, to, b);
                    boxes[b] = to;
                }
                else if (char.IsUpper(moves[i])) throw new ArgumentException("解答推箱标记不一致");
                player = next;
            }
            foreach (var b in boxes) if (!goals.Contains(b)) throw new ArgumentException("解答未完成关卡");
            report.dependencyEdges = dependencies.Count;
            report.dependencyChain = LongestChain(dependencies, boxes.Length);
            foreach (var pair in usage) if (pair.Value.Count > 1) report.sharedCriticalCells++;
            report.destinationBlockPairs = blockedPairs.Count;
            report.dependencyScore = Clamp(dependencies.Count / 4f + Math.Max(0, report.dependencyChain - 1) / 6f);
            report.planningScore = Clamp((report.temporaryExcursions + 2f * report.goalDepartures + 0.5f * report.boxRevisits) / 4f);
            report.spaceScore = Clamp(report.sharedCriticalCells / 2f + blockedPairs.Count / 4f);
            return trace;
        }
        private static void ObserveUsage(Dictionary<int, HashSet<int>> usage, bool[] critical, int cell, int box)
        {
            if (!critical[cell]) return;
            if (!usage.TryGetValue(cell, out var set)) { set = new HashSet<int>(); usage[cell] = set; }
            set.Add(box);
        }
        private static void ObserveDependencies(SokobanGenerationGrid grid, int player, int[] boxes,
            HashSet<int> dependencies, HashSet<int> blockedPairs)
        {
            var reach = grid.Reach(player, boxes);
            for (var a = 0; a < boxes.Length; a++)
            {
                var without = grid.Reach(player, boxes, a);
                for (var b = 0; b < boxes.Length; b++)
                {
                    if (a == b) continue;
                    for (var d = 0; d < 4; d++)
                    {
                        var to = grid.Neighbors[boxes[b], d]; var support = grid.Neighbors[boxes[b], d ^ 1];
                        if (!grid.Floor(to) || !grid.Floor(support) || grid.GoalDistance[to] < 0) continue;
                        var destBox = Array.IndexOf(boxes, to);
                        if (destBox == a && without[support] >= 0)
                        { dependencies.Add(a * boxes.Length + b); blockedPairs.Add(a * boxes.Length + b); }
                        else if (destBox < 0 && reach[support] < 0 && without[support] >= 0)
                            dependencies.Add(a * boxes.Length + b);
                    }
                }
            }
        }
        private static int LongestChain(HashSet<int> edges, int count)
        {
            if (edges.Count == 0) return 0;
            var best = 0;
            for (var b = 0; b < count; b++) best = Math.Max(best, Chain(edges, count, b, 0));
            return best;
        }
        private static int Chain(HashSet<int> edges, int count, int box, int visited)
        {
            visited |= 1 << box; var best = 1;
            for (var next = 0; next < count; next++)
                if ((visited & (1 << next)) == 0 && edges.Contains(box * count + next))
                    best = Math.Max(best, 1 + Chain(edges, count, next, visited));
            return best;
        }
    }
}
