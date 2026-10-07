using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Kuluobishi.Sokoban;

// Editor-only deterministic regression checks, including an independent exhaustive
// move-level Dijkstra oracle. Run via MCP execute_code: return SokobanSolverChecks.Run();
public static class SokobanSolverChecks
{
    private sealed class ReferenceNode
    {
        public SokobanState State;
        public string Key;
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Solver regression: " + message); }

    public static SokobanJsonLevel Fixture(params string[] rows)
    {
        var boxes = new List<SokobanJsonPoint>(); var goals = new List<SokobanJsonPoint>();
        var terrain = new string[rows.Length]; SokobanJsonPoint player = null;
        for (var y = 0; y < rows.Length; y++)
        {
            var chars = new char[rows[y].Length];
            for (var x = 0; x < chars.Length; x++)
            {
                var c = rows[y][x]; chars[x] = c == '#' ? '#' : '.';
                if (c == '@' || c == '+') player = new SokobanJsonPoint(x, y);
                if (c == '$' || c == '*') boxes.Add(new SokobanJsonPoint(x, y));
                if (c == '.' || c == '*' || c == '+') goals.Add(new SokobanJsonPoint(x, y));
            }
            terrain[y] = new string(chars);
        }
        return new SokobanJsonLevel { levelId = "Regression", name = "求解回归关卡",
            size = new SokobanJsonSize { width = rows[0].Length, height = rows.Length }, terrain = terrain,
            player = player, boxes = boxes.ToArray(), goals = goals.ToArray() };
    }
    private static string Key(SokobanState state, int width)
    {
        return string.Join(",", state.Boxes.Select(b => b.y * width + b.x).OrderBy(b => b)) + ":" + (state.Player.y * width + state.Player.x);
    }
    private static int Compare(SokobanState a, SokobanState b)
    {
        var comparison = a.PushCount.CompareTo(b.PushCount);
        return comparison != 0 ? comparison : a.MoveCount.CompareTo(b.MoveCount);
    }
    private static SokobanState ReferenceSolve(SokobanJsonLevel data)
    {
        var level = new SokobanLevelRuntime(data); var first = level.CreateInitialState(); first.RefreshWin(level);
        var key = Key(first, level.Width); var best = new Dictionary<string, SokobanState> { { key, first } };
        var frontier = new List<ReferenceNode> { new ReferenceNode { State = first, Key = key } };
        while (frontier.Count > 0)
        {
            var index = 0;
            for (var i = 1; i < frontier.Count; i++) if (Compare(frontier[i].State, frontier[index].State) < 0) index = i;
            var node = frontier[index]; frontier.RemoveAt(index);
            if (!ReferenceEquals(best[node.Key], node.State)) continue;
            if (node.State.IsWon) return node.State;
            foreach (SokobanDirection direction in Enum.GetValues(typeof(SokobanDirection)))
            {
                var next = node.State.Clone();
                if (!SokobanSimulation.TryMove(level, next, direction).Accepted) continue;
                var nextKey = Key(next, level.Width);
                if (best.TryGetValue(nextKey, out var old) && Compare(old, next) <= 0) continue;
                best[nextKey] = next;
                frontier.Add(new ReferenceNode { State = next, Key = nextKey });
            }
        }
        return null;
    }
    private static SokobanSolveResult Solve(SokobanJsonLevel data, SokobanSolveOptions options = null, CancellationToken token = default)
    {
        Require(SokobanSolverAdapter.TryCreateSnapshot(data, out var snapshot, out var error), error);
        return SokobanSolver.Solve(snapshot, options, token);
    }
    private static void CheckPlayback(SokobanJsonLevel data, SokobanSolveResult result)
    {
        var source = UnityEngine.JsonUtility.ToJson(data);
        var replay = new SokobanSolutionPlayback(data, result);
        Require(!replay.Previous(), "cannot step backward before start");
        for (var i = 0; i < result.Moves.Length; i++) Require(replay.Next(), "forward replay " + i);
        Require(replay.State.IsWon && !replay.Next(), "replay ends at victory");
        Require(replay.State.MoveCount == result.Moves.Length && replay.State.PushCount == result.Pushes, "replay counters");
        for (var i = result.Moves.Length; i > 0; i--) Require(replay.Previous(), "backward replay " + i);
        Require(replay.Index == 0 && replay.State.MoveCount == 0 && replay.State.PushCount == 0, "rewind counters");
        Require(replay.State.Player == replay.Level.PlayerStart && replay.State.Boxes.SetEquals(replay.Level.BoxesStart), "rewind positions");
        Require(source == UnityEngine.JsonUtility.ToJson(data), "replay must not mutate source level");
        replay.Next(); replay.Reset(); Require(replay.Index == 0, "reset");
        var report = replay.FullReport();
        for (var i = 0; i < replay.Steps.Count; i++) Require(report.Contains(replay.StepText(i)), "full report contains step " + i);
    }
    private static void CompareWithReference(SokobanJsonLevel data, string name)
    {
        var reference = ReferenceSolve(data); var result = Solve(data);
        Require(result.Status == (reference == null ? SokobanSolveStatus.Unsolvable : SokobanSolveStatus.Solved), name + " solvability " + result.Status);
        if (reference == null) return;
        Require(result.Pushes == reference.PushCount && result.Moves.Length == reference.MoveCount,
            name + $" optimal costs: actual {result.Pushes},{result.Moves.Length}; expected {reference.PushCount},{reference.MoveCount}");
        CheckPlayback(data, result);
    }

