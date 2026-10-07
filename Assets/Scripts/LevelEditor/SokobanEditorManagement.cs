using System;
using System.Collections.Generic;
using System.Linq;
using Kuluobishi.Sokoban;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private RectTransform managerOverlay, managerContent, managerCategoryContent, managerTargetPopup, managerDeleteOverlay, managerDragMarker;
        private ScrollRect managerScroll;
        private UnityEngine.UI.Text managerStatus;
        private InputField managerCategoryName;
        private Button managerTarget;
        private readonly List<Button> managerSelectionActions = new List<Button>();
        private readonly Dictionary<string, Button> managerRows = new Dictionary<string, Button>();
        private readonly SokobanLibrarySelection managerSelection = new SokobanLibrarySelection();
        private List<SokobanLevelDescriptor> managerAll = new List<SokobanLevelDescriptor>();
        private List<SokobanLevelDescriptor> managerVisible = new List<SokobanLevelDescriptor>();
        private SokobanLibraryStore managerStore;
        private string managerFilter = "All", managerTargetCategory = "Generated", managerFocusId, managerPressedId;
        private string[] managerPendingDelete;
        private float managerPressTime;
        private Vector2 managerPressPosition, managerPointerPosition;
        private bool managerDragging, managerScrollGesture, managerDeferredClick, managerSuppressClick;
        private int managerInsertion;
        private CanvasGroup managerRootGroup;
        private bool managerRootWasInteractable;

        private void OpenLevelManager()
        {
            if (stampOverlay != null || managerOverlay != null || closeTabOverlay != null || solverOverlay != null || generationOverlay != null || logOverlay != null || verificationOverlay != null) return;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            try { managerStore = SokobanLevelRepository.Library; managerAll = SokobanLevelRepository.ListAll().ToList(); managerStore.Read(); }
            catch (Exception error) { SetDetail("关卡管理读取失败：" + error.Message); return; }
            managerSelection.Clear(); managerRows.Clear(); managerSelectionActions.Clear();
            managerFilter = categoryFilter; managerTargetCategory = "Generated"; managerFocusId = null;
            boxSelecting = false; moveArmed = false; panning = false; ClearMarquee();
            managerRootGroup = Root.GetComponent<CanvasGroup>();
            if (managerRootGroup == null) managerRootGroup = Root.gameObject.AddComponent<CanvasGroup>();
            managerRootWasInteractable = managerRootGroup.interactable; managerRootGroup.interactable = false;
            managerOverlay = SokobanUI.Panel(Root, "LevelManagerOverlay", SokobanTheme.Background,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            managerOverlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            LabelAt(managerOverlay, "ManagerTitle", "关卡管理", 38, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Margin, -30f), new Vector2(600f, 64f));
            managerSelectionActions.Add(ButtonAt(managerOverlay, "ManagerExportSelection", "导出所选 XLSX", Vector2.one, Vector2.one,
                new Vector2(-Margin - 936f, -30f), new Vector2(210f, 54f), () => OpenXlsxExport(true)));
            ButtonAt(managerOverlay, "ManagerExportCategory", "导出分类 XLSX", Vector2.one, Vector2.one,
                new Vector2(-Margin - 702f, -30f), new Vector2(210f, 54f), () => OpenXlsxExport(false));
            ButtonAt(managerOverlay, "ManagerImportXlsx", "导入 XLSX", Vector2.one, Vector2.one,
                new Vector2(-Margin - 468f, -30f), new Vector2(210f, 54f), OpenXlsxImport);
            ButtonAt(managerOverlay, "ManagerRefresh", "刷新", Vector2.one, Vector2.one,
                new Vector2(-Margin - 234f, -30f), new Vector2(210f, 54f), ReloadLevelManager);
            ButtonAt(managerOverlay, "ManagerClose", "返回编辑", Vector2.one, Vector2.one,
                new Vector2(-Margin, -30f), new Vector2(210f, 54f), CloseLevelManager);
            var categories = InsetPanel(managerOverlay, "ManagerCategoryPanel", SokobanTheme.Panel, Margin, 1584f, TopInset, 28f);
            LabelAt(categories, "ManagerCategoryTitle", "分类 / 玩家公开", 24, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(264f, 42f));
            SokobanUI.ScrollList(categories, "ManagerCategories", Vector2.zero, Vector2.one,
                new Vector2(12f, 208f), new Vector2(-12f, -76f), out managerCategoryContent);
            LabelAt(categories, "ManagerNewCategoryLabel", "新增分类", 20, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 166f), new Vector2(252f, 32f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            managerCategoryName = SokobanUI.InputField(categories, "ManagerCategoryName", "", "输入分类名称", new Vector2(252f, 48f));
            var inputRect = managerCategoryName.GetComponent<RectTransform>();
            inputRect.anchorMin = inputRect.anchorMax = inputRect.pivot = new Vector2(0.5f, 0f);
            inputRect.anchoredPosition = new Vector2(0f, 106f); managerCategoryName.characterLimit = 40;
            ButtonAt(categories, "ManagerCreateCategory", "新增分类", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 36f), new Vector2(252f, 52f), CreateManagerCategory);
            var table = InsetPanel(managerOverlay, "ManagerTable", SokobanTheme.Panel, 356f, Margin, TopInset, 220f);
            LabelAt(table, "ManagerHeader", "顺序        关卡名称 / ID", 22, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -18f), new Vector2(840f, 42f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            LabelAt(table, "ManagerCategoryHeader", "分类", 22, new Vector2(0.65f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -18f), new Vector2(210f, 42f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            LabelAt(table, "ManagerSourceHeader", "来源", 22, new Vector2(0.83f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -18f), new Vector2(200f, 42f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            managerScroll = SokobanUI.ScrollList(table, "ManagerLevels", Vector2.zero, Vector2.one,
                new Vector2(12f, 12f), new Vector2(-12f, -72f), out managerContent);
            managerScroll.gameObject.AddComponent<SokobanLibraryRowPointer>().Click = e =>
            {
                if (e.button != PointerEventData.InputButton.Left) return;
                managerSelection.Clear(); RefreshManagerSelection(); RecordManagement("清空关卡选择");
            };
            managerDragMarker = SokobanUI.Panel(managerScroll.viewport, "ManagerDropMarker", SokobanTheme.Accent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            managerDragMarker.GetComponent<Image>().raycastTarget = false; managerDragMarker.sizeDelta = new Vector2(1400f, 4f);
            managerDragMarker.gameObject.SetActive(false);
            managerStatus = LabelAt(managerOverlay, "ManagerStatus", "", 20, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(356f, 158f), new Vector2(1528f, 52f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            var x = 356f;
            AddManagerAction("ManagerOpen", "打开编辑", 168f, ref x, OpenManagerSelection);
            AddManagerAction("ManagerDelete", "删除所选", 168f, ref x, RequestManagerDelete);
            AddManagerAction("ManagerMoveUp", "上移", 128f, ref x, () => MoveManagerSelection(-1));
            AddManagerAction("ManagerMoveDown", "下移", 128f, ref x, () => MoveManagerSelection(1));
            AddManagerAction("ManagerMoveFirst", "移至首位", 152f, ref x, () => ReorderManagerSelection(0));
            AddManagerAction("ManagerMoveLast", "移至末位", 152f, ref x, () => ReorderManagerSelection(managerVisible.Count));
            ButtonAt(managerOverlay, "ManagerSelectAll", "全选", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(x, 94f), new Vector2(128f, 52f), SelectAllManagerLevels); x += 140f;
            ButtonAt(managerOverlay, "ManagerLog", "日志", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(x, 94f), new Vector2(128f, 52f), OpenOperationLog);
            managerTarget = ButtonAt(managerOverlay, "ManagerTarget", "", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(356f, 28f), new Vector2(320f, 52f), ToggleManagerTarget);
            var migrate = ButtonAt(managerOverlay, "ManagerMigrate", "迁移所选", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(692f, 28f), new Vector2(168f, 52f), MigrateManagerSelection, SokobanTheme.Accent);
            managerSelectionActions.Add(migrate);
            LabelAt(managerOverlay, "ManagerKeys", "Ctrl：切换多选  Shift：连续选择\n长按后拖动：调整顺序  Delete：删除", 19,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, 28f), new Vector2(740f, 52f),
                TextAnchor.MiddleRight, SokobanTheme.TextSecondary);
            UnityEngine.Canvas.ForceUpdateCanvases();
            RefreshManagerView();
            RecordManagement("打开关卡管理");
        }

        private void AddManagerAction(string name, string title, float width, ref float x, Action action)
        {
            managerSelectionActions.Add(ButtonAt(managerOverlay, name, title, new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(x, 94f), new Vector2(width, 52f), action)); x += width + 12f;
        }

        private static void ClearManagerChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            { var child = parent.GetChild(i).gameObject; child.SetActive(false); UnityEngine.Object.Destroy(child); }
        }

        private void RefreshManagerView()
        {
            var catalog = managerStore.Read();
            var categories = catalog.categories;
            var published = new HashSet<string>(catalog.publishedCategories ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (managerFilter != "All" && !categories.Contains(managerFilter)) managerFilter = "All";
            if (!categories.Contains(managerTargetCategory)) managerTargetCategory = categories[0];
            ClearManagerChildren(managerCategoryContent);
            foreach (var category in new[] { "All" }.Concat(categories))
            {
                var current = category;
                var count = category == "All" ? managerAll.Count : managerAll.Count(l => l.Category == category);
                if (category == "All")
                {
                    var allButton = SokobanUI.Button(managerCategoryContent, "ManagerCategory_All", "全部  (" + count + ")",
                        new Vector2(0f, 48f), () => FilterManagerLevels(current),
                        "All" == managerFilter ? SokobanTheme.Accent : (Color?)null,
                        "All" == managerFilter ? SokobanTheme.AccentText : (Color?)null);
                    allButton.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
                    allButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 20;
                    SokobanUI.Ellipsize(allButton.GetComponentInChildren<UnityEngine.UI.Text>(), "全部", "  (" + count + ")", 228f);
                    continue;
                }
                // 每个分类一行：左侧筛选按钮 + 右侧"对玩家公开"开关。
                var row = new GameObject("ManagerCategoryRow_" + category, typeof(RectTransform));
                row.transform.SetParent(managerCategoryContent, false);
                var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
                rowLayout.spacing = 6f;
                rowLayout.childControlWidth = true;
                rowLayout.childControlHeight = true;
                rowLayout.childForceExpandWidth = false;
                rowLayout.childForceExpandHeight = true;
                var rowElement = row.AddComponent<LayoutElement>();
                rowElement.preferredHeight = 48f;
                rowElement.minHeight = 48f;
                var isFilter = category == managerFilter;
                var button = SokobanUI.Button(row.transform, "ManagerCategory_" + category, category + "  (" + count + ")",
                    new Vector2(0f, 48f), () => FilterManagerLevels(current),
                    isFilter ? SokobanTheme.Accent : (Color?)null, isFilter ? SokobanTheme.AccentText : (Color?)null);
                var buttonElement = button.gameObject.AddComponent<LayoutElement>();
                buttonElement.preferredHeight = 48f;
                buttonElement.flexibleWidth = 1f;
                button.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 20;
                SokobanUI.Ellipsize(button.GetComponentInChildren<UnityEngine.UI.Text>(), category, "  (" + count + ")", 150f);
                var isPublished = published.Contains(category);
                var toggle = SokobanUI.Button(row.transform, "ManagerPublish_" + category,
                    isPublished ? "公开" : "隐藏", new Vector2(76f, 48f),
                    () => ToggleManagerPublish(current),
                    isPublished ? SokobanTheme.Accent : (Color?)null, isPublished ? SokobanTheme.AccentText : (Color?)null);
                var toggleElement = toggle.gameObject.AddComponent<LayoutElement>();
                toggleElement.preferredWidth = 76f;
                toggleElement.preferredHeight = 48f;
                toggleElement.minWidth = 76f;
            }
            SetButtonLabel(managerTarget, "目标分类：" + managerTargetCategory + " ▾");
            SokobanUI.Ellipsize(managerTarget.GetComponentInChildren<UnityEngine.UI.Text>(), "目标分类：" + managerTargetCategory, " ▾", 296f);
            managerVisible = managerAll.Where(l => managerFilter == "All" || l.Category == managerFilter).ToList();
            managerSelection.Retain(ManagerVisibleIds());
            ClearManagerChildren(managerContent); managerRows.Clear();
            if (managerVisible.Count == 0)
            {
                var label = SokobanUI.Text(managerContent, "ManagerEmpty", "该分类暂无关卡", 24, SokobanTheme.TextSecondary, TextAnchor.MiddleCenter);
                label.gameObject.AddComponent<LayoutElement>().preferredHeight = 100f;
            }
            for (var i = 0; i < managerVisible.Count; i++) BuildManagerRow(managerVisible[i], i);
            RefreshManagerSelection();
        }

        private void BuildManagerRow(SokobanLevelDescriptor descriptor, int index)
        {
            var row = SokobanUI.Button(managerContent, "ManagerLevel_" + descriptor.LevelId, "", new Vector2(0f, 64f), () => { });
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f;
            row.navigation = new Navigation { mode = Navigation.Mode.None };
            UnityEngine.Object.Destroy(row.transform.Find("Label").gameObject);
            LabelAt(row.transform, "Order", (index + 1).ToString(), 22, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(12f, 0f), new Vector2(64f, 58f));
            ManagerRowLabel(row.transform, "Title", descriptor.DisplayTitle, 22, 0.07f, 0.63f, true);
            ManagerRowLabel(row.transform, "LevelId", descriptor.LevelId, 17, 0.07f, 0.63f, false);
            ManagerRowLabel(row.transform, "Category", descriptor.Category, 20, 0.65f, 0.82f, null);
            ManagerRowLabel(row.transform, "Source", descriptor.Source, 20, 0.83f, 0.99f, null);
            managerRows[descriptor.LevelId] = row;
            var pointer = row.gameObject.AddComponent<SokobanLibraryRowPointer>();
            pointer.Press = e => PressManagerRow(descriptor.LevelId, e);
            pointer.Release = ReleaseManagerRow;
            pointer.Click = e => ClickManagerRow(descriptor.LevelId, e);
            pointer.InitializeDrag = e => managerScroll.OnInitializePotentialDrag(e);
            pointer.BeginDrag = e => { if (!managerDragging) managerScroll.OnBeginDrag(e); };
            pointer.Drag = e =>
            {
                managerPointerPosition = e.position;
                if (!managerDragging) { managerScrollGesture = true; managerScroll.OnDrag(e); }
                else UpdateManagerDropPosition(e.position);
            };
            pointer.EndDrag = e => { if (!managerDragging) managerScroll.OnEndDrag(e); else ReleaseManagerRow(e); };
        }

        private void ManagerRowLabel(Transform parent, string name, string text, int size, float left, float right, bool? upper)
        {
            var label = SokobanUI.Text(parent, name, text, size, SokobanTheme.TextPrimary, TextAnchor.MiddleLeft);
            label.supportRichText = false;
            label.rectTransform.anchorMin = new Vector2(left, upper == true ? 0.5f : 0f);
            label.rectTransform.anchorMax = new Vector2(right, upper == false ? 0.5f : 1f);
            label.rectTransform.offsetMin = new Vector2(0f, 0f); label.rectTransform.offsetMax = Vector2.zero;
            if (upper == false) label.color = SokobanTheme.TextSecondary;
            SokobanUI.Ellipsize(label, (text ?? "").Replace('\n', ' ').Replace('\r', ' '), "", Mathf.Max(80f, (managerContent.rect.width - 24f) * (right - left)));
        }

        private List<string> ManagerVisibleIds() => managerVisible.Select(l => l.LevelId).ToList();

        private void RefreshManagerSelection()
        {
            foreach (var pair in managerRows)
            {
                var selected = managerSelection.Selected.Contains(pair.Key);
                pair.Value.GetComponent<Image>().color = selected ? SokobanTheme.Accent : SokobanTheme.Surface;
                foreach (var text in pair.Value.GetComponentsInChildren<UnityEngine.UI.Text>())
                    text.color = selected ? SokobanTheme.AccentText : text.name == "LevelId" ? SokobanTheme.TextSecondary : SokobanTheme.TextPrimary;
            }
            foreach (var action in managerSelectionActions) action.interactable = managerSelection.Selected.Count > 0;
            managerStatus.text = $"分类：{(managerFilter == "All" ? "全部" : managerFilter)} · 共 {managerVisible.Count} 个关卡 · 已选 {managerSelection.Selected.Count} 个";
        }

        private void FilterManagerLevels(string category)
        {
            CancelManagerDrag(); managerSelection.Clear(); managerFilter = category; managerFocusId = null;
            RefreshManagerView(); managerScroll.verticalNormalizedPosition = 1f;
            RecordManagement("筛选分类：" + (category == "All" ? "全部" : category));
        }

        private void SelectAllManagerLevels()
        {
            managerSelection.SelectAll(ManagerVisibleIds()); RefreshManagerSelection(); RecordManagement("全选 " + managerVisible.Count + " 个关卡");
        }

        private void SelectManagerLevel(string id, bool control, bool shift)
        {
            managerSelection.Click(ManagerVisibleIds(), id, control, shift); managerFocusId = id;
            RefreshManagerSelection(); RecordManagement($"选择关卡 {id}（Ctrl={control}，Shift={shift}），已选 {managerSelection.Selected.Count} 个");
        }

        private void PressManagerRow(string id, PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || managerDeleteOverlay != null) return;
            managerSuppressClick = false; managerScrollGesture = false;
            var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            managerDeferredClick = !control && !shift && managerSelection.Selected.Contains(id);
            if (!managerDeferredClick) SelectManagerLevel(id, control, shift);
            managerFocusId = id; managerPressedId = id; managerPressTime = Time.unscaledTime;
            managerPressPosition = managerPointerPosition = e.position;
        }

        private void ClickManagerRow(string id, PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left || managerSuppressClick || managerDeleteOverlay != null || logOverlay != null) return;
            if (e.clickCount >= 2) { SelectManagerLevel(id, false, false); OpenManagerSelection(); }
        }

        private void ReleaseManagerRow(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (managerDragging)
            {
                UpdateManagerDropPosition(e.position);
                var boundary = managerInsertion;
                var inside = RectTransformUtility.RectangleContainsScreenPoint(managerScroll.viewport, e.position, null);
                CancelManagerDrag(); managerSuppressClick = true;
                if (inside) ReorderManagerSelection(boundary);
                else managerStatus.text = "已取消拖动：请在列表内放开。";
            }
            else if (managerDeferredClick && !managerScrollGesture && managerPressedId != null)
                SelectManagerLevel(managerPressedId, false, false);
            managerPressedId = null; managerDeferredClick = false;
        }

        private void BeginManagerReorder()
        {
            if (managerPressedId == null || !managerSelection.Selected.Contains(managerPressedId)) return;
            managerDragging = true; managerScroll.StopMovement(); managerDragMarker.gameObject.SetActive(true);
            UpdateManagerDropPosition(managerPointerPosition);
            RecordManagement("长按开始调整顺序：" + string.Join("、", managerVisible.Where(l => managerSelection.Selected.Contains(l.LevelId)).Select(l => l.LevelId)));
        }

        private void UpdateManagerDropPosition(Vector2 screenPoint)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(managerContent, screenPoint, null, out var local);
            managerInsertion = Mathf.Clamp(Mathf.FloorToInt((-local.y - 12f + 32f) / 72f), 0, managerVisible.Count);
            var world = managerContent.TransformPoint(new Vector3(0f, -12f - managerInsertion * 72f, 0f));
            var markerPoint = managerScroll.viewport.InverseTransformPoint(world);
            managerDragMarker.anchoredPosition = new Vector2(0f, markerPoint.y);
            managerDragMarker.sizeDelta = new Vector2(managerScroll.viewport.rect.width - 24f, 4f);
            managerDragMarker.SetAsLastSibling();
            managerStatus.text = $"正在移动 {managerSelection.Selected.Count} 个关卡 · 放到第 {managerInsertion + 1} 个位置前 · 松开完成，Esc 取消";
        }

        private void CancelManagerDrag()
        {
            if (managerDragging) managerSuppressClick = true;
            managerDragging = false; managerPressedId = null; managerDeferredClick = false;
            if (managerDragMarker != null) managerDragMarker.gameObject.SetActive(false);
        }

        private bool UpdateLevelManager()
        {
            if (managerOverlay == null) return false;
            if (managerDeleteOverlay != null)
            { if (Input.GetKeyDown(KeyCode.Escape)) CancelManagerDelete(); return true; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (managerDragging) { CancelManagerDrag(); RefreshManagerSelection(); RecordManagement("取消拖动排序"); }
                else if (managerTargetPopup != null) CloseManagerTarget();
                else CloseLevelManager();
                return true;
            }
            if (managerPressedId != null && !managerDragging && !managerScrollGesture && Input.GetMouseButton(0) &&
                Time.unscaledTime - managerPressTime >= LongPressSeconds &&
                ((Vector2)Input.mousePosition - managerPressPosition).sqrMagnitude < 64f) BeginManagerReorder();
            if (managerDragging)
            {
                managerPointerPosition = Input.mousePosition;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(managerScroll.viewport, managerPointerPosition, null, out var local);
                var edge = local.y > managerScroll.viewport.rect.yMax - 36f ? 1f : local.y < managerScroll.viewport.rect.yMin + 36f ? -1f : 0f;
                if (edge != 0f) managerScroll.verticalNormalizedPosition = Mathf.Clamp01(managerScroll.verticalNormalizedPosition + edge * Time.unscaledDeltaTime * 0.6f);
                UpdateManagerDropPosition(managerPointerPosition);
            }
            var selectedObject = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var focused = selectedObject != null ? selectedObject.GetComponent<InputField>() : null;
            if (focused != null) return true;
            var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (control && Input.GetKeyDown(KeyCode.A)) SelectAllManagerLevels();
            else if (Input.GetKeyDown(KeyCode.Delete)) RequestManagerDelete();
            else if (Input.GetKeyDown(KeyCode.Return))
            {
                var selected = EventSystem.current?.currentSelectedGameObject;
                if (selected == null || selected.name.StartsWith("ManagerLevel_", StringComparison.Ordinal)) OpenManagerSelection();
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                var index = managerVisible.FindIndex(l => l.LevelId == managerFocusId);
                index = Mathf.Clamp(index + (Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 1), 0, managerVisible.Count - 1);
                if (index >= 0 && index < managerVisible.Count)
                {
                    if (control && !shift) managerFocusId = managerVisible[index].LevelId;
                    else SelectManagerLevel(managerVisible[index].LevelId, control, shift);
                }
            }
            return true;
        }

        private void ToggleManagerTarget()
        {
            if (managerTargetPopup != null) { CloseManagerTarget(); return; }
            var categories = managerStore.Read().categories;
            managerTargetPopup = SokobanUI.Panel(managerOverlay, "ManagerTargetPopup", SokobanTheme.Panel,
                new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero);
            managerTargetPopup.pivot = Vector2.zero; managerTargetPopup.anchoredPosition = new Vector2(356f, 86f);
            managerTargetPopup.sizeDelta = new Vector2(320f, Mathf.Min(430f, categories.Length * 50f + 24f));
            SokobanUI.ScrollList(managerTargetPopup, "ManagerTargetOptions", Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, out var content);
            foreach (var category in categories)
            {
                var current = category;
                var button = SokobanUI.Button(content, "ManagerTarget_" + category, category, new Vector2(0f, 42f), () =>
                { managerTargetCategory = current; CloseManagerTarget(); RefreshManagerView(); });
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 42f;
            }
        }

        private void CloseManagerTarget()
        {
            if (managerTargetPopup != null) { managerTargetPopup.gameObject.SetActive(false); UnityEngine.Object.Destroy(managerTargetPopup.gameObject); }
            managerTargetPopup = null;
        }

        private void CreateManagerCategory()
        {
            var name = managerCategoryName.text.Trim();
            RunManagerChange(() =>
            {
                managerStore.CreateCategory(name); managerTargetCategory = name; managerCategoryName.SetTextWithoutNotify("");
            }, "新增分类：" + name);
        }

        /// <summary>切换某分类是否对玩家公开；公开分类下的全部关卡会进入玩家战役链。所有分类都可切换。</summary>
        private void ToggleManagerPublish(string category)
        {
            var wasPublished = managerStore.PublishedCategories()
                .Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
            RunManagerChange(() =>
            {
                var current = new List<string>(managerStore.PublishedCategories());
                if (wasPublished) current.RemoveAll(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase));
                else current.Add(category);
                managerStore.SetPublishedCategories(current);
            }, (wasPublished ? "取消对玩家公开分类：" : "对玩家公开分类：") + category);
        }

        private void MigrateManagerSelection()
        {
            var ids = managerVisible.Where(l => managerSelection.Selected.Contains(l.LevelId)).Select(l => l.LevelId).ToArray();
            RunManagerChange(() => managerStore.Migrate(managerAll, ids, managerTargetCategory),
                "分类迁移至“" + managerTargetCategory + "”：" + string.Join("、", ids));
        }

        private void MoveManagerSelection(int delta)
        {
            var indexes = managerVisible.Select((l, i) => new { l.LevelId, Index = i }).Where(l => managerSelection.Selected.Contains(l.LevelId)).Select(l => l.Index).ToArray();
            if (indexes.Length == 0) return;
            ReorderManagerSelection(delta < 0 ? indexes.Min() - 1 : indexes.Max() + 2);
        }

        private void ReorderManagerSelection(int boundary)
        {
            if (managerSelection.Selected.Count == 0) return;
            var visible = ManagerVisibleIds();
            var reordered = SokobanLibraryOrdering.MoveGroup(visible, managerSelection.Selected, boundary);
            if (visible.SequenceEqual(reordered)) { RefreshManagerSelection(); return; }
            RunManagerChange(() => managerStore.Reorder(managerAll, visible, reordered), "调整关卡顺序：" + string.Join(" → ", reordered));
        }

        private void RunManagerChange(Action operation, string message)
        {
            CancelManagerDrag(); CloseManagerTarget();
            try
            {
                operation(); managerAll = managerStore.Apply(managerAll).ToList();
                NotifyLibraryChanged(); RefreshManagerView(); managerStatus.text = message;
                RecordManagement(message);
            }
            catch (Exception error) { managerStatus.text = "操作失败：" + error.Message; RecordManagement(managerStatus.text); }
        }

        private void NotifyLibraryChanged()
        {
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
            var existing = managerAll.ToDictionary(l => l.LevelId);
            foreach (var tab in tabs) if (tab.Descriptor != null && existing.TryGetValue(tab.Data.levelId, out var descriptor)) tab.Descriptor = descriptor;
            RefreshList(); RefreshDetail();
        }

        private void ReloadLevelManager()
        {
            CancelManagerDrag();
            try { managerAll = SokobanLevelRepository.ListAll().ToList(); RefreshManagerView(); RecordManagement("刷新关卡管理列表"); }
            catch (Exception error) { managerStatus.text = "读取失败：" + error.Message; RecordManagement(managerStatus.text); }
        }

        private void OpenManagerSelection()
        {
            var selected = managerVisible.Where(l => managerSelection.Selected.Contains(l.LevelId)).ToList();
            if (selected.Count == 0) return;
            CloseLevelManager();
            foreach (var descriptor in selected) OpenTab(descriptor);
        }

        private void RequestManagerDelete()
        {
            if (managerSelection.Selected.Count == 0 || managerDeleteOverlay != null) return;
            CancelManagerDrag(); CloseManagerTarget();
            managerPendingDelete = managerVisible.Where(l => managerSelection.Selected.Contains(l.LevelId)).Select(l => l.LevelId).ToArray();
            if (managerPendingDelete.Length == 0) return;
            managerOverlay.GetComponent<CanvasGroup>().interactable = false;
            managerDeleteOverlay = SokobanUI.Panel(managerOverlay, "ManagerDeleteOverlay", new Color(0f, 0f, 0f, 0.76f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            managerDeleteOverlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            var panel = SokobanUI.Panel(managerDeleteOverlay, "ManagerDeleteDialog", SokobanTheme.Panel,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.sizeDelta = new Vector2(900f, 520f);
            LabelAt(panel, "ManagerDeleteTitle", $"删除 {managerPendingDelete.Length} 个关卡？", 30, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -24f), new Vector2(836f, 48f));
            SokobanUI.ScrollList(panel, "ManagerDeleteNames", Vector2.zero, Vector2.one,
                new Vector2(24f, 190f), new Vector2(-24f, -88f), out var names);
            foreach (var level in managerVisible.Where(l => managerPendingDelete.Contains(l.LevelId)))
            {
                var label = SokobanUI.Text(names, "DeleteName_" + level.LevelId, level.DisplayTitle + " · " + level.LevelId, 20, SokobanTheme.TextPrimary);
                label.supportRichText = false; label.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f;
                SokobanUI.Ellipsize(label, label.text.Replace('\n', ' '), "", 800f);
            }
            var dirty = tabs.Count(t => t.Descriptor != null && managerPendingDelete.Contains(t.Data.levelId) && t.Dirty);
            LabelAt(panel, "ManagerDeleteWarning", "删除后将从关卡列表移除，并关闭对应的编辑页签。" +
                (dirty > 0 ? $"\n其中 {dirty} 个页签有未保存修改，确认删除会同时丢弃这些修改。" : ""), 21,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 92f), new Vector2(836f, 82f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            var confirm = ButtonAt(panel, "ManagerDeleteConfirm", "确认删除", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-130f, 24f), new Vector2(230f, 54f), ConfirmManagerDelete);
            var cancel = ButtonAt(panel, "ManagerDeleteCancel", "取消", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(130f, 24f), new Vector2(230f, 54f), CancelManagerDelete, SokobanTheme.Accent);
            confirm.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = cancel, selectOnRight = cancel };
            cancel.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirm, selectOnRight = confirm };
            EventSystem.current?.SetSelectedGameObject(cancel.gameObject);
            RecordManagement("请求删除：" + string.Join("、", managerPendingDelete));
        }

        private void ConfirmManagerDelete()
        {
            var ids = managerPendingDelete;
            if (ids == null) return;
            try
            {
                managerStore.Delete(managerAll, ids);
                foreach (var tab in tabs.Where(t => t.Descriptor != null && ids.Contains(t.Data.levelId)).ToList()) RemoveTab(tab, "删除关卡并关闭页签");
                managerAll.RemoveAll(l => ids.Contains(l.LevelId));
                DismissManagerDelete(); NotifyLibraryChanged(); RefreshManagerView();
                managerStatus.text = $"已删除 {ids.Length} 个关卡"; RecordManagement("已删除关卡：" + string.Join("、", ids));
            }
            catch (Exception error)
            {
                DismissManagerDelete(); managerStatus.text = "删除失败：" + error.Message; RecordManagement(managerStatus.text);
            }
        }

        private void CancelManagerDelete() { RecordManagement("取消删除关卡"); DismissManagerDelete(); }
        private void DismissManagerDelete()
        {
            if (managerDeleteOverlay != null) { managerDeleteOverlay.gameObject.SetActive(false); UnityEngine.Object.Destroy(managerDeleteOverlay.gameObject); }
            managerDeleteOverlay = null; managerPendingDelete = null;
            if (managerOverlay != null) managerOverlay.GetComponent<CanvasGroup>().interactable = true;
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private void CloseLevelManager()
        {
            CloseExchange();
            CancelManagerDrag(); CloseManagerTarget(); DismissManagerDelete();
            if (managerRootGroup != null) managerRootGroup.interactable = managerRootWasInteractable;
            if (managerOverlay != null) { managerOverlay.gameObject.SetActive(false); UnityEngine.Object.Destroy(managerOverlay.gameObject); RecordManagement("关闭关卡管理"); }
            managerOverlay = null; managerRows.Clear(); managerSelection.Clear();
            EventSystem.current?.SetSelectedGameObject(null);
        }

        private void RecordManagement(string message) => RecordOperation(message, null, "关卡管理");
    }
}
