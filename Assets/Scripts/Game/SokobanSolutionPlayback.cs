using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    public static class SokobanSolverAdapter
    {
        // Call only on Unity's main thread; the returned arrays contain no Unity objects.
        public static bool TryCreateSnapshot(SokobanJsonLevel data, out SokobanSolverLevel snapshot, out string error)
        {
            snapshot = null;
            var errors = SokobanValidation.Validate(data);
            error = string.Join("\n", errors);
            if (errors.Count > 0) return false;
            var width = data.size.width; var height = data.size.height;
            snapshot = new SokobanSolverLevel { Width = width, Height = height,
                Player = data.player.y * width + data.player.x, Walls = new bool[width * height],
                Boxes = new int[data.boxes.Length], Goals = new int[data.goals.Length] };
            for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++) snapshot.Walls[y * width + x] = data.terrain[y][x] == '#';
            for (var i = 0; i < data.boxes.Length; i++) snapshot.Boxes[i] = data.boxes[i].y * width + data.boxes[i].x;
            for (var i = 0; i < data.goals.Length; i++) snapshot.Goals[i] = data.goals[i].y * width + data.goals[i].x;
            return true;
        }
    }

    public sealed class SokobanSolutionStep
    {
        public SokobanDirection Direction;
        public bool Pushed;
        public SokobanGridPoint PlayerFrom, PlayerTo, BoxFrom, BoxTo;
    }

    /// <summary>A separate replay session; never writes to the editor's level document.</summary>
    public sealed class SokobanSolutionPlayback
    {
        public SokobanLevelRuntime Level { get; }
        public SokobanState State { get; private set; }
        public SokobanSolveResult Result { get; }
        public IReadOnlyList<SokobanSolutionStep> Steps => steps;
        public int Index { get; private set; }
        private readonly List<SokobanSolutionStep> steps = new List<SokobanSolutionStep>();
        private readonly Stack<SokobanState> previous = new Stack<SokobanState>();

        public SokobanSolutionPlayback(SokobanJsonLevel source, SokobanSolveResult result)
        {
            if (result == null || !result.IsSolved) throw new ArgumentException("需要已求解的结果。");
            if (!SokobanSolverAdapter.TryCreateSnapshot(source, out _, out var error)) throw new ArgumentException(error);
            var copy = SokobanLevelRepository.Parse(JsonUtility.ToJson(source));
            Level = new SokobanLevelRuntime(copy);
            Result = result;
            var check = Level.CreateInitialState(); check.RefreshWin(Level);
            foreach (var move in result.Moves)
            {
                if (check.IsWon) throw new InvalidOperationException("解答在通关后仍包含多余步骤。");
                var direction = ParseDirection(move);
                var from = check.Player;
                var action = SokobanSimulation.TryMove(Level, check, direction);
                if (!action.Accepted || action.Pushed != char.IsUpper(move)) throw new InvalidOperationException("解答与运行时规则不一致。");
                var delta = SokobanSimulation.DirectionDelta(direction);
                steps.Add(new SokobanSolutionStep { Direction = direction, Pushed = action.Pushed,
                    PlayerFrom = from, PlayerTo = check.Player, BoxFrom = check.Player,
                    BoxTo = new SokobanGridPoint(check.Player.x + delta.x, check.Player.y + delta.y) });
            }
            if (!check.IsWon || check.PushCount != result.Pushes) throw new InvalidOperationException("解答没有完成全部目标或推箱统计不一致。");
            Reset();
        }

        public void Reset()
        {
            State = Level.CreateInitialState(); State.RefreshWin(Level);
            Index = 0; previous.Clear();
        }
        public bool Next()
        {
            if (Index >= steps.Count) return false;
            var before = State.Clone();
            if (!SokobanSimulation.TryMove(Level, State, steps[Index].Direction).Accepted)
                throw new InvalidOperationException("回放步骤无法执行。");
            previous.Push(before); Index++; return true;
        }
        public bool Previous()
        {
            if (Index == 0) return false;
            State = previous.Pop(); Index--; return true;
        }
        public string StepText(int index)
        {
            var step = steps[index];
            var direction = DirectionName(step.Direction);
            var text = $"{index + 1}. 向{direction}{(step.Pushed ? "推箱" : "移动")}  玩家 {step.PlayerFrom} → {step.PlayerTo}";
            if (step.Pushed) text += $"\n    箱子 {step.BoxFrom} → {step.BoxTo}";
            return text;
        }
        public string FullReport()
        {
            var text = new StringBuilder();
            text.AppendLine("推箱子完整求解流程");
            text.AppendLine($"关卡：{Level.Name} ({Level.LevelId})");
            text.AppendLine($"尺寸：{Level.Width} × {Level.Height}；坐标从左上角 (0,0) 开始");
            text.AppendLine($"最少推箱：{Result.Pushes}；同推箱数下最少移动：{steps.Count}");
            text.AppendLine($"搜索节点：{Result.ExploredNodes}；耗时：{Result.ElapsedMs} ms");
            text.AppendLine("方向串（小写=移动，大写=推箱）：" + Result.Moves);
            text.AppendLine("0. 初始状态  玩家 " + Level.PlayerStart);
            for (var i = 0; i < steps.Count; i++) text.AppendLine(StepText(i));
            text.AppendLine("完成：全部箱子到达目标点。");
            return text.ToString();
        }
        public static SokobanDirection ParseDirection(char value)
        {
            switch (char.ToLowerInvariant(value))
            {
                case 'u': return SokobanDirection.Up;
                case 'd': return SokobanDirection.Down;
                case 'l': return SokobanDirection.Left;
                case 'r': return SokobanDirection.Right;
                default: throw new ArgumentException("未知移动方向：" + value);
            }
        }
        public static string DirectionName(SokobanDirection direction)
        {
            switch (direction)
            {
                case SokobanDirection.Up: return "上";
                case SokobanDirection.Down: return "下";
                case SokobanDirection.Left: return "左";
                default: return "右";
            }
        }
    }
}
