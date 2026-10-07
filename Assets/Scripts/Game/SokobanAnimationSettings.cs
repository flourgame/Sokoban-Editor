using System;
using UnityEngine;

namespace Kuluobishi.Sokoban
{
    /// <summary>Saved motion preference and a visual-only clock; gameplay time is independent.</summary>
    [DefaultExecutionOrder(-1200)]
    public sealed class SokobanAnimationSettings : MonoBehaviour
    {
        public const string PreferenceKey = "Sokoban.AnimationsEnabled";
        private static SokobanAnimationSettings instance;
        private static bool? enabledPreference;
        private static readonly int ShaderTime = Shader.PropertyToID("_SokobanAnimationTime");
        public static event Action<bool> Changed;
        public static float VisualTime { get; private set; }
        public static bool Enabled
        {
            get
            {
                if (!enabledPreference.HasValue) enabledPreference = PlayerPrefs.GetInt(PreferenceKey, 1) != 0;
                return enabledPreference.Value;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            instance = null;
            enabledPreference = null;
            Changed = null;
            VisualTime = 0f;
            Shader.SetGlobalFloat(ShaderTime, VisualTime);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance == null) new GameObject("SokobanAnimationSettings").AddComponent<SokobanAnimationSettings>();
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public static void SetEnabled(bool value)
        {
            var changed = Enabled != value;
            enabledPreference = value;
            PlayerPrefs.SetInt(PreferenceKey, value ? 1 : 0);
            PlayerPrefs.Save();
            if (changed) Changed?.Invoke(value);
        }

        private void Update()
        {
            if (!Enabled) return;
            VisualTime += Time.unscaledDeltaTime;
            Shader.SetGlobalFloat(ShaderTime, VisualTime);
        }

        private void OnDestroy() { if (instance == this) instance = null; }
    }
}
