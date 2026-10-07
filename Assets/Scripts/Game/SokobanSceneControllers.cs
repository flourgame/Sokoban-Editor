using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    public static class SokobanRuntimeBootstrap
    {
        private static bool installed;
        private static readonly Dictionary<string, Action> RegisteredControllers = new Dictionary<string, Action>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            installed = false; RegisteredControllers.Clear();
            SokobanRuntimeContext.IsGmEnabled = false;
            SokobanRuntimeContext.ShowAllLevels = false; SokobanRuntimeContext.IsDebugPlay = false;
            SokobanRuntimeContext.IsEditorPreview = false; SokobanRuntimeContext.IsQuitConfirmationOpen = false;
        }

        /// <summary>
        /// 供外部模块（如编辑器）自注册场景控制器，避免游戏侧代码直接依赖编辑器类型。
        /// 编辑器在 Assets/level_editor 下通过 RuntimeInitializeOnLoadMethod 调用本方法注册 "editor" 场景。
        /// 注意：文件夹不能命名为 "Editor"——Unity 保留该名给编辑器专用脚本，运行时 MonoBehaviour 无法挂载。
        /// </summary>
        public static void RegisterController(string sceneName, Action creator)
        {
            if (string.IsNullOrEmpty(sceneName) || creator == null) return;
            RegisteredControllers[sceneName] = creator;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (installed) return;
            installed = true;
            SceneManager.sceneLoaded += OnSceneLoaded;
            // 处理编辑器禁用 Domain Reload 或脚本热重载后当前场景已经存在的情况。
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded) OnSceneLoaded(activeScene, LoadSceneMode.Single);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "start") CreateController<SokobanStartSceneController>("SokobanStartController");
            else if (scene.name == "level") CreateController<SokobanLevelSelectSceneController>("SokobanLevelSelectController");
            else if (scene.name == "game") CreateController<SokobanGameplaySceneController>("SokobanGameplayController");
            else if (RegisteredControllers.TryGetValue(scene.name, out var creator)) creator();
        }

        public static void ActivateForScene(Scene scene)
        {
            if (scene.IsValid() && scene.isLoaded) OnSceneLoaded(scene, LoadSceneMode.Single);
        }

        private static void CreateController<T>(string name) where T : Component
        {
            if (UnityEngine.Object.FindObjectOfType<T>() != null) return;
            SokobanUI.EnsureEventSystem();
            var root = new GameObject(name);
            root.AddComponent<T>();
        }
    }


    /// <summary>
    /// 所有界面共用的布局基类。
    /// 布局采用"边缘锚定"：顶部是标题带（0..TopInset），中部是内容带，底部是按钮带（0..BottomInset）。
    /// 面板一律锚定在内容带内，标题/返回键等锚定在各自的带内，因此任何分辨率下都不会互相重叠。
    /// </summary>
    public abstract class SokobanSceneController : MonoBehaviour
    {
        protected Canvas Canvas;
        protected RectTransform Root;

        protected const float Margin = 36f;
        protected const float TopInset = 116f;
        protected const float BottomInset = 104f;

        protected virtual void Awake()
        {
            CreateShell();
        }

        protected virtual void Start()
        {
            SokobanPlayerPresentation.Install(Canvas, Root);
        }

        /// <summary>
        /// 构建界面外壳（画布 + 工作区）。
        /// 默认实现沿用"运行时动态创建"，供 start / level / editor 三个场景使用；
        /// 游玩场景改为在 game.unity 里烘焙外壳，由 SokobanGameplaySceneController 重写为"绑定场景对象"，
        /// 这样布局可以在 Scene 视图里所见即所得地手动调整。
        /// </summary>
        protected virtual void CreateShell()
        {
            SokobanUI.EnsureEventSystem();
            Canvas = SokobanUI.CreateCanvas("SokobanRuntimeCanvas", 100);
            SokobanUI.Panel(Canvas.transform, "ScreenBackground", SokobanTheme.Background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var workspace = new GameObject("Workspace", typeof(RectTransform));
            Root = workspace.GetComponent<RectTransform>(); Root.SetParent(Canvas.transform, false);
            Root.anchorMin = Root.anchorMax = Root.pivot = new Vector2(0.5f, 0.5f);
            Root.sizeDelta = new Vector2(1920f, 1080f);
            var background = SokobanUI.Panel(Root, "Background", SokobanTheme.Background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            background.SetAsFirstSibling();
        }

        protected static Color? TextFor(Color? background)
        {
            if (background.HasValue && background.Value == SokobanTheme.Accent) return SokobanTheme.AccentText;
            return null;
        }

        /// <summary>横向拉伸、锚定到父级某条边的文本。</summary>
        protected UnityEngine.UI.Text StretchLabel(Transform parent, string name, string text, int size, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, Vector2 sizeDelta, TextAnchor alignment = TextAnchor.MiddleCenter, Color? color = null)
        {
            var label = SokobanUI.Text(parent, name, text, size, color ?? SokobanTheme.TextPrimary, alignment);
            var rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;
            return label;
        }

        /// <summary>固定尺寸、锚定到父级某个锚点的文本。</summary>
        protected UnityEngine.UI.Text LabelAt(Transform parent, string name, string text, int size, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 sizeDelta, TextAnchor alignment = TextAnchor.MiddleLeft, Color? color = null)
        {
            var label = SokobanUI.Text(parent, name, text, size, color ?? SokobanTheme.TextPrimary, alignment);
            var rect = label.rectTransform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = sizeDelta;
            return label;
        }

        /// <summary>顶部标题带内的主标题，横向铺满，不会与内容带重叠。框高随字号自适应，避免 Truncate 裁掉大字。</summary>
        protected UnityEngine.UI.Text TopTitle(string text, int size)
        {
            return StretchLabel(Root, "Title", text, size, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(0f, size + 28f));
        }

        /// <summary>主标题下方的副标题。</summary>
        protected UnityEngine.UI.Text TopSubtitle(string text, int size)
        {
            return StretchLabel(Root, "Subtitle", text, size, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(0f, 40f), TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);
        }

        /// <summary>面板顶部的栏目标题，横向铺满，位于滚动列表上方。</summary>
        protected UnityEngine.UI.Text PanelHeader(Transform parent, string text, int size)
        {
            return StretchLabel(parent, "Header", text, size, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(0f, 46f), TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);
        }

        /// <summary>填充父级、四周留白的面板（边缘锚定，分辨率无关）。</summary>
        protected RectTransform InsetPanel(Transform parent, string name, Color color, float left, float right, float top, float bottom)
        {
            return SokobanUI.Panel(parent, name, color, Vector2.zero, Vector2.one, new Vector2(left, bottom), new Vector2(-right, -top));
        }

        /// <summary>固定尺寸、锚定到父级某个锚点的按钮。</summary>
        protected Button ButtonAt(Transform parent, string name, string text, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, Action action, Color? color = null)
        {
            var button = SokobanUI.Button(parent, name, text, size, action, color, TextFor(color));
            var rect = button.GetComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            return button;
        }

        /// <summary>居中锚定的按钮。</summary>
        protected Button ActionButton(Transform parent, string name, string text, Vector2 position, Vector2 size, Action action, Color? color = null)
        {
            return ButtonAt(parent, name, text, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), position, size, action, color);
        }

        protected void GoTo(string sceneName)
        {
            SokobanSceneTransition.Load(sceneName);
        }
    }

    public sealed class SokobanStartSceneController : SokobanSceneController
    {
        private Button settingsButton;
        private Button quitButton;

        protected override void Awake()
        {
            base.Awake();
            CreateStartLogo();
            var card = SokobanUI.Panel(Root, "MenuCard", SokobanTheme.Panel,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-632f, 66f), new Vector2(632f, 210f));
            var startButton = ActionButton(card, "StartButton", "开始游戏", new Vector2(-360f, 0f), new Vector2(496f, 104f),
                () => { SokobanRuntimeContext.ShowAllLevels = false; GoTo("level"); }, SokobanTheme.Accent);
            settingsButton = ActionButton(card, "SettingsButton", "设置", new Vector2(80f, 0f), new Vector2(336f, 104f), SokobanSettingsPanel.Show);
            quitButton = ActionButton(card, "QuitButton", "退出", new Vector2(440f, 0f), new Vector2(336f, 104f), Application.Quit);
            StyleMenuLabel(startButton, 44);
            StyleMenuLabel(settingsButton, 36);
            StyleMenuLabel(quitButton, 36);
        }

        protected override void Start()
        {
            base.Start();
            settingsButton.targetGraphic.color = new Color(0.30f, 0.55f, 0.51f, 1f);
            quitButton.targetGraphic.color = new Color(0.61f, 0.35f, 0.30f, 1f);
        }

        private void CreateStartLogo()
        {
            var logo = SokobanUI.BalatroLogo(Root, "StartLogo", new Vector2(0.5f, 0.61f), Vector2.one * 0.5f,
                Vector2.zero, new Vector2(1360f, 780f));
            // The complete generated logo can replace the temporary composition.
            var customLogo = Resources.Load<Sprite>("StartLogo") ?? Resources.Load<Sprite>("UI/StartLogo");
            if (customLogo != null)
            {
                logo.sprite = customLogo;
                logo.gameObject.AddComponent<SokobanLogoMotion>();
                return;
            }

            var box = new GameObject("PlaceholderBox", typeof(RectTransform), typeof(Image), typeof(Shadow));
            var rect = box.GetComponent<RectTransform>();
            rect.SetParent(logo.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
            rect.anchoredPosition = new Vector2(-42f, 0f);
            rect.sizeDelta = new Vector2(210f, 210f);
            rect.localRotation = Quaternion.Euler(0f, 0f, -8f);
            var image = box.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("img/Box");
            image.color = SokobanBalatroSkin.BoxTint;
            image.preserveAspect = true;
            image.raycastTarget = false;
            var shadow = box.GetComponent<Shadow>();
            shadow.effectColor = new Color(0.05f, 0.09f, 0.10f, 0.85f);
            shadow.effectDistance = new Vector2(5f, -9f);
            logo.gameObject.AddComponent<SokobanLogoMotion>();
        }

        private static void StyleMenuLabel(Button button, int size)
        {
            var label = button.GetComponentInChildren<Text>();
            label.fontSize = size;
            label.resizeTextMinSize = 24;
            label.resizeTextMaxSize = size;
            label.fontStyle = FontStyle.Bold;
        }
    }

    public sealed class SokobanLevelSelectSceneController : SokobanSceneController
    {
        private readonly List<SokobanLevelDescriptor> descriptors = new List<SokobanLevelDescriptor>();
        private SokobanLevelDescriptor selected;
        private UnityEngine.UI.Text previewTitle;
        private UnityEngine.UI.Text previewInfo;
        private RectTransform listContent;
        private Button startButton;
        private bool showAll;
        private SokobanProgressStore progress;
        private List<string> campaignIds;
        private readonly Dictionary<string, Button> rows = new Dictionary<string, Button>();
        private bool progressSubscribed;

        protected override void Awake()
        {
            base.Awake();
            showAll = SokobanRuntimeContext.ShowAllLevels;
            progress = SokobanProgressStore.Current;
            SubscribeProgress();
            TopTitle(showAll ? "全部关卡 · GM" : "选择关卡", 52);
            var left = InsetPanel(Root, "LevelListPanel", SokobanTheme.Panel, Margin, 984f, TopInset, BottomInset);
            var right = InsetPanel(Root, "PreviewPanel", SokobanTheme.Panel, 984f, Margin, TopInset, BottomInset);
            PanelHeader(left, showAll ? "全部关卡" : "玩家关卡", 26);
            SokobanUI.ScrollList(left, "LevelScroll", Vector2.zero, Vector2.one, new Vector2(16f, 16f), new Vector2(-16f, -80f), out listContent);
            previewTitle = StretchLabel(right, "PreviewTitle", "请选择关卡", 34, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(0f, 60f));
            previewInfo = StretchLabel(right, "PreviewInfo", "", 22, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(0f, 300f));
            startButton = ButtonAt(right, "StartButton", showAll ? "GM 游玩" : "开始关卡", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 42f), new Vector2(320f, 62f), StartSelected, SokobanTheme.Accent);
            ButtonAt(Root, "BackButton", "返回", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, Margin), new Vector2(180f, 56f), () => GoTo("start"));
            RefreshList();
        }

        private void OnEnable() { SubscribeProgress(); }
        private void OnDisable()
        {
            if (progress != null && progressSubscribed)
            {
                progress.Changed -= RefreshList;
                progressSubscribed = false;
            }
        }

        private void SubscribeProgress()
        {
            if (progress != null && !progressSubscribed)
            {
                progress.Changed += RefreshList;
                progressSubscribed = true;
            }
        }

        private void RefreshList()
        {
            var selectedId = selected?.LevelId;
            descriptors.Clear();
            selected = null; rows.Clear(); startButton.interactable = false;
            var all = SokobanLevelRepository.ListAll();
            campaignIds = SokobanCampaign.Ids();
            descriptors.AddRange(showAll ? all : all.Where(d => campaignIds.Contains(d.LevelId)));
            foreach (Transform child in listContent) child.gameObject.SetActive(false);
            SokobanUI.DestroyChildren(listContent);
            foreach (var descriptor in descriptors)
            {
                var current = descriptor;
                var button = SokobanUI.Button(listContent, "Level_" + descriptor.LevelId, descriptor.DisplayTitle, new Vector2(0f, 64f), () => Select(current));
                var layout = button.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 64f;
                layout.minHeight = 64f;
                rows[descriptor.LevelId] = button;
                var label = button.GetComponentInChildren<UnityEngine.UI.Text>();
                label.supportRichText = false;
                var data = SokobanLevelRepository.LoadJson(descriptor);
                var score = data != null && SokobanValidation.Validate(data).Count == 0 ? progress.Find(data) : null;
                var unlocked = progress.IsUnlocked(descriptor.LevelId, campaignIds);
                SokobanUI.Ellipsize(label, descriptor.DisplayTitle.Replace('\n', ' ').Replace('\r', ' '),
                    (showAll ? " · " + descriptor.Category : "") + (score?.completed == true ? " · 已完成 " + score.bestMoves + " 步" : !showAll && !unlocked ? " · 未解锁" : ""), 840f);
                if (descriptor.LevelId == selectedId) selected = current;
                else if (selectedId == null && selected == null && (showAll || unlocked) && score?.completed != true) selected = current;
            }
            if (selected == null) selected = descriptors.FirstOrDefault();
            if (selected != null) Select(selected);
            if (descriptors.Count == 0)
            {
                previewTitle.text = "暂无关卡"; previewInfo.text = "";
                SokobanUI.Text(listContent, "Empty", "暂无可显示的关卡。", 20, SokobanTheme.TextSecondary).gameObject.AddComponent<LayoutElement>().preferredHeight = 120f;
            }
            SokobanPlayerPresentation.Adopt(listContent);
        }

        private void Select(SokobanLevelDescriptor descriptor)
        {
            selected = descriptor;
            var data = SokobanLevelRepository.LoadJson(descriptor);
            previewTitle.supportRichText = false;
            SokobanUI.Ellipsize(previewTitle, descriptor.DisplayTitle.Replace('\n', ' ').Replace('\r', ' '), "", 840f);
            foreach (var pair in rows)
            {
                pair.Value.targetGraphic.color = pair.Key == descriptor.LevelId ? SokobanBalatroSkin.AccentColor : SokobanBalatroSkin.ButtonColor;
                pair.Value.GetComponentInChildren<UnityEngine.UI.Text>().color = pair.Key == descriptor.LevelId ? SokobanBalatroSkin.AccentTextColor : SokobanBalatroSkin.TextColor;
            }
            if (data == null)
            {
                previewInfo.text = "关卡读取失败";
                startButton.interactable = false;
                return;
            }
            var validation = SokobanValidation.Validate(data);
            var unlocked = showAll || progress.IsUnlocked(descriptor.LevelId, campaignIds);
            startButton.interactable = unlocked && validation.Count == 0 && data.solution?.status != "Unsolvable";
            var score = validation.Count == 0 ? progress.Find(data) : null;
            previewInfo.supportRichText = false;
            previewInfo.text = $"尺寸：{data.size.width} × {data.size.height}\n箱子：{data.boxes.Length}    目标：{data.goals.Length}\n" +
                (showAll ? "分类：" + descriptor.Category + "\n" : "") +
                "状态：" + (validation.Count > 0 ? string.Join("；", validation.Take(2)) : data.solution?.status == "Unsolvable" ? "已确认无解" : !unlocked ? "未解锁，请先通关上一关" : showAll ? "GM 游玩 · 不计普通成绩" : "可进入") +
                (score?.completed == true ? $"\n已完成 · 最佳 {score.bestMoves} 步 / {score.bestPushes} 推" : "\n尚未完成") +
                (progress.LastError != null ? "\n" + progress.LastError : "");
        }

        private void StartSelected()
        {
            if (selected == null || !startButton.interactable) return;
            if (!showAll && !progress.IsUnlocked(selected.LevelId, campaignIds)) { Select(selected); return; }
            SokobanRuntimeContext.SelectedLevelId = selected.LevelId;
            SokobanRuntimeContext.IsEditorPreview = false;
            SokobanRuntimeContext.IsDebugPlay = showAll;
            GoTo("game");
        }

        private void Update()
        {
            if (SokobanRuntimeContext.IsQuitConfirmationOpen || SokobanGmController.BlocksInput) return;
            if (Input.GetKeyDown(KeyCode.Escape)) GoTo("start");
        }
    }

}
