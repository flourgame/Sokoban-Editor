using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuluobishi.Sokoban;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban.Editor
{
    public sealed partial class SokobanEditorSceneController
    {
        private RectTransform exchangeOverlay;
        private CanvasGroup exchangeOwner;
        private bool exchangeOwnerWasInteractable;
        private InputField exchangeInput;
        private UnityEngine.UI.Text exchangeStatus;
        private Button exchangeConfirm;
        private List<SokobanExchangeRow> exchangePendingRows;
        private List<string[]> exchangeExportRows;
        private string exchangePreviewPath;
        private bool exchangeIsExport, exchangeOverwriteApproved;

        private bool UpdateExchange()
        {
            if (exchangeOverlay == null) return false;
            if (Input.GetKeyDown(KeyCode.Escape)) CloseExchange();
            return true;
        }

        private void OpenJsonImport()
        {
            if (exchangeOverlay != null || managerOverlay != null || stampOverlay != null || closeTabOverlay != null ||
                solverOverlay != null || generationOverlay != null || verificationOverlay != null || logOverlay != null) return;
            BeginExchange(false, "导入关卡 JSON", true);
            LabelAt(exchangeOverlay.Find("ExchangeDialog"), "ExchangeHint", "粘贴完整关卡 JSON，检查通过后打开为新页签；保存前不会写入关卡库。", 22,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -92f), new Vector2(1280f, 46f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            exchangeInput = ExchangeInput("JsonImportText", "粘贴关卡 JSON…", new Vector2(1280f, 380f), new Vector2(0f, -148f));
            exchangeInput.lineType = InputField.LineType.MultiLineNewline;
            exchangeInput.characterLimit = 1024 * 1024;
            exchangeInput.textComponent.fontSize = 20;
            exchangeInput.textComponent.alignment = TextAnchor.UpperLeft;
            exchangeInput.textComponent.supportRichText = false;
            exchangeInput.onValueChanged.AddListener(_ => exchangeStatus.text = "内容已更新，点击“检查并打开”导入为新关卡。");
            ButtonAt(exchangeOverlay.Find("ExchangeDialog"), "JsonImportPaste", "粘贴剪贴板", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(60f, 32f), new Vector2(240f, 54f), () => exchangeInput.text = GUIUtility.systemCopyBuffer);
            exchangeConfirm = ButtonAt(exchangeOverlay.Find("ExchangeDialog"), "JsonImportConfirm", "检查并打开", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-330f, 32f), new Vector2(240f, 54f), ConfirmJsonImport, SokobanTheme.Accent);
            exchangeStatus.text = "支持本项目“复制 JSON”输出的关卡格式；尺寸为 3–40，箱子数须等于目标数。";
            RecordOperation("打开 JSON 导入");
        }

        private void ConfirmJsonImport()
        {
            try
            {
                var level = SokobanLevelExchange.ParseImport(exchangeInput.text);
                level.levelId = SokobanLevelExchange.NewId();
                CloseExchange();
                tabs.Add(new SokobanEditorTab { Data = level, Dirty = true });
                SetActiveTab(tabs.Count - 1);
                SetDetail("已从 JSON 打开新关卡，点击保存加入关卡库。");
                RecordOperation("导入 JSON：" + level.name);
            }
            catch (Exception error) { exchangeStatus.text = "导入失败，内容已保留：\n" + error.Message; }
        }

        private void OpenXlsxExport(bool selectedOnly)
        {
            if (exchangeOverlay != null) return;
            var selected = managerVisible.Where(l => !selectedOnly || managerSelection.Selected.Contains(l.LevelId)).ToList();
            if (selected.Count == 0) { managerStatus.text = "没有可导出的关卡。"; return; }
            try { exchangeExportRows = SokobanLevelExchange.ExportRows(selected); }
            catch (Exception error) { managerStatus.text = "导出准备失败：" + error.Message; return; }
            BeginExchange(true, "导出 XLSX · " + selected.Count + " 个关卡", false);
            exchangeIsExport = true;
            BuildExchangePath(true);
            exchangeConfirm = ButtonAt(exchangeOverlay.Find("ExchangeDialog"), "XlsxExportConfirm", "导出文件", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-330f, 32f), new Vector2(240f, 54f), ConfirmXlsxExport, SokobanTheme.Accent);
            exchangeStatus.text = "导出范围：" + (selectedOnly ? "已选关卡" : "当前分类") + "\n包含 ID、名称、分类、顺序、尺寸、箱子数、推箱次数、验证步数和配置JSON。\n导出的是已保存内容，未保存页签请先保存。\n\n" + string.Join("\n", selected.Take(12).Select(l => l.DisplayTitle));
        }

        private void ConfirmXlsxExport()
        {
            try
            {
                var path = CheckedExchangePath();
                if (File.Exists(path) && !exchangeOverwriteApproved)
                {
                    exchangeOverwriteApproved = true;
                    SetButtonLabel(exchangeConfirm, "确认覆盖并导出");
                    exchangeStatus.text = "此文件已存在：\n" + path + "\n再次点击确认覆盖，或更改路径。";
                    return;
                }
                SokobanXlsx.Write(path, exchangeExportRows);
                var message = "已导出 " + (exchangeExportRows.Count - 1) + " 个关卡：" + path;
                CloseExchange(); managerStatus.text = message; RecordManagement(message);
            }
            catch (Exception error) { exchangeStatus.text = "导出失败：\n" + error.Message; }
        }

        private void OpenXlsxImport()
        {
            if (exchangeOverlay != null) return;
            BeginExchange(true, "导入关卡 XLSX", false);
            BuildExchangePath(false);
            ButtonAt(exchangeOverlay.Find("ExchangeDialog"), "XlsxImportPreview", "读取并预览", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(60f, 32f), new Vector2(240f, 54f), PreviewXlsxImport);
            exchangeConfirm = ButtonAt(exchangeOverlay.Find("ExchangeDialog"), "XlsxImportConfirm", "导入为新关卡", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-330f, 32f), new Vector2(240f, 54f), ConfirmXlsxImport, SokobanTheme.Accent);
            exchangeConfirm.interactable = false;
            exchangeStatus.text = "读取第一张工作表，第一行须包含“配置JSON”列。\n可使用本项目导出的表格，也可只提供“名称”和“配置JSON”。\n名称列优先于 JSON 中的名称；分类缺省为 Generated，顺序缺省为行顺序。\n先检查整表，再导入为新 ID，不覆盖现有内容，不改动分类公开设置。";
        }

        private void PreviewXlsxImport()
        {
            exchangePendingRows = null; exchangeConfirm.interactable = false;
            try
            {
                exchangePreviewPath = CheckedExchangePath();
                exchangePendingRows = SokobanLevelExchange.ParseRows(SokobanXlsx.Read(exchangePreviewPath));
                exchangeStatus.text = "检查通过，共 " + exchangePendingRows.Count + " 个关卡。将按顺序追加为新关卡：\n\n" +
                    string.Join("\n", exchangePendingRows.Take(18).Select(r => "第 " + r.RowNumber + " 行 · " + r.Level.name + " · " + r.Category + " · " + r.Level.size.width + "×" + r.Level.size.height)) +
                    (exchangePendingRows.Count > 18 ? "\n……" : "") + "\n\n点击“导入为新关卡”完成保存。";
                exchangeConfirm.interactable = true;
            }
            catch (Exception error) { exchangeStatus.text = "读取失败，未导入任何关卡：\n" + error.Message; }
        }

        private void ConfirmXlsxImport()
        {
            if (exchangePendingRows == null || exchangePendingRows.Count == 0) return;
            try
            {
                // Use the reviewed snapshot, even if Excel changes the file after preview.
                var imported = SokobanLevelExchange.SaveNew(exchangePendingRows, SokobanLevelRepository.GeneratedRoot,
                    managerStore, SokobanLevelRepository.ListAll());
                CloseExchange();
#if UNITY_EDITOR
                UnityEditor.AssetDatabase.Refresh();
#endif
                managerFilter = "All";
                managerAll = SokobanLevelRepository.ListAll().ToList();
                RefreshManagerView();
                managerSelection.SelectAll(imported.Select(l => l.LevelId).ToList());
                RefreshManagerSelection(); NotifyLibraryChanged();
                managerStatus.text = "已导入 " + imported.Count + " 个新关卡，可点击“打开编辑”查看。";
                RecordManagement(managerStatus.text);
            }
            catch (Exception error) { if (exchangeStatus != null) exchangeStatus.text = "导入失败：\n" + error.Message; else managerStatus.text = "刷新失败，请点击刷新：" + error.Message; }
        }

        private void BeginExchange(bool manager, string title, bool json)
        {
            CancelManagerDrag(); CloseManagerTarget();
            exchangeIsExport = false; exchangeOverwriteApproved = false;
            exchangePendingRows = null; exchangePreviewPath = null;
            var parent = manager ? managerOverlay : Root;
            // Unity may return a managed wrapper for a missing component in the Editor.
            // Use Unity's overloaded null comparison rather than the C# ?? operator.
            exchangeOwner = parent.GetComponent<CanvasGroup>();
            if (exchangeOwner == null) exchangeOwner = parent.gameObject.AddComponent<CanvasGroup>();
            exchangeOwnerWasInteractable = exchangeOwner.interactable; exchangeOwner.interactable = false;
            EventSystem.current?.SetSelectedGameObject(null);
            boxSelecting = false; panning = false; moveArmed = false; ClearMarquee(); ClearGhost();
            exchangeOverlay = SokobanUI.Panel(parent, "ExchangeOverlay", new Color(0f, 0f, 0f, 0.82f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            exchangeOverlay.gameObject.AddComponent<CanvasGroup>().ignoreParentGroups = true;
            var panel = SokobanUI.Panel(exchangeOverlay, "ExchangeDialog", SokobanTheme.Panel,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            panel.sizeDelta = new Vector2(1400f, 850f);
            LabelAt(panel, "ExchangeTitle", title, 32, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -28f), new Vector2(1280f, 54f), TextAnchor.MiddleLeft);
            var statusPanel = InsetPanel(panel, "ExchangeStatusPanel", SokobanTheme.Field, 60f, 60f, json ? 548f : 230f, 116f);
            SokobanUI.ScrollList(statusPanel, "ExchangeStatusScroll", Vector2.zero, Vector2.one, new Vector2(16f, 12f), new Vector2(-16f, -12f), out var statusContent);
            exchangeStatus = SokobanUI.Text(statusContent, "ExchangeStatus", "", 22, SokobanTheme.TextSecondary, TextAnchor.UpperLeft);
            exchangeStatus.supportRichText = false;
            ButtonAt(panel, "ExchangeCancel", "返回", new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-60f, 32f), new Vector2(240f, 54f), CloseExchange);
        }

        private InputField ExchangeInput(string name, string placeholder, Vector2 size, Vector2 position)
        {
            var input = SokobanUI.InputField(exchangeOverlay.Find("ExchangeDialog"), name, "", placeholder, size);
            var rect = input.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = position;
            return input;
        }

        private void BuildExchangePath(bool save)
        {
            LabelAt(exchangeOverlay.Find("ExchangeDialog"), "ExchangePathLabel", "XLSX 文件完整路径", 22,
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(1280f, 40f), TextAnchor.MiddleLeft, SokobanTheme.TextSecondary);
            exchangeInput = ExchangeInput("XlsxPath", "选择文件或输入完整路径…", new Vector2(1010f, 54f), new Vector2(-135f, -152f));
            exchangeInput.characterLimit = 4096;
            var directory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(directory)) directory = Application.persistentDataPath;
            exchangeInput.SetTextWithoutNotify(Path.Combine(directory, "Sokoban-Levels.xlsx"));
            exchangeInput.onValueChanged.AddListener(_ =>
            {
                exchangeOverwriteApproved = false;
                if (exchangeIsExport) { if (exchangeConfirm != null) SetButtonLabel(exchangeConfirm, "导出文件"); }
                else { exchangePendingRows = null; if (exchangeConfirm != null) exchangeConfirm.interactable = false; }
            });
            ButtonAt(exchangeOverlay.Find("ExchangeDialog"), "XlsxBrowse", save ? "选择保存位置" : "选择 XLSX", Vector2.one, Vector2.one,
                new Vector2(-60f, -152f), new Vector2(240f, 54f), () =>
                {
                    try { var path = SokobanExchangeFileDialog.Choose(save, exchangeInput.text); if (!string.IsNullOrWhiteSpace(path)) exchangeInput.text = path; }
                    catch (Exception error) { exchangeStatus.text = error.Message; }
                });
        }

        private string CheckedExchangePath()
        {
            var path = (exchangeInput.text ?? "").Trim().Trim('"');
            if (path.Length == 0 || !Path.IsPathRooted(path)) throw new InvalidDataException("请填写 XLSX 文件的完整路径。");
            if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("文件扩展名须为 .xlsx。");
            return Path.GetFullPath(path);
        }

        private void CloseExchange()
        {
            if (exchangeOverlay == null) return;
            exchangeOverlay.gameObject.SetActive(false); UnityEngine.Object.Destroy(exchangeOverlay.gameObject);
            exchangeOverlay = null; exchangeInput = null; exchangeStatus = null; exchangeConfirm = null;
            exchangePendingRows = null; exchangeExportRows = null; exchangePreviewPath = null;
            if (exchangeOwner != null) exchangeOwner.interactable = exchangeOwnerWasInteractable;
            exchangeOwner = null;
            EventSystem.current?.SetSelectedGameObject(null);
        }
    }
}
