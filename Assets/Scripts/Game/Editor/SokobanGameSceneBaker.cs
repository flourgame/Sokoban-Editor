using System;
using Kuluobishi.Sokoban;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// 把游玩界面从"运行时动态创建"迁移为"场景烘焙 + 预制体"的一次性/可重跑工具。
///
///   Sokoban/Create Board Prefabs  生成 Resources/Prefabs/Board 下五个棋盘预制体
///   Sokoban/Bake Game Scene       重建 game.unity 的画布/HUD/弹窗并绑定到控制器
///
/// 烘焙后所有布局都能在 Scene 视图里所见即所得地手动调整；
/// 重新运行本工具会按代码里的布局常量重建，可用于"改坏了就重来"。
/// 仅编辑器使用，放在 Editor 文件夹，不会打进运行时程序集。
/// </summary>
public static class SokobanGameSceneBaker
{
    private const float Margin = 36f, TopInset = 116f, BottomInset = 104f;
    private const string PrefabDir = "Assets/Resources/Prefabs/Board";
    private const string ScenePath = "Assets/Scenes/game.unity";
    private static Font cachedFont;

    private static Font Font => cachedFont != null ? cachedFont :
        (cachedFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/fonts/STXIHEI.TTF"));

    private static Sprite Img(string name)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/img/" + name + ".png");
        if (sprite == null) Debug.LogError("缺少素材 Assets/Resources/img/" + name + ".png");
        return sprite;
    }

    // ---------------------------------------------------------------- 预制体

    [MenuItem("Sokoban/Create Board Prefabs")]
    public static void CreateBoardPrefabs()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Prefabs"))
            AssetDatabase.CreateFolder("Assets/Resources", "Prefabs");
        if (!AssetDatabase.IsValidFolder(PrefabDir))
            AssetDatabase.CreateFolder("Assets/Resources/Prefabs", "Board");

