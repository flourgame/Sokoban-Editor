using System.Collections;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    /// <summary>小丑牌式弹窗：从屏幕下方快速上升、过冲、回正。</summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    public sealed class SokobanPopupMotion : MonoBehaviour
    {
        [SerializeField] private float riseDuration = 0.18f;
        [SerializeField] private float settleDuration = 0.16f;
        [SerializeField] private float overshoot = 12f;
        private RectTransform rect;
        private CanvasGroup group;
        private Vector2 restPosition;
        private Vector3 restScale;
        private bool initialized;
        private Coroutine motion;

        private void Awake()
        {
            EnsureReferences();
        }

        private bool EnsureReferences()
        {
            if (rect == null) rect = transform as RectTransform;
            if (group == null) group = GetComponent<CanvasGroup>();
            if (rect == null || group == null) return false;
            return true;
        }

        public void Initialize()
        {
            if (initialized) return;
            if (!EnsureReferences()) return;
            restPosition = rect.anchoredPosition;
            restScale = transform.localScale;
            initialized = true;
            if (gameObject.activeInHierarchy) PlayIn();
        }

        private void OnEnable()
        {
            SokobanAnimationSettings.Changed += ApplyAnimationSetting;
            if (initialized) PlayIn();
        }

        private void ApplyAnimationSetting(bool enabled) { if (!enabled) FinishMotion(); }

        private void FinishMotion()
        {
            if (!initialized || !EnsureReferences()) return;
            if (motion != null) StopCoroutine(motion);
            motion = null;
            rect.anchoredPosition = restPosition;
            transform.localScale = restScale;
            group.alpha = 1f;
        }

        private void OnDisable()
        {
            SokobanAnimationSettings.Changed -= ApplyAnimationSetting;
            FinishMotion();
        }

        public void PlayIn()
        {
            if (!initialized || !EnsureReferences()) return;
            if (!SokobanAnimationSettings.Enabled) { FinishMotion(); return; }
            if (motion != null) StopCoroutine(motion);
            motion = StartCoroutine(AnimateIn());
        }

        private IEnumerator AnimateIn()
        {
            if (!EnsureReferences()) yield break;
            var height = Mathf.Max(180f, rect.rect.height + 120f);
            rect.anchoredPosition = restPosition + Vector2.down * height;
            transform.localScale = restScale * 0.94f;
            group.alpha = 0f;
            var t = 0f;
            while (t < riseDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / riseDuration);
                var eased = 1f - Mathf.Pow(1f - p, 3f);
                rect.anchoredPosition = Vector2.LerpUnclamped(restPosition + Vector2.down * height, restPosition + Vector2.up * overshoot, eased);
                transform.localScale = Vector3.LerpUnclamped(restScale * 0.94f, restScale * 1.015f, eased);
                group.alpha = p;
                yield return null;
            }
            t = 0f;
            while (t < settleDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / settleDuration);
                var eased = p * p * (3f - 2f * p);
                rect.anchoredPosition = Vector2.LerpUnclamped(restPosition + Vector2.up * overshoot, restPosition, eased);
                transform.localScale = Vector3.LerpUnclamped(restScale * 1.015f, restScale, eased);
                yield return null;
            }
            rect.anchoredPosition = restPosition;
            transform.localScale = restScale;
            group.alpha = 1f;
        }
    }
}
