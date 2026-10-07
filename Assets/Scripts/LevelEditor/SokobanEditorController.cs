using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using Kuluobishi.Sokoban;

namespace Kuluobishi.Sokoban.Editor
{
    /// <summary>
    /// 运行时关卡编辑器（设计图版）：顶部工具栏 + 页签栏 + 左列表 / 中编辑区 / 右参数栏。
    /// 编辑区支持：hover 虚影、左键落笔、左键拖拽框选、Ctrl 多选、蓝色虚线选中框、
    /// 中键平移、Ctrl+滚轮缩放、Ctrl+C 复制、Ctrl+V 粘贴虚影、填充、Ctrl+Z 撤销。
    /// 本类自注册到 SokobanRuntimeBootstrap，游戏侧代码不直接引用编辑器类型。
    /// </summary>
    public sealed partial class SokobanEditorSceneController : SokobanSceneController
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterWithBootstrap()
        {
            SokobanRuntimeBootstrap.RegisterController("editor", () =>
            {
                if (UnityEngine.Object.FindObjectOfType<SokobanEditorSceneController>() != null) return;
                SokobanUI.EnsureEventSystem();
                var root = new GameObject("SokobanEditorController");
                root.AddComponent<SokobanEditorSceneController>();
            });
            // 注册与 bootstrap.Install 的先后顺序不保证：若进入 Play 时 editor 场景已加载，
            // 这里主动激活一次，避免 Install 先跑而注册表尚空导致控制器缺失。
            var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (active.IsValid() && active.isLoaded && active.name == "editor")
            {
                SokobanRuntimeBootstrap.ActivateForScene(active);
            }
        }

        private const float ToolbarHeight = 64f;
        private const float TabHeight = 44f;
        private const float SideWidth = 300f;
        private const int UndoLimit = 100;

        private readonly List<SokobanEditorTab> tabs = new List<SokobanEditorTab>();
        private int activeIndex = -1;

        private SokobanBrush? brush = null;
        private readonly Dictionary<SokobanBrush, Button> brushButtons = new Dictionary<SokobanBrush, Button>();
        private Button pasteButton;

        private bool pasteMode;
        private bool pasteOneShot;
        private List<SokobanClipboardCell> clipboard = new List<SokobanClipboardCell>();

        private const float LongPressSeconds = 0.35f;
        private float pressTime;
        private SokobanGridPoint pressCell = new SokobanGridPoint(-1, -1);
        private bool moveArmed;

        private readonly HashSet<SokobanGridPoint> selection = new HashSet<SokobanGridPoint>();
        private bool boxSelecting;
        private bool boxSelectMoved;
        private Vector2 boxAnchorLocal;
        private Vector2 boxCurrentLocal;

        private Vector2 pan = Vector2.zero;
        private float zoom = 1f;
        private bool panning;
        private Vector2 panLastMouse;

        private SokobanGridPoint hoverCell = new SokobanGridPoint(-1, -1);
        private readonly List<SokobanGridPoint> ghostedCells = new List<SokobanGridPoint>();

        private RectTransform viewport;
        private RectTransform emptyEditorState;
        private readonly List<Selectable> documentControls = new List<Selectable>();
        private RectTransform content;
        private RectTransform selectionRoot;
        private RectTransform marqueeBox;
        private readonly List<Image> cellImages = new List<Image>();
        private readonly List<SokobanGoalGraphic> cellGoals = new List<SokobanGoalGraphic>();
        private readonly List<Image> cellObjects = new List<Image>();
        private float cellSize = 48f;
        private const float CellSpacing = 2f;

        private RectTransform tabBar;
        private RectTransform listContent;
        private string categoryFilter = "All";
        private RectTransform categoryPopup;
        private UnityEngine.UI.Text categoryLabel;
        private UnityEngine.UI.InputField nameField;
        private UnityEngine.UI.InputField widthField;
        private UnityEngine.UI.InputField heightField;
        private UnityEngine.UI.Text detailText;
        private RectTransform closeTabOverlay;
        private UnityEngine.UI.Text closeTabMessage;
        private SokobanEditorTab pendingCloseTab;

        private static Sprite dashHorizontal;
        private static Sprite dashVertical;

        private SokobanEditorTab Active => activeIndex >= 0 && activeIndex < tabs.Count ? tabs[activeIndex] : null;
        private SokobanJsonLevel Data => Active?.Data;

        protected override void Awake()
        {
            base.Awake();
            BuildToolbar();
            BuildTabBar();
            BuildBody();
            RefreshList();
            tabs.AddRange(SokobanEditorSession.Tabs);
            SetActiveTab(SokobanEditorSession.ActiveIndex);
            if (tabs.Count > 0) { pan = SokobanEditorSession.Pan; zoom = SokobanEditorSession.Zoom; RebuildGrid(); }
            RecordOperation("进入关卡编辑器");
        }

        // ---------- 布局 ----------

