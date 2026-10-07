using System;
using System.Reflection;
using Kuluobishi.Sokoban;
using UnityEngine;

/// <summary>
/// 棋盘视图回归：双层（地块层 Tiles + 实体层 Entities）+ 预制体实例化契约。
/// 旧契约（Cell_{x}_{y}/Object 单格换色）已随 v0.14 的预制体化/场景化改造废弃。
/// </summary>
public static class SokobanBoardViewChecks
{
    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception("Board view regression: " + message); }

    private static Vector2 CenterOf(SokobanBoardView view, SokobanLevelRuntime level, SokobanGridPoint point)
    {
        var stride = view.CellStride;
        return new Vector2((point.x + 0.5f) * stride - level.Width * stride * 0.5f,
            level.Height * stride * 0.5f - (point.y + 0.5f) * stride);
    }

    public static string Run()
    {
        var root = new GameObject("BoardViewRegression", typeof(RectTransform));
        var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(1000f, 700f);
        try
        {
            var smallData = SokobanSolverChecks.Fixture("#######", "#@ $ .#", "#     #", "#######");
            var small = new SokobanLevelRuntime(smallData); var smallState = small.CreateInitialState();
            var view = SokobanBoardView.Create(rect, "SmallView", small, smallState);
            Require(!view.IsFollowing && view.Board.anchoredPosition == Vector2.zero, "small board uses fixed view");
            Require(view.Board.rect.width <= view.Viewport.rect.width && view.Board.rect.height <= view.Viewport.rect.height, "small board fits completely");

            var tiles = view.Board.Find("Tiles");
            Require(tiles != null && tiles.childCount == small.Width * small.Height, "tile layer holds one prefab instance per cell");
            var wallTile = tiles.Find("Tile_0_0");
            Require(wallTile != null && wallTile.GetComponent<SokobanTileView>().Kind == SokobanTileKind.Wall, "border cell instantiates the wall prefab");
            var floorTile = tiles.Find("Tile_1_1");
            Require(floorTile != null && floorTile.GetComponent<SokobanTileView>().Kind == SokobanTileKind.Floor, "open cell instantiates the floor prefab");
            var goalTile = tiles.Find("Tile_5_1");
            Require(goalTile != null && goalTile.GetComponent<SokobanTileView>().Kind == SokobanTileKind.Goal, "goal cell instantiates the goal prefab");
            Require(goalTile.GetComponent<SokobanTileView>().GoalOverlay != null, "goal prefab carries a goal overlay mount");

            var entities = view.Board.Find("Entities");
            Require(entities != null && entities.childCount == smallState.Boxes.Count + 1, "entity layer holds the player plus one instance per box");
            var player = entities.Find("Player");
            Require(player != null && player.GetComponent<SokobanEntityView>() != null && player.GetComponent<SokobanEntityView>().IsPlayer, "player entity prefab is instantiated with its view");
            Require(((RectTransform)player).anchoredPosition == CenterOf(view, small, smallState.Player), "player entity sits centered on its cell");
            var box = entities.childCount > 1 ? entities.GetChild(1) : null;
            Require(box != null && box.GetComponent<SokobanEntityView>() != null && !box.GetComponent<SokobanEntityView>().IsPlayer, "box entity prefab is instantiated with its view");
            Require(tiles.GetSiblingIndex() < entities.GetSiblingIndex(), "entity layer is a later sibling so a box on a goal keeps the goal visible");

            var largeData = LargeFixture(); var large = new SokobanLevelRuntime(largeData); var state = large.CreateInitialState();
            var following = SokobanBoardView.Create(rect, "LargeView", large, state);
            Require(following.IsFollowing && following.CellStride > 50f, "large board follows instead of shrinking icons");
            var atStart = following.CameraTarget;
            for (var i = 0; i < 10; i++) Require(SokobanSimulation.TryMove(large, state, SokobanDirection.Right).Accepted, "fixture movement");
            following.SetState(state, true);
            Require(following.CameraTarget != atStart, "camera follows player movement");
            var largePlayer = following.Board.Find("Entities/Player");
            Require(largePlayer != null && ((RectTransform)largePlayer).anchoredPosition == CenterOf(following, large, state.Player),
                "player entity recenters on its cell after a snapped state update");
            var largeGoalTile = following.Board.Find("Tiles/Tile_17_17");
            Require(largeGoalTile != null && largeGoalTile.GetComponent<SokobanTileView>().Kind == SokobanTileKind.Goal, "goal tile survives on large boards");

            // 目标菱形几何仍由 SokobanGoalGraphic 负责（编辑器网格继续使用），确认顶点不越出格子。
            var markerHost = new GameObject("GoalMarkerHost", typeof(RectTransform));
            var markerRect = markerHost.GetComponent<RectTransform>(); markerRect.sizeDelta = new Vector2(64f, 64f);
            var marker = SokobanUI.GoalMarker(markerRect);
            var method = typeof(SokobanGoalGraphic).GetMethod("OnPopulateMesh", BindingFlags.NonPublic | BindingFlags.Instance,
                null, new[] { typeof(UnityEngine.UI.VertexHelper) }, null);
            using (var mesh = new UnityEngine.UI.VertexHelper())
            {
                method.Invoke(marker, new object[] { mesh });
                Require(mesh.currentVertCount > 0, "goal marker produces geometry");
                for (var i = 0; i < mesh.currentVertCount; i++)
                {
                    var vertex = new UIVertex(); mesh.PopulateUIVertex(ref vertex, i);
                    Require(markerRect.rect.Contains(vertex.position), "all goal vertices stay inside cell");
                }
            }
            UnityEngine.Object.DestroyImmediate(markerHost);

            var overview = SokobanBoardView.Create(rect, "Overview", large, state, false);
            Require(!overview.IsFollowing && overview.Board.anchoredPosition == Vector2.zero &&
                overview.Board.rect.width <= overview.Viewport.rect.width + 0.01f &&
                overview.Board.rect.height <= overview.Viewport.rect.height + 0.01f, "whole-map overview fits every row and column");
            var topLeft = SokobanBoardView.FollowPosition(20, 20, new SokobanGridPoint(0, 0), 64f, new Vector2(900f, 640f));
            var bottomRight = SokobanBoardView.FollowPosition(20, 20, new SokobanGridPoint(19, 19), 64f, new Vector2(900f, 640f));
            Require(topLeft == new Vector2(190f, -320f) && bottomRight == new Vector2(-190f, 320f), "camera clamps at opposite edges");
            Require(SokobanBoardView.FollowPosition(5, 20, new SokobanGridPoint(4, 19), 64f, new Vector2(900f, 640f)).x == 0f,
                "short axis stays centered");
            return "PASS: per-cell tile prefabs (floor/wall/goal), entity layer (player+boxes) centered above tiles, goal marker geometry inside cell, small fixed view, large player following, boundary clamping, short-axis centering, whole-map fit.";
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static SokobanJsonLevel LargeFixture()
    {
        var data = new SokobanJsonLevel { levelId = "ViewRegression", name = "20×20 视野验证", size = new SokobanJsonSize { width = 20, height = 20 },
            player = new SokobanJsonPoint(1, 1), boxes = new[] { new SokobanJsonPoint(15, 15) }, goals = new[] { new SokobanJsonPoint(17, 17) } };
        var rows = new string[20];
        for (var y = 0; y < 20; y++)
        {
            var chars = new char[20];
            for (var x = 0; x < 20; x++) chars[x] = x == 0 || y == 0 || x == 19 || y == 19 ? '#' : '.';
            rows[y] = new string(chars);
        }
        data.terrain = rows; return data;
    }
}
