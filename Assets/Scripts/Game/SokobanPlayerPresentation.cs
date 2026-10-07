using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>普通玩家流程的表现层安装器。编辑器场景主动跳过。</summary>
    public static class SokobanPlayerPresentation
    {
        public static bool IsEnabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsEnabled = false;
        }

        public static void Install(Canvas canvas, RectTransform root)
        {
            var sceneName = SceneManager.GetActiveScene().name;
            if (sceneName == "editor" || canvas == null) { IsEnabled = false; return; }
            IsEnabled = true;
            SokobanPresentationAudio.Ensure();
            SokobanCrtOverlay.Attach(canvas.transform);
            ApplyBackground(canvas.transform);
            if (sceneName == "game") SokobanGameplayHudStyle.Apply(root);
            Adopt(root != null ? root : canvas.transform);
        }

        public static void Adopt(Transform root)
        {
            if (!IsEnabled || root == null) return;
            foreach (var button in root.GetComponentsInChildren<Button>(true))
            {
                SokobanBalatroSkin.ApplyButton(button);
                if (button.GetComponent<SokobanButtonFeedback>() == null)
                    button.gameObject.AddComponent<SokobanButtonFeedback>();
            }
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                var name = image.name;
                if (name.EndsWith("Dialog") || name == "MenuCard" || name == "LevelListPanel" || name == "PreviewPanel" || name == "BoardPanel" || name == "MapArea")
                    SokobanBalatroSkin.ApplyPanel(image);
            }
            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                if (text.GetComponentInParent<Button>(true) != null) continue;
                if (text.color.a < 0.01f) continue;
                text.color = text.name.Contains("Hint") || text.name.Contains("Info") || text.name == "Subtitle" ? SokobanBalatroSkin.SecondaryTextColor : SokobanBalatroSkin.TextColor;
            }
            foreach (var rect in PopupCandidates(root))
            {
                var motion = rect.GetComponent<SokobanPopupMotion>();
                if (motion == null) motion = rect.gameObject.AddComponent<SokobanPopupMotion>();
                motion.Initialize();
            }
        }

        private static IEnumerable<RectTransform> PopupCandidates(Transform root)
        {
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                var name = rect.name;
                if (name == "MenuCard" || name == "LevelListPanel" || name == "PreviewPanel" || name == "WinDialog" ||
                    name == "PauseDialog" || name == "MapArea" || name == "ErrorPanel" || name == "SettingsDialog")
                    yield return rect;
            }
        }

        private static void ApplyBackground(Transform root)
        {
            var shader = Shader.Find("UI/SokobanBalatroBackground");
            if (shader == null) return;
            var material = new Material(shader) { name = "BalatroBackground_Runtime" };
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "ScreenBackground")
                {
                    image.material = material;
                    image.color = Color.white;
                    image.raycastTarget = false;
                }
                else if (image.name == "Background")
                {
                    image.color = Color.clear;
                    image.raycastTarget = false;
                    image.material = null;
                }
            }
        }
    }
}
