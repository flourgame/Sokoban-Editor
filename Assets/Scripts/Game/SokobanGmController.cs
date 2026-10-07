using System;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace Kuluobishi.Sokoban
{
    /// <summary>Persistent top-right dropdown; only its own controls consume player input.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SokobanGmController : MonoBehaviour
    {
        private static SokobanGmController current;
        private static int inputFrame = -1;
        private static int escapeFrame = -1;
        private RectTransform overlay, dialog, commands, confirmation, clearBackdrop;
        private Button header;
        private Text status;
        private Button previous, next, forceWin, restart;
        private const float HeaderHeight = 40f, MenuWidth = 252f, CommandsHeight = 400f;
        private const string ClearMessage = "解锁进度、完成记录和最佳成绩将重置。\n关卡和印章文件保留。";
        public bool IsExpanded { get; private set; }
        // escapeFrame：按下 Esc 的当帧不让 GM 拦截，保证场景必定能收到"返回上一界面"。
        public static bool BlocksInput => SokobanSettingsPanel.BlocksInput || inputFrame == Time.frameCount || current != null && SokobanRuntimeContext.IsGmEnabled && escapeFrame != Time.frameCount &&
            (current.confirmation.gameObject.activeInHierarchy || RectTransformUtility.RectangleContainsScreenPoint(current.dialog, SokobanCrtOverlay.ScreenToSource(Input.mousePosition), null) ||
             EventSystem.current?.currentSelectedGameObject != null && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(current.dialog));
        public static SokobanGmController Current => current != null ? current : Install();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { current = null; inputFrame = -1; escapeFrame = -1; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() { Install(); }
        private static SokobanGmController Install()
        {
            if (current != null) return current;
            var canvas = SokobanUI.CreateCanvas("SokobanGmCanvas", 700);
            current = canvas.gameObject.AddComponent<SokobanGmController>();
            return current;
        }
        private void Awake()
        {
            current = this; DontDestroyOnLoad(gameObject);
            overlay = SokobanUI.Panel(transform, "GMOverlay", Color.clear, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            overlay.GetComponent<Image>().raycastTarget = false;
            dialog = SokobanUI.Panel(overlay, "GMPanel", SokobanTheme.GmPanel, Vector2.one, Vector2.one, Vector2.zero, Vector2.zero);
            dialog.pivot = Vector2.one; dialog.anchoredPosition = new Vector2(-16f, -16f);
            header = Button(dialog, "GmDropdown", "GM  ▼", 0f, 0f, () => SetExpanded(!IsExpanded));
            var headerRect = header.GetComponent<RectTransform>();
            headerRect.anchorMin = new Vector2(0f, 1f); headerRect.anchorMax = Vector2.one;
            headerRect.offsetMin = new Vector2(0f, -HeaderHeight); headerRect.offsetMax = Vector2.zero;
            commands = SokobanUI.Panel(dialog, "GmCommands", Color.clear, new Vector2(0f, 1f), Vector2.one, Vector2.zero, Vector2.zero);
            commands.pivot = new Vector2(0.5f, 1f);
            commands.offsetMin = new Vector2(8f, -HeaderHeight - CommandsHeight - 4f);
            commands.offsetMax = new Vector2(-8f, -HeaderHeight - 4f);
            commands.GetComponent<Image>().raycastTarget = false;
            Command("GmEditor", "关卡编辑器", 0, () => Navigate("editor"));
            Command("GmAllLevels", "查看所有关卡", 1, () => ShowLevels(true));
            Command("GmPlayerLevels", "查看玩家关卡", 2, () => ShowLevels(false));
            Command("GmMenu", "返回主界面", 3, () => Navigate("start"));
            previous = Command("GmPrevious", "上一关", 4, () => Jump(-1));
            next = Command("GmNext", "下一关", 5, () => Jump(1));
            forceWin = Command("GmForceWin", "强制胜利", 6, () => Game()?.GmForceWin());
            restart = Command("GmRestart", "重开当前关", 7, () => Game()?.GmRestart());
            Command("GmUnlockAll", "一键解锁所有关卡", 8, UnlockAll);
            Command("GmClearData", "清空玩家数据", 9, OpenClear);
            status = Label(commands, "GmStatus", "", 18, new Vector2(0f, -CommandsHeight - 24f), new Vector2(MenuWidth - 16f, 48f));
            status.rectTransform.anchorMin = status.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            status.supportRichText = false;
            clearBackdrop = SokobanUI.Panel(overlay, "GmClearBackdrop", new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            confirmation = SokobanUI.Panel(clearBackdrop, "GmClearConfirmation", SokobanTheme.GmPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260f, -150f), new Vector2(260f, 150f));
            Label(confirmation, "GmClearTitle", "清空玩家数据？", 26, new Vector2(0f, 100f), new Vector2(472f, 60f));
            Label(confirmation, "GmClearHint", ClearMessage, 22, new Vector2(0f, 14f), new Vector2(472f, 112f));
            Button(confirmation, "GmConfirmClear", "确认清空", -126f, -102f, ClearData);
            Button(confirmation, "GmCancelClear", "取消", 126f, -102f, CancelClear);
            clearBackdrop.gameObject.SetActive(false);
            overlay.gameObject.SetActive(SokobanRuntimeContext.IsGmEnabled);
            ApplyLayout();
            SceneManager.sceneLoaded += OnSceneLoaded;
            RefreshCommands();
        }
        private static Text Label(Transform parent, string name, string text, int size, Vector2 position, Vector2 dimensions)
        {
            var label = SokobanUI.Text(parent, name, text, size, SokobanTheme.TextPrimary, TextAnchor.MiddleCenter);
            Position(label.rectTransform, position, dimensions); label.raycastTarget = false; return label;
        }
        private static Button Button(Transform parent, string name, string text, float x, float y, Action action)
        {
            var button = SokobanUI.Button(parent, name, text, new Vector2(MenuWidth - 16f, 36f), action);
            Position(button.GetComponent<RectTransform>(), new Vector2(x, y), new Vector2(MenuWidth - 16f, 36f));
            var label = button.GetComponentInChildren<Text>(); label.raycastTarget = false;
            label.fontSize = label.resizeTextMaxSize = 20; label.resizeTextMinSize = 16;
            return button;
        }
        private Button Command(string name, string text, int row, Action action)
        {
            var button = Button(commands, name, text, 0f, -18f - row * 40f, action);
            var rect = button.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            return button;
        }
        private static void Position(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
        private static SokobanGameplaySceneController Game() => FindObjectOfType<SokobanGameplaySceneController>();
        private void OnDestroy() { SceneManager.sceneLoaded -= OnSceneLoaded; if (current == this) current = null; }
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        { SokobanUI.EnsureEventSystem(); RefreshCommands(); if (SokobanRuntimeContext.IsGmEnabled) EventSystem.current?.SetSelectedGameObject(null); }
        private void RefreshCommands()
        {
            var playable = Game()?.CanUseGmGameplay == true;
            previous.interactable = next.interactable = playable;
            forceWin.interactable = restart.interactable = playable;
        }
        public void SetOpen(bool opened, bool expanded = true)
        {
            SokobanRuntimeContext.IsGmEnabled = opened; inputFrame = Time.frameCount;
            IsExpanded = opened && expanded;
            overlay.gameObject.SetActive(opened); CancelClear(); RefreshCommands(); ApplyLayout();
            EventSystem.current?.SetSelectedGameObject(null);
        }
        public void Toggle() => SetOpen(!SokobanRuntimeContext.IsGmEnabled, false);
        public void SetExpanded(bool expanded)
        {
            if (!SokobanRuntimeContext.IsGmEnabled) return;
            IsExpanded = expanded; inputFrame = Time.frameCount; CancelClear(); RefreshCommands(); ApplyLayout();
            EventSystem.current?.SetSelectedGameObject(null);
        }
        private void ApplyLayout()
        {
            var statusHeight = string.IsNullOrEmpty(status.text) ? 0f : Mathf.Clamp(status.preferredHeight + 8f, 36f, 120f);
            status.gameObject.SetActive(statusHeight > 0f);
            status.rectTransform.sizeDelta = new Vector2(MenuWidth - 16f, statusHeight);
            status.rectTransform.anchoredPosition = new Vector2(0f, -CommandsHeight - statusHeight * 0.5f);
            commands.gameObject.SetActive(IsExpanded);
            dialog.sizeDelta = IsExpanded ? new Vector2(MenuWidth, HeaderHeight + CommandsHeight + 12f + statusHeight) : new Vector2(132f, HeaderHeight);
            header.GetComponentInChildren<Text>().text = IsExpanded ? "GM  ▲" : "GM  ▼";
        }
        private void SetStatus(string message)
        {
            status.text = message;
            ApplyLayout();
        }
        private void Update()
        {
            if (SokobanRuntimeContext.IsQuitConfirmationOpen || SokobanSettingsPanel.BlocksInput) return;
            if (!SokobanUI.IsEditingText() && Input.GetKeyDown(KeyCode.Equals)) { Toggle(); return; }
            if (SokobanRuntimeContext.IsGmEnabled && Input.GetKeyDown(KeyCode.Escape))
            {
                // 清空确认是 GM 自己的模态，Esc 取消它（这本身也是"返回"）。
                if (clearBackdrop.gameObject.activeSelf) { CancelClear(); return; }
                // 其余情况 Esc 归场景所有：释放 GM 焦点且本帧不再拦截，交给场景执行"返回上一界面"。
                escapeFrame = Time.frameCount; EventSystem.current?.SetSelectedGameObject(null);
            }
            if (Input.GetMouseButtonDown(0)) CollapseIfPointerOutside(SokobanCrtOverlay.ScreenToSource(Input.mousePosition));
        }

        /// <summary>
        /// 展开状态下，指针落在面板以外的位置时收起。清空确认是独立模态，期间不触发。
        /// 收起会置 inputFrame，使这一次点击不会同时作用到下层界面。
        /// </summary>
        private bool CollapseIfPointerOutside(Vector2 screenPosition)
        {
            if (!IsExpanded || clearBackdrop.gameObject.activeSelf) return false;
            if (RectTransformUtility.RectangleContainsScreenPoint(dialog, screenPosition, null)) return false;
            SetExpanded(false);
            return true;
        }
        private void Navigate(string scene)
        {
            if (scene == "editor") { SokobanRuntimeContext.IsDebugPlay = false; SokobanRuntimeContext.IsEditorPreview = false; }
            inputFrame = Time.frameCount; EventSystem.current?.SetSelectedGameObject(null);
            SceneManager.LoadScene(scene);
        }
        private void ShowLevels(bool all)
        {
            SokobanRuntimeContext.ShowAllLevels = all;
            SokobanRuntimeContext.IsDebugPlay = SokobanRuntimeContext.IsEditorPreview = false;
            SetStatus("");
            Navigate("level");
        }
        private void Jump(int offset)
        {
            var levels = SokobanLevelRepository.ListAll().Where(d =>
            { var data = SokobanLevelRepository.LoadJson(d); return SokobanValidation.Validate(data).Count == 0 && data.solution?.status != "Unsolvable"; }).ToList();
            if (levels.Count == 0) return;
            var index = levels.FindIndex(d => d.LevelId == SokobanRuntimeContext.SelectedLevelId);
            SokobanRuntimeContext.SelectedLevelId = levels[(Mathf.Max(0, index) + offset + levels.Count) % levels.Count].LevelId;
            SokobanRuntimeContext.IsEditorPreview = false; SokobanRuntimeContext.IsDebugPlay = true;
            Navigate("game");
        }
        private void UnlockAll()
        {
            var ids = SokobanLevelRepository.ListAll().Select(d => d.LevelId).ToList();
            SetStatus(SokobanProgressStore.Current.UnlockAll(ids) ? $"已解锁 {ids.Count} 个关卡" : SokobanProgressStore.Current.LastError);
        }
        private void OpenClear()
        {
            inputFrame = Time.frameCount;
            confirmation.Find("GmClearHint").GetComponent<Text>().text = ClearMessage;
            clearBackdrop.gameObject.SetActive(true); EventSystem.current?.SetSelectedGameObject(null);
        }
        private void CancelClear() { inputFrame = Time.frameCount; clearBackdrop.gameObject.SetActive(false); EventSystem.current?.SetSelectedGameObject(null); }
        private void ClearData()
        {
            if (!SokobanProgressStore.Current.Clear()) { confirmation.Find("GmClearHint").GetComponent<Text>().text = SokobanProgressStore.Current.LastError; return; }
            CancelClear(); SetStatus("玩家数据已清空");
            SokobanRuntimeContext.ShowAllLevels = false;
            SokobanRuntimeContext.IsDebugPlay = SokobanRuntimeContext.IsEditorPreview = false;
            Navigate("level");
        }
    }
}
