using UnityEngine;

namespace Kuluobishi.Sokoban
{
    public sealed partial class SokobanGameplaySceneController
    {
        private SokobanBoardView mapBoard;

        private void OpenMap()
        {
            if (level == null || state == null || IsMapOpen) return;
            if (refs.mapOverlay == null) return;
            refs.mapOverlay.gameObject.SetActive(true);
            if (refs.mapTitle != null) refs.mapTitle.text = "完整地图 · " + level.Name;
            if (refs.mapHint != null)
                refs.mapHint.text = $"{level.Width} × {level.Height} · 显示当前玩家与箱子位置 · M / Esc 回到游戏";
            mapBoard = refs.mapView;
            if (mapBoard != null) mapBoard.Initialize(level, state, false);
            if (gameBoard != null) gameBoard.enabled = false;
        }

        private void CloseMap()
        {
            if (refs.mapOverlay != null) refs.mapOverlay.gameObject.SetActive(false);
            mapBoard = null;
            if (gameBoard != null) gameBoard.enabled = true;
        }
    }
}
