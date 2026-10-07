using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace Kuluobishi.Sokoban
{
    // This module deliberately contains no Unity API calls: solve only a copied snapshot.
    public sealed class SokobanSolverLevel
    {
        public int Width;
        public int Height;
        public bool[] Walls;
        public int Player;
        public int[] Boxes;
        public int[] Goals;
    }

    public enum SokobanSolveStatus { Solved, Unsolvable, Invalid, Timeout, LimitReached, Cancelled, Error }

    public sealed class SokobanSolveOptions
    {
        public int TimeoutMs = 30000;
        public int MaxExpandedNodes = 100000;
        public int MaxDiscoveredStates = 200000;
    }

    public sealed class SokobanSolveProgress
    {
        public int ExploredNodes;
        public int FrontierNodes;
        public int ElapsedMs;
    }

    public sealed class SokobanSolveResult
    {
        public const string AlgorithmName = "Push A* / reverse-push matching / lexicographic pushes,moves";
        public SokobanSolveStatus Status;
        // Standard LURD notation: lowercase = walking, uppercase = pushing.
        public string Moves = "";
        public int Pushes;
        public int ExploredNodes;
        public int ElapsedMs;
        public string Message = "";
        public string Algorithm = AlgorithmName;
        public bool IsSolved => Status == SokobanSolveStatus.Solved;
    }

    public static class SokobanSolver
    {
        private const int Infinity = 1000000;
        private static readonly char[] DirectionChars = { 'u', 'd', 'l', 'r' };

        private sealed class Node
        {
            public int Player, Pushes, Moves, Heuristic;
            public int[] Boxes;
            public Node Parent;
            public string Segment, Key;
            public long Order;
        }

        private sealed class Heap
        {
            private readonly List<Node> items = new List<Node>();
            public int Count => items.Count;
            private static bool Before(Node a, Node b)
            {
                var c = (a.Pushes + a.Heuristic).CompareTo(b.Pushes + b.Heuristic);
                if (c == 0) c = a.Moves.CompareTo(b.Moves);
                return c < 0 || (c == 0 && a.Order < b.Order);
            }
            public void Add(Node node)
            {
                items.Add(node);
                var index = items.Count - 1;
                while (index > 0)
                {
                    var parent = (index - 1) / 2;
                    if (!Before(node, items[parent])) break;
                    items[index] = items[parent];
                    index = parent;
                }
                items[index] = node;
            }
            public Node Pop()
            {
                var result = items[0];
                var tail = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                if (items.Count == 0) return result;
                var index = 0;
                while (index * 2 + 1 < items.Count)
                {
                    var child = index * 2 + 1;
                    if (child + 1 < items.Count && Before(items[child + 1], items[child])) child++;
                    if (!Before(items[child], tail)) break;
                    items[index] = items[child];
                    index = child;
                }
                items[index] = tail;
                return result;
            }
        }

        private sealed class Search
        {
            private readonly SokobanSolverLevel level;
            private readonly SokobanSolveOptions options;
            private readonly CancellationToken cancellation;
            private readonly Action<SokobanSolveProgress> report;
            private readonly Stopwatch watch;
            private readonly bool[] goals, occupied;
            private readonly int[,] neighbors;
            private readonly int[][] pullDistances;
            private readonly Dictionary<string, int> heuristicCache = new Dictionary<string, int>();
            private readonly int[] walkDistance, walkParent, walkDirection, queue;
            private int expanded;
            private long order;
            private int lastReport = -100;

            public Search(SokobanSolverLevel snapshot, SokobanSolveOptions limits, CancellationToken token,
                Action<SokobanSolveProgress> progress, Stopwatch timer)
            {
                level = snapshot; options = limits; cancellation = token; report = progress; watch = timer;
                var area = level.Walls.Length;
                goals = new bool[area]; occupied = new bool[area];
                neighbors = new int[area, 4];
                walkDistance = new int[area]; walkParent = new int[area];
                walkDirection = new int[area]; queue = new int[area];
                foreach (var goal in level.Goals) goals[goal] = true;
                for (var cell = 0; cell < area; cell++)
                {
                    var x = cell % level.Width; var y = cell / level.Width;
                    neighbors[cell, 0] = y > 0 ? cell - level.Width : -1;
                    neighbors[cell, 1] = y + 1 < level.Height ? cell + level.Width : -1;
                    neighbors[cell, 2] = x > 0 ? cell - 1 : -1;
                    neighbors[cell, 3] = x + 1 < level.Width ? cell + 1 : -1;
                }
                pullDistances = new int[level.Goals.Length][];
                for (var i = 0; i < level.Goals.Length; i++)
                {
                    CheckBudget();
                    var distances = new int[area];
                    for (var j = 0; j < area; j++) distances[j] = Infinity;
                    var head = 0; var tail = 0;
                    queue[tail++] = level.Goals[i]; distances[level.Goals[i]] = 0;
                    while (head < tail)
                    {
                        CheckBudget();
                        var cell = queue[head++];
                        for (var direction = 0; direction < 4; direction++)
                        {
                            var previous = neighbors[cell, direction ^ 1];
                            if (!Floor(previous)) continue;
                            var standing = neighbors[previous, direction ^ 1];
                            if (!Floor(standing) || distances[previous] != Infinity) continue;
                            distances[previous] = distances[cell] + 1;
                            queue[tail++] = previous;
                        }
                    }
                    pullDistances[i] = distances;
                }
            }

            private bool Floor(int cell) => cell >= 0 && !level.Walls[cell];
            private void CheckBudget()
            {
                cancellation.ThrowIfCancellationRequested();
                if (watch.ElapsedMilliseconds >= options.TimeoutMs) throw new TimeoutException();
            }
            private static string BoxKey(int[] boxes)
            {
                var key = new char[boxes.Length];
                for (var i = 0; i < boxes.Length; i++) key[i] = (char)boxes[i];
                return new string(key);
            }
            private static string StateKey(string boxes, int player) => boxes + (char)player;

            // Hungarian minimum assignment over reverse-push distances. An admissible
            // push lower bound, also rejecting layouts with no box-to-goal matching.
            private int Estimate(int[] boxes, string key)
            {
                if (heuristicCache.TryGetValue(key, out var cached)) return cached;
                var count = boxes.Length;
                var u = new int[count + 1]; var v = new int[count + 1];
                var p = new int[count + 1]; var way = new int[count + 1];
                for (var row = 1; row <= count; row++)
                {
                    CheckBudget();
                    p[0] = row;
                    var min = new int[count + 1]; var used = new bool[count + 1];
                    for (var j = 1; j <= count; j++) min[j] = Infinity;
                    var column = 0;
                    do
                    {
                        CheckBudget();
                        used[column] = true;
                        var currentRow = p[column]; var delta = Infinity; var next = 0;
                        for (var j = 1; j <= count; j++)
                        {
                            if (used[j]) continue;
                            var cost = pullDistances[j - 1][boxes[currentRow - 1]] - u[currentRow] - v[j];
                            if (cost < min[j]) { min[j] = cost; way[j] = column; }
                            if (min[j] < delta) { delta = min[j]; next = j; }
                        }
                        if (next == 0 || delta >= Infinity) { heuristicCache[key] = Infinity; return Infinity; }
                        for (var j = 0; j <= count; j++)
                        {
                            if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                            else min[j] -= delta;
                        }
                        column = next;
                    } while (p[column] != 0);
                    do { var previous = way[column]; p[column] = p[previous]; column = previous; } while (column != 0);
                }
                var result = 0;
                for (var column = 1; column <= count; column++)
                {
                    var distance = pullDistances[column - 1][boxes[p[column] - 1]];
                    if (distance >= Infinity) { result = Infinity; break; }
                    result += distance;
                }
                heuristicCache[key] = result;
                return result;
            }

            private void Walk(int player)
            {
                for (var i = 0; i < walkDistance.Length; i++) walkDistance[i] = -1;
                var head = 0; var tail = 0;
                queue[tail++] = player; walkDistance[player] = 0; walkParent[player] = -1;
                while (head < tail)
                {
                    if ((head & 63) == 0) CheckBudget();
                    var cell = queue[head++];
                    for (var direction = 0; direction < 4; direction++)
                    {
                        var next = neighbors[cell, direction];
                        if (!Floor(next) || occupied[next] || walkDistance[next] >= 0) continue;
                        walkDistance[next] = walkDistance[cell] + 1;
                        walkParent[next] = cell; walkDirection[next] = direction;
                        queue[tail++] = next;
                    }
                }
            }
            private string WalkPath(int destination, int pushDirection)
            {
                var chars = new char[walkDistance[destination] + 1];
                chars[chars.Length - 1] = char.ToUpperInvariant(DirectionChars[pushDirection]);
                var cursor = destination;
                for (var i = chars.Length - 2; i >= 0; i--)
                { chars[i] = DirectionChars[walkDirection[cursor]]; cursor = walkParent[cursor]; }
                return new string(chars);
            }
            private bool FrozenSquare(int movedBox)
            {
                var x = movedBox % level.Width; var y = movedBox / level.Width;
                for (var left = x - 1; left <= x; left++)
                for (var top = y - 1; top <= y; top++)
                {
                    if (left < 0 || top < 0 || left + 1 >= level.Width || top + 1 >= level.Height) continue;
                    var cells = new[] { top * level.Width + left, top * level.Width + left + 1,
                        (top + 1) * level.Width + left, (top + 1) * level.Width + left + 1 };
                    var solid = true; var unfinished = false;
                    foreach (var cell in cells)
                    { if (!level.Walls[cell] && !occupied[cell]) solid = false; if (occupied[cell] && !goals[cell]) unfinished = true; }
                    if (solid && unfinished) return true;
                }
                return false;
            }
            private void Progress(int frontier, bool force = false)
            {
                var elapsed = (int)watch.ElapsedMilliseconds;
                if (!force && elapsed - lastReport < 100) return;
                lastReport = elapsed;
                report?.Invoke(new SokobanSolveProgress { ExploredNodes = expanded, FrontierNodes = frontier, ElapsedMs = elapsed });
            }
            public SokobanSolveResult Result(SokobanSolveStatus status, string message, Node end = null)
            {
                var result = new SokobanSolveResult { Status = status, Message = message,
                    ExploredNodes = expanded, ElapsedMs = (int)watch.ElapsedMilliseconds };
                if (end != null)
                {
                    var segments = new Stack<string>();
                    for (var node = end; node.Parent != null; node = node.Parent) segments.Push(node.Segment);
                    var moves = new StringBuilder();
                    foreach (var segment in segments) moves.Append(segment);
                    result.Moves = moves.ToString(); result.Pushes = end.Pushes;
                }
                return result;
            }

            public SokobanSolveResult Run()
            {
                CheckBudget();
                var boxes = (int[])level.Boxes.Clone(); Array.Sort(boxes);
                var boxKey = BoxKey(boxes);
                var initialH = Estimate(boxes, boxKey);
                if (initialH >= Infinity) return Result(SokobanSolveStatus.Unsolvable, "箱子无法与目标匹配：存在死点或不可达目标。");
                var first = new Node { Boxes = boxes, Player = level.Player, Heuristic = initialH, Key = StateKey(boxKey, level.Player) };
                var best = new Dictionary<string, Node> { { first.Key, first } };
                var frontier = new Heap(); frontier.Add(first);
                while (frontier.Count > 0)
                {
                    CheckBudget();
                    var node = frontier.Pop();
                    if (!ReferenceEquals(best[node.Key], node)) continue;
                    if (node.Heuristic == 0)
                    { Progress(frontier.Count, true); return Result(SokobanSolveStatus.Solved, "已找到最少推箱、同推箱数下最少移动的解答。", node); }
                    if (expanded >= options.MaxExpandedNodes)
                        return Result(SokobanSolveStatus.LimitReached, "达到搜索节点上限，尚未确定是否有解。");
                    expanded++; Progress(frontier.Count);
                    Array.Clear(occupied, 0, occupied.Length);
                    foreach (var box in node.Boxes) occupied[box] = true;
                    Walk(node.Player);
                    for (var index = 0; index < node.Boxes.Length; index++)
                    for (var direction = 0; direction < 4; direction++)
                    {
                        CheckBudget();
                        var box = node.Boxes[index];
                        var standing = neighbors[box, direction ^ 1]; var destination = neighbors[box, direction];
                        if (!Floor(standing) || walkDistance[standing] < 0 || !Floor(destination) || occupied[destination]) continue;
                        occupied[box] = false; occupied[destination] = true;
                        var frozen = FrozenSquare(destination);
                        occupied[box] = true; occupied[destination] = false;
                        if (frozen) continue;
                        var nextBoxes = (int[])node.Boxes.Clone(); nextBoxes[index] = destination; Array.Sort(nextBoxes);
                        var nextBoxKey = BoxKey(nextBoxes); var key = StateKey(nextBoxKey, box);
                        var pushes = node.Pushes + 1; var moves = node.Moves + walkDistance[standing] + 1;
                        if (best.TryGetValue(key, out var old) && (old.Pushes < pushes || (old.Pushes == pushes && old.Moves <= moves))) continue;
                        var heuristic = Estimate(nextBoxes, nextBoxKey);
                        if (heuristic >= Infinity) continue;
                        if (old == null && best.Count >= options.MaxDiscoveredStates)
                            return Result(SokobanSolveStatus.LimitReached, "达到搜索状态上限，尚未确定是否有解。");
                        var next = new Node { Player = box, Boxes = nextBoxes, Pushes = pushes, Moves = moves,
                            Heuristic = heuristic, Parent = node, Segment = WalkPath(standing, direction), Key = key, Order = ++order };
                        best[key] = next; frontier.Add(next);
                    }
                }
                return Result(SokobanSolveStatus.Unsolvable, "已搜索完所有可行状态，此关卡无解。");
            }
        }

        public static SokobanSolveResult Solve(SokobanSolverLevel snapshot, SokobanSolveOptions options = null,
            CancellationToken cancellation = default, Action<SokobanSolveProgress> progress = null)
        {
            var watch = Stopwatch.StartNew(); Search search = null;
            Func<SokobanSolveStatus, string, SokobanSolveResult> failure = (status, message) => search != null
                ? search.Result(status, message) : new SokobanSolveResult { Status = status, Message = message, ElapsedMs = (int)watch.ElapsedMilliseconds };
            try
            {
                cancellation.ThrowIfCancellationRequested();
                options = options ?? new SokobanSolveOptions();
                if (options.TimeoutMs <= 0) return failure(SokobanSolveStatus.Timeout, "求解超时，尚未确定是否有解。");
                if (options.MaxExpandedNodes < 0 || options.MaxDiscoveredStates < 1)
                    return failure(SokobanSolveStatus.Invalid, "搜索上限设置无效。");
                var error = Validate(snapshot);
                if (error != null) return failure(SokobanSolveStatus.Invalid, error);
                // Own all buffers, including when callers reuse their snapshot after starting a task.
                var copy = new SokobanSolverLevel { Width = snapshot.Width, Height = snapshot.Height,
                    Walls = (bool[])snapshot.Walls.Clone(), Player = snapshot.Player,
                    Boxes = (int[])snapshot.Boxes.Clone(), Goals = (int[])snapshot.Goals.Clone() };
                search = new Search(copy, options, cancellation, progress, watch);
                return search.Run();
            }
            catch (OperationCanceledException) { return failure(SokobanSolveStatus.Cancelled, "求解已取消。"); }
            catch (TimeoutException) { return failure(SokobanSolveStatus.Timeout, "求解超时，尚未确定是否有解。"); }
            catch (Exception error) { return failure(SokobanSolveStatus.Error, "求解失败：" + error.Message); }
        }

        private static string Validate(SokobanSolverLevel level)
        {
            if (level == null || level.Width < 3 || level.Height < 3 || level.Width > 40 || level.Height > 40)
                return "关卡尺寸必须为 3–40 格。";
            var area = level.Width * level.Height;
            if (level.Walls == null || level.Walls.Length != area || level.Boxes == null || level.Goals == null)
                return "关卡地形或实体数据不完整。";
            if (level.Boxes.Length == 0 || level.Boxes.Length != level.Goals.Length)
                return "至少需要一个箱子，且箱子数量必须等于目标数量。";
            if (level.Player < 0 || level.Player >= area || level.Walls[level.Player]) return "玩家必须位于可走地面。";
            var boxes = new HashSet<int>(); var goals = new HashSet<int>();
            foreach (var box in level.Boxes)
                if (box < 0 || box >= area || level.Walls[box] || !boxes.Add(box) || box == level.Player)
                    return "箱子越界、位于墙内、彼此重叠或与玩家重叠。";
            foreach (var goal in level.Goals)
                if (goal < 0 || goal >= area || level.Walls[goal] || !goals.Add(goal)) return "目标越界、位于墙内或彼此重叠。";
            return null;
        }
    }
}
