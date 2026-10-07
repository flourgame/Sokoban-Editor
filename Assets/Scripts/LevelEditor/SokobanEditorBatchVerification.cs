using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private RectTransform verificationOverlay, verificationContent, verificationCategories, verificationProgressFill;
        private ScrollRect verificationScroll;
        private InputField verificationBudget;
        private UnityEngine.UI.Text verificationStatus, verificationDetails;
        private Button verificationStart, verificationCancel, verificationReplay;
        private readonly SokobanLibrarySelection verificationSelection = new SokobanLibrarySelection();
        private readonly Dictionary<string, Button> verificationRows = new Dictionary<string, Button>();
        private readonly Dictionary<string, SokobanJsonLevel> verificationSavedData = new Dictionary<string, SokobanJsonLevel>();
        private List<SokobanLevelDescriptor> verificationAll = new List<SokobanLevelDescriptor>(), verificationVisible = new List<SokobanLevelDescriptor>();
        private string verificationFilter = "All", verificationFocusId, verificationUiMessage;
        private int verificationShownRevision = -1, verificationObservedRevision = -1;
        private CanvasGroup verificationRootGroup;
        private bool verificationRootWasInteractable;

        private void OpenBatchVerification()
        {
            if (stampOverlay != null || verificationOverlay != null || managerOverlay != null || logOverlay != null || solverOverlay != null || generationOverlay != null || closeTabOverlay != null) return;
            var service = SokobanBatchVerificationService.Instance;
            service.DismissNotification(); verificationFilter = categoryFilter; verificationUiMessage = null;
            EventSystem.current?.SetSelectedGameObject(null); boxSelecting = false; moveArmed = false; panning = false; ClearMarquee();
            verificationRootGroup = Root.GetComponent<CanvasGroup>();
            if (verificationRootGroup == null) verificationRootGroup = Root.gameObject.AddComponent<CanvasGroup>();
            verificationRootWasInteractable = verificationRootGroup.interactable; verificationRootGroup.interactable = false;
            verificationOverlay = SokobanUI.Panel(Root, "BatchVerificationOverlay", SokobanTheme.Background,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            verificationOverlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            LabelAt(verificationOverlay, "VerificationTitle", "批量验证关卡", 38, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(Margin, -30f), new Vector2(1100f, 64f));
            ButtonAt(verificationOverlay, "VerificationRefresh", "刷新", Vector2.one, Vector2.one,
                new Vector2(-Margin - 234f, -30f), new Vector2(210f, 54f), ReloadVerificationLibrary);
            ButtonAt(verificationOverlay, "VerificationClose", "返回编辑", Vector2.one, Vector2.one,
                new Vector2(-Margin, -30f), new Vector2(210f, 54f), CloseBatchVerification);
            var categories = InsetPanel(verificationOverlay, "VerificationCategoryPanel", SokobanTheme.Panel, Margin, 1584f, TopInset, 28f);
            LabelAt(categories, "VerificationCategoryTitle", "分类", 24, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -18f), new Vector2(264f, 42f));
            SokobanUI.ScrollList(categories, "VerificationCategories", Vector2.zero, Vector2.one,
                new Vector2(12f, 280f), new Vector2(-12f, -76f), out verificationCategories);
            LabelAt(categories, "VerificationBudgetLabel", "每关验证预算（秒）", 20, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 222f), new Vector2(252f, 36f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            verificationBudget = SokobanUI.InputField(categories, "VerificationBudget", service.TimeoutSeconds.ToString(), "1–300", new Vector2(252f, 48f));
            var budgetRect = verificationBudget.GetComponent<RectTransform>(); budgetRect.anchorMin = budgetRect.anchorMax = budgetRect.pivot = new Vector2(0.5f, 0f);
            budgetRect.anchoredPosition = new Vector2(0f, 166f); verificationBudget.contentType = InputField.ContentType.IntegerNumber; verificationBudget.characterLimit = 3;
            LabelAt(categories, "VerificationHint", "检查所选关卡的已保存版本。\n未保存的编辑继续保留。\n启动后可切换页面，\n整批结束时弹出提醒。", 19,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(252f, 136f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            var table = InsetPanel(verificationOverlay, "VerificationTable", SokobanTheme.Panel, 356f, Margin, TopInset, 360f);
            VerificationHeader(table, "VerificationNameHeader", "关卡名称 / ID", 0.04f, 0.45f);
            VerificationHeader(table, "VerificationCategoryHeader", "分类", 0.47f, 0.60f);
            VerificationHeader(table, "VerificationResultHeader", "验证结果", 0.62f, 0.83f);
            VerificationHeader(table, "VerificationMovesHeader", "解关步数", 0.85f, 0.99f);
            verificationScroll = SokobanUI.ScrollList(table, "VerificationLevels", Vector2.zero, Vector2.one,
                new Vector2(12f, 12f), new Vector2(-12f, -68f), out verificationContent);
            verificationScroll.gameObject.AddComponent<SokobanLibraryRowPointer>().Click = e =>
            { if (e.button == PointerEventData.InputButton.Left) { verificationSelection.Clear(); verificationFocusId = null; RefreshVerificationSelection(); } };
            var detailsPanel = InsetPanel(verificationOverlay, "VerificationDetailsPanel", SokobanTheme.Panel, 356f, Margin, 736f, 190f);
            RectTransform detailsContent;
            SokobanUI.ScrollList(detailsPanel, "VerificationDetailsScroll", Vector2.zero, Vector2.one,
                new Vector2(12f, 12f), new Vector2(-12f, -12f), out detailsContent);
            verificationDetails = SokobanUI.Text(detailsContent, "VerificationDetails", "选择关卡可查看验证结果。", 21, SokobanTheme.TextSecondary, TextAnchor.UpperLeft);
            verificationDetails.supportRichText = false; verificationDetails.raycastTarget = false; verificationDetails.verticalOverflow = VerticalWrapMode.Overflow;
            verificationStatus = LabelAt(verificationOverlay, "VerificationStatus", "", 20, Vector2.zero, Vector2.zero,
                new Vector2(356f, 106f), new Vector2(1528f, 78f), TextAnchor.UpperLeft, SokobanTheme.TextSecondary);
            var track = SokobanUI.Panel(verificationOverlay, "VerificationProgressTrack", SokobanTheme.Field,
                Vector2.zero, new Vector2(1f, 0f), new Vector2(356f, 86f), new Vector2(-Margin, 104f));
            verificationProgressFill = SokobanUI.Panel(track, "VerificationProgressFill", SokobanTheme.Accent,
                Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            verificationStart = ButtonAt(verificationOverlay, "VerificationStart", "验证所选关卡", Vector2.zero, Vector2.zero,
                new Vector2(356f, 28f), new Vector2(240f, 52f), StartBatchVerification, SokobanTheme.Accent);
            verificationCancel = ButtonAt(verificationOverlay, "VerificationCancel", "取消整批", Vector2.zero, Vector2.zero,
                new Vector2(612f, 28f), new Vector2(180f, 52f), () => SokobanBatchVerificationService.Instance.Cancel());
            ButtonAt(verificationOverlay, "VerificationSelectAll", "全选", Vector2.zero, Vector2.zero,
                new Vector2(808f, 28f), new Vector2(128f, 52f), SelectAllVerificationLevels);
            ButtonAt(verificationOverlay, "VerificationCopy", "复制结果", Vector2.zero, Vector2.zero,
                new Vector2(952f, 28f), new Vector2(164f, 52f), CopyBatchVerification);
            verificationReplay = ButtonAt(verificationOverlay, "VerificationReplay", "查看 / 播放解答", Vector2.zero, Vector2.zero,
                new Vector2(1132f, 28f), new Vector2(248f, 52f), ReplayBatchVerification);
            ButtonAt(verificationOverlay, "VerificationLog", "日志", Vector2.zero, Vector2.zero,
                new Vector2(1396f, 28f), new Vector2(128f, 52f), OpenOperationLog);
            LabelAt(verificationOverlay, "VerificationKeys", "Ctrl：多选\nShift：连续选择", 17, new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-Margin, 28f), new Vector2(328f, 52f), TextAnchor.MiddleRight, SokobanTheme.TextSecondary);
            verificationShownRevision = -1; UnityEngine.Canvas.ForceUpdateCanvases(); ReloadVerificationLibrary();
            if (verificationSelection.Selected.Count == 0 && service.Entries.Count > 0)
            {
                verificationSelection.Selected.UnionWith(service.Entries.Select(e => e.Descriptor.LevelId).Intersect(VerificationVisibleIds()));
                verificationFocusId = service.Entries[0].Descriptor.LevelId;
            }
            RefreshVerificationSelection();
            RecordOperation("打开批量验证页面", null, "批量验证");
        }

        private void VerificationHeader(Transform parent, string name, string text, float left, float right)
        {
            var label = SokobanUI.Text(parent, name, text, 22, SokobanTheme.TextSecondary);
            label.rectTransform.anchorMin = new Vector2(left, 1f); label.rectTransform.anchorMax = new Vector2(right, 1f);
            label.rectTransform.offsetMin = new Vector2(12f, -60f); label.rectTransform.offsetMax = new Vector2(-12f, -18f);
        }
        private List<string> VerificationVisibleIds() => verificationVisible.Select(d => d.LevelId).ToList();
        private void ReloadVerificationLibrary()
        {
            verificationAll = SokobanLevelRepository.ListAll().ToList(); RefreshVerificationSavedData();
            var categories = verificationAll.Select(d => d.Category).Distinct().ToList();
            try { categories = SokobanLevelRepository.Library.Read().categories.ToList(); } catch { }
            if (verificationFilter != "All" && !categories.Contains(verificationFilter)) verificationFilter = "All";
            ClearManagerChildren(verificationCategories);
            foreach (var category in new[] { "All" }.Concat(categories))
            {
                var current = category; var count = category == "All" ? verificationAll.Count : verificationAll.Count(d => d.Category == category);
                var button = SokobanUI.Button(verificationCategories, "VerificationCategory_" + category,
                    (category == "All" ? "全部" : category) + " (" + count + ")", new Vector2(0f, 48f),
                    () => { verificationFilter = current; verificationSelection.Clear(); verificationFocusId = null; ReloadVerificationLibrary(); },
                    category == verificationFilter ? SokobanTheme.Accent : (Color?)null,
                    category == verificationFilter ? SokobanTheme.AccentText : (Color?)null);
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 48f;
                SokobanUI.Ellipsize(button.GetComponentInChildren<UnityEngine.UI.Text>(), (category == "All" ? "全部" : category) + " (" + count + ")", "", 240f);
            }
            verificationVisible = verificationAll.Where(d => verificationFilter == "All" || d.Category == verificationFilter).ToList();
            verificationSelection.Retain(VerificationVisibleIds()); verificationRows.Clear(); ClearManagerChildren(verificationContent);
            for (var i = 0; i < verificationVisible.Count; i++) BuildVerificationRow(verificationVisible[i], i);
            if (verificationVisible.Count == 0)
                SokobanUI.Text(verificationContent, "VerificationEmpty", "该分类暂无关卡", 24, SokobanTheme.TextSecondary).gameObject.AddComponent<LayoutElement>().preferredHeight = 80f;
            RefreshVerificationSelection();
        }
        private void BuildVerificationRow(SokobanLevelDescriptor descriptor, int index)
        {
            var row = SokobanUI.Button(verificationContent, "VerificationLevel_" + descriptor.LevelId, "", new Vector2(0f, 64f), () => { });
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = 64f; row.navigation = new Navigation { mode = Navigation.Mode.None };
            Destroy(row.transform.Find("Label").gameObject);
            VerificationRowText(row.transform, "Order", (index + 1).ToString(), 19, 0f, 0.04f, null);
            VerificationRowText(row.transform, "Title", descriptor.DisplayTitle, 22, 0.04f, 0.45f, true);
            VerificationRowText(row.transform, "LevelId", descriptor.LevelId, 16, 0.04f, 0.45f, false);
            VerificationRowText(row.transform, "Category", descriptor.Category, 20, 0.47f, 0.60f, null);
            VerificationRowText(row.transform, "Result", "", 20, 0.62f, 0.83f, null);
            VerificationRowText(row.transform, "VerifiedMoves", "-1", 21, 0.85f, 0.99f, null);
            verificationRows[descriptor.LevelId] = row;
            var pointer = row.gameObject.AddComponent<SokobanLibraryRowPointer>();
            pointer.Click = e =>
            {
                if (e.button != PointerEventData.InputButton.Left || logOverlay != null) return;
                SelectVerificationLevel(descriptor.LevelId, Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl),
                    Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            };
            pointer.InitializeDrag = verificationScroll.OnInitializePotentialDrag; pointer.BeginDrag = verificationScroll.OnBeginDrag;
            pointer.Drag = verificationScroll.OnDrag; pointer.EndDrag = verificationScroll.OnEndDrag;
        }
        private void VerificationRowText(Transform parent, string name, string value, int size, float left, float right, bool? upper)
        {
            var label = SokobanUI.Text(parent, name, value, size, SokobanTheme.TextPrimary);
            label.supportRichText = false;
            label.rectTransform.anchorMin = new Vector2(left, upper == true ? 0.5f : 0f);
            label.rectTransform.anchorMax = new Vector2(right, upper == false ? 0.5f : 1f);
            label.rectTransform.offsetMin = new Vector2(8f, 0f); label.rectTransform.offsetMax = new Vector2(-8f, 0f);
            SokobanUI.Ellipsize(label, (value ?? "").Replace('\n', ' ').Replace('\r', ' '), "", Mathf.Max(40f, (verificationContent.rect.width - 24f) * (right - left) - 16f));
        }
        private void SelectVerificationLevel(string id, bool control, bool shift)
        {
            verificationSelection.Click(VerificationVisibleIds(), id, control, shift); verificationFocusId = id;
            verificationUiMessage = null; RefreshVerificationSelection();
            RecordOperation($"选择 {id}（Ctrl={control}，Shift={shift}），已选 {verificationSelection.Selected.Count} 个", null, "批量验证");
        }
        private void SelectAllVerificationLevels()
        { verificationSelection.SelectAll(VerificationVisibleIds()); verificationFocusId = verificationVisible.FirstOrDefault()?.LevelId; RefreshVerificationSelection(); }
        private void RefreshVerificationSelection()
        {
            foreach (var pair in verificationRows)
            {
                var selected = verificationSelection.Selected.Contains(pair.Key);
                pair.Value.GetComponent<Image>().color = selected ? SokobanTheme.Accent : SokobanTheme.Surface;
                foreach (var label in pair.Value.GetComponentsInChildren<UnityEngine.UI.Text>())
                    label.color = selected ? SokobanTheme.AccentText : label.name == "LevelId" ? SokobanTheme.TextSecondary : SokobanTheme.TextPrimary;
            }
            RefreshVerificationResults();
        }
        private void StartBatchVerification()
        {
            if (!int.TryParse(verificationBudget.text, out var seconds)) { verificationUiMessage = "每关验证预算须为 1–300 秒"; return; }
            var selected = verificationVisible.Where(d => verificationSelection.Selected.Contains(d.LevelId)).ToList();
            if (!SokobanBatchVerificationService.Instance.StartBatch(selected, seconds, out var error)) { verificationUiMessage = error; return; }
            verificationUiMessage = null; RefreshVerificationResults();
        }
        private SokobanBatchVerificationService.Entry FocusedVerificationEntry() =>
            SokobanBatchVerificationService.Instance.Entries.FirstOrDefault(e => e.Descriptor.LevelId == verificationFocusId);
        private void RefreshVerificationSavedData()
        {
            verificationSavedData.Clear();
            foreach (var descriptor in verificationAll)
                verificationSavedData[descriptor.LevelId] = !string.IsNullOrEmpty(descriptor.FilePath) && !System.IO.File.Exists(descriptor.FilePath) && string.IsNullOrEmpty(descriptor.BundledJson)
                    ? null : SokobanLevelRepository.LoadJson(descriptor);
            verificationShownRevision = SokobanBatchVerificationService.Instance.Revision;
        }
        private void RefreshVerificationResults()
        {
            var service = SokobanBatchVerificationService.Instance;
            // 仅在打开/刷新页面或发布新结果时读文件，不在每帧读取，也不以历史结果替代当前字段。
            if (verificationShownRevision != service.Revision) RefreshVerificationSavedData();
            foreach (var pair in verificationRows)
            {
                var entry = service.Entries.FirstOrDefault(e => e.Descriptor.LevelId == pair.Key);
                verificationSavedData.TryGetValue(pair.Key, out var saved);
                var moves = saved?.verifiedMoves ?? -1;
                var status = moves >= 0 ? "已验证 · 有解" : saved?.solution?.status == "Unsolvable" ? "已验证 · 无解" : "未验证";
                if (entry != null)
                {
                    if (entry.Result == null) status = service.Running && service.Progress?.CurrentIndex == service.Entries.IndexOf(entry) ? "正在验证…" : service.Running ? "等待验证" : "未处理";
                    else
                    {
                        status = VerificationStatusName(entry.Result.Status) + (entry.Saved ? "" : " · 未写入");
                        if (entry.Saved && !entry.MatchesSavedResult(saved))
                            status = saved == null ? "文件不可用" : moves >= 0 ? "已验证 · 有解" :
                                saved.solution?.status == "Unsolvable" ? "已验证 · 无解" : "已改动 · 需验证";
                    }
                }
                SokobanUI.Ellipsize(pair.Value.transform.Find("Result").GetComponent<UnityEngine.UI.Text>(), status, "",
                    Mathf.Max(40f, (verificationContent.rect.width - 24f) * 0.21f - 16f));
                pair.Value.transform.Find("VerifiedMoves").GetComponent<UnityEngine.UI.Text>().text = moves.ToString();
            }
            var entryFocus = FocusedVerificationEntry();
            verificationSavedData.TryGetValue(verificationFocusId ?? "", out var currentFocus);
            verificationReplay.interactable = entryFocus?.Result != null && entryFocus.Result.IsSolved;
            if (entryFocus != null)
                verificationDetails.text = entryFocus.Descriptor.DisplayTitle + " / " + entryFocus.Descriptor.LevelId + "\n" +
                    (entryFocus.Result == null ? service.Running ? "等待 / 正在验证已保存版本…" : "尚未处理" :
                    VerificationStatusName(entryFocus.Result.Status) + (entryFocus.Result.IsSolved ? $" · 移动 {entryFocus.Result.Moves.Length} 步 / 推箱 {entryFocus.Result.Pushes} 次" : "\n" + entryFocus.Result.Message) +
                    $"\n搜索 {entryFocus.Result.ExploredNodes} 个节点 · {entryFocus.Result.ElapsedMs} ms\n" + entryFocus.SaveMessage +
                    (!entryFocus.Result.IsSolved ? "\n解关步数为 -1；超时或上限不能据此判为无解。" : "") +
                    (entryFocus.Saved && !entryFocus.MatchesSavedResult(currentFocus) ?
                        "\n历史结果：当前关卡已修改或重新验证；当前文件解关步数为 " + (currentFocus?.verifiedMoves ?? -1) +
                        "。播放将查看当时的关卡快照。" : ""));
            else
            {
                verificationSavedData.TryGetValue(verificationFocusId ?? "", out var data);
                verificationDetails.text = data == null ? "选择关卡可查看验证结果。" : data.name + "\n解关步数：" + data.verifiedMoves +
                    "\n-1 表示没有可信解答；编辑后需重新验证。";
            }
            UpdateVerificationProgress();
        }
        private void UpdateVerificationProgress()
        {
            var service = SokobanBatchVerificationService.Instance; var progress = service.Progress;
            verificationStart.interactable = !service.Running && verificationSelection.Selected.Count > 0;
            verificationCancel.interactable = service.Running && !service.Cancelling; verificationBudget.interactable = !service.Running;
            verificationProgressFill.anchorMax = new Vector2(Mathf.Clamp01(progress?.Fraction ?? 0f), 1f);
            verificationProgressFill.offsetMin = verificationProgressFill.offsetMax = Vector2.zero;
            verificationStatus.text = service.Entries.Count == 0 ? $"共 {verificationVisible.Count} 个关卡 · 已选 {verificationSelection.Selected.Count} 个 · Ctrl / Shift 多选" :
                service.Summary + $" · 已选 {verificationSelection.Selected.Count} 个\n进度 {(progress?.Fraction ?? 0f):P0} · 耗时 {(progress?.ElapsedMs ?? 0) / 1000f:0.0} 秒" +
                (service.Running && progress?.Current != null ? $" · 当前搜索 {progress.Current.ExploredNodes} 个节点" : $" · 未写入 {service.Unsaved} 个");
            if (verificationUiMessage != null) verificationStatus.text = verificationUiMessage;
        }
        private bool UpdateBatchVerification()
        {
            if (verificationOverlay == null) return false;
            var service = SokobanBatchVerificationService.Instance;
            if (verificationShownRevision != service.Revision) RefreshVerificationResults();
            else UpdateVerificationProgress();
            if (Input.GetKeyDown(KeyCode.Escape)) { CloseBatchVerification(); return true; }
            var selectedObject = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (selectedObject != null && selectedObject.GetComponent<InputField>() != null) return true;
            var control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (control && Input.GetKeyDown(KeyCode.A)) SelectAllVerificationLevels();
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
            {
                var index = Mathf.Clamp(verificationVisible.FindIndex(d => d.LevelId == verificationFocusId) +
                    (Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 1), 0, verificationVisible.Count - 1);
                if (verificationVisible.Count > 0) SelectVerificationLevel(verificationVisible[index].LevelId, control, shift);
            }
            return true;
        }
        private void CopyBatchVerification()
        {
            var service = SokobanBatchVerificationService.Instance; var report = new StringBuilder(service.Summary + "\n");
            foreach (var entry in service.Entries)
                report.AppendLine(entry.Descriptor.DisplayTitle + " / " + entry.Descriptor.LevelId + "：" +
                    (entry.Result == null ? "未处理" : VerificationStatusName(entry.Result.Status) +
                        $" · 解关步数 {(entry.Result.IsSolved ? entry.Result.Moves.Length : -1)} · 推箱 {entry.Result.Pushes} · 节点 {entry.Result.ExploredNodes} · {entry.Result.ElapsedMs} ms\n" + entry.Result.Message + "；" + entry.SaveMessage));
            GUIUtility.systemCopyBuffer = report.ToString(); verificationUiMessage = "已复制本批次所有验证结果";
            RecordOperation("复制批量验证结果", null, "批量验证");
        }
        private void ReplayBatchVerification()
        {
            var entry = FocusedVerificationEntry(); if (entry?.Result == null || !entry.Result.IsSolved) return;
            CloseBatchVerification(); OpenSolver(entry.Source, entry.Result);
        }
        private void CloseBatchVerification()
        {
            if (verificationRootGroup != null) verificationRootGroup.interactable = verificationRootWasInteractable;
            if (verificationOverlay != null) { verificationOverlay.gameObject.SetActive(false); Destroy(verificationOverlay.gameObject); }
            verificationOverlay = null; verificationRows.Clear(); EventSystem.current?.SetSelectedGameObject(null);
            RecordOperation("离开批量验证页面（任务和结果保留）", null, "批量验证");
        }
        internal static string VerificationStatusName(SokobanSolveStatus status)
        {
            switch (status)
            {
                case SokobanSolveStatus.Solved: return "有解";
                case SokobanSolveStatus.Unsolvable: return "确认无解";
                case SokobanSolveStatus.Invalid: return "结构错误";
                case SokobanSolveStatus.Timeout: return "超时（未确定）";
                case SokobanSolveStatus.LimitReached: return "上限（未确定）";
                case SokobanSolveStatus.Cancelled: return "已取消";
                default: return "异常";
            }
        }
        internal void AcceptBatchVerification(SokobanBatchVerificationService.Entry entry)
        {
            foreach (var tab in tabs)
                if (tab.Data != null && tab.Data.levelId == entry.Descriptor.LevelId && SokobanVerificationMetadata.Revision(tab.Data) == entry.SourceRevision)
                    SokobanVerificationMetadata.Apply(tab.Data, entry.Result);
            if (Data != null && Data.levelId == entry.Descriptor.LevelId) RefreshDetail();
        }
        private void ObserveBatchVerification()
        {
            var service = SokobanBatchVerificationService.Existing;
            if (service == null) return;
            if (verificationObservedRevision != service.Revision)
            { verificationObservedRevision = service.Revision; RefreshList(); }
            if (!service.PendingOpen || stampOverlay != null || managerOverlay != null || logOverlay != null || solverOverlay != null || generationOverlay != null || closeTabOverlay != null) return;
            service.PendingOpen = false;
            if (verificationOverlay == null) OpenBatchVerification();
        }
    }
}
