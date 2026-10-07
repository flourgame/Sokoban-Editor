using System;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    /// <summary>Desktop display preference; saves the window size independently of fullscreen.</summary>
    [DefaultExecutionOrder(-1300)]
    public sealed class SokobanDisplaySettings : MonoBehaviour
    {
        public const int MinimumWidth = 1280, MinimumHeight = 720;
        private static SokobanDisplaySettings instance;
        private static string preferencePrefix = "Sokoban.Display.";
        private static bool borderless;
        private static Vector2Int windowSize;
        private bool pending;
        private float requestDeadline, resizeTime, appliedAt;
        private Vector2Int requestedSize, observedSize;
        public static event Action Changed;
        public static bool Supported => Application.platform == RuntimePlatform.WindowsPlayer;
        public static bool Borderless => borderless;
        public static bool IsSwitching => instance != null && instance.pending;
        public static Vector2Int WindowSize => windowSize;
        public static string ModeKey => preferencePrefix + "Borderless";
        public static string WidthKey => preferencePrefix + "WindowWidth";
        public static string HeightKey => preferencePrefix + "WindowHeight";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            instance = null; Changed = null;
            preferencePrefix = "Sokoban.Display.";
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Standalone checks keep display preferences separate from the user's settings.
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "-sokobanSmokeDir");
            if (index >= 0 && index + 1 < args.Length)
                preferencePrefix += "Smoke." + System.IO.Path.GetFullPath(args[index + 1]) + ".";
#endif
            borderless = PlayerPrefs.GetInt(ModeKey, 0) != 0;
            windowSize = ClampWindowSize(new Vector2Int(
                PlayerPrefs.GetInt(WidthKey, Mathf.Max(MinimumWidth, Screen.width)),
                PlayerPrefs.GetInt(HeightKey, Mathf.Max(MinimumHeight, Screen.height))));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance == null) new GameObject("SokobanDisplaySettings").AddComponent<SokobanDisplaySettings>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this; DontDestroyOnLoad(gameObject);
            if (Supported) Apply();
        }

        public static void SetBorderless(bool value)
        {
            if (!Supported || instance == null || instance.pending) return;
            if (borderless == value) return;
            instance.CaptureWindowSize();
            borderless = value;
            instance.Save();
            instance.Apply();
        }

        private static Vector2Int DesktopSize()
        {
            var resolution = Screen.currentResolution;
            return new Vector2Int(Mathf.Max(1, resolution.width), Mathf.Max(1, resolution.height));
        }

        private static Vector2Int ClampWindowSize(Vector2Int size)
        {
            var desktop = DesktopSize();
            return new Vector2Int(Mathf.Clamp(size.x, Mathf.Min(MinimumWidth, desktop.x), desktop.x),
                Mathf.Clamp(size.y, Mathf.Min(MinimumHeight, desktop.y), desktop.y));
        }

        private void Apply()
        {
            windowSize = ClampWindowSize(windowSize);
            requestedSize = borderless ? DesktopSize() : windowSize;
            pending = true; appliedAt = -1f; requestDeadline = Time.realtimeSinceStartup + 3f;
            var mode = borderless ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (Screen.width != requestedSize.x || Screen.height != requestedSize.y || Screen.fullScreenMode != mode)
                Screen.SetResolution(requestedSize.x, requestedSize.y, mode);
            Changed?.Invoke();
        }

        private void Update()
        {
            if (!Supported || Screen.width <= 0 || Screen.height <= 0) return;
            var size = new Vector2Int(Screen.width, Screen.height);
            var actualBorderless = Screen.fullScreenMode != FullScreenMode.Windowed;
            if (pending)
            {
                // SetResolution is deferred. Never record transient fullscreen dimensions as a window size.
                if (actualBorderless != borderless || size != requestedSize)
                {
                    appliedAt = -1f;
                    if (Time.realtimeSinceStartup < requestDeadline) return;
                }
                else
                {
                    if (appliedAt < 0f) appliedAt = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - appliedAt < 0.5f) return;
                }
                pending = false; borderless = actualBorderless;
                observedSize = size; resizeTime = Time.realtimeSinceStartup;
                CaptureWindowSize(); Save(); Changed?.Invoke();
                return;
            }
            if (actualBorderless != borderless)
            {
                // Also observe display changes made by the operating system.
                borderless = actualBorderless; Save(); Changed?.Invoke();
            }
            if (borderless) return;
            if (observedSize != size)
            {
                observedSize = size; resizeTime = Time.realtimeSinceStartup;
                Changed?.Invoke();
            }
            if (Time.realtimeSinceStartup - resizeTime < 0.5f) return;
            var clamped = ClampWindowSize(size);
            if (clamped != size) { windowSize = clamped; Apply(); return; }
            if (windowSize != size) { windowSize = size; Save(); }
        }

        private void CaptureWindowSize()
        {
            if (!Supported || pending || Screen.fullScreenMode != FullScreenMode.Windowed || Screen.width <= 0 || Screen.height <= 0) return;
            windowSize = ClampWindowSize(new Vector2Int(Screen.width, Screen.height));
        }

        private void Save()
        {
            PlayerPrefs.SetInt(ModeKey, borderless ? 1 : 0);
            PlayerPrefs.SetInt(WidthKey, windowSize.x); PlayerPrefs.SetInt(HeightKey, windowSize.y);
            PlayerPrefs.Save();
        }

        private void OnApplicationFocus(bool focus) { if (!focus && Supported) { CaptureWindowSize(); Save(); } }
        private void OnApplicationQuit() { if (Supported) { CaptureWindowSize(); Save(); } }
        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