        SavePrefab(MakeTile("Tile_Floor", SokobanTileKind.Floor, Img("Floor"), null), PrefabDir + "/Tile_Floor.prefab");
        SavePrefab(MakeTile("Tile_Wall", SokobanTileKind.Wall, Img("Wall"), null), PrefabDir + "/Tile_Wall.prefab");
        SavePrefab(MakeTile("Tile_Goal", SokobanTileKind.Goal, Img("Floor"), Img("Aid")), PrefabDir + "/Tile_Goal.prefab");
        SavePrefab(MakeEntity("Entity_Box", SokobanEntityKind.Box, Img("Box"), null), PrefabDir + "/Entity_Box.prefab");
        SavePrefab(MakeEntity("Entity_Player", SokobanEntityKind.Player, Img("player_d_00"), PlayerFrames()), PrefabDir + "/Entity_Player.prefab");
        // 注意：这里绝不能调用 AssetDatabase.Refresh()——它会在烘焙中途触发域重载，
        // 使后续 AddComponent 拿到旧程序集的失效类型，产出 m_Script 为 0/裸 fileID 的坏引用。
        Debug.Log("SOKOBAN_BAKE: 5 board prefabs written to " + PrefabDir);
    }

    private static Sprite[][] PlayerFrames()
    {
        return new[]
        {
            new[] { Img("player_u_00"), Img("player_u_01"), Img("player_u_02") },
            new[] { Img("player_d_00"), Img("player_d_01"), Img("player_d_02") },
            new[] { Img("player_l_00"), Img("player_l_01"), Img("player_l_02") },
            new[] { Img("player_r_00"), Img("player_r_01"), Img("player_r_02") }
        };
    }

    private static GameObject MakeTile(string name, SokobanTileKind kind, Sprite background, Sprite goalOverlay)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.pivot = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        var image = go.GetComponent<Image>();
        image.sprite = background;
        image.color = Color.white;
        image.raycastTarget = false;
        var view = go.AddComponent<SokobanTileView>();
        EnsureScript(view);
        var so = new SerializedObject(view);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("background").objectReferenceValue = image;
        if (goalOverlay != null)
        {
            var overlayObject = new GameObject("GoalOverlay", typeof(RectTransform), typeof(Image));
            var overlayRect = overlayObject.GetComponent<RectTransform>();
            overlayRect.SetParent(rect, false);
            Stretch(overlayRect);
            var overlayImage = overlayObject.GetComponent<Image>();
            overlayImage.sprite = goalOverlay;
            overlayImage.color = Color.white;
            overlayImage.raycastTarget = false;
            so.FindProperty("goalOverlay").objectReferenceValue = overlayImage;
        }
        var fx = new GameObject("FxMount", typeof(RectTransform));
        var fxRect = fx.GetComponent<RectTransform>();
        fxRect.SetParent(rect, false);
        Stretch(fxRect);
        so.FindProperty("fxMount").objectReferenceValue = fxRect;
        so.ApplyModifiedPropertiesWithoutUndo();
        return go;
    }

    private static GameObject MakeEntity(string name, SokobanEntityKind kind, Sprite body, Sprite[][] frames)
    {
            var go = new GameObject(name, typeof(RectTransform));
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            var visual = new GameObject("Body", typeof(RectTransform), typeof(Image));
            var visualRect = visual.GetComponent<RectTransform>();
            visualRect.SetParent(rect, false);
            Stretch(visualRect);
            var image = visual.GetComponent<Image>();
        image.sprite = body;
        image.color = Color.white;
        image.raycastTarget = false;
        var view = go.AddComponent<SokobanEntityView>();
        EnsureScript(view);
        var so = new SerializedObject(view);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.FindProperty("body").objectReferenceValue = image;
        var fx = new GameObject("FxMount", typeof(RectTransform));
        var fxRect = fx.GetComponent<RectTransform>();
        fxRect.SetParent(rect, false);
        Stretch(fxRect);
        so.FindProperty("fxMount").objectReferenceValue = fxRect;
        var audio = go.AddComponent<AudioSource>();
        audio.playOnAwake = false;
        so.FindProperty("audioSource").objectReferenceValue = audio;
        if (kind == SokobanEntityKind.Box)
        {
            so.FindProperty("boxSprite").objectReferenceValue = Img("Box");
            so.FindProperty("boxOnGoalSprite").objectReferenceValue = Img("Box_Aid");
        }
        else if (frames != null)
        {
            string[] names = { "playerUp", "playerDown", "playerLeft", "playerRight" };
            for (var i = 0; i < names.Length; i++)
            {
                var array = so.FindProperty(names[i]);
                array.arraySize = frames[i].Length;
                for (var f = 0; f < frames[i].Length; f++)
                    array.GetArrayElementAtIndex(f).objectReferenceValue = frames[i][f];
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        return go;
    }

    private static void SavePrefab(GameObject source, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(source, path);
        Object.DestroyImmediate(source);
    }

    /// <summary>
    /// 显式把 MonoScript 资产引用写进组件，避免 AddComponent 在"脚本刚被修改/新增的同一次域里"
    /// 解析出空引用或裸 fileID（表现为 m_Script: {fileID: 0} / 加载时 "referenced script is missing"）。
    /// </summary>
    private static void EnsureScript(MonoBehaviour component)
    {
        var script = MonoScript.FromMonoBehaviour(component);
        if (script == null) { Debug.LogError("无法解析 MonoScript: " + component.GetType().FullName); return; }
        var so = new SerializedObject(component);
        var property = so.FindProperty("m_Script");
        if (property.objectReferenceValue != script)
        {
            property.objectReferenceValue = script;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ---------------------------------------------------------------- 场景

    [MenuItem("Sokoban/Bake Game Scene")]
    public static void BakeGameScene()
    {
        CreateBoardPrefabs();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 0f);
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);
        cameraObject.AddComponent<SokobanSceneStartup>();
        EnsureScript(cameraObject.GetComponent<SokobanSceneStartup>());

        var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        var canvasObject = new GameObject("SokobanRuntimeCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0.5f;
        Panel(canvasObject.transform, "ScreenBackground", SokobanTheme.Background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        var workspaceRect = new GameObject("Workspace", typeof(RectTransform)).GetComponent<RectTransform>();
        workspaceRect.SetParent(canvasObject.transform, false);
        workspaceRect.anchorMin = workspaceRect.anchorMax = workspaceRect.pivot = new Vector2(0.5f, 0.5f);
        workspaceRect.sizeDelta = new Vector2(1920f, 1080f);
        var workspaceBackground = Panel(workspaceRect, "Background", SokobanTheme.Background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        workspaceBackground.SetAsFirstSibling();
        var interaction = workspaceRect.gameObject.AddComponent<CanvasGroup>();

        var controllerObject = new GameObject("SokobanGameplayController");
        controllerObject.transform.SetParent(canvasObject.transform, false);
        var controller = controllerObject.AddComponent<SokobanGameplaySceneController>();
        EnsureScript(controller);
        var controllerSo = new SerializedObject(controller);
        var refs = controllerSo.FindProperty("refs");

        var hud = new GameObject("Hud", typeof(RectTransform)).GetComponent<RectTransform>();
        hud.SetParent(workspaceRect, false);
        Stretch(hud);

        refs.FindPropertyRelative("canvas").objectReferenceValue = canvas;
        refs.FindPropertyRelative("root").objectReferenceValue = workspaceRect;
        refs.FindPropertyRelative("hud").objectReferenceValue = hud;
        refs.FindPropertyRelative("interaction").objectReferenceValue = interaction;

        refs.FindPropertyRelative("title").objectReferenceValue =
            Text(hud, "Title", "推箱子", 40, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(0f, 68f), TextAnchor.MiddleCenter);
        refs.FindPropertyRelative("stats").objectReferenceValue =
            Text(hud, "Stats", "", 24, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -104f), new Vector2(560f, 40f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
        _ = Text(hud, "GMHint", "方向键 / WASD · Z 撤销 · R 重开", 18, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Margin, -104f), new Vector2(460f, 40f), TextAnchor.MiddleRight, SokobanTheme.TextSecondary);
        refs.FindPropertyRelative("viewMode").objectReferenceValue =
            Text(hud, "ViewMode", "固定视野 · M 查看完整地图", 20, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -104f), new Vector2(560f, 40f), TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);
        refs.FindPropertyRelative("message").objectReferenceValue =
            Text(hud, "Message", "", 22, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, BottomInset + 16f), new Vector2(900f, 44f), TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);

        var boardPanel = Panel(hud, "BoardPanel", SokobanTheme.BoardBackground, Vector2.zero, Vector2.one,
            new Vector2(340f, BottomInset + 66f), new Vector2(-340f, -(TopInset + 54f)));
        var viewportRect = new GameObject("GameViewport", typeof(RectTransform), typeof(RectMask2D), typeof(SokobanBoardView)).GetComponent<RectTransform>();
        viewportRect.SetParent(boardPanel, false);
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = new Vector2(18f, 18f);
        viewportRect.offsetMax = new Vector2(-18f, -18f);
        var boardView = viewportRect.GetComponent<SokobanBoardView>();
        EnsureScript(boardView);
        AssignBoardPrefabs(boardView);
        refs.FindPropertyRelative("boardView").objectReferenceValue = boardView;

        refs.FindPropertyRelative("undoButton").objectReferenceValue = Button(hud, "UndoButton", "撤销", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, Margin), new Vector2(170f, 58f));
        refs.FindPropertyRelative("restartButton").objectReferenceValue = Button(hud, "RestartButton", "重开", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin + 190f, Margin), new Vector2(170f, 58f));
        refs.FindPropertyRelative("levelButton").objectReferenceValue = Button(hud, "LevelButton", "选关", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(170f, 58f));
        refs.FindPropertyRelative("mapButton").objectReferenceValue = Button(hud, "MapButton", "地图  M", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-120f, Margin), new Vector2(210f, 58f));
        refs.FindPropertyRelative("pauseButton").objectReferenceValue = Button(hud, "PauseButton", "暂停  P", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(120f, Margin), new Vector2(210f, 58f));

        // ---- 错误分支（默认隐藏）----
        var errorPanel = Panel(workspaceRect, "ErrorPanel", new Color(0f, 0f, 0f, 0f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        errorPanel.gameObject.SetActive(false);
        Text(errorPanel, "ErrorTitle", "关卡暂时无法开始", 42, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(0f, 70f), TextAnchor.MiddleCenter);
        refs.FindPropertyRelative("errorPanel").objectReferenceValue = errorPanel;
        refs.FindPropertyRelative("errorText").objectReferenceValue =
            Text(errorPanel, "Error", "", 26, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000f, 220f), TextAnchor.MiddleCenter);
        refs.FindPropertyRelative("errorBack").objectReferenceValue = Button(errorPanel, "ErrorBack", "返回选关", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, Margin), new Vector2(220f, 58f));

        // ---- 胜利弹窗（默认隐藏）----
        var winPanel = Overlay(workspaceRect, "WinPanel");
        refs.FindPropertyRelative("winPanel").objectReferenceValue = winPanel;
        var winDialog = Panel(winPanel, "WinDialog", SokobanTheme.WinPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-330f, -235f), new Vector2(330f, 235f));
        refs.FindPropertyRelative("winTitle").objectReferenceValue =
            Text(winDialog, "WinTitle", "通关！", 48, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 148f), new Vector2(580f, 84f), TextAnchor.MiddleCenter);
        refs.FindPropertyRelative("winStats").objectReferenceValue =
            Text(winDialog, "WinStats", "", 24, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(580f, 150f), TextAnchor.MiddleCenter);
        refs.FindPropertyRelative("nextButton").objectReferenceValue = AccentButton(winDialog, "Next", "下一关", new Vector2(-152f, -112f), new Vector2(280f, 56f));
        refs.FindPropertyRelative("replayButton").objectReferenceValue = Button(winDialog, "Replay", "重玩", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(152f, -112f), new Vector2(280f, 56f));
        refs.FindPropertyRelative("winBackButton").objectReferenceValue = Button(winDialog, "WinBack", "返回选关", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -180f), new Vector2(584f, 48f));

        // ---- 暂停弹窗（默认隐藏）----
        var pauseOverlay = Overlay(workspaceRect, "PauseOverlay");
        refs.FindPropertyRelative("pauseOverlay").objectReferenceValue = pauseOverlay;
        var pauseDialog = Panel(pauseOverlay, "PauseDialog", SokobanTheme.Panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-310f, -220f), new Vector2(310f, 220f));
        Text(pauseDialog, "PauseTitle", "已暂停", 42, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 155f), new Vector2(540f, 70f), TextAnchor.MiddleCenter);
        Text(pauseDialog, "PauseHint", "P / Esc 继续 · 暂停期间不计时", 21, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 85f), new Vector2(540f, 40f), TextAnchor.MiddleCenter);
        refs.FindPropertyRelative("pauseResume").objectReferenceValue = AccentButton(pauseDialog, "PauseResume", "继续游戏", new Vector2(0f, 22f), new Vector2(480f, 54f));
        refs.FindPropertyRelative("pauseRestart").objectReferenceValue = Button(pauseDialog, "PauseRestart", "重开当前关卡", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(480f, 54f));
        refs.FindPropertyRelative("pauseSettings").objectReferenceValue = Button(pauseDialog, "PauseSettings", "设置", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -102f), new Vector2(480f, 54f));
        refs.FindPropertyRelative("pauseLevels").objectReferenceValue = Button(pauseDialog, "PauseLevels", "返回选关", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -164f), new Vector2(480f, 54f));

        // ---- 完整地图弹窗（默认隐藏）----
        var mapOverlay = Overlay(workspaceRect, "MapOverlay", SokobanTheme.Background);
        refs.FindPropertyRelative("mapOverlay").objectReferenceValue = mapOverlay;
        refs.FindPropertyRelative("mapTitle").objectReferenceValue =
            Text(mapOverlay, "MapTitle", "完整地图", 34, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -24f), new Vector2(1450f, 64f), TextAnchor.MiddleLeft);
        refs.FindPropertyRelative("mapClose").objectReferenceValue = Button(mapOverlay, "MapClose", "回到游戏", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Margin, -30f), new Vector2(210f, 54f));
        var mapArea = Panel(mapOverlay, "MapArea", SokobanTheme.BoardBackground, Vector2.zero, Vector2.one,
            new Vector2(Margin, BottomInset), new Vector2(-Margin, -TopInset));
        var mapViewportRect = new GameObject("MapViewport", typeof(RectTransform), typeof(RectMask2D), typeof(SokobanBoardView)).GetComponent<RectTransform>();
        mapViewportRect.SetParent(mapArea, false);
        mapViewportRect.anchorMin = Vector2.zero;
        mapViewportRect.anchorMax = Vector2.one;
        mapViewportRect.offsetMin = new Vector2(18f, 18f);
        mapViewportRect.offsetMax = new Vector2(-18f, -18f);
        var mapView = mapViewportRect.GetComponent<SokobanBoardView>();
        EnsureScript(mapView);
        AssignBoardPrefabs(mapView);
        refs.FindPropertyRelative("mapView").objectReferenceValue = mapView;
        refs.FindPropertyRelative("mapHint").objectReferenceValue =
            Text(mapOverlay, "MapHint", "", 22, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(1400f, 48f), TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);

        controllerSo.ApplyModifiedPropertiesWithoutUndo();

        Wire(refs, controller, "undoButton", "OnUndoClicked");
        Wire(refs, controller, "restartButton", "OnRestartClicked");
        Wire(refs, controller, "levelButton", "OnLevelClicked");
        Wire(refs, controller, "mapButton", "OnMapClicked");
        Wire(refs, controller, "pauseButton", "OnPauseClicked");
        Wire(refs, controller, "errorBack", "OnErrorBackClicked");
        Wire(refs, controller, "nextButton", "OnWinNextClicked");
        Wire(refs, controller, "replayButton", "OnWinReplayClicked");
        Wire(refs, controller, "winBackButton", "OnWinBackClicked");
        Wire(refs, controller, "pauseResume", "OnPauseResumeClicked");
        Wire(refs, controller, "pauseRestart", "OnPauseRestartClicked");
        Wire(refs, controller, "pauseSettings", "OnPauseSettingsClicked");
        Wire(refs, controller, "pauseLevels", "OnPauseLevelsClicked");
        Wire(refs, controller, "mapClose", "OnMapCloseClicked");

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("SOKOBAN_BAKE: game scene baked to " + ScenePath);
    }

    private static void AssignBoardPrefabs(SokobanBoardView view)
    {
        var so = new SerializedObject(view);
        so.FindProperty("floorTilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Tile_Floor.prefab");
        so.FindProperty("wallTilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Tile_Wall.prefab");
        so.FindProperty("goalTilePrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Tile_Goal.prefab");
        so.FindProperty("boxEntityPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Entity_Box.prefab");
        so.FindProperty("playerEntityPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Entity_Player.prefab");
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static RectTransform Overlay(Transform parent, string name, Color? color = null)
    {
        var rect = Panel(parent, name, color ?? new Color(0f, 0f, 0f, 0.76f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        rect.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
        rect.gameObject.SetActive(false);
        return rect;
    }

    private static Button AccentButton(Transform parent, string name, string label, Vector2 position, Vector2 size)
    {
        return Button(parent, name, label, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size, SokobanTheme.Accent, SokobanTheme.AccentText);
    }

    // ---------------------------------------------------------------- 基础构件

    private static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        go.GetComponent<Image>().color = color;
        return rect;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static Text Text(Transform parent, string name, string value, int size, Vector2 anchorMin, Vector2 anchorMax,
        Vector2 pivot, Vector2 position, Vector2 sizeDelta, TextAnchor alignment, Color? color = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = sizeDelta;
        var label = go.GetComponent<Text>();
        label.text = value ?? "";
        label.fontSize = size;
        label.color = color ?? SokobanTheme.TextPrimary;
        label.alignment = alignment;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        label.font = Font;
        label.raycastTarget = false;
        return label;
    }

    private static Button Button(Transform parent, string name, string label, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size,
        Color? color = null, Color? textColor = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = go.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        var image = go.GetComponent<Image>();
        image.color = color ?? SokobanTheme.Surface;
        var button = go.GetComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
        button.colors = colors;
        var labelText = Text(go.transform, "Label", label, 24, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
            TextAnchor.MiddleCenter, textColor ?? SokobanTheme.TextPrimary);
        labelText.rectTransform.offsetMin = new Vector2(12f, 4f);
        labelText.rectTransform.offsetMax = new Vector2(-12f, -4f);
        labelText.verticalOverflow = VerticalWrapMode.Overflow;
        if (!label.Contains("\n"))
        {
            labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
            labelText.resizeTextForBestFit = true;
            labelText.resizeTextMinSize = 14;
            labelText.resizeTextMaxSize = 24;
        }
        return button;
    }

    private static void Wire(SerializedProperty refs, SokobanGameplaySceneController controller, string field, string method)
    {
        var button = refs.FindPropertyRelative(field).objectReferenceValue as Button;
        if (button == null) { Debug.LogError("wire missing button " + field); return; }
        var action = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), controller, method);
        UnityEventTools.AddPersistentListener(button.onClick, action);
    }
}
