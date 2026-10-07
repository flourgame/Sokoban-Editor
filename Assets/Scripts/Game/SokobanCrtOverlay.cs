using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>Single full-screen Balatro CRT pass above all player UI, below transitions.</summary>
    public sealed class SokobanCrtOverlay : MonoBehaviour
    {
        private static SokobanCrtOverlay current;
        private Material material;
        private Image image;
        private float curvature;
        private int screenHeight;
        private bool lowEffects;
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        private static readonly int Scanlines = Shader.PropertyToID("_Scanlines");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic() { current = null; }

        public static SokobanCrtOverlay Attach(Transform parent)
        {
            if (current != null) return current;
            if (parent == null || UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "editor") return null;
            var root = new GameObject("BalatroCRTCanvas", typeof(RectTransform), typeof(Canvas));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4000;
            var visual = new GameObject("BalatroCRT", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(SokobanCrtOverlay));
            visual.transform.SetParent(root.transform, false);
            var rect = visual.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            current = visual.GetComponent<SokobanCrtOverlay>();
            current.Setup();
            return current;
        }

        private void Setup()
        {
            image = GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.white;
            var shader = Shader.Find("UI/SokobanCRTOverlay");
            if (shader == null || !shader.isSupported) { image.enabled = false; return; }
            material = new Material(shader) { name = "BalatroCRT_Runtime" };
            image.material = material;
            SetIntensity(PlayerPrefs.GetFloat("Sokoban.CRTIntensity", 0.22f));
            SokobanUI.EnsureEventSystem();
            var module = EventSystem.current.GetComponent<StandaloneInputModule>();
            if (module != null && module.inputOverride == null)
            {
                var input = module.GetComponent<SokobanCrtInput>();
                if (input == null) input = module.gameObject.AddComponent<SokobanCrtInput>();
                module.inputOverride = input;
            }
        }

        public void SetIntensity(float value)
        {
            if (material == null) return;
            value = Mathf.Clamp01(value);
            lowEffects = PlayerPrefs.GetInt("Sokoban.LowEffects", 0) != 0;
            var strength = Mathf.Clamp01(value / 0.22f);
            curvature = 0.0075f * strength * (lowEffects ? 0.5f : 1f);
            image.enabled = value > 0f;
            material.SetFloat(Intensity, value * (lowEffects ? 0.45f : 1f));
            material.SetFloat("_Curvature", curvature);
            material.SetFloat("_Vignette", 0.32f * strength * (lowEffects ? 0.65f : 1f));
            material.SetFloat("_NoiseAmount", lowEffects ? 0f : 0.006f * strength);
            material.SetFloat("_Chromatic", lowEffects ? 0f : 0.35f * strength);
            UpdateResolution();
        }

        private void UpdateResolution()
        {
            screenHeight = Screen.height;
            material.SetFloat(Scanlines, Mathf.Max(1f, screenHeight / (lowEffects ? 8f : 6f)));
        }

        private void LateUpdate()
        {
            if (material != null && screenHeight != Screen.height) UpdateResolution();
        }

        /// <summary>Map the hardware cursor to the source UI sampled under that pixel.</summary>
        public static Vector2 ScreenToSource(Vector2 position)
        {
            if (current == null || current.image == null || !current.image.isActiveAndEnabled || Screen.width <= 0 || Screen.height <= 0) return position;
            var p = new Vector2(position.x / Screen.width, position.y / Screen.height) * 2f - Vector2.one;
            var warped = p + new Vector2(p.y * p.y * p.x, p.x * p.x * p.y) * current.curvature;
            return Vector2.Scale((warped + Vector2.one) * 0.5f, new Vector2(Screen.width, Screen.height));
        }

        public static void RefreshAll()
        {
            if (current != null) current.SetIntensity(PlayerPrefs.GetFloat("Sokoban.CRTIntensity", 0.22f));
        }

        private void OnDestroy()
        {
            if (current == this) current = null;
            if (material != null) Destroy(material);
        }
    }
}
