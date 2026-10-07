using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    public sealed partial class SokobanGameplaySceneController : SokobanSceneController
    {
        [SerializeField] private SokobanGameplaySceneRefs refs = new SokobanGameplaySceneRefs();

        private SokobanLevelRuntime level;
        private SokobanState state;
        private readonly Stack<SokobanState> undo = new Stack<SokobanState>();
        private UnityEngine.UI.Text stats;
        private UnityEngine.UI.Text message;
        private SokobanBoardView gameBoard;
        [SerializeField, Min(1)] private int fixedViewMaxWidth = SokobanBoardView.DefaultFixedWidth;
        [SerializeField, Min(1)] private int fixedViewMaxHeight = SokobanBoardView.DefaultFixedHeight;
        private RectTransform winPanel;
        private List<SokobanLevelDescriptor> allLevels;
        private List<string> campaignIds;
        private CanvasGroup interaction;
        private RectTransform pauseOverlay;
        private RectTransform mapOverlay;
        private float elapsedSeconds;
        private bool gmCompleted, resultRecorded;
        private string scoreMessage;
        private Button nextButton;

        private bool IsPauseOpen => pauseOverlay != null && pauseOverlay.gameObject.activeSelf;
        private bool IsMapOpen => mapOverlay != null && mapOverlay.gameObject.activeSelf;
        private bool IsWinOpen => winPanel != null && winPanel.gameObject.activeSelf;

        /// <summary>外壳已在 game.unity 烘焙：这里只做绑定，不再运行时创建。</summary>
        protected override void CreateShell()
        {
            if (refs == null || refs.canvas == null || refs.root == null)
            {
                Debug.LogError("game 场景缺少烘焙的界面外壳：请运行菜单 Sokoban/Bake Game Scene 重新生成。");
                base.CreateShell();
                return;
            }
            Canvas = refs.canvas;
            Root = refs.root;
            interaction = refs.interaction;
            stats = refs.stats;
            message = refs.message;
            gameBoard = refs.boardView;
            winPanel = refs.winPanel;
            pauseOverlay = refs.pauseOverlay;
            mapOverlay = refs.mapOverlay;
            nextButton = refs.nextButton;
            SokobanUI.EnsureEventSystem();
        }

        protected override void Awake()
        {
            base.Awake();
            if (interaction == null && Root != null) interaction = Root.gameObject.AddComponent<CanvasGroup>();
            campaignIds = SokobanCampaign.Ids();
            allLevels = SokobanLevelRepository.ListAll().Where(d =>
            {
                if (!SokobanRuntimeContext.IsDebugPlay && !campaignIds.Contains(d.LevelId)) return false;
                var data = SokobanLevelRepository.LoadJson(d);
                return data != null && SokobanValidation.Validate(data).Count == 0 && data.solution?.status != "Unsolvable";
            }).ToList();
            var source = SokobanLevelRepository.LoadJson(SokobanRuntimeContext.SelectedLevelId);
            var errors = SokobanValidation.Validate(source);
            var locked = errors.Count == 0 && !SokobanRuntimeContext.IsEditorPreview && !SokobanRuntimeContext.IsDebugPlay &&
                (!campaignIds.Contains(source.levelId) || !SokobanProgressStore.Current.IsUnlocked(source.levelId, campaignIds));
            if (errors.Count > 0 || source?.solution?.status == "Unsolvable" || locked)
            {
                ShowError(locked ? "关卡尚未解锁，请先完成前面的玩家关卡。" : source?.solution?.status == "Unsolvable" ? "关卡已确认无解，请返回选关。" : string.Join("\n", errors.Take(4)));
                return;
            }
            level = new SokobanLevelRuntime(source);

            state = level.CreateInitialState();
            // 开局检测一次：若所有箱子已就位，则 0 步完成关卡。
            state.RefreshWin(level);
            if (refs.hud != null) refs.hud.gameObject.SetActive(true);
            if (refs.title != null)
            {
                refs.title.supportRichText = false;
                SokobanUI.Ellipsize(refs.title, level.Name.Replace('\n', ' ').Replace('\r', ' '),
                    SokobanRuntimeContext.IsEditorPreview ? " · 试玩" : SokobanRuntimeContext.IsDebugPlay ? " · GM 游玩" : "", 1800f);
            }
            if (refs.viewMode != null)
                refs.viewMode.text = (gameBoard != null && gameBoard.IsFollowing ? "跟随视野" : "固定视野") + " · M 查看完整地图";
            if (gameBoard != null) gameBoard.Initialize(level, state, true, fixedViewMaxWidth, fixedViewMaxHeight);
            UpdateView(true);
        }

        private void ShowError(string detail)
        {
            if (refs.hud != null) refs.hud.gameObject.SetActive(false);
            if (refs.errorPanel != null) refs.errorPanel.gameObject.SetActive(true);
            if (refs.errorText != null) refs.errorText.text = detail;
        }

        private void UpdateView(bool snapCamera = false)
        {
            if (gameBoard != null) gameBoard.SetState(state, snapCamera, lastDirection);
            UpdateStats();
            if (state.IsWon) ShowWin();
        }

        private SokobanDirection? lastDirection;

        private void TryMove(SokobanDirection direction)
        {
            if (state == null || state.IsWon || IsMapOpen || IsPauseOpen || SokobanGmController.BlocksInput || SokobanSceneTransition.IsRunning) return;
            var previous = state.Clone();
            var result = SokobanSimulation.TryMove(level, state, direction);
            if (!result.Accepted)
            {
                message.text = result.Reason;
                return;
            }
            undo.Push(previous);
            lastDirection = direction;
            message.text = result.Pushed ? "推动箱子" : "";
            UpdateView();
        }

        private void UndoMove()
        {
            if (undo.Count == 0 || IsMapOpen || IsPauseOpen || SokobanGmController.BlocksInput) return;
            state = undo.Pop();
            lastDirection = null;
            resultRecorded = false; SetInteraction(true);
            if (winPanel != null) winPanel.gameObject.SetActive(false);
            UpdateView();
        }

        private void Restart()
        {
            if (level == null || IsMapOpen) return;
            ClosePause(); SetInteraction(true);
            state = level.CreateInitialState();
            state.RefreshWin(level);
            undo.Clear();
            lastDirection = null;
            elapsedSeconds = 0f; gmCompleted = resultRecorded = false; scoreMessage = null;
            if (winPanel != null) winPanel.gameObject.SetActive(false);
            message.text = "已重开";
            UpdateView(true);
        }

        private void ShowWin()
        {
            if (!resultRecorded)
            {
                resultRecorded = true;
                scoreMessage = gmCompleted || SokobanRuntimeContext.IsDebugPlay ? "GM 完成 · 不计入成绩" : SokobanRuntimeContext.IsEditorPreview ? "编辑试玩 · 不计入成绩" :
                    SokobanProgressStore.Current.RecordWin(level.Source, state.MoveCount, state.PushCount, campaign: campaignIds) ? "成绩与解锁进度已保存" : SokobanProgressStore.Current.LastError;
            }
            SetInteraction(false);
            if (winPanel == null) return;
            if (refs.winTitle != null) refs.winTitle.text = gmCompleted || SokobanRuntimeContext.IsDebugPlay ? "GM 完成" : "通关！";
            if (refs.winStats != null) refs.winStats.text = WinStats();
            if (nextButton != null)
            {
                var label = nextButton.GetComponentInChildren<UnityEngine.UI.Text>();
                if (label != null) label.text = SokobanRuntimeContext.IsEditorPreview ? "返回编辑器" : NextDescriptor() != null ? "下一关" : "返回选关";
            }
            winPanel.gameObject.SetActive(true);
        }

        private string WinStats() => $"步数：{state.MoveCount}    推箱：{state.PushCount}\n用时：{elapsedSeconds:0.0} 秒\n{scoreMessage}";
        private SokobanLevelDescriptor NextDescriptor()
        {
            if (level == null || allLevels == null) return null;
            var current = allLevels.FirstOrDefault(d => d.LevelId == level.LevelId);
            if (current == null) return null;
            var next = allLevels.Skip(allLevels.IndexOf(current) + 1)
                .FirstOrDefault(d => !SokobanRuntimeContext.IsDebugPlay || d.Source == current.Source);
            return next != null && (SokobanRuntimeContext.IsDebugPlay || SokobanProgressStore.Current.IsUnlocked(next.LevelId, campaignIds)) ? next : null;
        }

        private void NextLevel()
        {
            var next = NextDescriptor();
            if (next == null) { GoTo("level"); return; }
            SokobanRuntimeContext.SelectedLevelId = next.LevelId;
            GoTo("game");
        }

        public bool CanUseGmGameplay => level != null && state != null;
        public void GmForceWin() => ForceWin();
        public void GmRestart() => Restart();

        private void ForceWin()
        {
            if (level == null || state == null || level.Goals.Count != state.Boxes.Count) return;
            gmCompleted = true; resultRecorded = false;
            state = new SokobanState(state.Player, level.Goals);
            state.ForceWin(level);
            message.text = "GM 已强制胜利";
            UpdateView();
        }

        // ---- 烘焙按钮的回调入口（在 Inspector 里以 UnityEvent 持久化绑定）----
        public void OnUndoClicked() => UndoMove();
        public void OnRestartClicked() => Restart();
        public void OnLevelClicked() => GoTo("level");
        public void OnMapClicked() => OpenMap();
        public void OnPauseClicked() => OpenPause();
        public void OnMapCloseClicked() => CloseMap();
        public void OnErrorBackClicked() => GoTo("level");
        public void OnWinNextClicked()
        {
            if (SokobanRuntimeContext.IsEditorPreview) GoTo("editor");
            else NextLevel();
        }
        public void OnWinReplayClicked() => Restart();
        public void OnWinBackClicked() => GoTo("level");
        public void OnPauseResumeClicked() => ClosePause();
        public void OnPauseRestartClicked() => Restart();
        public void OnPauseSettingsClicked() => SokobanSettingsPanel.Show();
        public void OnPauseLevelsClicked() => GoTo("level");

        [Header("长按连发")]
        [Tooltip("按下方向键立即走一步；继续按住这么久之后开始连发。")]
        [SerializeField, Min(0f)] private float moveRepeatDelay = 0.28f;
        [Tooltip("连发期间每多久走一步。略大于实体补间时长时观感为连续滑动。")]
        [SerializeField, Min(0.02f)] private float moveRepeatInterval = 0.13f;
        private readonly List<SokobanDirection> pressedDirections = new List<SokobanDirection>();
        private SokobanDirection? repeatingDirection;
        private float moveRepeatTimer;

        private static readonly KeyCode[] DirectionKeys =
        {
            KeyCode.UpArrow, KeyCode.W, KeyCode.DownArrow, KeyCode.S,
            KeyCode.LeftArrow, KeyCode.A, KeyCode.RightArrow, KeyCode.D
        };

        private static SokobanDirection? DirectionOf(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.UpArrow: case KeyCode.W: return SokobanDirection.Up;
                case KeyCode.DownArrow: case KeyCode.S: return SokobanDirection.Down;
                case KeyCode.LeftArrow: case KeyCode.A: return SokobanDirection.Left;
                case KeyCode.RightArrow: case KeyCode.D: return SokobanDirection.Right;
                default: return null;
            }
        }

        /// <summary>
        /// 每帧最先消费方向键的按下/抬起，保证暂停、地图、结算等提前 return 的分支里
        /// 也不会漏掉 GetKeyUp（漏掉会导致松开后仍"幽灵连发"）。
        /// 列表顺序即按下顺序，末尾为"最后按下的方向"。
        /// </summary>
        private void TrackDirectionKeys()
        {
            foreach (var key in DirectionKeys)
            {
                var direction = DirectionOf(key);
                if (!direction.HasValue) continue;
                if (Input.GetKeyDown(key))
                {
                    if (!pressedDirections.Contains(direction.Value)) pressedDirections.Add(direction.Value);
                }
                else if (Input.GetKeyUp(key))
                {
                    pressedDirections.Remove(direction.Value);
                }
            }
        }

        /// <summary>长按连发：换方向时立即走一步并重置延迟，同方向按住则按间隔连发。</summary>
        private void UpdateHeldMovement()
        {
            if (pressedDirections.Count == 0)
            {
                repeatingDirection = null;
                moveRepeatTimer = 0f;
                return;
            }
            var direction = pressedDirections[pressedDirections.Count - 1];
            if (repeatingDirection == null || repeatingDirection.Value != direction)
            {
                repeatingDirection = direction;
                moveRepeatTimer = moveRepeatDelay;
                TryMove(direction);
                return;
            }
            moveRepeatTimer -= Time.unscaledDeltaTime;
            if (moveRepeatTimer <= 0f)
            {
                moveRepeatTimer = moveRepeatInterval;
                TryMove(direction);
            }
        }

        private void Update()
        {
            TrackDirectionKeys();
            if (SokobanRuntimeContext.IsQuitConfirmationOpen || SokobanGmController.BlocksInput) return;
            if (state == null) { if (Input.GetKeyDown(KeyCode.Escape)) GoTo("level"); return; }
            if (IsPauseOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) ClosePause();
                return;
            }
            if (IsMapOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.M)) CloseMap();
                return;
            }
            if (IsWinOpen)
            { if (Input.GetKeyDown(KeyCode.R)) Restart(); else if (Input.GetKeyDown(KeyCode.Escape)) GoTo("level"); return; }
            elapsedSeconds += Time.unscaledDeltaTime; UpdateStats();
            if (SokobanUI.IsEditingText()) return;
            if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Escape)) { OpenPause(); return; }
            if (Input.GetKeyDown(KeyCode.M)) { OpenMap(); return; }
            UpdateHeldMovement();
            if (Input.GetKeyDown(KeyCode.Z) || Input.GetKeyDown(KeyCode.Backspace)) UndoMove();
            else if (Input.GetKeyDown(KeyCode.R)) Restart();
        }

        private void UpdateStats()
        { if (stats != null && state != null) stats.text = $"步数 {state.MoveCount}    推箱 {state.PushCount}    用时 {elapsedSeconds:0} 秒"; }
        private void SetInteraction(bool enabled) { if (interaction != null) interaction.interactable = enabled; }
        private void OpenPause()
        {
            if (state == null || state.IsWon || IsPauseOpen || IsMapOpen || SokobanGmController.BlocksInput) return;
            SetInteraction(false);
            if (pauseOverlay != null) pauseOverlay.gameObject.SetActive(true);
        }
        private void ClosePause()
        {
            if (pauseOverlay != null) pauseOverlay.gameObject.SetActive(false);
            SetInteraction(true);
        }
        private void OnApplicationFocus(bool focused) { if (!focused && !SokobanRuntimeContext.IsGmEnabled) OpenPause(); }
    }
}
