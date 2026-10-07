using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kuluobishi.Sokoban
{
    /// <summary>普通玩家流程的轻量音频服务，使用提取的 Balatro 音频资源。</summary>
    public sealed class SokobanPresentationAudio : MonoBehaviour
    {
        public const float DefaultVolume = 0.5f;
        public const float MaximumVolume = 0.4f;
        private static float MusicGain => Mathf.Clamp01(PlayerPrefs.GetFloat("Sokoban.MusicVolume", DefaultVolume)) * MaximumVolume;
        private static float SoundGain => Mathf.Clamp01(PlayerPrefs.GetFloat("Sokoban.SfxVolume", DefaultVolume)) * MaximumVolume;
        private static SokobanPresentationAudio instance;
        private AudioSource music;
        private AudioSource ui;
        private AudioSource sfx;
        private string currentMusic;
        private double nextHoverTime = double.NegativeInfinity;

        public static void PlayHover()
        {
            var service = Ensure();
            var now = Time.unscaledTimeAsDouble;
            // One shared cooldown across buttons; skip rapid entries without
            // queuing a delayed sound. Unscaled time also works in pause menus.
            if (now < service.nextHoverTime) return;
            service.nextHoverTime = now + 0.1;
            PlayUi("card3", 0.7f);
        }

        public static SokobanPresentationAudio Ensure()
        {
            if (instance != null) return instance;
            var root = new GameObject("SokobanPresentationAudio");
            instance = root.AddComponent<SokobanPresentationAudio>();
            DontDestroyOnLoad(root);
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            music = CreateSource("Music", true);
            ui = CreateSource("UI", false);
            sfx = CreateSource("SFX", false);
            SceneManager.sceneLoaded += OnSceneLoaded;
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.IsValid() && activeScene.isLoaded)
                OnSceneLoaded(activeScene, LoadSceneMode.Single);
        }

        private void OnDestroy()
        {
            if (instance == this) SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private AudioSource CreateSource(string name, bool loop)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            var source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = loop;
            source.volume = loop ? MusicGain : SoundGain;
            return source;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var clipName = scene.name == "game" ? "music3" : scene.name == "level" ? "music2" : "music1";
            PlayMusic(clipName);
        }

        private void PlayMusic(string clipName)
        {
            if (music == null || currentMusic == clipName) return;
            var clip = Resources.Load<AudioClip>("Balatro/sounds/" + clipName);
            if (clip == null) return;
            currentMusic = clipName;
            music.clip = clip;
            music.volume = MusicGain;
            music.Play();
        }

        public static void PlayUi(string clipName, float gain = 1f)
        {
            var service = Ensure();
            if (service.ui == null) return;
            var clip = Resources.Load<AudioClip>("Balatro/sounds/" + clipName);
            if (clip == null) return;
            service.ui.volume = SoundGain;
            service.ui.PlayOneShot(clip, gain);
        }

        public static void PlaySfx(string clipName, float gain = 1f)
        {
            var service = Ensure();
            if (service.sfx == null) return;
            var clip = Resources.Load<AudioClip>("Balatro/sounds/" + clipName);
            if (clip == null) return;
            service.sfx.volume = SoundGain;
            service.sfx.PlayOneShot(clip, gain);
        }

        public static void RefreshVolumes()
        {
            if (instance == null) return;
            instance.music.volume = MusicGain;
            var volume = SoundGain;
            instance.ui.volume = volume;
            instance.sfx.volume = volume;
        }
    }
}