    public static string Run()
    {
        var report = new StringBuilder();
        var onePush = Fixture("#######", "#-----#", "#-@$.-#", "#-----#", "#######");
        var simple = Solve(onePush);
        Require(simple.Moves == "R" && simple.Pushes == 1, "single push"); CheckPlayback(onePush, simple);
        var walk = Fixture("########", "#@-----#", "#------#", "#--$-.-#", "#------#", "#------#", "########");
        var walked = Solve(walk); Require(walked.Pushes == 2 && walked.Moves.Length == 5, "walking steps included"); CheckPlayback(walk, walked);
        CompareWithReference(Fixture("########", "#--.--.#", "#--$-$-#", "#--@---#", "#------#", "########"), "two boxes");
        var won = Fixture("#####", "#@*-#", "#---#", "#####");
        var zero = Solve(won); Require(zero.IsSolved && zero.Moves == "" && zero.Pushes == 0, "zero-step victory"); CheckPlayback(won, zero);
        var locked = Fixture("#######", "#--.--#", "#--#--#", "#-#$#-#", "#--#--#", "#--@--#", "#######");
        Require(Solve(locked).Status == SokobanSolveStatus.Unsolvable, "static deadlock");
        CompareWithReference(Fixture("----", "@$.-", "----", "----"), "open edges without row wrapping");
        Require(Solve(onePush, new SokobanSolveOptions { TimeoutMs = 0 }).Status == SokobanSolveStatus.Timeout, "timeout is not unsolvable");
        Require(Solve(onePush, new SokobanSolveOptions { MaxExpandedNodes = 0 }).Status == SokobanSolveStatus.LimitReached, "node limit is not unsolvable");
        Require(Solve(onePush, new SokobanSolveOptions { MaxDiscoveredStates = 1 }).Status == SokobanSolveStatus.LimitReached, "state limit is not unsolvable");
        using (var cancellation = new CancellationTokenSource())
        { cancellation.Cancel(); Require(Solve(onePush, null, cancellation.Token).Status == SokobanSolveStatus.Cancelled, "cancellation"); }
        using (var cancellation = new CancellationTokenSource())
        {
            Require(SokobanSolverAdapter.TryCreateSnapshot(onePush, out var snapshot, out _), "cancellation fixture");
            var cancelled = SokobanSolver.Solve(snapshot, null, cancellation.Token, progress => cancellation.Cancel());
            Require(cancelled.Status == SokobanSolveStatus.Cancelled, "cancellation during search");
        }
        var corridor = Fixture(new string('#', 40), "#@$" + new string('-', 35) + ".#", new string('#', 40));
        var longResult = Solve(corridor);
        Require(longResult.IsSolved && longResult.Pushes == 36 && longResult.Moves.Length == 36, "long complete solution");
        CheckPlayback(corridor, longResult);
        var invalid = Fixture("#####", "#@$-#", "#---#", "#####");
        Require(!SokobanSolverAdapter.TryCreateSnapshot(invalid, out _, out _), "mismatched entities rejected");
        invalid = Fixture("#####", "#@$.#", "#---#", "#####"); invalid.boxes[0] = null;
        Require(!SokobanSolverAdapter.TryCreateSnapshot(invalid, out _, out _), "null box rejected without crashing");
        invalid = Fixture("#####", "#@$.#", "#---#", "#####"); invalid.player = invalid.boxes[0];
        Require(!SokobanSolverAdapter.TryCreateSnapshot(invalid, out _, out _), "player/box overlap rejected");
        invalid = Fixture("######", "#@$$.#", "#----#", "######"); invalid.goals = new[] { new SokobanJsonPoint(4, 1), new SokobanJsonPoint(4, 1) };
        Require(!SokobanSolverAdapter.TryCreateSnapshot(invalid, out _, out _), "duplicate goal rejected");
        Require(SokobanSolver.Solve(null).Status == SokobanSolveStatus.Invalid, "invalid snapshot");
        report.AppendLine("PASS: movement, multiple boxes, deadlocks, zero-step, 36-step solution, complete replay/rewind/reset, validation, cancellation before/during search, timeout and limits.");

        var random = new Random(20261005);
        for (var test = 0; test < 60; test++)
        {
            var width = test % 2 == 0 ? 5 : 6; var height = width;
            var terrain = new string[height]; var floor = new List<SokobanJsonPoint>();
            for (var y = 0; y < height; y++)
            {
                var chars = new char[width];
                for (var x = 0; x < width; x++)
                {
                    var wall = x == 0 || y == 0 || x == width - 1 || y == height - 1 || random.NextDouble() < 0.12;
                    chars[x] = wall ? '#' : '.'; if (!wall) floor.Add(new SokobanJsonPoint(x, y));
                }
                terrain[y] = new string(chars);
            }
            for (var i = floor.Count - 1; i > 0; i--) { var j = random.Next(i + 1); var temp = floor[i]; floor[i] = floor[j]; floor[j] = temp; }
            var count = test % 3 == 0 ? 2 : 1;
            if (floor.Count < 2 * count + 1) { test--; continue; }
            var data = new SokobanJsonLevel { size = new SokobanJsonSize { width = width, height = height }, terrain = terrain,
                player = floor[0], boxes = floor.Skip(1).Take(count).ToArray(), goals = floor.Skip(1 + count).Take(count).ToArray() };
            CompareWithReference(data, "random " + test);
        }
        report.AppendLine("PASS: 60 seeded random levels agree with exhaustive move-level Dijkstra on solvability, minimum pushes and minimum moves.");

        foreach (var descriptor in SokobanLevelRepository.ListAll())
        {
            var data = SokobanLevelRepository.LoadJson(descriptor);
            if (!SokobanSolverAdapter.TryCreateSnapshot(data, out var snapshot, out var error))
            { report.AppendLine(descriptor.LevelId + ": Invalid — " + error.Replace('\n', ' ')); continue; }
            var result = SokobanSolver.Solve(snapshot, new SokobanSolveOptions { TimeoutMs = 5000, MaxExpandedNodes = 50000, MaxDiscoveredStates = 100000 });
            if (result.IsSolved) CheckPlayback(data, result);
            // 只对已标记可信解的关卡断言；未验证的草稿关卡只记录，不影响结果。
            if (data.verifiedMoves >= 0)
                Require(result.IsSolved, descriptor.LevelId + " 已标记可信解 verifiedMoves=" + data.verifiedMoves + "，但重新求解未成功：" + result.Status + " " + result.Message);
            report.AppendLine(descriptor.LevelId + $": {result.Status}, {result.Pushes} pushes, {result.Moves.Length} moves, {result.ExploredNodes} nodes, {result.ElapsedMs} ms");
        }
        return report.ToString();
    }
}