        private void BuildToolbar()
        {
            var bar = InsetPanel(Root, "Toolbar", SokobanTheme.Panel, Margin, Margin, 12f, 1080f - 12f - ToolbarHeight);
            var brushes = new[] { SokobanBrush.Box, SokobanBrush.Player, SokobanBrush.Wall, SokobanBrush.Goal, SokobanBrush.Floor };
            // 工具栏/页签按钮统一用"居中锚点 + 相对中心偏移"，避免边锚点组合下 Text 网格不生成的问题。
            var leftEdge = -960f + Margin;
            var x = leftEdge + 20f;
            foreach (var b in brushes)
            {
                var current = b;
                var button = ButtonAt(bar, "Brush_" + b, BrushName(b), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 55f, 0f), new Vector2(110f, 44f), () => SetBrush(brush == current ? (SokobanBrush?)null : current));
                brushButtons[b] = button;
                x += 120f;
            }
            x += 40f;
            ButtonAt(bar, "Copy", "复制", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 50f, 0f), new Vector2(100f, 44f), CopySelection); x += 110f;
            pasteButton = ButtonAt(bar, "Paste", "粘贴", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 50f, 0f), new Vector2(100f, 44f), TogglePasteMode); x += 110f;
            ButtonAt(bar, "Fill", "填充", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 50f, 0f), new Vector2(100f, 44f), FillSelection); x += 110f;
            stampButton = ButtonAt(bar, "Stamp", "印章", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 50f, 0f), new Vector2(100f, 44f), OpenStampLibrary);
            ButtonAt(bar, "ImportJson", "导入 JSON", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(x + 220f, 0f), new Vector2(180f, 44f), OpenJsonImport);
            transparentButton = ButtonAt(bar, "StampTransparent", "透明", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(x + 380f, 0f), new Vector2(100f, 44f), ToggleTransparentBrush);
            transparentButton.gameObject.SetActive(false);
            SetBrush(null);
        }

        private void BuildTabBar()
        {
            tabBar = InsetPanel(Root, "TabBar", SokobanTheme.PanelAlt, Margin, Margin, 12f + ToolbarHeight + 8f, 1080f - 12f - ToolbarHeight - 8f - TabHeight);
        }

        private void BuildBody()
        {
            const float top = 12f + ToolbarHeight + 8f + TabHeight + 8f;
            const float bottom = Margin + 68f;

            var left = InsetPanel(Root, "LeftPanel", SokobanTheme.Panel, Margin, 1920f - Margin - SideWidth, top, bottom);
            var categoryButton = ButtonAt(left, "Category", "分类：全部", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(220f, 44f), ToggleCategoryPopup);
            categoryLabel = categoryButton.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
            if (categoryLabel != null) categoryLabel.fontSize = 20;
            SokobanUI.ScrollList(left, "LevelScroll", Vector2.zero, Vector2.one, new Vector2(12f, 76f), new Vector2(-12f, -64f), out listContent);
            ButtonAt(left, "Refresh", "刷新", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(220f, 44f), () => { RefreshList(); SetDetail("已刷新关卡列表"); });
            ButtonAt(Root, "Back", "返回", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Margin, Margin), new Vector2(220f, 52f), ReturnToLevels);
            // 底部功能按钮统一靠右排布。
            ButtonAt(Root, "BatchGenerate", "批量生成", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin - 600f, Margin), new Vector2(180f, 52f), OpenBatchGenerator);
            ButtonAt(Root, "BatchValidate", "批量验证", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin - 400f, Margin), new Vector2(180f, 52f), OpenBatchVerification);
            ButtonAt(Root, "AddCategory", "关卡管理", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin - 200f, Margin), new Vector2(180f, 52f), OpenLevelManager);
            ButtonAt(Root, "OpenLog", "日志", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, Margin), new Vector2(180f, 52f), OpenOperationLog);

            viewport = InsetPanel(Root, "EditViewport", SokobanTheme.BoardBackground, Margin + SideWidth + 12f, Margin + SideWidth + 12f, top, bottom);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = new GameObject("EditContent", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0.5f, 0.5f);
            content.anchorMax = new Vector2(0.5f, 0.5f);
            content.pivot = new Vector2(0.5f, 0.5f);
            selectionRoot = new GameObject("SelectionOverlay", typeof(RectTransform)).GetComponent<RectTransform>();
            selectionRoot.SetParent(content, false);
            selectionRoot.anchorMin = Vector2.zero;
            selectionRoot.anchorMax = Vector2.one;
            selectionRoot.offsetMin = Vector2.zero;
            selectionRoot.offsetMax = Vector2.zero;

            var right = InsetPanel(Root, "RightPanel", SokobanTheme.Panel, 1920f - Margin - SideWidth, Margin, top, bottom);
            StretchLabel(right, "NameLabel", "关卡名称", 18, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(0f, 30f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            nameField = SokobanUI.InputField(right, "NameField", "", "关卡名称", Vector2.zero);
            var nameRect = nameField.GetComponent<RectTransform>();
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.anchoredPosition = new Vector2(0f, -48f);
            nameRect.sizeDelta = new Vector2(-32f, 44f);
            nameField.onEndEdit.AddListener(CommitNameEdit);

            var widthBox = SokobanUI.InputField(right, "WidthField", "8", "长", Vector2.zero);
            widthField = widthBox;
            var widthRect = widthBox.GetComponent<RectTransform>();
            widthRect.anchorMin = new Vector2(0f, 1f); widthRect.anchorMax = new Vector2(0f, 1f); widthRect.pivot = new Vector2(0f, 1f);
            widthRect.anchoredPosition = new Vector2(16f, -104f); widthRect.sizeDelta = new Vector2(100f, 44f);
            LabelAt(right, "SizeX", "×", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(128f, -104f), new Vector2(30f, 44f), TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);
            var heightBox = SokobanUI.InputField(right, "HeightField", "8", "宽", Vector2.zero);
            heightField = heightBox;
            var heightRect = heightBox.GetComponent<RectTransform>();
            heightRect.anchorMin = new Vector2(0f, 1f); heightRect.anchorMax = new Vector2(0f, 1f); heightRect.pivot = new Vector2(0f, 1f);
            heightRect.anchoredPosition = new Vector2(170f, -104f); heightRect.sizeDelta = new Vector2(100f, 44f);
            widthField.onEndEdit.AddListener(value => ApplySize(value, heightField.text));
            heightField.onEndEdit.AddListener(value => ApplySize(widthField.text, value));

            var detailPanel = InsetPanel(right, "DetailPanel", SokobanTheme.Field, 16f, 16f, 168f, 470f);
            RectTransform detailContent;
            SokobanUI.ScrollList(detailPanel, "DetailScroll", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, out detailContent);
            detailText = SokobanUI.Text(detailContent, "Detail", "", 18, SokobanTheme.TextSecondary, TextAnchor.UpperLeft);
            // Let the layout group use the text's preferred height so long validation messages remain readable.
            // Keep the scroll viewport above the document buttons instead of letting text cover them.

            var y = 12f;
            ButtonAt(right, "Play", "试玩", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y + 340f), new Vector2(240f, 50f), PlayCurrent, SokobanTheme.Accent);
            ButtonAt(right, "AutoGen", "自动生成", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y + 280f), new Vector2(240f, 50f), OpenGenerator);
            ButtonAt(right, "Validate", "验证", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y + 220f), new Vector2(240f, 50f), ValidateCurrent);
            ButtonAt(right, "CopyJson", "复制json", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y + 160f), new Vector2(240f, 50f), CopyJson);
            ButtonAt(right, "Write", "保存", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y + 100f), new Vector2(240f, 50f), WriteCurrent);
            ButtonAt(right, "Export", "保存为关卡", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, y + 40f), new Vector2(240f, 50f), ExportAsNew);

            documentControls.AddRange(new Selectable[] { nameField, widthField, heightField });
            documentControls.AddRange(brushButtons.Values);
            var documentButtonNames = new HashSet<string> { "Copy", "Paste", "Fill", "Play", "Validate", "CopyJson", "Write", "Export" };
            documentControls.AddRange(Root.GetComponentsInChildren<Button>().Where(button => documentButtonNames.Contains(button.name)));
            emptyEditorState = new GameObject("EditorEmptyState", typeof(RectTransform)).GetComponent<RectTransform>();
            emptyEditorState.SetParent(viewport, false);
            emptyEditorState.anchorMin = Vector2.zero; emptyEditorState.anchorMax = Vector2.one;
            emptyEditorState.offsetMin = emptyEditorState.offsetMax = Vector2.zero;
            LabelAt(emptyEditorState, "EditorEmptyHint", "请打开左侧关卡以编辑，\n或者点击下面的按钮新增关卡", 28,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 28f), new Vector2(900f, 100f),
                TextAnchor.MiddleCenter, SokobanTheme.TextSecondary);
            ButtonAt(emptyEditorState, "EmptyAddLevel", "新增关卡", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0f, -72f), new Vector2(240f, 54f), AddBlankTab, SokobanTheme.Accent);
        }

        // ---------- 页签 ----------

        private void AddBlankTab()
        {
            var data = new SokobanJsonLevel
            {
                levelId = CreateGeneratedLevelId(),
                name = "新建关卡",
                size = new SokobanJsonSize { width = 8, height = 8 },
                terrain = SokobanLevelRepository.BuildEmptyTerrain(8, 8),
                player = new SokobanJsonPoint(1, 1),
                boxes = new[] { new SokobanJsonPoint(3, 3) },
                goals = new[] { new SokobanJsonPoint(5, 3) },
                generation = new SokobanJsonGeneration { isGenerated = true, seed = Environment.TickCount }
            };
            tabs.Add(new SokobanEditorTab { Data = data, Descriptor = null, Dirty = true });
            SetActiveTab(tabs.Count - 1);
            RecordOperation("新增关卡：8 × 8");
        }

        private void OpenTab(SokobanLevelDescriptor descriptor)
        {
            var existing = tabs.FindIndex(t => t.Descriptor != null && t.Descriptor.LevelId == descriptor.LevelId);
            if (existing >= 0) { SelectTab(existing); return; }
            var data = SokobanLevelRepository.LoadJson(descriptor);
            if (data == null) { SetDetail("读取失败：" + descriptor.LevelId); return; }
            tabs.Add(new SokobanEditorTab { Data = data, Descriptor = descriptor, Dirty = false });
            SetActiveTab(tabs.Count - 1);
            RecordOperation("打开关卡：来源 " + descriptor.Source);
        }

        private void CloseTab(int index)
        {
            if (stampOverlay != null || closeTabOverlay != null || managerOverlay != null || verificationOverlay != null || index < 0 || index >= tabs.Count) return;
            var tab = tabs[index];
            // 先提交仍有焦点的字段；关闭目标用对象引用，避免页签索引变化后误关其他关卡。
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            if (tab == Active) CommitNameEdit(nameField.text);
            if (!tab.Dirty && tab.IsSaved) { RemoveTab(tab); return; }

            pendingCloseTab = tab;
            RecordOperation("请求关闭页签：有未保存修改", tab);
            panning = false; boxSelecting = false; moveArmed = false;
            ClearMarquee();
            closeTabOverlay = SokobanUI.Panel(Root, "CloseTabOverlay", new Color(0f, 0f, 0f, 0.72f),
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var panel = SokobanUI.Panel(closeTabOverlay, "CloseTabDialog", SokobanTheme.Panel,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.sizeDelta = new Vector2(780f, 320f);
            LabelAt(panel, "CloseTabTitle", tab.IsStamp ? "印章尚未保存" : "关卡尚未保存", 32, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(716f, 50f), TextAnchor.MiddleLeft);
            var levelName = LabelAt(panel, "CloseTabName", "“" + tab.DisplayName.Replace('\n', ' ').Replace('\r', ' ') + "”", 24,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(716f, 40f));
            SokobanUI.Ellipsize(levelName, levelName.text, "", 716f);
            closeTabMessage = LabelAt(panel, "CloseTabMessage", "是否保存后关闭？选择丢弃将失去未保存的修改。", 22,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -134f), new Vector2(716f, 84f),
                TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            var save = ButtonAt(panel, "CloseTabSave", "保存并关闭", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-242f, 26f), new Vector2(220f, 54f), SaveAndCloseTab, SokobanTheme.Accent);
            var discard = ButtonAt(panel, "CloseTabDiscard", "丢弃", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 26f), new Vector2(220f, 54f), DiscardAndCloseTab);
            var cancel = ButtonAt(panel, "CloseTabCancel", "取消", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(242f, 26f), new Vector2(220f, 54f), CancelTabClose);
            var choices = new[] { save, discard, cancel };
            for (var i = 0; i < choices.Length; i++)
            {
                choices[i].navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnLeft = choices[(i + choices.Length - 1) % choices.Length],
                    selectOnRight = choices[(i + 1) % choices.Length],
                    selectOnUp = choices[i], selectOnDown = choices[i]
                };
            }
            // 键盘默认落在取消上，避免 Enter 意外丢弃。
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
        }

        private void SaveAndCloseTab()
        {
            var tab = pendingCloseTab;
            if (tab == null || !tabs.Contains(tab)) { CancelTabClose(); return; }
            if (!SaveTab(tab, out var error))
            {
                closeTabMessage.text = "保存失败：" + error + "\n文档仍保留，请重试或取消。";
                return;
            }
            DismissTabClose();
            RemoveTab(tab, "保存后关闭关卡");
        }

        private void DiscardAndCloseTab()
        {
            var tab = pendingCloseTab;
            DismissTabClose();
            RemoveTab(tab, "丢弃未保存修改并关闭关卡");
        }

        private void CancelTabClose()
        {
            if (pendingCloseTab != null) RecordOperation("取消关闭页签", pendingCloseTab);
            DismissTabClose();
        }

        private void DismissTabClose()
        {
            if (closeTabOverlay != null)
            {
                closeTabOverlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(closeTabOverlay.gameObject);
            }
            closeTabOverlay = null; closeTabMessage = null; pendingCloseTab = null;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        private void RemoveTab(SokobanEditorTab tab, string operation = "关闭关卡（已保存）")
        {
            var index = tabs.IndexOf(tab);
            if (index < 0) return;
            var previousActive = Active;
            RecordOperation(operation, tab);
            tabs.RemoveAt(index);
            if (tabs.Count == 0) { SetActiveTab(-1); return; }
            if (previousActive != tab)
            {
                activeIndex = tabs.IndexOf(previousActive);
                RefreshTabs();
                return;
            }
            SetActiveTab(Mathf.Clamp(index, 0, tabs.Count - 1));
        }

        private void SetActiveTab(int index)
        {
            activeIndex = tabs.Count == 0 ? -1 : Mathf.Clamp(index, 0, tabs.Count - 1);
            selection.Clear();
            pasteMode = false;
            pasteOneShot = false;
            stampMode = false; transparentBrush = false;
            moveArmed = false; boxSelecting = false; panning = false;
            ClearMarquee();
            hoverCell = new SokobanGridPoint(-1, -1); ghostedCells.Clear();
            RefreshPasteButton();
            pan = Vector2.zero;
            zoom = 1f;
            var hasDocument = Data != null;
            emptyEditorState.gameObject.SetActive(!hasDocument);
            content.gameObject.SetActive(hasDocument);
            foreach (var control in documentControls) control.interactable = hasDocument;
            RefreshStampTools();
            RefreshTabs();
            SyncInputsFromData();
            RebuildGrid();
            RefreshSelectionVisual();
            RefreshDetail();
        }

        private void SelectTab(int index)
        {
            if (index < 0 || index >= tabs.Count) return;
            var previous = Active;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            SetActiveTab(index);
            if (previous != Active) RecordOperation("切换关卡页签");
        }

        private void ReturnToLevels()
        {
            RecordOperation("返回关卡选择");
            GoTo("level");
        }

        private void RefreshTabs()
        {
            SokobanUI.DestroyChildren(tabBar);
            const float tabLeftEdge = -924f;
            var x = 12f;
            for (var i = 0; i < tabs.Count; i++)
            {
                var index = i;
                var tab = tabs[i];
                var isActive = i == activeIndex;
                var button = ButtonAt(tabBar, "Tab_" + i, tab.DisplayName + (tab.Dirty ? " *" : ""), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(tabLeftEdge + x + 80f, 0f), new Vector2(160f, 36f), () => SelectTab(index), isActive ? SokobanTheme.Accent : (Color?)null);
                var tabWidth = 160f;
                var label = button.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
                if (label != null)
                {
                    label.color = isActive ? SokobanTheme.AccentText : SokobanTheme.TextPrimary; label.fontSize = 20;
                    label.resizeTextForBestFit = false; label.horizontalOverflow = HorizontalWrapMode.Overflow;
                    tabWidth = Mathf.Clamp(label.preferredWidth + 46f, 160f, 280f);
                    var buttonRect = button.GetComponent<RectTransform>(); buttonRect.sizeDelta = new Vector2(tabWidth, 36f);
                    buttonRect.anchoredPosition = new Vector2(tabLeftEdge + x + tabWidth * 0.5f, 0f);
                    label.alignment = TextAnchor.MiddleLeft;
                    label.rectTransform.offsetMin = new Vector2(10f, 0f); label.rectTransform.offsetMax = new Vector2(-36f, 0f);
                    SokobanUI.Ellipsize(label, tab.DisplayName.Replace('\n', ' ').Replace('\r', ' '), tab.Dirty ? " *" : "", tabWidth - 46f);
                }
                var close = ButtonAt(button.transform, "Close", "×", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(30f, 30f), () => CloseTab(index));
                close.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
                var closeLabel = close.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
                if (closeLabel != null)
                {
                    // 小按钮上默认 12/4 内边距会把字形挤偏：改为全出血并居中。
                    var closeRect = closeLabel.rectTransform;
                    closeRect.offsetMin = Vector2.zero;
                    closeRect.offsetMax = Vector2.zero;
                    closeLabel.color = SokobanTheme.TextSecondary;
                    closeLabel.fontSize = 22;
                    closeLabel.alignment = TextAnchor.MiddleCenter;
                }
                x += tabWidth + 10f;
            }
            ButtonAt(tabBar, "AddTab", "+", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(tabLeftEdge + x + 20f, 0f), new Vector2(40f, 36f), AddBlankTab);
        }

        // ---------- 列表 ----------

        // ---------- 分类筛选 ----------

        private void ToggleCategoryPopup()
        {
            if (categoryPopup != null)
            {
                UnityEngine.Object.Destroy(categoryPopup.gameObject);
                categoryPopup = null;
                return;
            }
            var left = Root.Find("LeftPanel");
            // 点锚点 + 布局组自适应高度：行数变化也不会溢出或错位。
            categoryPopup = new GameObject("CategoryPopup", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            categoryPopup.SetParent(left, false);
            categoryPopup.anchorMin = new Vector2(0.5f, 1f);
            categoryPopup.anchorMax = new Vector2(0.5f, 1f);
            categoryPopup.pivot = new Vector2(0.5f, 1f);
            categoryPopup.anchoredPosition = new Vector2(0f, -66f);
            categoryPopup.sizeDelta = new Vector2(240f, 0f);
            categoryPopup.GetComponent<Image>().color = SokobanTheme.Panel;
            categoryPopup.SetAsLastSibling();
            var options = new[] { "All" }.Concat(SokobanLevelRepository.Library.Read().categories).ToArray();
            categoryPopup.sizeDelta = new Vector2(240f, Mathf.Min(420f, options.Length * 46f + 24f));
            SokobanUI.ScrollList(categoryPopup, "CategoryOptions", Vector2.zero, Vector2.one,
                Vector2.zero, Vector2.zero, out var optionContent);
            for (var i = 0; i < options.Length; i++)
            {
                var option = options[i];
                var active = option == categoryFilter;
                var button = SokobanUI.Button(optionContent, "Opt_" + option, option == "All" ? "全部" : option, new Vector2(0f, 38f), () => ApplyCategoryFilter(option), active ? SokobanTheme.Accent : (Color?)null, active ? SokobanTheme.AccentText : (Color?)null);
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 38f;
                var label = button.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
                if (label != null) { label.fontSize = 20; SokobanUI.Ellipsize(label, option == "All" ? "全部" : option, "", 192f); }
            }
        }

        private void ApplyCategoryFilter(string filter)
        {
            categoryFilter = filter;
            if (categoryPopup != null)
            {
                UnityEngine.Object.Destroy(categoryPopup.gameObject);
                categoryPopup = null;
            }
            if (categoryLabel != null) SokobanUI.Ellipsize(categoryLabel, "分类：" + (filter == "All" ? "全部" : filter), "", 196f);
            RefreshList();
            RecordOperation("筛选关卡：" + (filter == "All" ? "全部" : filter));
        }

        private void RefreshList()
        {
            SokobanUI.DestroyChildren(listContent);
            foreach (var descriptor in SokobanLevelRepository.ListAll())
            {
                if (categoryFilter != "All" && descriptor.Category != categoryFilter) continue;
                var current = descriptor;
                var button = SokobanUI.Button(listContent, "Level_" + descriptor.LevelId, descriptor.DisplayTitle + "\n" + descriptor.Category, new Vector2(0f, 64f), () => OpenTab(current));
                var layout = button.gameObject.AddComponent<LayoutElement>();
                layout.preferredHeight = 64f;
                layout.minHeight = 64f;
            }
        }

        // ---------- 网格 ----------

        private void RebuildGrid()
        {
            for (var i = cellImages.Count - 1; i >= 0; i--)
            {
                if (cellImages[i] != null) UnityEngine.Object.Destroy(cellImages[i].gameObject);
            }
            cellImages.Clear();
            cellGoals.Clear();
            cellObjects.Clear();
            if (Data == null || Data.size == null) return;
            if (Data.size.width < (IsStampDocument ? 1 : 3) || Data.size.height < (IsStampDocument ? 1 : 3) || Data.size.width > 40 || Data.size.height > 40)
            { content.sizeDelta = Vector2.zero; return; }

            var w = Mathf.Max(1, Data.size.width);
            var h = Mathf.Max(1, Data.size.height);
            cellSize = Mathf.Clamp(Mathf.Min(860f / w, 620f / h), 22f, 64f);
            var step = cellSize + CellSpacing;
            content.sizeDelta = new Vector2(w * step - CellSpacing, h * step - CellSpacing);
            content.localPosition = pan;
            content.localScale = Vector3.one * zoom;

            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    var cellObject = new GameObject($"Cell_{x}_{y}", typeof(RectTransform), typeof(Image));
                    var rect = cellObject.GetComponent<RectTransform>();
                    rect.SetParent(content, false);
                    rect.anchorMin = new Vector2(0f, 1f);
                    rect.anchorMax = new Vector2(0f, 1f);
                    rect.pivot = new Vector2(0f, 1f);
                    rect.anchoredPosition = new Vector2(x * step, -y * step);
                    rect.sizeDelta = new Vector2(cellSize, cellSize);
                    var image = cellObject.GetComponent<Image>();
                    cellImages.Add(image);
                    cellGoals.Add(SokobanUI.GoalMarker(cellObject.transform));
                    var obj = new GameObject("Obj", typeof(RectTransform), typeof(Image));
                    var objRect = obj.GetComponent<RectTransform>();
                    objRect.SetParent(cellObject.transform, false);
                    objRect.anchorMin = new Vector2(0.5f, 0.5f);
                    objRect.anchorMax = new Vector2(0.5f, 0.5f);
                    objRect.pivot = new Vector2(0.5f, 0.5f);
                    objRect.anchoredPosition = Vector2.zero;
                    objRect.sizeDelta = Vector2.one * (cellSize * 0.62f);
                    var objImage = obj.GetComponent<Image>();
                    objImage.raycastTarget = false;
                    obj.SetActive(false);
                    cellObjects.Add(objImage);
                    RefreshCellVisual(x, y);
                }
            }
            // 选区覆盖层必须画在格子之上：格子是后创建的兄弟，否则虚线会被格子图像盖住。
            selectionRoot.SetAsLastSibling();
        }

        private int Width => Data?.size?.width ?? 0;
        private int Height => Data?.size?.height ?? 0;

        private void RefreshCellVisual(int x, int y)
        {
            if (Data == null || x < 0 || y < 0 || x >= Width || y >= Height) return;
            var index = y * Width + x;
            if (index >= cellImages.Count) return;
            var cell = GetCell(x, y);
            Color color;
            if (cell.Transparent) color = (x + y) % 2 == 0 ? SokobanTheme.BoardBackground : SokobanTheme.Field;
            else if (cell.Wall) color = SokobanTheme.BoardWall;
            else color = cell.Goal ? SokobanTheme.Goal : SokobanTheme.BoardFloor;
            cellImages[index].color = color;
            cellGoals[index].gameObject.SetActive(!cell.Wall && cell.Goal);
            cellGoals[index].color = Color.white;
            var obj = cellObjects[index];
            var hasObj = cell.Box || cell.Player;
            obj.gameObject.SetActive(hasObj);
            if (hasObj) obj.color = cell.Player ? SokobanTheme.Player : SokobanTheme.Box;
        }

        private SokobanEditorCell GetCell(int x, int y)
        {
            var cell = new SokobanEditorCell();
            if (Data == null) return cell;
            if (IsStampDocument && !Active.StampMask.Contains(new SokobanGridPoint(x, y))) return new SokobanEditorCell { Transparent = true };
            cell.Wall = IsWall(x, y);
            cell.Goal = Data.goals != null && Data.goals.Any(g => g != null && g.x == x && g.y == y);
            cell.Box = Data.boxes != null && Data.boxes.Any(b => b != null && b.x == x && b.y == y);
            cell.Player = Data.player != null && Data.player.x == x && Data.player.y == y;
            return cell;
        }

        private bool IsWall(int x, int y)
        {
            return Data?.terrain != null && y >= 0 && y < Data.terrain.Length && Data.terrain[y] != null && x >= 0 && x < Data.terrain[y].Length && Data.terrain[y][x] == '#';
        }

        private void SetCell(int x, int y, SokobanEditorCell cell)
        {
            if (Data == null || x < 0 || y < 0 || x >= Width || y >= Height) return;
            if (cell.Transparent && !IsStampDocument) return;
            var point = new SokobanGridPoint(x, y);
            if (IsStampDocument) { if (cell.Transparent) Active.StampMask.Remove(point); else Active.StampMask.Add(point); }
            var rows = Data.terrain ?? SokobanLevelRepository.BuildEmptyTerrain(Width, Height);
            if (y >= rows.Length || rows[y] == null || rows[y].Length < Width) return;
            var chars = rows[y].ToCharArray();
            chars[x] = cell.Wall ? '#' : '.';
            rows[y] = new string(chars);
            Data.terrain = rows;

            Data.goals = (Data.goals ?? Array.Empty<SokobanJsonPoint>()).Where(g => !(g.x == x && g.y == y)).ToArray();
            if (cell.Goal) Data.goals = Data.goals.Concat(new[] { new SokobanJsonPoint(x, y) }).ToArray();
            Data.boxes = (Data.boxes ?? Array.Empty<SokobanJsonPoint>()).Where(b => !(b.x == x && b.y == y)).ToArray();
            if (cell.Box) Data.boxes = Data.boxes.Concat(new[] { new SokobanJsonPoint(x, y) }).ToArray();
            var oldPlayer = Data.player;
            if (!cell.Player && oldPlayer != null && oldPlayer.x == x && oldPlayer.y == y) Data.player = new SokobanJsonPoint(-1, -1);
            if (cell.Player) Data.player = new SokobanJsonPoint(x, y);
            if (cell.Player && oldPlayer != null && (oldPlayer.x != x || oldPlayer.y != y)) RefreshCellVisual(oldPlayer.x, oldPlayer.y);
        }

        /// <summary>单层内容合并落位：保留目标格下层（如目标点），物体互斥替换（玩家/箱子不同格）。</summary>
        private void ApplyMerged(int x, int y, SokobanEditorCell content)
        {
            if (Data == null || x < 0 || y < 0 || x >= Width || y >= Height) return;
            var dst = GetCell(x, y);
            var hasObject = content.Player || content.Box || content.Goal;
            var merged = new SokobanEditorCell
            {
                Wall = hasObject ? false : dst.Wall,
                Goal = dst.Goal || content.Goal,
                Box = content.Box || (!content.Player && dst.Box),
                Player = content.Player || (!content.Box && dst.Player)
            };
            SetCell(x, y, merged);
        }

        // ---------- 笔刷 / 变更 ----------

        private void SetBrush(SokobanBrush? value)
        {
            var changed = brush != value;
            if (value.HasValue) { transparentBrush = false; stampMode = false; }
            brush = value;
            if (value.HasValue) pasteMode = false;
            foreach (var pair in brushButtons)
            {
                var selected = value.HasValue && pair.Key == value.Value;
                pair.Value.GetComponent<Image>().color = selected ? SokobanTheme.Accent : SokobanTheme.Surface;
                var label = pair.Value.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
                if (label != null) label.color = selected ? SokobanTheme.AccentText : SokobanTheme.TextPrimary;
            }
            RefreshPasteButton();
            RefreshStampTools();
            RefreshGhost();
            if (changed) RecordOperation(value.HasValue ? "选择笔刷：" + BrushName(value.Value) : "取消笔刷");
        }

        private static SokobanEditorCell BrushToCell(SokobanBrush value, SokobanEditorCell existing)
        {
            switch (value)
            {
                case SokobanBrush.Wall: return new SokobanEditorCell { Wall = true };
                case SokobanBrush.Goal: return new SokobanEditorCell { Wall = false, Goal = true, Box = existing.Box, Player = existing.Player };
                case SokobanBrush.Box: return new SokobanEditorCell { Wall = false, Goal = existing.Goal, Box = true };
                case SokobanBrush.Player: return new SokobanEditorCell { Wall = false, Goal = existing.Goal, Player = true };
                default: return new SokobanEditorCell { Wall = false };
            }
        }

        private void PushUndo()
        {
            if (Active == null || Data == null) return;
            Active.AddUndo(Active.CaptureUndo(), UndoLimit);
        }

        private void Undo()
        {
            if (Active == null || Active.UndoStack.Count == 0) { SetDetail("没有可撤销的操作"); return; }
            var json = Active.UndoStack.Pop();
            if (!Active.RestoreUndo(json)) return;
            selection.Clear();
            MarkDirty();
            SyncInputsFromData();
            RebuildGrid();
            RefreshSelectionVisual();
            RefreshDetail();
            SetDetail("已撤销");
        }

        private void MarkDirty()
        {
            if (Data != null)
            {
                SokobanVerificationMetadata.Invalidate(Data);
            }
            if (Active != null) Active.Dirty = true;
            RefreshTabs();
        }

        private void ApplyBrushAt(int x, int y)
        {
            if (Data == null || !brush.HasValue && !transparentBrush) return;
            PushUndo();
            SetCell(x, y, transparentBrush ? new SokobanEditorCell { Transparent = true } : BrushToCell(brush.Value, GetCell(x, y)));
            MarkDirty();
            RefreshCellVisual(x, y);
            RefreshDetail();
            RecordOperation($"绘制{(transparentBrush ? "透明" : BrushName(brush.Value))}：坐标 ({x}, {y})");
        }

        private void FillSelection()
        {
            if (Data == null || selection.Count == 0) { SetDetail("请先框选/多选要填充的区域"); return; }
            if (!brush.HasValue && !transparentBrush) { SetDetail("请先选择笔刷"); return; }
            PushUndo();
            var count = selection.Count;
            foreach (var point in selection.ToList())
            {
                SetCell(point.x, point.y, transparentBrush ? new SokobanEditorCell { Transparent = true } : BrushToCell(brush.Value, GetCell(point.x, point.y)));
                RefreshCellVisual(point.x, point.y);
            }
            selection.Clear();
            RefreshSelectionVisual();
            MarkDirty();
            RefreshDetail();
            SetDetail($"已在 {count} 个选中格填充 {(transparentBrush ? "透明" : BrushName(brush.Value))}，选中已清空");
        }

        // ---------- 复制 / 粘贴 ----------

        private void CopySelection()
        {
            if (Data == null || selection.Count == 0) { SetDetail("请先选择要复制的地块"); return; }
            var minX = selection.Min(p => p.x);
            var minY = selection.Min(p => p.y);
            clipboard = selection.OrderBy(p => p.y).ThenBy(p => p.x)
                .Select(p => new SokobanClipboardCell { Dx = p.x - minX, Dy = p.y - minY, Cell = GetCell(p.x, p.y), WholeCell = true })
                .ToList();
            var copied = clipboard.Count;
            selection.Clear();
            RefreshSelectionVisual();
            RefreshDetail();
            SetDetail($"已复制 {copied} 个地块（支持不相邻多选），选中已清空");
        }

        /// <summary>粘贴为切换模式：按钮/快捷键点亮进入（可连续粘贴），再点一次熄灭退出；与笔刷互斥。</summary>
        private void TogglePasteMode()
        {
            stampMode = false; transparentBrush = false; RefreshStampTools();
            if (pasteMode)
            {
                ExitPasteMode("已退出粘贴模式");
                return;
            }
            if (clipboard.Count == 0) { SetDetail("剪贴板为空，请先复制或剪切"); return; }
            EnterPasteMode(false);
        }

        private void EnterPasteMode(bool oneShot)
        {
            stampMode = false; transparentBrush = false; RefreshStampTools();
            pasteMode = true;
            pasteOneShot = oneShot;
            SetBrush(null);
            RefreshPasteButton();
            RefreshGhost();
            SetDetail(oneShot
                ? "粘贴模式（一次性）：虚影跟随鼠标，左键落位后自动退出"
                : "粘贴模式：虚影跟随鼠标，左键可连续落位，右键或再点粘贴退出");
        }

        private void ExitPasteMode(string message)
        {
            pasteMode = false;
            pasteOneShot = false;
            RefreshPasteButton();
            RefreshGhost();
            SetDetail(message);
        }

        /// <summary>剪切：把集合内地块存入剪贴板（相对偏移），并把这些地块全部变成地面。</summary>
        private void CutCells(IEnumerable<SokobanGridPoint> cells)
        {
            if (Data == null) return;
            var list = cells.ToList();
            if (list.Count == 0) return;
            PushUndo();
            var minX = list.Min(p => p.x);
            var minY = list.Min(p => p.y);
            clipboard = list.OrderBy(p => p.y).ThenBy(p => p.x)
                .Select(p => new SokobanClipboardCell { Dx = p.x - minX, Dy = p.y - minY, Cell = GetCell(p.x, p.y), WholeCell = true })
                .ToList();
            foreach (var point in list)
            {
                ClearToFloor(point.x, point.y);
                RefreshCellVisual(point.x, point.y);
            }
            selection.Clear();
            RefreshSelectionVisual();
            MarkDirty();
            RefreshDetail();
        }

        private void ClearToFloor(int x, int y)
        {
            SetCell(x, y, new SokobanEditorCell { Transparent = IsStampDocument });
        }

        private void CutSelection()
        {
            if (selection.Count == 0) { SetDetail("请先选择要剪切的地块"); return; }
            CutCells(selection);
            EnterPasteMode(true);
            SetDetail($"已剪切 {clipboard.Count} 个地块（原位置变为地面），左键落位一次后自动退出粘贴");
        }

        /// <summary>只取"上面那层"（玩家 > 箱子 > 目标）剪切，下层保留在原格。</summary>
        private bool CutTopLayerAt(int x, int y)
        {
            if (Data == null) return false;
            var cell = GetCell(x, y);
            SokobanEditorCell taken;
            if (cell.Player) taken = new SokobanEditorCell { Player = true };
            else if (cell.Box) taken = new SokobanEditorCell { Box = true };
            else if (cell.Goal) taken = new SokobanEditorCell { Goal = true };
            else return false;

            PushUndo();
            clipboard = new List<SokobanClipboardCell> { new SokobanClipboardCell { Dx = 0, Dy = 0, Cell = taken } };
            if (taken.Player) Data.player = new SokobanJsonPoint(-1, -1);
            if (taken.Box) Data.boxes = (Data.boxes ?? Array.Empty<SokobanJsonPoint>()).Where(b => !(b.x == x && b.y == y)).ToArray();
            if (taken.Goal) Data.goals = (Data.goals ?? Array.Empty<SokobanJsonPoint>()).Where(g => !(g.x == x && g.y == y)).ToArray();
            RefreshCellVisual(x, y);
            MarkDirty();
            RefreshDetail();
            return true;
        }

        /// <summary>左键长按：按在已选区域内→整格剪切选区（两层一起）；按在物块上→只剪切最上层；随后进入一次性粘贴形成移动。</summary>
        private void TryLongPressMove(SokobanGridPoint cell)
        {
            if (Data == null) return;
            if (selection.Count > 0 && selection.Contains(cell))
            {
                CutCells(selection);
            }
            else if (!CutTopLayerAt(cell.x, cell.y))
            {
                return;
            }
            EnterPasteMode(true);
            SetDetail("长按触发移动：已剪切，虚影跟随鼠标，左键落位一次后自动退出");
        }

        private void RefreshPasteButton()
        {
            if (pasteButton == null) return;
            pasteButton.GetComponent<Image>().color = pasteMode ? SokobanTheme.Accent : SokobanTheme.Surface;
            var label = pasteButton.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
            if (label != null) label.color = pasteMode ? SokobanTheme.AccentText : SokobanTheme.TextPrimary;
        }

        /// <summary>右键取消当前状态：粘贴模式、笔刷选择、进行中的框选。</summary>
        private void CancelCurrentState()
        {
            var changed = pasteMode || stampMode || transparentBrush || brush.HasValue || boxSelecting;
            pasteMode = false;
            stampMode = false; transparentBrush = false; RefreshStampTools();
            pasteOneShot = false;
            boxSelecting = false;
            moveArmed = false;
            ClearMarquee();
            SetBrush(null);
            RefreshPasteButton();
            RefreshGhost();
            if (changed) SetDetail("已取消当前状态（粘贴/笔刷/框选）");
        }

        private void CommitPaste(int anchorX, int anchorY)
        {
            if (Data == null || clipboard.Count == 0) return;
            // Reject the whole placement before consuming a cut or changing the document.
            if (clipboard.Any(item => anchorX + item.Dx < 0 || anchorY + item.Dy < 0 ||
                anchorX + item.Dx >= Width || anchorY + item.Dy >= Height))
            {
                SetDetail("粘贴区域超出地图边界，未写入任何格子；请移动到图内后重新落位。");
                return;
            }
            RecordOperation($"粘贴 {clipboard.Count} 个地块：起点 ({anchorX}, {anchorY})");
            PushUndo();
            foreach (var item in clipboard)
            {
                var x = anchorX + item.Dx;
                var y = anchorY + item.Dy;
                if (item.WholeCell) SetCell(x, y, item.Cell);
                else ApplyMerged(x, y, item.Cell);
                RefreshCellVisual(x, y);
            }
            MarkDirty();
            RefreshDetail();
            if (pasteOneShot)
            {
                clipboard.Clear();
                ExitPasteMode("已粘贴一次，剪切内容落位，粘贴模式已退出");
            }
            else
            {
                SetDetail("已粘贴，可继续左键落位（右键或再点粘贴退出）");
            }
        }

        // ---------- 输入 / 视口 ----------

        private bool ScreenToContentLocal(out Vector2 local)
        {
            local = Vector2.zero;
            if (viewport == null) return false;
            if (!RectTransformUtility.RectangleContainsScreenPoint(viewport, Input.mousePosition, null)) return false;
            Vector2 vp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, Input.mousePosition, null, out vp)) return false;
            local = (vp - pan) / zoom;
            return true;
        }

        private bool LocalToCell(Vector2 c, out SokobanGridPoint point)
        {
            point = new SokobanGridPoint(-1, -1);
            if (Data == null) return false;
            var step = cellSize + CellSpacing;
            var sizeX = Width * step - CellSpacing;
            var sizeY = Height * step - CellSpacing;
            var lx = c.x + sizeX / 2f;
            var ly = sizeY / 2f - c.y;
            if (lx < 0 || ly < 0) return false;
            var x = Mathf.FloorToInt(lx / step);
            var y = Mathf.FloorToInt(ly / step);
            if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
            point = new SokobanGridPoint(x, y);
            return true;
        }

        private void Update()
        {
            ObserveBatchVerification();
            UpdateBatchResultsRequest();
            if (SokobanEditorSession.QuitPending || SokobanGmController.BlocksInput) return;
            if (UpdateExchange()) return;
            if (UpdateStampLibrary()) return;
            if (UpdateOperationLog()) return;
            if (UpdateBatchVerification()) return;
            if (UpdateLevelManager()) return;
            if (closeTabOverlay != null)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) CancelTabClose();
                return;
            }
            if (UpdateSolverWorkflow()) return;
            if (UpdateGenerationWorkflow()) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (pasteMode || stampMode || transparentBrush) { CancelCurrentState(); }
                else if (selection.Count > 0) { selection.Clear(); RefreshSelectionVisual(); RecordOperation("清空选区"); }
                else ReturnToLevels();
                return;
            }
            if (Data == null) return;
            var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (control && Input.GetKeyDown(KeyCode.C)) { CopySelection(); return; }
            if (control && Input.GetKeyDown(KeyCode.X)) { CutSelection(); return; }
            if (control && Input.GetKeyDown(KeyCode.V)) { TogglePasteMode(); return; }
            if (control && Input.GetKeyDown(KeyCode.Z)) { Undo(); return; }

            if (control && Input.mouseScrollDelta.y != 0f)
            {
                zoom = Mathf.Clamp(zoom + Input.mouseScrollDelta.y * 0.15f, 0.5f, 2.5f);
                content.localScale = Vector3.one * zoom;
                RecordOperation($"缩放编辑区：{zoom:0.00}x");
                return;
            }

            if (Input.GetMouseButtonDown(2)) { panning = true; panLastMouse = Input.mousePosition; }
            if (panning)
            {
                if (Input.GetMouseButton(2))
                {
                    pan += (Vector2)Input.mousePosition - panLastMouse;
                    panLastMouse = Input.mousePosition;
                    content.localPosition = pan;
                }
                if (Input.GetMouseButtonUp(2)) { panning = false; RecordOperation($"平移编辑区：({pan.x:0}, {pan.y:0})"); }
                return;
            }

            // 右键：取消当前状态（粘贴模式 / 笔刷 / 进行中的框选）。
            if (Input.GetMouseButtonDown(1))
            {
                CancelCurrentState();
                return;
            }

            Vector2 localPoint;
            var hasLocal = ScreenToContentLocal(out localPoint);
            var cell = new SokobanGridPoint(-1, -1);
            var hasCell = hasLocal && LocalToCell(localPoint, out cell);

            if (hasCell && !cell.Equals(hoverCell))
            {
                hoverCell = cell;
                RefreshGhost();
            }
            else if (!hasCell && hoverCell.x >= 0)
            {
                hoverCell = new SokobanGridPoint(-1, -1);
                RefreshGhost();
            }

            if (Input.GetMouseButtonDown(0) && hasLocal)
            {
                if (stampMode && hasCell) { CommitStamp(cell.x, cell.y); return; }
                if (pasteMode && hasCell) { CommitPaste(cell.x, cell.y); return; }
                if (control && hasCell)
                {
                    if (!selection.Remove(cell)) selection.Add(cell);
                    RefreshSelectionVisual();
                    RefreshDetail();
                    RecordOperation($"切换选中格 ({cell.x}, {cell.y})：共 {selection.Count} 格");
                    return;
                }
                boxSelecting = true;
                boxSelectMoved = false;
                moveArmed = false;
                pressTime = Time.unscaledTime;
                pressCell = hasCell ? cell : new SokobanGridPoint(-1, -1);
                boxAnchorLocal = localPoint;
                boxCurrentLocal = localPoint;
                UpdateMarqueeLocal();
            }

            if (boxSelecting && Input.GetMouseButton(0))
            {
                if (hasLocal) boxCurrentLocal = localPoint;
                if ((boxCurrentLocal - boxAnchorLocal).magnitude > 4f) boxSelectMoved = true;
                // 未移动且按住超过阈值：长按 → 自动剪切+粘贴（移动）。
                if (!boxSelectMoved && hasCell && cell.Equals(pressCell) && Time.unscaledTime - pressTime >= LongPressSeconds)
                {
                    boxSelecting = false;
                    ClearMarquee();
                    moveArmed = true;
                    TryLongPressMove(pressCell);
                }
                else if (boxSelectMoved)
                {
                    UpdateMarqueeLocal();
                }
            }

            if (Input.GetMouseButtonUp(0) && (boxSelecting || moveArmed))
            {
                var wasMove = moveArmed;
                boxSelecting = false;
                moveArmed = false;
                ClearMarquee();
                if (wasMove) return; // 长按移动已接管，松开不再落笔
                if (boxSelectMoved)
                {
                    if (!control) selection.Clear();
                    AddCellsInRect(boxAnchorLocal, boxCurrentLocal);
                    RefreshSelectionVisual();
                    RefreshDetail();
                    RecordOperation($"框选区域：共 {selection.Count} 格");
                }
                else if (hasCell)
                {
                    if (selection.Count > 0) RecordOperation("清空选区");
                    selection.Clear();
                    RefreshSelectionVisual();
                    ApplyBrushAt(cell.x, cell.y);
                }
                else
                {
                    if (selection.Count > 0) RecordOperation("清空选区");
                    selection.Clear();
                    RefreshSelectionVisual();
                }
            }
        }

        /// <summary>用内容层连续坐标画选区框：可在整个编辑区内拖出，不限于格子范围。</summary>
        private void UpdateMarqueeLocal()
        {
            if (Data == null) return;
            var step = cellSize + CellSpacing;
            var sizeX = Width * step - CellSpacing;
            var sizeY = Height * step - CellSpacing;
            var halfX = sizeX / 2f;
            var halfY = sizeY / 2f;
            var minX = Mathf.Clamp(Mathf.Min(boxAnchorLocal.x, boxCurrentLocal.x), -halfX, halfX);
            var maxX = Mathf.Clamp(Mathf.Max(boxAnchorLocal.x, boxCurrentLocal.x), -halfX, halfX);
            var minY = Mathf.Clamp(Mathf.Min(boxAnchorLocal.y, boxCurrentLocal.y), -halfY, halfY);
            var maxY = Mathf.Clamp(Mathf.Max(boxAnchorLocal.y, boxCurrentLocal.y), -halfY, halfY);
            ClearMarquee();
            EnsureDashSprites();
            // 挂到选区覆盖层下：该层在 RebuildGrid 后被置顶，保证选区框画在所有格子之上。
            marqueeBox = BuildDashedRect(selectionRoot, "Marquee", minX, maxY, maxX - minX, maxY - minY, true);
        }

        /// <summary>把与选区框矩形相交的格子加入选中。</summary>
        private void AddCellsInRect(Vector2 a, Vector2 b)
        {
            if (Data == null) return;
            var step = cellSize + CellSpacing;
            var sizeX = Width * step - CellSpacing;
            var sizeY = Height * step - CellSpacing;
            var halfX = sizeX / 2f;
            var halfY = sizeY / 2f;
            var minX = Mathf.Min(a.x, b.x);
            var maxX = Mathf.Max(a.x, b.x);
            var minY = Mathf.Min(a.y, b.y);
            var maxY = Mathf.Max(a.y, b.y);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                {
                    var left = -halfX + x * step;
                    var top = halfY - y * step;
                    var right = left + cellSize;
                    var bottom = top - cellSize;
                    if (right < minX || left > maxX || bottom > maxY || top < minY) continue;
                    selection.Add(new SokobanGridPoint(x, y));
                }
            }
        }

        private void ClearMarquee()
        {
            if (marqueeBox != null)
            {
                UnityEngine.Object.Destroy(marqueeBox.gameObject);
                marqueeBox = null;
            }
        }

        // ---------- 虚影 / 选中框 ----------

        private void ClearGhost()
        {
            foreach (var point in ghostedCells) RefreshCellVisual(point.x, point.y);
            ghostedCells.Clear();
        }

        private void RefreshGhost()
        {
            ClearGhost();
            if (Data == null || hoverCell.x < 0) return;
            var targets = new List<KeyValuePair<SokobanGridPoint, SokobanEditorCell>>();
            var stampOutside = stampMode && !activeStamp.Fits(Width, Height, hoverCell.x, hoverCell.y);
            if (pasteMode || stampMode)
            {
                foreach (var item in stampMode ? stampCells : clipboard)
                {
                    var p = new SokobanGridPoint(hoverCell.x + item.Dx, hoverCell.y + item.Dy);
                    if (p.x >= 0 && p.y >= 0 && p.x < Width && p.y < Height) targets.Add(new KeyValuePair<SokobanGridPoint, SokobanEditorCell>(p, item.Cell));
                }
            }
            else if (brush.HasValue)
            {
                targets.Add(new KeyValuePair<SokobanGridPoint, SokobanEditorCell>(hoverCell, BrushToCell(brush.Value, GetCell(hoverCell.x, hoverCell.y))));
            }
            else if (transparentBrush) targets.Add(new KeyValuePair<SokobanGridPoint, SokobanEditorCell>(hoverCell, new SokobanEditorCell { Transparent = true }));
            foreach (var pair in targets)
            {
                var index = pair.Key.y * Width + pair.Key.x;
                if (index < 0 || index >= cellImages.Count) continue;
                var content = pair.Value;
                if (content.Transparent)
                {
                    if (!IsStampDocument) continue;
                    cellImages[index].color = Color.Lerp(cellImages[index].color, SokobanTheme.BoardBackground, 0.65f);
                }
                else if (content.Box || content.Player)
                {
                    // 物体虚影：半透明内嵌色块，目标符号仍从底层露出。
                    var obj = cellObjects[index];
                    obj.gameObject.SetActive(true);
                    var oc = content.Player ? SokobanTheme.Player : SokobanTheme.Box;
                    oc.a = 0.5f;
                    obj.color = oc;
                }
                else if (content.Wall)
                {
                    cellImages[index].color = Color.Lerp(cellImages[index].color, SokobanTheme.BoardWall, 0.6f);
                }
                else if (content.Goal && !GetCell(pair.Key.x, pair.Key.y).Goal)
                {
                    cellGoals[index].gameObject.SetActive(true);
                    cellGoals[index].color = new Color(1f, 1f, 1f, 0.55f);
                }
                else
                {
                    cellImages[index].color = Color.Lerp(cellImages[index].color, SokobanTheme.BoardFloor, 0.5f);
                }
                if (stampOutside) cellImages[index].color = Color.Lerp(cellImages[index].color, new Color(0.8f, 0.2f, 0.2f), 0.55f);
                ghostedCells.Add(pair.Key);
            }
        }

        private void RefreshSelectionVisual()
        {
            SokobanUI.DestroyChildren(selectionRoot);
            if (Data == null) return;
            EnsureDashSprites();
            var step = cellSize + CellSpacing;
            var sizeX = Width * step - CellSpacing;
            var sizeY = Height * step - CellSpacing;
            foreach (var point in selection)
            {
                if (point.x < 0 || point.y < 0 || point.x >= Width || point.y >= Height) continue;
                var left = -sizeX / 2f + point.x * step;
                var top = sizeY / 2f - point.y * step;
                BuildDashedRect(selectionRoot, "Sel_" + point.x + "_" + point.y, left, top, cellSize, cellSize, false);
            }
        }

        /// <summary>在 parent 下建一个左上角位于 (left,top)、尺寸 w×h 的矩形：四边为蓝色虚线，可选半透明蓝填充。</summary>
        private static RectTransform BuildDashedRect(RectTransform parent, string name, float left, float top, float w, float h, bool fill)
        {
            var container = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            container.SetParent(parent, false);
            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.pivot = new Vector2(0f, 1f);
            container.anchoredPosition = new Vector2(left, top);
            container.sizeDelta = new Vector2(w, h);
            if (fill)
            {
                var fillImage = container.gameObject.AddComponent<Image>();
                fillImage.color = new Color(0.31f, 0.765f, 0.851f, 0.16f);
                fillImage.raycastTarget = false;
            }
            AddDashEdge(container, true, true);
            AddDashEdge(container, true, false);
            AddDashEdge(container, false, true);
            AddDashEdge(container, false, false);
            return container;
        }

        private static void AddDashEdge(RectTransform parent, bool horizontal, bool first)
        {
            const float thickness = 2f;
            var go = new GameObject(horizontal ? (first ? "DashTop" : "DashBottom") : (first ? "DashLeft" : "DashRight"), typeof(RectTransform), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.sprite = horizontal ? dashHorizontal : dashVertical;
            image.type = Image.Type.Tiled;
            image.color = new Color(0.31f, 0.765f, 0.851f, 0.95f);
            image.raycastTarget = false;
            if (horizontal)
            {
                rect.anchorMin = new Vector2(0f, first ? 1f : 0f);
                rect.anchorMax = new Vector2(1f, first ? 1f : 0f);
                rect.pivot = new Vector2(0.5f, first ? 1f : 0f);
                rect.sizeDelta = new Vector2(0f, thickness);
            }
            else
            {
                rect.anchorMin = new Vector2(first ? 0f : 1f, 0f);
                rect.anchorMax = new Vector2(first ? 0f : 1f, 1f);
                rect.pivot = new Vector2(first ? 0f : 1f, 0.5f);
                rect.sizeDelta = new Vector2(thickness, 0f);
            }
            rect.anchoredPosition = Vector2.zero;
        }

        private static void EnsureDashSprites()
        {
            if (dashHorizontal != null && dashVertical != null) return;
            dashHorizontal = MakeDashSprite(8, 2, true);
            dashVertical = MakeDashSprite(2, 8, false);
        }

        private static Sprite MakeDashSprite(int w, int h, bool horizontal)
        {
            var texture = new Texture2D(w, h);
            var pixels = new Color32[w * h];
            for (var i = 0; i < pixels.Length; i++)
            {
                var x = i % w;
                var y = i / w;
                var on = horizontal ? (x < 4) : (y < 4);
                pixels[i] = on ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0f, 1f), 1f);
        }

        // ---------- 右栏 ----------

        private void CommitNameEdit(string value)
        {
            if (Data == null || Data.name == value) return;
            PushUndo();
            var previousName = Data.name;
            Data.name = value;
            MarkDirty();
            RecordOperation((IsStampDocument ? "重命名印章：" : "重命名关卡：") + previousName + " → " + value);
        }

        private void SyncInputsFromData()
        {
            nameField.SetTextWithoutNotify(Data?.name ?? "");
            widthField.SetTextWithoutNotify(Data?.size?.width.ToString() ?? "");
            heightField.SetTextWithoutNotify(Data?.size?.height.ToString() ?? "");
        }

        private void ApplySize(string wText, string hText)
        {
            if (Data == null) return;
            int w, h;
            if (!int.TryParse(wText, out w) || !int.TryParse(hText, out h)) return;
            if (IsStampDocument) { ResizeStamp(w, h); return; }
            w = Mathf.Clamp(w, 3, 40);
            h = Mathf.Clamp(h, 3, 40);
            if (w == Data.size.width && h == Data.size.height) return;
            PushUndo();
            var old = Data.terrain ?? Array.Empty<string>();
            var rows = new string[h];
            for (var y = 0; y < h; y++)
            {
                var chars = new char[w];
                for (var x = 0; x < w; x++)
                {
                    var src = y < old.Length && old[y] != null && x < old[y].Length ? old[y][x] : '#';
                    chars[x] = (x == 0 || y == 0 || x == w - 1 || y == h - 1 || src == '#') ? '#' : '.';
                }
                rows[y] = new string(chars);
            }
            Data.size.width = w;
            Data.size.height = h;
            Data.terrain = rows;
            Data.player = new SokobanJsonPoint(Mathf.Clamp(Data.player.x, 1, w - 2), Mathf.Clamp(Data.player.y, 1, h - 2));
            Data.boxes = Data.boxes.Where(b => b.x > 0 && b.y > 0 && b.x < w - 1 && b.y < h - 1).ToArray();
            Data.goals = Data.goals.Where(g => g.x > 0 && g.y > 0 && g.x < w - 1 && g.y < h - 1).ToArray();
            selection.Clear();
            MarkDirty();
            SyncInputsFromData();
            RebuildGrid();
            RefreshSelectionVisual();
            RefreshDetail();
            SetDetail($"尺寸已调整为 {w} × {h}");
        }

        private void RefreshDetail()
        {
            if (Data == null) { detailText.text = ""; return; }
            if (IsStampDocument) { RefreshStampDetail(); return; }
            var validation = SokobanValidation.Validate(Data);
            detailText.text = $"ID：{Data.levelId}\n分类：{Active?.Descriptor?.Category ?? Active?.Descriptor?.Source ?? "未保存"}\n来源：{Active?.Descriptor?.Source ?? "未保存"}\n尺寸：{Data.size.width} × {Data.size.height}\n箱子：{Data.boxes.Length}    目标：{Data.goals.Length}\n选中：{selection.Count}    缩放：{zoom:0.00}x\n状态：{(validation.Count == 0 ? "可试玩" : string.Join("\n", validation.Take(4)))}";
            var complexity = Data.generation?.complexity;
            detailText.text += "\n解关步数：" + Data.verifiedMoves + (Data.verifiedMoves < 0 ? "（需验证）" : "");
            if (complexity != null && complexity.valid)
                detailText.text += $"\n复杂度估计：{SokobanDifficultyEvaluator.Name(complexity.difficulty)} {complexity.score:0.0}/100";
            if (Data.verifiedMoves >= 0 && Data.solution?.status == "Solved")
                detailText.text += $"\n求解：{Data.verifiedMoves} 步 / {Data.solution.pushes} 推";
        }

        private void SetDetail(string message)
        {
            if (detailText != null) detailText.text = message;
            RecordOperation(message);
        }

        private void ValidateCurrent()
        {
            if (IsStampDocument) { SetDetail("印章是预设画笔，不需要关卡求解。保存时会检查印章结构。"); return; }
            OpenSolver();
        }

        private void CopyJson()
        {
            if (Data == null) return;
            GUIUtility.systemCopyBuffer = IsStampDocument ? JsonUtility.ToJson(SokobanStamp.FromDocument(Active), true) : JsonUtility.ToJson(Data, true);
            SetDetail(IsStampDocument ? "印章 JSON 已复制到剪贴板" : "关卡 JSON 已复制到剪贴板");
        }

        private void WriteCurrent()
        {
            SaveTab(Active, out _);
        }

        private bool SaveTab(SokobanEditorTab tab, out string error)
        {
            error = null;
            if (tab?.Data == null) { error = "没有可保存的关卡。"; return false; }
            if (tab == Active) CommitNameEdit(nameField.text);
            var data = tab.Data;
            try
            {
                if (string.IsNullOrWhiteSpace(data.levelId)) data.levelId = CreateGeneratedLevelId();
                tab.Save();
                if (tab.IsStamp) selectedStamp = SokobanStamp.FromDocument(tab);
            }
            catch (Exception exception)
            {
                error = exception.Message;
                if (detailText != null) detailText.text = "保存失败：" + error;
                RecordOperation("保存失败：" + error, tab);
                return false;
            }
            RefreshTabs();
            RefreshList();
            if (detailText != null) detailText.text = (tab.IsStamp ? "印章已保存：" : "已保存：") + data.name;
            RecordOperation((tab.IsStamp ? "印章已保存：" : "已保存：") + data.levelId, tab);
            return true;
        }

        private void ExportAsNew()
        {
            if (IsStampDocument) { SetDetail("请使用保存印章，印章不会写入关卡目录。"); return; }
            if (Data == null) return;
            CommitNameEdit(nameField.text);
            var clone = SokobanLevelRepository.Parse(JsonUtility.ToJson(Data));
            if (clone == null) return;
            clone.levelId = CreateGeneratedLevelId();
            var descriptor = new SokobanLevelDescriptor { LevelId = clone.levelId, Title = clone.name, Folder = clone.levelId, Source = "Generated" };
            try { SokobanLevelRepository.SaveJson(clone, descriptor); }
            catch (Exception exception) { SetDetail("保存为关卡失败：" + exception.Message); return; }
            tabs.Add(new SokobanEditorTab { Data = clone, Descriptor = descriptor, Dirty = false });
            SetActiveTab(tabs.Count - 1);
            RefreshList();
            SetDetail("已保存为新关卡：" + clone.levelId);
        }

        private void PlayCurrent()
        {
            if (IsStampDocument) { SetDetail("请先将印章应用到关卡，再进行试玩。"); return; }
            if (Data == null) return;
            CommitNameEdit(nameField.text);
            var errors = SokobanValidation.Validate(Data);
            if (errors.Count > 0) { SetDetail("暂时无法试玩：" + string.Join("；", errors.Take(3))); return; }
            if (!SaveTab(Active, out _)) return;
            RecordOperation("进入关卡试玩");
            SokobanRuntimeContext.SelectedLevelId = Data.levelId;
            SokobanRuntimeContext.IsEditorPreview = true;
            SokobanRuntimeContext.IsDebugPlay = false;
            GoTo("game");
        }

        private static string BrushName(SokobanBrush value)
        {
            switch (value)
            {
                case SokobanBrush.Wall: return "墙面";
                case SokobanBrush.Goal: return "目标";
                case SokobanBrush.Box: return "箱子";
                case SokobanBrush.Player: return "玩家";
                default: return "地面";
            }
        }

        private static string CreateGeneratedLevelId() => "Generated_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
    }
}
