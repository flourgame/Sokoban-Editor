using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>游戏棋盘上的轻量玩家尾迹和目标入位粒子。</summary>
    public sealed class SokobanGameplayVfx : MonoBehaviour
    {
        private static Sprite dotSprite;
        private RectTransform rect;
        private float stride;
        private readonly List<GameObject> particles = new List<GameObject>();

        private void OnEnable() { SokobanAnimationSettings.Changed += ApplyAnimationSetting; }
        private void OnDisable()
        {
            SokobanAnimationSettings.Changed -= ApplyAnimationSetting;
            ClearParticles();
        }
        private void ApplyAnimationSetting(bool enabled) { if (!enabled) ClearParticles(); }
        private void ClearParticles()
        {
            StopAllCoroutines();
            foreach (var particle in particles)
                if (particle != null) { particle.SetActive(false); Destroy(particle); }
            particles.Clear();
        }

        public void Initialize(RectTransform boardRoot, float cellStride)
        {
            rect = boardRoot;
            stride = cellStride;
        }

        public void SetStride(float cellStride) { stride = cellStride; }

        public void EmitTrail(Vector2 position, SokobanDirection direction)
        {
            if (!SokobanAnimationSettings.Enabled || PlayerPrefs.GetInt("Sokoban.LowEffects", 0) != 0) return;
            var vector = DirectionVector(direction);
            var footPosition = position + Vector2.down * stride * 0.27f;
            var dustColor = new Color(0.74f, 0.74f, 0.74f, 0.90f);
            for (var i = 0; i < 3; i++)
            {
                var offset = new Vector2(Random.Range(-0.055f, 0.055f), Random.Range(-0.025f, 0.025f)) * stride;
                var drift = -vector * stride * (0.07f + i * 0.035f) + Vector2.up * stride * 0.025f;
                SpawnDot(footPosition + offset, dustColor, (6f - i) * 2f, 0.32f + i * 0.03f, i * 0.018f, drift, true);
            }
        }

        public void EmitBurst(Vector2 position, Color color)
        {
            if (!SokobanAnimationSettings.Enabled || PlayerPrefs.GetInt("Sokoban.LowEffects", 0) != 0) return;
            for (var i = 0; i < 6; i++)
            {
                var angle = i * Mathf.PI * 2f / 6f;
                var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Random.Range(8f, 18f) * 3f;
                SpawnDot(position, color, 6f, 0.22f, 0.13f, offset);
            }
        }

        private void SpawnDot(Vector2 position, Color color, float size, float duration, float delay, Vector2? velocity = null, bool behindEntities = false)
        {
            if (rect == null) return;
            var root = new GameObject("VfxDot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            particles.Add(root);
            root.transform.SetParent(rect, false);
            if (behindEntities) root.transform.SetAsFirstSibling();
            var dotRect = root.GetComponent<RectTransform>();
            dotRect.anchorMin = dotRect.anchorMax = dotRect.pivot = new Vector2(0.5f, 0.5f);
            dotRect.anchoredPosition = position;
            dotRect.sizeDelta = Vector2.one * size;
            var image = root.GetComponent<Image>();
            image.sprite = Dot();
            image.color = color;
            image.raycastTarget = false;
            root.GetComponent<CanvasGroup>().alpha = delay > 0f ? 0f : 1f;
            StartCoroutine(AnimateDot(root, duration, delay, velocity ?? Vector2.zero));
        }

        private IEnumerator AnimateDot(GameObject root, float duration, float delay, Vector2 velocity)
        {
            if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            var group = root.GetComponent<CanvasGroup>();
            var rectTransform = root.transform as RectTransform;
            var start = rectTransform.anchoredPosition;
            var elapsed = 0f;
            while (elapsed < duration && root != null)
            {
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / duration);
                rectTransform.anchoredPosition = start + velocity * p;
                rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.25f, p);
                group.alpha = 1f - p;
                yield return null;
            }
            particles.Remove(root);
            if (root != null) Destroy(root);
        }

        private static Sprite Dot()
        {
            if (dotSprite != null) return dotSprite;
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "SokobanVfxDot" };
            texture.SetPixel(0, 0, Color.white); texture.Apply();
            dotSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f);
            return dotSprite;
        }

        private static Vector2 DirectionVector(SokobanDirection direction)
        {
            switch (direction)
            {
                case SokobanDirection.Up: return Vector2.up;
                case SokobanDirection.Down: return Vector2.down;
                case SokobanDirection.Left: return Vector2.left;
                default: return Vector2.right;
            }
        }
    }
}
