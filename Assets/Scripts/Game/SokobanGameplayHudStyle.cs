using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>Backed header and hint strip; existing controller Text references stay intact.</summary>
    public static class SokobanGameplayHudStyle
    {
        public static void Apply(RectTransform workspace)
        {
            if (workspace == null) return;
            var hud = workspace.Find("Hud") as RectTransform;
            if (hud == null || hud.Find("GameplayHeader") != null) return;
            var title = hud.Find("Title")?.GetComponent<Text>();
            var stats = hud.Find("Stats")?.GetComponent<Text>();
            var view = hud.Find("ViewMode")?.GetComponent<Text>();
            var hint = hud.Find("GMHint")?.GetComponent<Text>();
            var message = hud.Find("Message")?.GetComponent<Text>();
            var header = SokobanUI.Panel(hud, "GameplayHeader", SokobanBalatroSkin.PanelColor,
                new Vector2(0f, 1f), Vector2.one, new Vector2(340f, -152f), new Vector2(-340f, -24f));
            SokobanBalatroSkin.ApplyPanel(header.GetComponent<Image>());
            Place(title, header, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -14f), new Vector2(-44f, 52f), TextAnchor.MiddleLeft, 34);
            Place(stats, header, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(24f, 14f), new Vector2(640f, 34f), TextAnchor.MiddleLeft, 23);
            Place(view, header, Vector2.right, Vector2.right, Vector2.right,
                new Vector2(-24f, 14f), new Vector2(520f, 34f), TextAnchor.MiddleRight, 20);
            var strip = SokobanUI.Panel(hud, "GameplayHintStrip", SokobanBalatroSkin.PanelColor,
                Vector2.zero, Vector2.right, new Vector2(340f, 112f), new Vector2(-340f, 178f));
            SokobanBalatroSkin.ApplyPanel(strip.GetComponent<Image>());
            Place(message, strip, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(24f, 0f), new Vector2(520f, 38f), TextAnchor.MiddleLeft, 21);
            Place(hint, strip, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                new Vector2(-24f, 0f), new Vector2(620f, 38f), TextAnchor.MiddleRight, 20);
            if (message != null && string.IsNullOrEmpty(message.text)) message.text = "将所有箱子推到目标点";
            var boardPanel = hud.Find("BoardPanel") as RectTransform;
            if (boardPanel != null)
            {
                boardPanel.offsetMin = new Vector2(340f, 196f);
                boardPanel.offsetMax = new Vector2(-340f, -172f);
            }
            foreach (var label in workspace.GetComponentsInChildren<Text>(true))
            {
                if (label.name != "MapTitle" && label.name != "MapHint" && label.name != "ErrorTitle" && label.name != "Error") continue;
                AddBacking(label);
            }
        }

        private static void Place(Text text, RectTransform parent, Vector2 min, Vector2 max, Vector2 pivot, Vector2 position,
            Vector2 size, TextAnchor alignment, int fontSize)
        {
            if (text == null) return;
            var rect = text.rectTransform;
            rect.SetParent(parent, false);
            rect.anchorMin = min; rect.anchorMax = max; rect.pivot = pivot;
            rect.anchoredPosition = position; rect.sizeDelta = size;
            text.alignment = alignment; text.fontSize = fontSize;
            text.color = SokobanBalatroSkin.TextColor;
            text.raycastTarget = false;
        }

        private static void AddBacking(Text label)
        {
            var original = label.rectTransform;
            var backing = new GameObject(label.name + "Backing", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            backing.SetParent(original.parent, false);
            backing.anchorMin = original.anchorMin; backing.anchorMax = original.anchorMax;
            backing.pivot = original.pivot; backing.anchoredPosition = original.anchoredPosition;
            backing.sizeDelta = original.sizeDelta;
            backing.SetSiblingIndex(original.GetSiblingIndex());
            backing.GetComponent<Image>().color = SokobanBalatroSkin.PanelColor;
            SokobanBalatroSkin.ApplyPanel(backing.GetComponent<Image>());
            original.SetParent(backing, false);
            original.anchorMin = Vector2.zero; original.anchorMax = Vector2.one;
            original.offsetMin = new Vector2(16f, 2f); original.offsetMax = new Vector2(-16f, -2f);
        }
    }
}
