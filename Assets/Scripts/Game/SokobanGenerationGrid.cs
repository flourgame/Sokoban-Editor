using System;
using System.Collections.Generic;
using System.Text;

namespace Kuluobishi.Sokoban
{
    // Shared, Unity-free board operations for generation and complexity analysis.
    internal sealed class SokobanGenerationGrid
    {
        internal readonly SokobanSolverLevel Level;
        internal readonly int[,] Neighbors;
        internal readonly int[] GoalDistance;
        internal static readonly char[] Directions = { 'u', 'd', 'l', 'r' };
        internal sealed class Push
        {
            internal int Box, To, Direction, Player;
            internal string Moves;
        }

        internal SokobanGenerationGrid(SokobanSolverLevel level)
        {
            Level = level;
            Neighbors = new int[level.Walls.Length, 4];
            for (var c = 0; c < level.Walls.Length; c++)
            {
                var x = c % level.Width; var y = c / level.Width;
                Neighbors[c, 0] = y > 0 ? c - level.Width : -1;
                Neighbors[c, 1] = y + 1 < level.Height ? c + level.Width : -1;
                Neighbors[c, 2] = x > 0 ? c - 1 : -1;
                Neighbors[c, 3] = x + 1 < level.Width ? c + 1 : -1;
            }
            GoalDistance = new int[level.Walls.Length];
            for (var c = 0; c < GoalDistance.Length; c++) GoalDistance[c] = -1;
            var queue = new Queue<int>();
            foreach (var goal in level.Goals) { GoalDistance[goal] = 0; queue.Enqueue(goal); }
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (var d = 0; d < 4; d++)
                {
                    var previous = Neighbors[c, d];
                    var support = previous < 0 ? -1 : Neighbors[previous, d];
                    if (!Floor(previous) || !Floor(support) || GoalDistance[previous] >= 0) continue;
                    GoalDistance[previous] = GoalDistance[c] + 1; queue.Enqueue(previous);
                }
            }
        }

        internal bool Floor(int c) => c >= 0 && c < Level.Walls.Length && !Level.Walls[c];
        internal static int Opposite(int d) => d ^ 1;
        internal bool[] Occupied(int[] boxes)
        {
            var result = new bool[Level.Walls.Length];
            foreach (var box in boxes) result[box] = true;
            return result;
        }
        internal int[] Reach(int player, int[] boxes, int ignoreBox = -1)
        {
            var occupied = new bool[Level.Walls.Length];
            for (var i = 0; i < boxes.Length; i++) if (i != ignoreBox) occupied[boxes[i]] = true;
            return Reach(player, occupied);
        }
        internal int[] Reach(int player, bool[] occupied, int blocked = -1)
        {
            var parents = new int[Level.Walls.Length];
            for (var i = 0; i < parents.Length; i++) parents[i] = -1;
            if (!Floor(player) || player == blocked || occupied[player]) return parents;
            var queue = new Queue<int>(); queue.Enqueue(player); parents[player] = player;
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                for (var d = 0; d < 4; d++)
                {
                    var next = Neighbors[c, d];
                    if (!Floor(next) || next == blocked || occupied[next] || parents[next] >= 0) continue;
                    parents[next] = c; queue.Enqueue(next);
                }
            }
            return parents;
        }
        internal string WalkPath(int[] parents, int destination)
        {
            var reversed = new StringBuilder();
            var c = destination;
            while (parents[c] != c)
            {
                var previous = parents[c];
                for (var d = 0; d < 4; d++) if (Neighbors[previous, d] == c) { reversed.Append(Directions[d]); break; }
                c = previous;
            }
            var chars = reversed.ToString().ToCharArray(); Array.Reverse(chars); return new string(chars);
        }
        internal List<Push> Pushes(int player, int[] boxes)
        {
            var parents = Reach(player, boxes); var occupied = Occupied(boxes);
            var result = new List<Push>();
            for (var b = 0; b < boxes.Length; b++)
            for (var d = 0; d < 4; d++)
            {
                var to = Neighbors[boxes[b], d]; var support = Neighbors[boxes[b], Opposite(d)];
                if (!Floor(to) || !Floor(support) || occupied[to] || parents[support] < 0) continue;
                result.Add(new Push { Box = b, To = to, Direction = d, Player = boxes[b],
                    Moves = WalkPath(parents, support) + char.ToUpperInvariant(Directions[d]) });
            }
            return result;
        }
        internal SokobanSolverLevel Snapshot(int player, int[] boxes)
        {
            return new SokobanSolverLevel { Width = Level.Width, Height = Level.Height,
                Walls = (bool[])Level.Walls.Clone(), Player = player, Boxes = (int[])boxes.Clone(), Goals = (int[])Level.Goals.Clone() };
        }
        internal bool[] CriticalCells()
        {
            // Boards are small; removal connectivity also identifies narrow passages without recursion.
            var critical = new bool[Level.Walls.Length]; var empty = new bool[Level.Walls.Length];
            var count = 0; var first = -1;
            for (var c = 0; c < empty.Length; c++) if (Floor(c)) { count++; first = c; }
            for (var blocked = 0; blocked < empty.Length; blocked++)
            {
                if (!Floor(blocked)) continue;
                var start = first;
                if (start == blocked) for (var c = 0; c < empty.Length; c++) if (Floor(c) && c != blocked) { start = c; break; }
                var reached = Reach(start, empty, blocked); var remaining = 0; var degree = 0;
                for (var c = 0; c < reached.Length; c++) if (reached[c] >= 0) remaining++;
                for (var d = 0; d < 4; d++) if (Floor(Neighbors[blocked, d])) degree++;
                critical[blocked] = remaining < count - 1 || degree == 2 &&
                    (Floor(Neighbors[blocked, 0]) && Floor(Neighbors[blocked, 1]) || Floor(Neighbors[blocked, 2]) && Floor(Neighbors[blocked, 3]));
            }
            return critical;
        }
    }
}
