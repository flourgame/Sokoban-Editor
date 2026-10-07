using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>Player settings for audio, CRT and effects, with saved preferences.</summary>
    [DefaultExecutionOrder(-1100)]
    public sealed class SokobanSettingsPanel : MonoBehaviour
    {
        private static SokobanSettingsPanel current;
        private static int inputFrame = -1;
        private static float musicVolume = SokobanPresentationAudio.DefaultVolume, soundVolume = SokobanPresentationAudio.DefaultVolume, crtIntensity = 0.22f;
        private static bool lowEffects;
        private Button windowMode, borderlessMode;
        public static bool IsOpen => current != null && current.gameObject.activeInHierarchy;
        public static bool BlocksInput => IsOpen || inputFrame == Time.frameCount;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            current = null; inputFrame = -1;
            musicVolume = PlayerPrefs.GetFloat("Sokoban.MusicVolume", SokobanPresentationAudio.DefaultVolume);
            soundVolume = PlayerPrefs.GetFloat("Sokoban.SfxVolume", SokobanPresentationAudio.DefaultVolume);
            crtIntensity = PlayerPrefs.GetFloat("Sokoban.CRTIntensity", 0.22f);
            lowEffects = PlayerPrefs.GetInt("Sokoban.LowEffects", 0) != 0;
        }

        public static void Show()
        {
            if (IsOpen || SokobanRuntimeContext.IsQuitConfirmationOpen) return;
            var canvas = SokobanUI.CreateCanvas("SokobanSettingsCanvas", 800);
            canvas.gameObject.AddComponent<SokobanSettingsPanel>();
        }

        private void Awake()
        {
            current = this; inputFrame = Time.frameCount;
            SokobanUI.EnsureEventSystem(); EventSystem.current?.SetSelectedGameObject(null);
            var backdrop = SokobanUI.Panel(transform, "SettingsBackdrop", new Color(0f, 0f, 0f, 0.76f), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var dialog = SokobanUI.Panel(backdrop, "SettingsDialog", SokobanTheme.Panel, Vector2.one * 0.5f, Vector2.one * 0.5f,
                new Vector2(-340f, -320f), new Vector2(340f, 320f));
            Label(dialog, "SettingsTitle", "设置", 40, new Vector2(0f, 252f), new Vector2(600f, 76f));
            Volume(dialog, "MusicVolume", "音乐", 170f, musicVolume, value => { musicVolume = value; PlayerPrefs.SetFloat("Sokoban.MusicVolume", value); SokobanPresentationAudio.RefreshVolumes(); });
            Volume(dialog, "SoundVolume", "音效", 102f, soundVolume, value => { soundVolume = value; PlayerPrefs.SetFloat("Sokoban.SfxVolume", value); SokobanPresentationAudio.RefreshVolumes(); });
            Volume(dialog, "CrtIntensity", "CRT", 34f, crtIntensity, value => { crtIntensity = value; PlayerPrefs.SetFloat("Sokoban.CRTIntensity", value); SokobanCrtOverlay.RefreshAll(); });
            ToggleRow(dialog, "AnimationToggle", "动画", -38f, SokobanAnimationSettings.Enabled, SokobanAnimationSettings.SetEnabled);
            ToggleRow(dialog, "LowEffectsToggle", "低特效", -110f, lowEffects, value => { lowEffects = value; PlayerPrefs.SetInt("Sokoban.LowEffects", value ? 1 : 0); SokobanCrtOverlay.RefreshAll(); });
            Label(dialog, "DisplayModeLabel", "显示模式", 24, new Vector2(-218f, -188f), new Vector2(108f, 52f), TextAnchor.MiddleLeft);
            windowMode = SokobanUI.Button(dialog, "DisplayWindowed", "窗口模式", new Vector2(176f, 52f), () => SokobanDisplaySettings.SetBorderless(false));
            Position(windowMode.GetComponent<RectTransform>(), new Vector2(-44f, -188f), new Vector2(176f, 52f));
            borderlessMode = SokobanUI.Button(dialog, "DisplayBorderless", "无边框全屏", new Vector2(212f, 52f), () => SokobanDisplaySettings.SetBorderless(true));
            Position(borderlessMode.GetComponent<RectTransform>(), new Vector2(162f, -188f), new Vector2(212f, 52f));
            SokobanDisplaySettings.Changed += RefreshDisplay;
            RefreshDisplay();
            var close = SokobanUI.Button(dialog, "SettingsBack", "返回", new Vector2(520f, 56f), Close, SokobanTheme.Accent, SokobanTheme.AccentText);
            Position(close.GetComponent<RectTransform>(), new Vector2(0f, -268f), new Vector2(520f, 56f));
            close.GetComponentInChildren<Text>().raycastTarget = false;
            SokobanPlayerPresentation.Adopt(transform);
            if (SokobanPlayerPresentation.IsEnabled) SokobanCrtOverlay.Attach(transform);
        }

        private void RefreshDisplay()
        {
            var available = SokobanDisplaySettings.Supported && !SokobanDisplaySettings.IsSwitching;
            windowMode.interactable = available && SokobanDisplaySettings.Borderless;
            borderlessMode.interactable = available && !SokobanDisplaySettings.Borderless;
        }

        private static void ToggleRow(Transform parent, string name, string title, float y, bool initial, System.Action<bool> changed)
        {
            Label(parent, name + "Label", title, 24, new Vector2(-218f, y), new Vector2(108f, 52f), TextAnchor.MiddleLeft);
            var toggleObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Toggle));
            var toggleRect = toggleObject.GetComponent<RectTransform>(); toggleRect.SetParent(parent, false);
            Position(toggleRect, new Vector2(-50f, y), new Vector2(196f, 52f));
            toggleObject.GetComponent<Image>().color = Color.clear;
            var box = SokobanUI.Panel(toggleRect, "Box", SokobanTheme.Field, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(4f, -16f), new Vector2(36f, 16f));
            var mark = SokobanUI.Panel(box, "Checkmark", SokobanTheme.Accent, Vector2.zero, Vector2.one, new Vector2(7f, 7f), new Vector2(-7f, -7f));
            mark.GetComponent<Image>().raycastTarget = false;
            var valueText = Label(toggleRect, "Value", initial ? "开启" : "关闭", 24, new Vector2(18f, 0f), new Vector2(100f, 52f), TextAnchor.MiddleLeft);
            var toggle = toggleObject.GetComponent<Toggle>(); toggle.targetGraphic = box.GetComponent<Image>(); toggle.graphic = mark.GetComponent<Image>();
            toggle.toggleTransition = Toggle.ToggleTransition.None; toggle.isOn = initial;
            // Explicit visibility also survives popup CanvasGroup/layout rebuilds.
            toggle.graphic.enabled = initial;
            toggle.onValueChanged.AddListener(value =>
            {
                toggle.graphic.enabled = value;
                changed(value);
                valueText.text = value ? "开启" : "关闭";
            });
        }

        private static void Volume(Transform parent, string name, string title, float y, float value, System.Action<float> changed)
        {
            Label(parent, name + "Label", title, 24, new Vector2(-218f, y), new Vector2(108f, 52f), TextAnchor.MiddleLeft);
            var percentage = Label(parent, name + "Value", Mathf.RoundToInt(value * 100f) + "%", 24, new Vector2(232f, y), new Vector2(76f, 52f), TextAnchor.MiddleRight);
            var sliderObject = new GameObject(name + "Slider", typeof(RectTransform), typeof(Image), typeof(Slider));
            var rect = sliderObject.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            Position(rect, new Vector2(16f, y), new Vector2(328f, 52f)); sliderObject.GetComponent<Image>().color = Color.clear;
            SokobanUI.Panel(rect, "Track", SokobanTheme.Field, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -5f), new Vector2(0f, 5f));
            var fillArea = SokobanUI.Panel(rect, "FillArea", Color.clear, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(9f, -5f), new Vector2(-9f, 5f));
            fillArea.GetComponent<Image>().raycastTarget = false;
            var fill = SokobanUI.Panel(fillArea, "Fill", SokobanTheme.Accent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            fill.GetComponent<Image>().raycastTarget = false;
            // Slider drives the handle's vertical anchors to stretch: constrain the visual lane,
            // while keeping the slider root tall enough for comfortable pointer interaction.
            var handleArea = SokobanUI.Panel(rect, "HandleArea", Color.clear, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(9f, -10f), new Vector2(-9f, 10f));
            handleArea.GetComponent<Image>().raycastTarget = false;
            var handle = SokobanUI.Panel(handleArea, "Handle", SokobanTheme.TextPrimary, Vector2.one * 0.5f, Vector2.one * 0.5f, new Vector2(-7f, 0f), new Vector2(7f, 0f));
            var slider = sliderObject.GetComponent<Slider>(); slider.minValue = 0f; slider.maxValue = 1f;
            slider.wholeNumbers = false; slider.direction = Slider.Direction.LeftToRight;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = handle.GetComponent<Image>(); slider.value = value;
            slider.onValueChanged.AddListener(volume => { changed(volume); percentage.text = Mathf.RoundToInt(volume * 100f) + "%"; });
        }

        private static Text Label(Transform parent, string name, string text, int size, Vector2 position, Vector2 dimensions, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var label = SokobanUI.Text(parent, name, text, size, SokobanTheme.TextPrimary, alignment);
            Position(label.rectTransform, position, dimensions); label.raycastTarget = false; return label;
        }
        private static void Position(RectTransform rect, Vector2 position, Vector2 size)
        { rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f; rect.anchoredPosition = position; rect.sizeDelta = size; }
        public void Close()
        {
            inputFrame = Time.frameCount; EventSystem.current?.SetSelectedGameObject(null);
            gameObject.SetActive(false); Destroy(gameObject);
        }
        private void Update()
        {
            if (!SokobanRuntimeContext.IsQuitConfirmationOpen && Input.GetKeyDown(KeyCode.Escape)) Close();
        }
        private void OnDestroy() { SokobanDisplaySettings.Changed -= RefreshDisplay; if (current == this) current = null; }
    }
}
