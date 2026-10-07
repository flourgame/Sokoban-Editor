using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>Original Balatro atlas tiles and pixel UI geometry (see Attribution.md).</summary>
    public static class SokobanBalatroSkin
    {
        private static Sprite cardFrame, buttonShape, chip, hourglass;
        public static readonly Color PanelColor = Hex(0x1C3034);
        public static readonly Color FrameColor = Hex(0x5C8A86);
        public static readonly Color AccentColor = Hex(0xD5A86E);
        public static readonly Color AccentTextColor = Hex(0x203034);
        public static readonly Color TextColor = Hex(0xF2EADF);
        public static readonly Color SecondaryTextColor = Hex(0xB9CEC7);
        public static readonly Color ButtonColor = Hex(0x3E5A5D);
        public static readonly Color WallTint = new Color(0.88f, 0.85f, 0.78f, 1f);
        public static readonly Color FloorTint = new Color(0.88f, 0.94f, 0.90f, 1f);
        public static readonly Color BoxTint = new Color(1f, 0.94f, 0.83f, 1f);
        public static readonly Color PlayerTint = new Color(1f, 1f, 0.92f, 1f);
        public static readonly Color GoalTint = new Color(0.85f, 1f, 0.88f, 1f);
        public static bool IsPlayerScene
        {
            get
            {
                var name = SceneManager.GetActiveScene().name;
                return name == "start" || name == "level" || name == "game";
            }
        }

        private static Color Hex(int value) => new Color(((value >> 16) & 255) / 255f, ((value >> 8) & 255) / 255f, (value & 255) / 255f, 1f);

        private static Sprite CardFrame()
        {
            if (cardFrame != null) return cardFrame;
            var texture = Resources.Load<Texture2D>("Balatro/cards/Enhancers");
            if (texture == null) return null;
            cardFrame = Sprite.Create(texture, new Rect(142f, texture.height - 190f, 142f, 190f), Vector2.one * 0.5f,
                100f, 0, SpriteMeshType.FullRect, new Vector4(10f, 10f, 10f, 10f));
            cardFrame.name = "Balatro_OriginalCardFrame";
            return cardFrame;
        }

        private static Sprite ButtonShape()
        {
            if (buttonShape != null) return buttonShape;
            var texture = Resources.Load<Texture2D>("Balatro/ui/button_shape");
            if (texture == null) return CardFrame();
            buttonShape = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), Vector2.one * 0.5f,
                100f, 0, SpriteMeshType.FullRect, new Vector4(8f, 8f, 8f, 8f));
            buttonShape.name = "Balatro_UIElementPixelContour";
            return buttonShape;
        }

        public static void ApplyPanel(Image image)
        {
            if (image == null || !IsPlayerScene || image.color.a < 0.01f) return;
            image.material = null;
            if (image.name == "MenuCard" && SceneManager.GetActiveScene().name == "start")
            {
                // Main-menu tray: a solid, slightly lighter silhouette without
                // the card atlas's bright rim. Its shadow only affects the tray.
                image.sprite = ButtonShape();
                image.type = Image.Type.Sliced;
                image.color = Hex(0x263D40);
                image.raycastTarget = false;
                var oldFrame = image.transform.Find("BalatroFrame");
                if (oldFrame != null) oldFrame.gameObject.SetActive(false);
                var shadow = image.GetComponent<Shadow>();
                if (shadow == null) shadow = image.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0.025f, 0.045f, 0.05f, 0.65f);
                shadow.effectDistance = new Vector2(0f, -10f);
                shadow.useGraphicAlpha = true;
                return;
            }
            image.sprite = CardFrame();
            image.type = Image.Type.Sliced;
            image.color = PanelColor;
            if (image.transform.Find("BalatroFrame") != null) return;
            var frame = new GameObject("BalatroFrame", typeof(RectTransform), typeof(Image));
            var rect = frame.GetComponent<RectTransform>();
            rect.SetParent(image.transform, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var border = frame.GetComponent<Image>();
            border.sprite = CardFrame();
            border.type = Image.Type.Sliced;
            border.fillCenter = false;
            border.color = FrameColor;
            border.raycastTarget = false;
        }

        public static void ApplyButton(Button button)
        {
            if (button == null || !IsPlayerScene) return;
            var image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image == null) return;
            image.material = null;
            image.sprite = ButtonShape();
            image.type = Image.Type.Sliced;
            var name = button.name;
            var primary = name == "StartButton" || name == "Next" || name == "PauseResume" || name == "SettingsBack";
            var row = button.GetComponent<LayoutElement>() != null;
            if (!row) image.color = primary ? AccentColor : name.Contains("Map") ? Hex(0x4D8B83) : ButtonColor;
            var shadow = image.GetComponent<Shadow>();
            if (shadow == null) shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0.06f, 0.10f, 0.11f, 0.95f);
            shadow.effectDistance = new Vector2(0f, -7f);
            shadow.useGraphicAlpha = true;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
            colors.pressedColor = new Color(0.83f, 0.83f, 0.83f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.65f);
            button.colors = colors;
            foreach (var label in button.GetComponentsInChildren<Text>(true))
            {
                label.color = primary || row && image.color == AccentColor ? AccentTextColor : TextColor;
                label.raycastTarget = false;
            }
            if (!primary && name != "PauseButton" && name != "MapButton") return;
            if (image.transform.Find("BalatroIcon") != null) return;
            var icon = new GameObject("BalatroIcon", typeof(RectTransform), typeof(Image));
            var iconRect = icon.GetComponent<RectTransform>();
            iconRect.SetParent(image.transform, false);
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
            iconRect.anchoredPosition = new Vector2(29f, 0f);
            iconRect.sizeDelta = Vector2.one * 28f;
            var iconImage = icon.GetComponent<Image>();
            iconImage.sprite = Icon(name == "PauseButton");
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
        }

        private static Sprite Icon(bool timer)
        {
            if (timer && hourglass != null) return hourglass;
            if (!timer && chip != null) return chip;
            var texture = Resources.Load<Texture2D>("Balatro/ui/ui_assets");
            if (texture == null) return null;
            var sprite = Sprite.Create(texture, new Rect(timer ? 72f : 0f, texture.height - 36f, 36f, 36f), Vector2.one * 0.5f, 100f, 0, SpriteMeshType.FullRect);
            sprite.name = timer ? "Balatro_Hourglass" : "Balatro_Chip";
            if (timer) hourglass = sprite; else chip = sprite;
            return sprite;
        }
    }
}
