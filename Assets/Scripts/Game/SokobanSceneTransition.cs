using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kuluobishi.Sokoban
{
    /// <summary>Persistent iris: close before loading, reveal after the new UI is ready.</summary>
    public sealed class SokobanSceneTransition : MonoBehaviour
    {
        private static SokobanSceneTransition instance;
        private Image overlay;
        private Material material;
        private bool running;
        public static bool IsRunning => instance != null && instance.running;
        private static readonly int Radius = Shader.PropertyToID("_Radius");
        private static readonly int Aspect = Shader.PropertyToID("_Aspect");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() { Ensure(); }

        public static void Load(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName) || IsRunning) return;
            if (!SokobanAnimationSettings.Enabled || SceneManager.GetActiveScene().name == "editor" || sceneName == "editor")
            {
                SceneManager.LoadScene(sceneName);
                return;
            }
            var transition = Ensure();
            transition.running = true;
            transition.StartCoroutine(transition.Run(sceneName));
        }

        private static SokobanSceneTransition Ensure()
        {
            if (instance != null) return instance;
            var root = new GameObject("SokobanSceneTransition");
            return root.AddComponent<SokobanSceneTransition>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5000;
            gameObject.AddComponent<GraphicRaycaster>();
            var mask = new GameObject("CircleMask", typeof(RectTransform), typeof(Image));
            var rect = mask.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            overlay = mask.GetComponent<Image>();
            overlay.color = Color.black;
            overlay.raycastTarget = true;
            var shader = Shader.Find("UI/SokobanCircleTransition");
            if (shader != null)
            {
                material = new Material(shader) { name = "CircleTransition_Runtime" };
                material.SetColor("_Color", Color.black);
                overlay.material = material;
            }
            overlay.enabled = false;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SokobanAnimationSettings.Changed += ApplyAnimationSetting;
        }

        // Keep an in-flight scene load alive, but remove the animated mask immediately.
        private void ApplyAnimationSetting(bool enabled) { if (!enabled && overlay != null) overlay.enabled = false; }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (running) return;
            if (!SokobanAnimationSettings.Enabled || (scene.name != "start" && scene.name != "level" && scene.name != "game"))
            {
                overlay.enabled = false;
                return;
            }
            running = true;
            SetRadius(-0.06f);
            overlay.enabled = true;
            StartCoroutine(Reveal());
        }

        private IEnumerator Run(string sceneName)
        {
            overlay.enabled = true;
            SetRadius(OpenRadius());
            yield return AnimateRadius(OpenRadius(), -0.06f, 0.34f);
            var load = SceneManager.LoadSceneAsync(sceneName);
            while (load != null && !load.isDone) yield return null;
            yield return Reveal();
        }

        private IEnumerator Reveal()
        {
            if (!SokobanAnimationSettings.Enabled)
            {
                overlay.enabled = false;
                running = false;
                yield break;
            }
            SetRadius(-0.06f);
            // Start and layout run under the opaque mask. WaitForEndOfFrame can
            // stall when the Game view loses focus, so use ordinary frame waits.
            yield return null;
            Canvas.ForceUpdateCanvases();
            yield return null;
            yield return AnimateRadius(-0.06f, OpenRadius(), 0.56f);
            overlay.enabled = false;
            running = false;
        }

        private float OpenRadius()
        {
            var aspect = (float)Screen.width / Mathf.Max(1, Screen.height);
            return new Vector2(aspect * 0.5f, 0.5f).magnitude + 0.1f;
        }

        private IEnumerator AnimateRadius(float from, float to, float duration)
        {
            var elapsed = 0f;
            SetRadius(from);
            while (elapsed < duration && SokobanAnimationSettings.Enabled)
            {
                yield return null;
                elapsed += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(elapsed / duration);
                SetRadius(Mathf.Lerp(from, to, p * p * (3f - 2f * p)));
            }
            SetRadius(to);
        }

        private void SetRadius(float radius)
        {
            if (material == null) return;
            material.SetFloat(Radius, radius);
            material.SetFloat(Aspect, (float)Screen.width / Mathf.Max(1, Screen.height));
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SokobanAnimationSettings.Changed -= ApplyAnimationSetting;
            if (material != null) Destroy(material);
            if (instance == this) instance = null;
        }
    }
}
