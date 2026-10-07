using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>
    /// 棋盘视口：双层渲染 + 实体补间移动。
    ///
    /// 层级结构（全部可在 Scene 视图里看到并手动调整）：
    ///   BoardView(RectMask2D)
    ///   └── Board            随"跟随视野"平移的整体容器
    ///       ├── Tiles        W×H 个地块预制体实例（Floor / Wall / Goal）
    ///       └── Entities     1 个玩家 + N 个箱子预制体实例，是 Tiles 的后兄弟，天然渲染在其上
    ///
    /// 地块在关卡加载时按 JSON 一次性实例化，之后不再变化；
    /// 实体不随格子开关显隐，而是持有自己的网格坐标并按补间移动，
    /// 因此推箱/行走可以做出平滑动画，也方便美术在预制体上挂 Animator、粒子与音效。
    ///
    /// 预制体引用优先取 Inspector 上序列化的字段（场景烘焙时可直接拖拽），
    /// 为空时回退到 Resources/Prefabs/Board 下的同名预制体，保证编辑器回归测试无需场景也能跑。
    /// </summary>
    public sealed class SokobanBoardView : MonoBehaviour
    {
        public const int DefaultFixedWidth = 14, DefaultFixedHeight = 10;
        private const float MoveDuration = 0.11f;
        private const float EntityScale = 0.62f;

        [Header("地块预制体（留空则回退到 Resources/Prefabs/Board）")]
        [SerializeField] private GameObject floorTilePrefab;
        [SerializeField] private GameObject wallTilePrefab;
        [SerializeField] private GameObject goalTilePrefab;
        [Header("实体预制体（留空则回退到 Resources/Prefabs/Board）")]
        [SerializeField] private GameObject boxEntityPrefab;
        [SerializeField] private GameObject playerEntityPrefab;

        private SokobanLevelRuntime level;
        private SokobanState state;
        private RectTransform viewport, board, tilesRoot, entitiesRoot;
        private SokobanGameplayVfx gameplayVfx;
        private readonly List<SokobanTileView> tiles = new List<SokobanTileView>();
        private readonly List<EntitySlot> boxes = new List<EntitySlot>();
        private EntitySlot player;
        private bool allowFollow, initialized;
        private int fixedWidth, fixedHeight;
        private Vector2 lastSize, targetPosition, velocity;
        private float stride;
        private SokobanDirection facing = SokobanDirection.Down;
        private bool layoutInitialized;

        public bool IsFollowing => allowFollow && level != null && (level.Width > fixedWidth || level.Height > fixedHeight);
        public RectTransform Board => board;
        public RectTransform Viewport => viewport;
        public float CellStride => stride;
        public Vector2 CameraTarget => targetPosition;

        private void OnEnable()
        {
            SokobanAnimationSettings.Changed += ApplyAnimationSetting;
            ApplyAnimationSetting(SokobanAnimationSettings.Enabled);
        }
        private void OnDisable() { SokobanAnimationSettings.Changed -= ApplyAnimationSetting; }

        private void ApplyAnimationSetting(bool enabled)
        {
            if (enabled || !initialized || state == null) return;
            SetState(state, true, facing);
        }

        private sealed class EntitySlot
        {
            public RectTransform rect;
            public SokobanEntityView view;
            public SokobanGridPoint grid;
            public SokobanGridPoint fromGrid;
            public Vector2 from, to;
            public float t = 1f;
        }

        public static SokobanBoardView Create(Transform parent, string name, SokobanLevelRuntime level, SokobanState state,
            bool allowFollow = true, int fixedWidth = DefaultFixedWidth, int fixedHeight = DefaultFixedHeight)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(RectMask2D), typeof(SokobanBoardView));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(18f, 18f);
            rect.offsetMax = new Vector2(-18f, -18f);
            var view = root.GetComponent<SokobanBoardView>();
            view.viewport = rect;
            view.Initialize(level, state, allowFollow, fixedWidth, fixedHeight);
            return view;
        }

        /// <summary>供场景烘焙的视口就地初始化（不新建 GameObject）。</summary>
        public void Initialize(SokobanLevelRuntime levelData, SokobanState initialState,
            bool follow = true, int viewWidth = DefaultFixedWidth, int viewHeight = DefaultFixedHeight)
        {
            level = levelData;
            state = initialState;
            allowFollow = follow;
            fixedWidth = Mathf.Max(1, viewWidth);
            fixedHeight = Mathf.Max(1, viewHeight);
            if (viewport == null) viewport = GetComponent<RectTransform>();
            Clear();
            Build();
            initialized = true;
            SetState(initialState, true);
        }

        private void Clear()
        {
            tiles.Clear();
            boxes.Clear();
            player = null;
            layoutInitialized = false;
            lastSize = Vector2.zero;
            if (board != null) Destroy(board.gameObject);
            board = tilesRoot = entitiesRoot = null;
        }

        private void Build()
        {
            if (level == null || viewport == null) return;
            board = new GameObject("Board", typeof(RectTransform)).GetComponent<RectTransform>();
            board.SetParent(viewport, false);
            board.anchorMin = board.anchorMax = board.pivot = new Vector2(0.5f, 0.5f);
            tilesRoot = new GameObject("Tiles", typeof(RectTransform)).GetComponent<RectTransform>();
            tilesRoot.SetParent(board, false);
            Stretch(tilesRoot);
            entitiesRoot = new GameObject("Entities", typeof(RectTransform)).GetComponent<RectTransform>();
            entitiesRoot.SetParent(board, false);
            Stretch(entitiesRoot);
            gameplayVfx = board.gameObject.AddComponent<SokobanGameplayVfx>();
            gameplayVfx.Initialize(entitiesRoot, 0f);

            var floor = Resolve(ref floorTilePrefab, "Tile_Floor");
            var wall = Resolve(ref wallTilePrefab, "Tile_Wall");
            var goal = Resolve(ref goalTilePrefab, "Tile_Goal");
            for (var y = 0; y < level.Height; y++)
            {
                for (var x = 0; x < level.Width; x++)
                {
                    var point = new SokobanGridPoint(x, y);
                    var prefab = level.IsWall(point) ? wall : level.Goals.Contains(point) ? goal : floor;
                    var instance = Instantiate(prefab, tilesRoot);
                    instance.name = $"Tile_{x}_{y}";
                    var rect = instance.GetComponent<RectTransform>();
                    rect.anchorMin = rect.pivot = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    var tile = instance.GetComponent<SokobanTileView>();
                    if (tile == null) tile = instance.AddComponent<SokobanTileView>();
                    tile.ApplyFallbackTint(SokobanTheme.BoardFloor, SokobanTheme.BoardWall, SokobanTheme.Goal, Color.white);
                    tiles.Add(tile);
                }
            }

            var playerPrefab = Resolve(ref playerEntityPrefab, "Entity_Player");
            var playerObject = Instantiate(playerPrefab, entitiesRoot);
            playerObject.name = "Player";
            player = MakeSlot(playerObject, level.PlayerStart);

            var boxPrefab = Resolve(ref boxEntityPrefab, "Entity_Box");
            foreach (var point in level.BoxesStart)
            {
                var boxObject = Instantiate(boxPrefab, entitiesRoot);
                boxObject.name = $"Box_{point.x}_{point.y}";
                boxes.Add(MakeSlot(boxObject, point));
            }
        }

        private EntitySlot MakeSlot(GameObject instance, SokobanGridPoint grid)
        {
            var slot = new EntitySlot();
            slot.rect = instance.GetComponent<RectTransform>();
            slot.rect.anchorMin = slot.rect.anchorMax = slot.rect.pivot = new Vector2(0.5f, 0.5f);
            slot.view = instance.GetComponent<SokobanEntityView>();
            if (slot.view == null) slot.view = instance.AddComponent<SokobanEntityView>();
            slot.view.EnsureSeparateBody();
            slot.grid = slot.fromGrid = grid;
            slot.from = slot.to = CenterOf(grid);
            slot.rect.anchoredPosition = slot.to;
            return slot;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static GameObject Resolve(ref GameObject field, string resource)
        {
            if (field != null) return field;
            field = Resources.Load<GameObject>("Prefabs/Board/" + resource);
            if (field == null) Debug.LogError("缺少棋盘预制体 Resources/Prefabs/Board/" + resource + ".prefab");
            return field;
        }

        public void SetState(SokobanState current, bool snap = false, SokobanDirection? direction = null)
        {
            var previousPlayer = player != null ? player.grid : default(SokobanGridPoint);
            var hadPreviousPlayer = player != null;
            state = current;
            if (!initialized || level == null || state == null) return;
            if (direction.HasValue) facing = direction.Value;

            // Preserve snap's original meaning for audio and move feedback;
            // disabling interpolation must not suppress a real push's sound.
            var instant = snap || !SokobanAnimationSettings.Enabled;
            MoveSlot(player, state.Player, instant);
            player.view.ConfigurePlayer(facing, instant ? 0f : player.t, level.Goals.Contains(state.Player));
            if (!snap && hadPreviousPlayer && previousPlayer != state.Player && SokobanPlayerPresentation.IsEnabled)
            {
                player.view.PlayMoveEffect(false, facing);
                gameplayVfx?.EmitTrail(player.rect.anchoredPosition, facing);
            }

            // 箱子集合没有身份标识：用"最近未匹配"把旧实例对应到新坐标，
            // 单步推动时恰好唯一匹配，撤销/重开等多格变化时给出合理的就近归位。
            var targets = new List<SokobanGridPoint>(state.Boxes);
            while (boxes.Count > targets.Count)
            {
                var last = boxes.Count - 1;
                Destroy(boxes[last].rect.gameObject);
                boxes.RemoveAt(last);
            }
            if (boxes.Count < targets.Count)
            {
                var boxPrefabForExtra = Resolve(ref boxEntityPrefab, "Entity_Box");
                while (boxes.Count < targets.Count)
                {
                    var extra = Instantiate(boxPrefabForExtra, entitiesRoot);
                    extra.name = "Box_extra";
                    boxes.Add(MakeSlot(extra, targets[boxes.Count]));
                }
            }
            // Reserve unchanged boxes before matching moving boxes. Otherwise
            // HashSet enumeration order can assign a pushed target to its neighbour.
            var used = new bool[boxes.Count];
            var matched = new int[targets.Count];
            for (var i = 0; i < targets.Count; i++)
            {
                matched[i] = -1;
                for (var b = 0; b < boxes.Count; b++)
                {
                    if (used[b] || boxes[b].grid != targets[i]) continue;
                    matched[i] = b; used[b] = true; break;
                }
            }
            for (var i = 0; i < targets.Count; i++)
            {
                var best = matched[i];
                var bestDistance = float.MaxValue;
                if (best == -1) for (var b = 0; b < boxes.Count; b++)
                {
                    if (used[b]) continue;
                    var d = Mathf.Abs(boxes[b].grid.x - targets[i].x) + Mathf.Abs(boxes[b].grid.y - targets[i].y);
                    if (d < bestDistance) { bestDistance = d; matched[i] = b; }
                }
                if (best == -1) best = matched[i];
                used[best] = true;
                var oldGrid = boxes[best].grid;
                var wasOnGoal = level.Goals.Contains(oldGrid);
                MoveSlot(boxes[best], targets[i], instant);
                boxes[best].view.ConfigureBox(level.Goals.Contains(targets[i]));
                if (!snap && oldGrid != targets[i] && SokobanPlayerPresentation.IsEnabled)
                {
                    var dx = targets[i].x - oldGrid.x;
                    var dy = targets[i].y - oldGrid.y;
                    var pushDirection = dx > 0 ? SokobanDirection.Right : dx < 0 ? SokobanDirection.Left : dy > 0 ? SokobanDirection.Down : SokobanDirection.Up;
                    boxes[best].view.PlayMoveEffect(true, pushDirection);
                    if (!wasOnGoal && level.Goals.Contains(targets[i]))
                    {
                        boxes[best].view.PlayGoalEffect();
                        gameplayVfx?.EmitBurst(CenterOf(targets[i]), SokobanBalatroSkin.AccentColor);
                        SokobanPresentationAudio.PlaySfx("coin1");
                    }
                    else SokobanPresentationAudio.PlaySfx("crumple1", 1.5f);
                }
            }
            RefreshLayout(instant);
            if (gameplayVfx != null) gameplayVfx.SetStride(stride);
        }

        private void MoveSlot(EntitySlot slot, SokobanGridPoint target, bool snap)
        {
            if (slot == null || slot.rect == null) return;
            if (!snap && slot.grid == target) return;
            // Interruptions (rapid input / undo) continue from the rendered point.
            var sourceGrid = slot.grid;
            var sourcePosition = slot.rect.anchoredPosition;
            slot.fromGrid = sourceGrid;
            slot.grid = target;
            slot.to = CenterOf(target);
            if (snap)
            {
                slot.from = slot.to;
                slot.t = 1f;
                slot.rect.anchoredPosition = slot.to;
                slot.view.ResetMotion();
            }
            else
            {
                slot.from = sourcePosition;
                slot.rect.anchoredPosition = slot.from;
                slot.t = 0f;
            }
        }

        private Vector2 CenterOf(SokobanGridPoint point)
        {
            if (level == null) return Vector2.zero;
            return new Vector2((point.x + 0.5f) * stride - level.Width * stride * 0.5f,
                level.Height * stride * 0.5f - (point.y + 0.5f) * stride);
        }

        private void RefreshLayout(bool snap)
        {
            if (level == null || viewport == null || board == null || state == null) return;
            var size = viewport.rect.size;
            if (size.x < 1f || size.y < 1f) return;
            if (size != lastSize)
            {
                var hadLayout = layoutInitialized;
                var oldStride = stride;
                lastSize = size;
                var columns = IsFollowing ? Mathf.Min(level.Width, fixedWidth) : level.Width;
                var rows = IsFollowing ? Mathf.Min(level.Height, fixedHeight) : level.Height;
                stride = Mathf.Min(82f, Mathf.Min(size.x / columns, size.y / rows));
                board.sizeDelta = new Vector2(level.Width * stride, level.Height * stride);
                var gap = Mathf.Min(3f, stride * 0.06f);
                for (var y = 0; y < level.Height; y++)
                {
                    for (var x = 0; x < level.Width; x++)
                    {
                        var rect = tiles[y * level.Width + x].GetComponent<RectTransform>();
                        rect.anchoredPosition = new Vector2(x * stride + gap * 0.5f, -y * stride - gap * 0.5f);
                        rect.sizeDelta = Vector2.one * (stride - gap);
                    }
                }
                var entitySize = (stride - gap) * EntityScale;
                var all = new List<EntitySlot>(boxes);
                if (player != null) all.Add(player);
                foreach (var slot in all)
                {
                    slot.rect.sizeDelta = Vector2.one * entitySize;
                    slot.from = hadLayout && oldStride > 0f ? slot.from * (stride / oldStride) : CenterOf(slot.fromGrid);
                    slot.to = CenterOf(slot.grid);
                    if (!hadLayout && snap)
                    {
                        slot.from = slot.to;
                        slot.t = 1f;
                    }
                    slot.rect.anchoredPosition = slot.t >= 1f ? slot.to : Vector2.Lerp(slot.from, slot.to, slot.t);
                }
                layoutInitialized = true;
                if (gameplayVfx != null) gameplayVfx.SetStride(stride);
            }
            targetPosition = IsFollowing ? FollowPosition(level.Width, level.Height, state.Player, stride, size) : Vector2.zero;
            if (snap) { board.anchoredPosition = targetPosition; velocity = Vector2.zero; }
        }

        public static Vector2 FollowPosition(int width, int height, SokobanGridPoint player, float stride, Vector2 visibleSize)
        {
            var playerCenter = new Vector2((player.x + 0.5f - width * 0.5f) * stride,
                (height * 0.5f - player.y - 0.5f) * stride);
            var limit = new Vector2(Mathf.Max(0f, (width * stride - visibleSize.x) * 0.5f),
                Mathf.Max(0f, (height * stride - visibleSize.y) * 0.5f));
            return new Vector2(Mathf.Clamp(-playerCenter.x, -limit.x, limit.x), Mathf.Clamp(-playerCenter.y, -limit.y, limit.y));
        }

        private void LateUpdate()
        {
            RefreshLayout(!SokobanAnimationSettings.Enabled);
            if (board == null) return;
            if (!SokobanAnimationSettings.Enabled) return;
            board.anchoredPosition = Vector2.SmoothDamp(board.anchoredPosition, targetPosition, ref velocity,
                0.12f, Mathf.Infinity, Time.unscaledDeltaTime);

            var step = Time.unscaledDeltaTime / MoveDuration;
            if (player != null && player.t < 1f)
            {
                player.t = Mathf.Min(1f, player.t + step);
                player.rect.anchoredPosition = Vector2.Lerp(player.from, player.to, player.t);
                player.view.ConfigurePlayer(facing, player.t, level != null && level.Goals.Contains(player.grid));
            }
            foreach (var slot in boxes)
            {
                if (slot.t >= 1f) continue;
                slot.t = Mathf.Min(1f, slot.t + step);
                slot.rect.anchoredPosition = Vector2.Lerp(slot.from, slot.to, slot.t);
            }
        }
    }
}
