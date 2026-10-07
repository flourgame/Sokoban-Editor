using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>
    /// 全局配色主题。所有界面统一从这里取色，避免各处硬编码导致风格割裂。
    /// 深色蓝灰底 + 暖琥珀主色 + 青蓝玩家色，保证中文白字有足够对比度。
    /// </summary>
    public static class SokobanTheme
    {
        public static readonly Color Background = new Color(0.075f, 0.098f, 0.130f, 1f);
        public static readonly Color Panel = new Color(0.118f, 0.153f, 0.208f, 0.98f);
        public static readonly Color PanelAlt = new Color(0.145f, 0.184f, 0.247f, 0.98f);
        public static readonly Color Surface = new Color(0.176f, 0.220f, 0.290f, 1f);
        public static readonly Color Accent = new Color(0.941f, 0.659f, 0.282f, 1f);
        public static readonly Color AccentText = new Color(0.110f, 0.078f, 0.031f, 1f);
        public static readonly Color TextPrimary = new Color(0.949f, 0.961f, 0.976f, 1f);
        public static readonly Color TextSecondary = new Color(0.639f, 0.706f, 0.788f, 1f);
        public static readonly Color Field = new Color(0.086f, 0.114f, 0.153f, 0.96f);
        public static readonly Color ListBackground = new Color(0.063f, 0.086f, 0.118f, 0.72f);
        public static readonly Color BoardBackground = new Color(0.098f, 0.129f, 0.173f, 1f);
        public static readonly Color BoardFloor = new Color(0.173f, 0.227f, 0.306f, 1f);
        public static readonly Color BoardWall = new Color(0.086f, 0.114f, 0.153f, 1f);
        public static readonly Color Goal = new Color(0.243f, 0.365f, 0.278f, 1f);
        public static readonly Color Box = new Color(0.722f, 0.478f, 0.208f, 1f);
        public static readonly Color BoxOnGoal = new Color(0.941f, 0.710f, 0.290f, 1f);
        public static readonly Color PlayerOnGoal = new Color(0.243f, 0.706f, 0.624f, 1f);
        public static readonly Color Player = new Color(0.310f, 0.765f, 0.851f, 1f);
        public static readonly Color WinPanel = new Color(0.110f, 0.176f, 0.161f, 0.98f);
        public static readonly Color GmPanel = new Color(0.208f, 0.110f, 0.176f, 0.98f);
    }

    public static class SokobanUI
    {
        private static UnityEngine.Font font;

        private static UnityEngine.Font Font
        {
            get
            {
                if (font == null)
                {
                    font = Resources.Load<UnityEngine.Font>("fonts/STXIHEI");
                    if (font == null) Debug.LogError("未找到中文字体资源 Assets/resources/fonts/STXIHEI.TTF。");
                }
                return font;
            }
        }

        public static Canvas CreateCanvas(string name, int sortingOrder = 100)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static bool IsEditingText()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<InputField>() != null;
        }

        public static void EnsureEventSystem()
        {
            // 场景级 EventSystem：不做 DontDestroyOnLoad。
            // level/editor 场景烘焙了自己的 EventSystem，若运行时再建一个持久实例，
            // 切换场景时会出现两个共存并刷 "There are 2 event systems" 警告。
            // 改为"当前场景没有才建、随场景销毁"，保证任意时刻全场只有一个。
            if (EventSystem.current != null) return;
            var eventSystem = new GameObject("SokobanEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.SetActive(true);
        }

        public static RectTransform Panel(Transform parent, string name, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            var objectRoot = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = objectRoot.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            var image = objectRoot.GetComponent<Image>();
            image.color = color;
            if (name.EndsWith("Dialog") || name == "MenuCard" || name == "LevelListPanel" || name == "PreviewPanel" || name == "BoardPanel" || name == "MapArea")
                SokobanBalatroSkin.ApplyPanel(image);
            return rect;
        }

        /// <summary>加载 Balatro 提取资源中的品牌图，作为普通玩家界面的装饰层。</summary>
        public static Image BalatroLogo(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            var image = root.GetComponent<Image>();
            image.sprite = Resources.Load<Sprite>("Balatro/ui/balatro_alt");
            image.color = Color.white;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        public static UnityEngine.UI.Text Text(Transform parent, string name, string value, int fontSize, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var objectRoot = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Text));
            var rect = objectRoot.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12f, 4f);
            rect.offsetMax = new Vector2(-12f, -4f);
            var label = objectRoot.GetComponent<UnityEngine.UI.Text>();
            label.text = value ?? "";
            label.fontSize = fontSize;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.font = Font;
            return label;
        }

        public static Button Button(Transform parent, string name, string label, Vector2 size, Action onClick, Color? color = null, Color? textColor = null)
        {
            var objectRoot = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = objectRoot.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            var image = objectRoot.GetComponent<Image>();
            image.color = color ?? SokobanTheme.Surface;
            var button = objectRoot.GetComponent<Button>();
            button.targetGraphic = image;
            // 注意：ColorBlock 的各色是与 targetGraphic 颜色相乘的 tint，
            // 因此 normal 必须为白，hover/pressed 用相对明暗，否则颜色会被平方压暗。
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(() => onClick());
            // 按钮为单行文本：改用 Overflow，避免"首行行高>框高"时被 Truncate 裁成 0 顶点而整块空白。
            var labelText = Text(objectRoot.transform, "Label", label, 24, textColor ?? SokobanTheme.TextPrimary, TextAnchor.MiddleCenter);
            labelText.verticalOverflow = VerticalWrapMode.Overflow;
            if (!(label ?? "").Contains("\n"))
            {
                labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
                labelText.resizeTextForBestFit = true;
                labelText.resizeTextMinSize = 14;
                labelText.resizeTextMaxSize = 24;
            }
            SokobanBalatroSkin.ApplyButton(button);
            return button;
        }

        public static SokobanGoalGraphic GoalMarker(Transform parent, string name = "Goal")
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(SokobanGoalGraphic));
            var rect = obj.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var marker = obj.GetComponent<SokobanGoalGraphic>(); marker.color = Color.white; marker.raycastTarget = false;
            return marker;
        }

        public static void Ellipsize(UnityEngine.UI.Text label, string title, string suffix, float availableWidth)
        {
            label.resizeTextForBestFit = false; label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow; label.text = title + suffix;
            var count = title.Length;
            while (label.preferredWidth > availableWidth && count > 0)
                label.text = title.Substring(0, --count) + "…" + suffix;
        }

        public static UnityEngine.UI.InputField InputField(Transform parent, string name, string value, string placeholder, Vector2 size)
        {
            var objectRoot = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(UnityEngine.UI.InputField));
            var rect = objectRoot.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = size;
            objectRoot.GetComponent<Image>().color = SokobanTheme.Field;
            var input = objectRoot.GetComponent<UnityEngine.UI.InputField>();
            var textObject = new GameObject("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(objectRoot.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);
            var text = textObject.GetComponent<UnityEngine.UI.Text>();
            text.font = Font;
            text.fontSize = 22;
            text.color = SokobanTheme.TextPrimary;
            text.alignment = TextAnchor.MiddleLeft;
            var placeholderObject = new GameObject("Placeholder", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            var placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.SetParent(objectRoot.transform, false);
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(12f, 4f);
            placeholderRect.offsetMax = new Vector2(-12f, -4f);
            var placeholderText = placeholderObject.GetComponent<UnityEngine.UI.Text>();
            placeholderText.text = placeholder ?? "";
            placeholderText.font = Font;
            placeholderText.fontSize = 22;
            placeholderText.color = SokobanTheme.TextSecondary;
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.text = value ?? "";
            return input;
        }

        public static ScrollRect ScrollList(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, out RectTransform content)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            root.GetComponent<Image>().color = SokobanTheme.ListBackground;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var viewportRect = viewport.GetComponent<RectTransform>();
            viewportRect.SetParent(root.transform, false);
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = Vector2.zero;
            viewportRect.offsetMax = Vector2.zero;
            content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)).GetComponent<RectTransform>();
            content.SetParent(viewport.transform, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24f;
            // 垂直滚动条：贴右侧、自动隐藏，让"可滚动"可见可用。
            var scrollbarObject = new GameObject("Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            var scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.SetParent(root.transform, false);
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = new Vector2(1f, 1f);
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-10f, 4f);
            scrollbarRect.offsetMax = new Vector2(-2f, -4f);
            scrollbarObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
            var scrollbar = scrollbarObject.GetComponent<Scrollbar>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            var slidingArea = new GameObject("SlidingArea", typeof(RectTransform)).GetComponent<RectTransform>();
            slidingArea.SetParent(scrollbarObject.transform, false);
            slidingArea.anchorMin = Vector2.zero;
            slidingArea.anchorMax = Vector2.one;
            slidingArea.offsetMin = Vector2.zero;
            slidingArea.offsetMax = Vector2.zero;
            var handle = new GameObject("Handle", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            handle.SetParent(slidingArea, false);
            handle.anchorMin = Vector2.zero;
            handle.anchorMax = Vector2.one;
            handle.offsetMin = Vector2.zero;
            handle.offsetMax = Vector2.zero;
            handle.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.28f);
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return scroll;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(parent.GetChild(i).gameObject);
            }
        }
    }
}
