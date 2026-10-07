using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private Button stampButton, transparentButton, stampUseButton, stampEditButton;
        private RectTransform stampOverlay, stampListContent, stampPreviewBoard;
        private UnityEngine.UI.Text stampSummary, stampLibraryStatus;
        private List<SokobanStamp> availableStamps = new List<SokobanStamp>();
        private List<SokobanClipboardCell> stampCells = new List<SokobanClipboardCell>();
        private SokobanStamp selectedStamp, activeStamp;
        private bool stampMode, transparentBrush;
        private bool IsStampDocument => Active != null && Active.IsStamp;

        private void RefreshStampTools()
        {
            if (stampButton != null) SetToolColor(stampButton, stampMode || stampOverlay != null);
            if (transparentButton != null) { transparentButton.gameObject.SetActive(IsStampDocument); SetToolColor(transparentButton, transparentBrush); }
            if (Root == null) return;
            var nameLabel = Root.Find("RightPanel/NameLabel")?.GetComponent<UnityEngine.UI.Text>();
            if (nameLabel != null) nameLabel.text = IsStampDocument ? "印章名称" : "关卡名称";
            var write = Root.Find("RightPanel/Write/Label")?.GetComponent<UnityEngine.UI.Text>();
            if (write != null) write.text = IsStampDocument ? "保存印章" : "保存";
            foreach (var key in new[] { "Play", "Validate", "Export" })
            {
                var button = Root.Find("RightPanel/" + key)?.GetComponent<Button>();
                if (button != null) button.interactable = Data != null && !IsStampDocument;
            }
        }

        private static void SetToolColor(Button button, bool selected)
        {
            button.GetComponent<Image>().color = selected ? SokobanTheme.Accent : SokobanTheme.Surface;
            var label = button.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>();
            if (label != null) label.color = selected ? SokobanTheme.AccentText : SokobanTheme.TextPrimary;
        }

        private void ToggleTransparentBrush()
        {
            if (!IsStampDocument) return;
            var enabled = !transparentBrush;
            CancelCurrentState(); transparentBrush = enabled;
            RefreshStampTools(); RefreshGhost();
            SetDetail(enabled ? "透明笔刷：清除印章格子；也可以框选后点击填充。" : "已取消透明笔刷");
        }

        private void OpenStampLibrary()
        {
            if (stampOverlay != null || closeTabOverlay != null || logOverlay != null || solverOverlay != null || generationOverlay != null || managerOverlay != null || verificationOverlay != null) return;
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
            CancelCurrentState(); panning = false;
            stampOverlay = SokobanUI.Panel(Root, "StampOverlay", SokobanTheme.Background, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            LabelAt(stampOverlay, "StampLibraryTitle", "印章 · 预设画笔", 38, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Margin, -30f), new Vector2(1320f, 64f));
            ButtonAt(stampOverlay, "StampClose", "返回编辑", Vector2.one, Vector2.one, new Vector2(-Margin, -30f), new Vector2(210f, 54f), CloseStampLibrary);
            var list = InsetPanel(stampOverlay, "StampListPanel", SokobanTheme.Panel, Margin, 1276f, 116f, 174f);
            SokobanUI.ScrollList(list, "StampScroll", Vector2.zero, Vector2.one, new Vector2(12f, 12f), new Vector2(-12f, -12f), out stampListContent);
            var preview = InsetPanel(stampOverlay, "StampPreviewPanel", SokobanTheme.Panel, 668f, Margin, 116f, 174f);
            stampPreviewBoard = InsetPanel(preview, "StampPreviewBoard", SokobanTheme.BoardBackground, 20f, 20f, 20f, 170f);
            stampSummary = LabelAt(preview, "StampSummary", "", 23, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 24f), new Vector2(1120f, 126f), TextAnchor.UpperLeft);
            stampSummary.supportRichText = false;
            ButtonAt(stampOverlay, "StampNew", "新建印章", Vector2.zero, Vector2.zero, new Vector2(Margin, 32f), new Vector2(240f, 54f), NewStampDocument, SokobanTheme.Accent);
            var fromSelection = ButtonAt(stampOverlay, "StampFromSelection", "从选区新建", Vector2.zero, Vector2.zero, new Vector2(Margin + 264f, 32f), new Vector2(240f, 54f), CreateStampFromSelection);
            fromSelection.interactable = Data != null && selection.Count > 0;
            stampUseButton = ButtonAt(stampOverlay, "StampUse", "使用印章", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin, 32f), new Vector2(240f, 54f), UseSelectedStamp, SokobanTheme.Accent);
            stampEditButton = ButtonAt(stampOverlay, "StampEdit", "编辑印章", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Margin - 264f, 32f), new Vector2(240f, 54f), EditSelectedStamp);
            stampLibraryStatus = LabelAt(stampOverlay, "StampLibraryStatus", "", 21, Vector2.zero, Vector2.zero, new Vector2(Margin, 102f), new Vector2(1848f, 50f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            ReloadStampLibrary(); RefreshStampTools();
            RecordOperation("打开印章库");
        }

        private void ReloadStampLibrary()
        {
            try
            {
                availableStamps = SokobanStampRepository.Store.ReadAll(out var problems);
                selectedStamp = availableStamps.FirstOrDefault(s => s.stampId == selectedStamp?.stampId) ?? availableStamps.FirstOrDefault();
                stampLibraryStatus.text = Data == null ? "请先打开关卡再使用印章；也可以新建或编辑印章。" : "选择印章后点击使用：左键连续落位，右键或 Esc 退出；透明格不覆盖。";
                if (problems.Count > 0) stampLibraryStatus.text = $"有 {problems.Count} 份印章未能读取：" + problems[0];
                SokobanUI.Ellipsize(stampLibraryStatus, stampLibraryStatus.text, "", 1816f);
            }
            catch (Exception error) { availableStamps.Clear(); selectedStamp = null; stampLibraryStatus.text = "无法读取印章库：" + error.Message; }
            RefreshStampLibrarySelection();
        }

        private void RefreshStampLibrarySelection()
        {
            foreach (Transform row in stampListContent) row.gameObject.SetActive(false);
            SokobanUI.DestroyChildren(stampListContent);
            if (availableStamps.Count == 0)
            {
                var empty = SokobanUI.Text(stampListContent, "StampLibraryEmpty", "暂无可用印章，点击下方新建印章。", 23, SokobanTheme.TextSecondary);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 90f;
            }
            foreach (var stamp in availableStamps)
            {
                var current = stamp; var chosen = stamp == selectedStamp;
                var button = SokobanUI.Button(stampListContent, "StampItem_" + stamp.stampId, stamp.name + "\n" + stamp.width + " × " + stamp.height + " · " + stamp.cells.Length + " 格", new Vector2(0f, 88f),
                    () => SelectStamp(current.stampId), chosen ? SokobanTheme.Accent : (Color?)null, chosen ? SokobanTheme.AccentText : (Color?)null);
                button.gameObject.AddComponent<LayoutElement>().preferredHeight = 88f;
                var label = button.GetComponentInChildren<UnityEngine.UI.Text>(); label.supportRichText = false; label.fontSize = 22;
                // Keep a long name on one row while retaining the dimensions underneath.
                var name = stamp.name;
                var probe = SokobanUI.Text(button.transform, "NameMeasure", "", 22, Color.clear);
                SokobanUI.Ellipsize(probe, name, "", 504f); name = probe.text; UnityEngine.Object.Destroy(probe.gameObject);
                label.text = name + "\n" + stamp.width + " × " + stamp.height + " · " + stamp.cells.Length + " 格";
            }
            stampUseButton.interactable = selectedStamp != null && Data != null;
            stampEditButton.interactable = selectedStamp != null;
            stampSummary.text = selectedStamp == null ? "新建后使用现有编辑区绘制并保存。" : selectedStamp.name + "\n" +
                $"尺寸 {selectedStamp.width} × {selectedStamp.height} · 绘制 {selectedStamp.cells.Length} 格 · 玩家 {selectedStamp.cells.Count(c => c.player)} 个\n" +
                "透明格不覆盖；绘制的地面格会清除原格内容。";
            // Bound the name independently so a long title cannot hide the usage instructions.
            if (selectedStamp != null)
            {
                var first = stampSummary.text.IndexOf('\n'); var suffix = stampSummary.text.Substring(first);
                var probe = SokobanUI.Text(stampSummary.transform, "StampSummaryMeasure", "", 23, Color.clear);
                SokobanUI.Ellipsize(probe, selectedStamp.name, "", 1100f); stampSummary.text = probe.text + suffix; UnityEngine.Object.Destroy(probe.gameObject);
            }
            UnityEngine.Canvas.ForceUpdateCanvases(); RenderStampPreview();
        }

        private void SelectStamp(string id)
        {
            selectedStamp = availableStamps.FirstOrDefault(s => s.stampId == id);
            RefreshStampLibrarySelection(); RecordOperation("选择印章：" + selectedStamp?.name);
        }

        private void RenderStampPreview()
        {
            foreach (Transform child in stampPreviewBoard) child.gameObject.SetActive(false);
            SokobanUI.DestroyChildren(stampPreviewBoard);
            if (selectedStamp == null) return;
            var stamp = selectedStamp;
            var step = Mathf.Min(96f, (stampPreviewBoard.rect.width - 24f) / stamp.width, (stampPreviewBoard.rect.height - 24f) / stamp.height);
            var lookup = stamp.cells.ToDictionary(c => new SokobanGridPoint(c.x, c.y));
            var grid = SokobanUI.Panel(stampPreviewBoard, "StampPreviewGrid", Color.clear, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            grid.sizeDelta = new Vector2(step * stamp.width, step * stamp.height);
            for (var y = 0; y < stamp.height; y++) for (var x = 0; x < stamp.width; x++)
            {
                lookup.TryGetValue(new SokobanGridPoint(x, y), out var cell);
                var color = cell == null ? ((x + y) % 2 == 0 ? SokobanTheme.BoardBackground : SokobanTheme.Field) : cell.wall ? SokobanTheme.BoardWall : cell.goal ? SokobanTheme.Goal : SokobanTheme.BoardFloor;
                var square = SokobanUI.Panel(grid, "StampPreviewCell_" + x + "_" + y, color, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
                square.pivot = new Vector2(0f, 1f); square.anchoredPosition = new Vector2(x * step, -y * step); square.sizeDelta = Vector2.one * Mathf.Max(1f, step - 2f);
                if (cell == null) continue;
                if (cell.goal) SokobanUI.GoalMarker(square);
                if (cell.box || cell.player)
                {
                    var obj = SokobanUI.Panel(square, "StampPreviewObject", cell.player ? SokobanTheme.Player : SokobanTheme.Box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                    obj.sizeDelta = Vector2.one * (step * 0.62f);
                }
            }
        }

        private void CloseStampLibrary()
        {
            if (stampOverlay != null) { stampOverlay.gameObject.SetActive(false); UnityEngine.Object.Destroy(stampOverlay.gameObject); }
            stampOverlay = null; RefreshStampTools();
            UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null);
        }

        private bool UpdateStampLibrary()
        {
            if (stampOverlay == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape)) CloseStampLibrary();
            return true;
        }

        private void UseSelectedStamp()
        {
            if (selectedStamp == null || Data == null) return;
            activeStamp = selectedStamp; stampCells = selectedStamp.AsBrush();
            CloseStampLibrary(); SetBrush(null);
            pasteMode = false; pasteOneShot = false; transparentBrush = false; stampMode = true;
            RefreshPasteButton(); RefreshStampTools(); RefreshGhost();
            SetDetail("印章「" + activeStamp.name + "」：鼠标对齐最上行最左侧绘制格；左键连续落位，右键或 Esc 退出，越界时整次拒绝。");
        }

        private void CommitStamp(int x, int y)
        {
            if (!stampMode || activeStamp == null || Data == null) return;
            if (!activeStamp.Fits(Width, Height, x, y)) { SetDetail("印章超出地图边界，请调整落位位置；未修改任何格子。"); return; }
            PushUndo();
            foreach (var item in stampCells) SetCell(x + item.Dx, y + item.Dy, item.Cell);
            MarkDirty(); RebuildGrid(); RefreshSelectionVisual(); RefreshGhost();
            SetDetail($"已应用印章「{activeStamp.name}」：({x}, {y})，{stampCells.Count} 格；可继续落位或撤销。");
        }

        private void NewStampDocument()
        {
            CloseStampLibrary();
            OpenStampDocument(new SokobanStamp { stampId = SokobanStamp.NewId(), name = "新建印章" }, false);
        }
        private void EditSelectedStamp()
        {
            if (selectedStamp == null) return;
            var stamp = selectedStamp; CloseStampLibrary(); OpenStampDocument(stamp, true);
        }
        private void OpenStampDocument(SokobanStamp stamp, bool saved)
        {
            var existing = tabs.FindIndex(t => t.IsStamp && t.Data.levelId == stamp.stampId);
            if (existing >= 0) { SelectTab(existing); return; }
            tabs.Add(stamp.CreateDocument(saved)); SetActiveTab(tabs.Count - 1);
            SetDetail("印章制作：与关卡编辑方式一致。透明格不覆盖；地面笔刷表示清空目标格。绘制后点击保存印章。");
            RecordOperation(saved ? "编辑印章：" + stamp.name : "新建印章：" + stamp.name);
        }

        private void CreateStampFromSelection()
        {
            if (Data == null || selection.Count == 0) return;
            var left = selection.Min(p => p.x); var top = selection.Min(p => p.y);
            var stamp = new SokobanStamp { stampId = SokobanStamp.NewId(), name = "选区印章", width = selection.Max(p => p.x) - left + 1, height = selection.Max(p => p.y) - top + 1,
                cells = selection.OrderBy(p => p.y).ThenBy(p => p.x).Where(p => !GetCell(p.x, p.y).Transparent).Select(p =>
                { var c = GetCell(p.x, p.y); return new SokobanStampCell { x = p.x - left, y = p.y - top, wall = c.Wall, goal = c.Goal, box = c.Box, player = c.Player }; }).ToArray() };
            if (stamp.cells.Length == 0) { stampLibraryStatus.text = "选区全部透明，请先绘制一些格子。"; return; }
            CloseStampLibrary(); OpenStampDocument(stamp, false);
        }

        private void ResizeStamp(int width, int height)
        {
            width = Mathf.Clamp(width, 1, 40); height = Mathf.Clamp(height, 1, 40);
            if (Width == width && Height == height) return;
            PushUndo(); var old = Data.terrain;
            Data.terrain = Enumerable.Range(0, height).Select(y => new string(Enumerable.Range(0, width).Select(x => y < old.Length && x < old[y].Length ? old[y][x] : '.').ToArray())).ToArray();
            Data.size.width = width; Data.size.height = height;
            Active.StampMask.RemoveWhere(p => p.x >= width || p.y >= height);
            Data.boxes = Data.boxes.Where(p => p.x < width && p.y < height).ToArray();
            Data.goals = Data.goals.Where(p => p.x < width && p.y < height).ToArray();
            if (Data.player.x >= width || Data.player.y >= height) Data.player = new SokobanJsonPoint(-1, -1);
            selection.Clear(); MarkDirty(); SyncInputsFromData(); RebuildGrid(); RefreshSelectionVisual(); RefreshStampDetail();
            RecordOperation($"调整印章尺寸：{width} × {height}");
        }

        private void RefreshStampDetail()
        {
            detailText.text = $"印章制作 · {(Active.Dirty ? "未保存" : "已保存")}\n尺寸：{Width} × {Height}\n绘制：{Active.StampMask.Count} 格\n透明：{Width * Height - Active.StampMask.Count} 格\n选中：{selection.Count}\n透明格不覆盖目标关卡。\n地面格会清除目标格内容。\n支持笔刷、填充、复制/剪切、\n粘贴、移动和撤销。\n印章不要求箱子与目标等量。";
        }
    }
}
