using System;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>
    /// 游玩场景在 game.unity 里烘焙的全部界面对象引用。
    /// 由 SokobanGameSceneBaker（菜单 Sokoban/Bake Game Scene）自动填充，
    /// 也可以在 Inspector 里手动重新拖拽。字段为 public 以便烘焙脚本直接赋值。
    /// </summary>
    [Serializable]
    public sealed class SokobanGameplaySceneRefs
    {
        [Header("外壳")]
        public Canvas canvas;
        public RectTransform root;
        public RectTransform hud;
        public CanvasGroup interaction;
        public Text title;
        public Text stats;
        public Text viewMode;
        public Text message;
        public SokobanBoardView boardView;

        [Header("错误分支")]
        public RectTransform errorPanel;
        public Text errorText;
        public Button errorBack;

        [Header("HUD 按钮")]
        public Button undoButton;
        public Button restartButton;
        public Button levelButton;
        public Button mapButton;
        public Button pauseButton;

        [Header("胜利弹窗")]
        public RectTransform winPanel;
        public Text winTitle;
        public Text winStats;
        public Button nextButton;
        public Button replayButton;
        public Button winBackButton;

        [Header("暂停弹窗")]
        public RectTransform pauseOverlay;
        public Button pauseResume;
        public Button pauseRestart;
        public Button pauseSettings;
        public Button pauseLevels;

        [Header("完整地图弹窗")]
        public RectTransform mapOverlay;
        public Text mapTitle;
        public Text mapHint;
        public Button mapClose;
        public SokobanBoardView mapView;
    }
}
