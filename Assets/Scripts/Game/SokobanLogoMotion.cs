using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>Idle sway, smooth hover lift and opposite-pointer tilt around the title centre.</summary>
    [RequireComponent(typeof(Image))]
    public sealed class SokobanLogoMotion : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler
    {
        private RectTransform hitArea, visual, shadow, softShadow;
        private Image shadowImage, softShadowImage;
        private Vector2 pointer, smoothPointer;
        private Vector2 rotationCenter;
        private float lift;
        private bool hovered;

        private void Awake()
        {
            hitArea = (RectTransform)transform;
            var source = GetComponent<Image>();
            // The large Chinese title sits above the smaller English tagline.
            // Use its visual centre, slightly above the full texture's midpoint.
            var artworkHeight = source.sprite != null
                ? Mathf.Min(hitArea.rect.height, hitArea.rect.width * source.sprite.rect.height / source.sprite.rect.width)
                : hitArea.rect.height;
            rotationCenter = Vector2.up * artworkHeight * 0.08f;
            var children = new List<Transform>();
            foreach (Transform child in transform) children.Add(child);
            softShadowImage = CreateLayer("LogoSoftShadow", source, new Color(0.01f, 0.025f, 0.03f, 0.16f));
            shadowImage = CreateLayer("LogoShadow", source, new Color(0.01f, 0.025f, 0.03f, 0.46f));
            var artwork = CreateLayer("LogoVisual", source, source.color);
            visual = artwork.rectTransform;
            shadow = shadowImage.rectTransform;
            softShadow = softShadowImage.rectTransform;
            foreach (var child in children) child.SetParent(visual, false);

            // Keep the original sprite for alpha hit testing, while drawing its
            // artwork separately. Transparent corners and holes aren't targets.
            source.material = null;
            source.color = Color.clear;
            source.raycastTarget = true;
            if (source.sprite != null && source.sprite.texture.isReadable)
                source.alphaHitTestMinimumThreshold = 0.15f;
            ResetVisuals();
        }

        private Image CreateLayer(string layerName, Image source, Color color)
        {
            var layer = new GameObject(layerName, typeof(RectTransform), typeof(Image));
            var rect = layer.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one * 0.5f;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = layer.GetComponent<Image>();
            image.sprite = source.sprite;
            image.preserveAspect = source.preserveAspect;
            image.type = source.type;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static bool InteractionBlocked => SokobanSettingsPanel.IsOpen ||
            SokobanSceneTransition.IsRunning || SokobanRuntimeContext.IsQuitConfirmationOpen || !SokobanAnimationSettings.Enabled;

        private void OnEnable()
        {
            SokobanAnimationSettings.Changed += ApplyAnimationSetting;
            ApplyAnimationSetting(SokobanAnimationSettings.Enabled);
        }
        private void ApplyAnimationSetting(bool enabled) { if (!enabled) ResetVisuals(); }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (InteractionBlocked) return;
            UpdatePointer(eventData);
            if (hovered) return;
            hovered = true;
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (InteractionBlocked) return;
            // Also recover hover if the pointer was already over the logo when
            // an entry transition or modal finished.
            if (!hovered) OnPointerEnter(eventData);
            else UpdatePointer(eventData);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            pointer = Vector2.zero;
        }

        private void UpdatePointer(PointerEventData eventData)
        {
            // Event positions already include the global CRT input correction.
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(hitArea, eventData.position,
                eventData.enterEventCamera, out var local)) return;
            local -= hitArea.rect.center + rotationCenter;
            pointer = new Vector2(
                Mathf.Clamp(local.x / Mathf.Max(1f, hitArea.rect.width * 0.5f), -1f, 1f),
                Mathf.Clamp(local.y / Mathf.Max(1f, hitArea.rect.height * 0.5f), -1f, 1f));
        }

        private void LateUpdate()
        {
            if (visual == null) return;
            if (!SokobanAnimationSettings.Enabled) return;
            if (InteractionBlocked) { hovered = false; pointer = Vector2.zero; }
            var lowEffects = PlayerPrefs.GetInt("Sokoban.LowEffects", 0) != 0;
            var strength = lowEffects ? 0.35f : 1f;
            var delta = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            lift = Mathf.Lerp(lift, hovered ? 1f : 0f, 1f - Mathf.Exp(-14f * delta));
            smoothPointer = Vector2.Lerp(smoothPointer, hovered ? pointer : Vector2.zero, 1f - Mathf.Exp(-10f * delta));

            var time = SokobanAnimationSettings.VisualTime;
            var bob = Mathf.Sin(time * 1.1f) * 3f * strength;
            var roll = Mathf.Sin(time * 0.8f) * 0.55f * strength;
            var position = new Vector2(0f, bob + lift * 14f * strength);
            // Rotate the complete flat artwork about its centre. No per-corner
            // perspective stretch, so the lettering keeps its original shape.
            var tilt = smoothPointer * lift * strength;
            var rotation = Quaternion.Euler(-tilt.y * 5f, -tilt.x * 7f, roll - tilt.x * 1.2f);
            PlaceLayer(visual, position, rotation, 1f + lift * 0.012f * strength);

            var offset = new Vector2(5f, -18f - lift * 9f * strength) - smoothPointer * lift * 6f * strength;
            PlaceLayer(shadow, position + offset, rotation, 1f + lift * 0.017f * strength);
            PlaceLayer(softShadow, position + offset + Vector2.down * 5f, rotation, 1.009f + lift * 0.022f * strength);
            shadowImage.color = new Color(0.01f, 0.025f, 0.03f, Mathf.Lerp(0.46f, 0.38f, lift));
        }

        private void PlaceLayer(RectTransform rect, Vector2 position, Quaternion rotation, float scale)
        {
            // Keep the chosen artwork point fixed under rotation and uniform
            // scaling, including depth, without moving the layout or hit mask.
            var center = (Vector3)rotationCenter;
            rect.anchoredPosition3D = (Vector3)position + center - rotation * (center * scale);
            rect.localRotation = rotation;
            rect.localScale = Vector3.one * scale;
        }

        private void ResetVisuals()
        {
            hovered = false;
            lift = 0f;
            pointer = smoothPointer = Vector2.zero;
            if (visual == null) return;
            PlaceLayer(visual, Vector2.zero, Quaternion.identity, 1f);
            PlaceLayer(shadow, new Vector2(5f, -18f), Quaternion.identity, 1f);
            PlaceLayer(softShadow, new Vector2(5f, -23f), Quaternion.identity, 1.009f);
            shadowImage.color = new Color(0.01f, 0.025f, 0.03f, 0.46f);
        }

        private void OnDisable()
        {
            SokobanAnimationSettings.Changed -= ApplyAnimationSetting;
            ResetVisuals();
        }
    }
}
