using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>普通玩家页面的按钮动效：一次短抖动、浮起、按下落位。</summary>
    [RequireComponent(typeof(Button))]
    public sealed class SokobanButtonFeedback : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, ISelectHandler, IDeselectHandler
    {
        [SerializeField] private float hoverLift = 5f;
        [SerializeField] private float hoverScale = 1.025f;
        [SerializeField] private float pressDrop = 3f;
        [SerializeField] private float jitterDuration = 0.07f;
        private RectTransform rect;
        private Button button;
        private Vector2 restPosition;
        private Vector3 restScale;
        private Quaternion restRotation;
        private Coroutine motion;
        private bool hovered;
        private bool pressed;
        private float colorFadeDuration;

        private void Awake()
        {
            button = GetComponent<Button>();
            var original = button.targetGraphic as Image;
            if (original != null && original.transform == transform)
            {
                var visual = new GameObject("ButtonVisual", typeof(RectTransform), typeof(Image));
                rect = visual.GetComponent<RectTransform>();
                rect.SetParent(transform, false);
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var image = visual.GetComponent<Image>();
                image.sprite = original.sprite; image.type = original.type;
                image.color = original.color; image.material = original.material;
                image.raycastTarget = false;
                var children = new System.Collections.Generic.List<Transform>();
                foreach (Transform child in transform) if (child != rect) children.Add(child);
                foreach (var child in children) child.SetParent(rect, false);
                var shadow = original.GetComponent<Shadow>();
                if (shadow != null)
                {
                    var visualShadow = visual.AddComponent<Shadow>();
                    visualShadow.effectColor = shadow.effectColor;
                    visualShadow.effectDistance = shadow.effectDistance;
                    shadow.enabled = false;
                }
                // Keep a stable invisible hit area at the layout position. The
                // lifted artwork must not move away from the pointer at an edge.
                original.sprite = null;
                original.color = Color.clear;
                original.raycastTarget = true;
                button.targetGraphic = image;
            }
            else rect = original != null ? original.rectTransform : transform as RectTransform;
            restPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            restScale = rect.localScale;
            restRotation = rect.localRotation;
            colorFadeDuration = button.colors.fadeDuration;
        }

        private void OnEnable()
        {
            SokobanAnimationSettings.Changed += ApplyAnimationSetting;
            ApplyAnimationSetting(SokobanAnimationSettings.Enabled);
        }

        private void ApplyAnimationSetting(bool enabled)
        {
            if (button != null)
            {
                var colors = button.colors;
                colors.fadeDuration = enabled ? colorFadeDuration : 0f;
                button.colors = colors;
            }
            if (!enabled) ResetMotion();
        }

        public void OnPointerEnter(PointerEventData eventData) { SetHovered(true); }
        public void OnPointerExit(PointerEventData eventData) { SetHovered(false); }
        public void OnSelect(BaseEventData eventData) { SetHovered(true); }
        public void OnDeselect(BaseEventData eventData) { SetHovered(false); }
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsUsable()) return;
            pressed = true;
            StartMotion(AnimateTo(restPosition + Vector2.down * pressDrop, restScale * 0.985f, 0.055f));
            SokobanPresentationAudio.PlayUi("button", 1.5f * 0.7f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            pressed = false;
            SetHovered(hovered);
        }

        private bool IsUsable() => button == null || button.interactable;

        private void SetHovered(bool value)
        {
            var entering = value && !hovered;
            hovered = value;
            if (!IsUsable() || pressed) return;
            // Start audio on entry, before either animation yields. Re-selecting
            // or releasing a click while already hovered must not replay it.
            if (entering) SokobanPresentationAudio.PlayHover();
            if (!value)
            {
                StartMotion(AnimateTo(restPosition, restScale, 0.12f));
                return;
            }
            StartMotion(HoverSequence());
        }

        private IEnumerator HoverSequence()
        {
            var elapsed = 0f;
            var origin = restPosition;
            while (elapsed < jitterDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / jitterDuration);
                var shake = Mathf.Sin(p * Mathf.PI * 5f) * (1f - p) * 2.5f;
                rect.anchoredPosition = origin + new Vector2(shake, Mathf.Sin(p * Mathf.PI * 3f) * 1.2f);
                yield return null;
            }
            if (!hovered || pressed) yield break;
            yield return AnimateTo(restPosition + Vector2.up * hoverLift, restScale * hoverScale, 0.13f);
        }

        private IEnumerator AnimateTo(Vector2 position, Vector3 scale, float duration)
        {
            if (rect == null) yield break;
            var fromPosition = rect.anchoredPosition;
            var fromScale = rect.localScale;
            var fromRotation = rect.localRotation;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / duration);
                p = p * p * (3f - 2f * p);
                rect.anchoredPosition = Vector2.LerpUnclamped(fromPosition, position, p);
                rect.localScale = Vector3.LerpUnclamped(fromScale, scale, p);
                rect.localRotation = Quaternion.SlerpUnclamped(fromRotation, restRotation, p);
                yield return null;
            }
            rect.anchoredPosition = position;
            rect.localScale = scale;
            rect.localRotation = restRotation;
        }

        private void StartMotion(IEnumerator routine)
        {
            if (!SokobanAnimationSettings.Enabled) { ResetMotion(); return; }
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(routine);
        }

        private void ResetMotion()
        {
            if (motion != null) StopCoroutine(motion);
            motion = null;
            if (rect == null) return;
            rect.anchoredPosition = restPosition;
            rect.localScale = restScale;
            rect.localRotation = restRotation;
        }

        private void OnDisable()
        {
            SokobanAnimationSettings.Changed -= ApplyAnimationSetting;
            hovered = pressed = false;
            ResetMotion();
        }
    }
}
