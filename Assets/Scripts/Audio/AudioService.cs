using System.Collections;
using LustHouse.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LustHouse.Audio
{
    /// <summary>
    /// The live sound system: a small pool of voices, a loop channel, and the volume
    /// model the player's settings ride on.
    ///
    /// It lives on the services root, so it survives every scene change and the
    /// player's volume settings survive with it.
    ///
    /// The one thing it guarantees that nothing else does: **there is an audio
    /// listener**. A scene with no listener plays nothing at all, and it looks
    /// perfectly correct while it does it — the code runs, the logs are clean, and the
    /// game is silent. The cameras here are built in code, and a camera built in code
    /// is not given a listener, so the service makes one per scene rather than
    /// trusting that somebody remembered.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioService : MonoBehaviour, IAudioOutput
    {
        /// <summary>The live service, or null when there is not one.</summary>
        public static AudioService Instance { get; private set; }

        [Header("Voices")]
        [Tooltip("How many one-shots can overlap before the oldest is cut off.")]
        [Range(2, 16)]
        public int voiceCount = 8;

        [Header("Mix")]
        [Range(0f, 1f)] public float masterVolume = 1f;
        [Range(0f, 1f)] public float sfxVolume = 1f;
        [Range(0f, 1f)] public float musicVolume = 1f;
        public bool muted = false;

        [Header("Loop")]
        [Tooltip("How long the loop takes to fade out when it stops. Without it the " +
                 "reel sound ends on a click.")]
        public float loopFadeSeconds = 0.08f;

        [Header("Content")]
        [Tooltip("Sound overrides for the current game. Cues with no entry fall back " +
                 "to the synthesised clip.")]
        public AudioBank bank;

        private AudioSource[] _voices;
        private int _nextVoice;
        private AudioSource _loop;
        private AudioSource _music;
        private Coroutine _loopFade;
        private AudioCue _loopCue = AudioCue.None;
        private float _loopBaseVolume = 1f;
        private float _sweepTimer;

        // ---- lifecycle -------------------------------------------------------

        private void Awake()
        {
            // Two services would fight over the listener and the volumes.
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            BuildSources();
            EnsureListener(SceneManager.GetActiveScene());
            ApplyVolumes();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Generates every sound up front, so the first press of the session does not
        /// pay for the synthesis of its own click.
        /// </summary>
        public static void WarmUp() => ProceduralAudio.WarmUp();

        private void BuildSources()
        {
            int voices = Mathf.Clamp(voiceCount, 2, 16);

            var root = new GameObject("Voices");
            root.transform.SetParent(transform, false);

            _voices = new AudioSource[voices];
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject("Voice" + i);
                go.transform.SetParent(root.transform, false);

                var source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;      // the UI is not in the world
                source.loop = false;
                _voices[i] = source;
            }

            var loopGo = new GameObject("Loop");
            loopGo.transform.SetParent(transform, false);
            _loop = loopGo.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.spatialBlend = 0f;
            _loop.loop = true;

            var musicGo = new GameObject("Music");
            musicGo.transform.SetParent(transform, false);
            _music = musicGo.AddComponent<AudioSource>();
            _music.playOnAwake = false;
            _music.spatialBlend = 0f;
            _music.loop = true;
        }

        // ---- the listener ----------------------------------------------------

        /// <summary>
        /// Makes sure the scene has an audio listener, and adds one if it does not.
        ///
        /// The listener belongs to the scene, not to this service: a listener on the
        /// services root would survive every scene change and leave the new scene
        /// with two of them, which Unity resolves by arbitrarily silencing one.
        /// </summary>
        private static void EnsureListener(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return;

            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                var listener = roots[i].GetComponentInChildren<AudioListener>(true);
                if (listener != null && listener.enabled) return;
            }

            var go = new GameObject("AudioListener");
            if (go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<AudioListener>();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureListener(scene);

        // ---- the output ------------------------------------------------------

        public void Play(AudioCue cue, float pitch = 1f)
        {
            if (cue == AudioCue.None) return;

            var clip = ClipFor(cue);
            if (clip == null) return;

            var source = NextVoice();
            if (source == null) return;

            source.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            source.PlayOneShot(clip, Mathf.Clamp01(sfxVolume) * EntryVolume(cue));
        }

        public void PlayLoop(AudioCue cue)
        {
            if (cue == AudioCue.None || _loop == null) return;

            var clip = ClipFor(cue);
            if (clip == null) return;

            // A loop that is already running is left running. Restarting it on every
            // call is a stutter, and the reel sound is asked for once per spin.
            if (_loop.isPlaying && _loop.clip == clip) return;

            CancelFade();
            _loop.clip = clip;
            _loop.loop = true;
            _loopCue = cue;
            _loopBaseVolume = Mathf.Clamp01(sfxVolume) * EntryVolume(cue);
            _loop.volume = _loopBaseVolume;
            _loop.Play();
        }

        public void StopLoop(AudioCue cue)
        {
            if (_loop == null || !_loop.isPlaying) return;

            CancelFade();
            _loopFade = StartCoroutine(FadeOutLoop());
        }

        /// <summary>
        /// Fades the loop out and stops it. A loop that stops dead ends on a step, and
        /// the step is heard over the top of whatever the game does next.
        /// </summary>
        private IEnumerator FadeOutLoop()
        {
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, loopFadeSeconds);

            while (elapsed < duration && _loop != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                if (_loop != null) _loop.volume = _loopBaseVolume * (1f - t);
                yield return null;
            }

            if (_loop != null)
            {
                _loop.Stop();
                _loop.volume = _loopBaseVolume;
            }

            _loopFade = null;
        }

        private void CancelFade()
        {
            if (_loopFade == null) return;
            StopCoroutine(_loopFade);
            _loopFade = null;
            if (_loop != null) _loop.volume = _loopBaseVolume;
        }

        private AudioSource NextVoice()
        {
            if (_voices == null || _voices.Length == 0) return null;

            var source = _voices[_nextVoice];
            _nextVoice = (_nextVoice + 1) % _voices.Length;
            return source;
        }

        /// <summary>The clip for a cue: the game's own if it has one, else the
        /// synthesised one.</summary>
        public AudioClip ClipFor(AudioCue cue)
        {
            var entry = bank != null ? bank.Find(cue) : null;
            if (entry != null && entry.clip != null) return entry.clip;
            return ProceduralAudio.ClipFor(cue);
        }

        private float EntryVolume(AudioCue cue)
        {
            var entry = bank != null ? bank.Find(cue) : null;
            return entry != null ? Mathf.Clamp01(entry.volume) : 1f;
        }

        /// <summary>Switches the game whose sound overrides are in use.</summary>
        public void SetBank(AudioBank next) => bank = next;

        // ---- volume ----------------------------------------------------------

        /// <summary>
        /// The volume a sound actually comes out at.
        ///
        /// Pure on purpose: the mix is worth testing without a speaker, a listener, or
        /// a scene.
        /// </summary>
        public static float EffectiveVolume(float master, float sfx, bool isMuted)
        {
            if (isMuted) return 0f;
            return Mathf.Clamp01(master) * Mathf.Clamp01(sfx);
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            ApplyVolumes();
            MarkDirty();
        }

        public void SetSfxVolume(float value)
        {
            sfxVolume = Mathf.Clamp01(value);
            ApplyVolumes();
            MarkDirty();
        }

        public void SetMusicVolume(float value)
        {
            musicVolume = Mathf.Clamp01(value);
            ApplyVolumes();
            MarkDirty();
        }

        public void SetMuted(bool value)
        {
            muted = value;
            ApplyVolumes();
            MarkDirty();
        }

        public void ApplyVolumes()
        {
            AudioListener.volume = muted ? 0f : Mathf.Clamp01(masterVolume);

            if (_loop != null)
            {
                // The loop's own level is worked out again, so turning the sound down
                // while the reels are turning turns the reel sound down with it.
                _loopBaseVolume = Mathf.Clamp01(sfxVolume) * EntryVolume(_loopCue);
                if (_loopFade == null) _loop.volume = _loopBaseVolume;
            }

            if (_music != null) _music.volume = Mathf.Clamp01(musicVolume);
        }

        /// <summary>Copies the volumes into a save document's settings.</summary>
        public void CopyVolumesInto(SettingsState settings)
        {
            if (settings == null) return;
            settings.masterVolume = masterVolume;
            settings.musicVolume = musicVolume;
            settings.sfxVolume = sfxVolume;
        }

        /// <summary>Reads the volumes back out of a save document's settings.</summary>
        public void ApplyVolumesFrom(SettingsState settings)
        {
            if (settings == null) return;
            masterVolume = Mathf.Clamp01(settings.masterVolume);
            musicVolume = Mathf.Clamp01(settings.musicVolume);
            sfxVolume = Mathf.Clamp01(settings.sfxVolume);
            ApplyVolumes();
        }

        private static void MarkDirty()
        {
            var services = ServicesBootstrap.Instance;
            if (services != null) services.MarkDirty();
        }

        // ---- button clicks ---------------------------------------------------

        /// <summary>
        /// Gives a click to every button that does not have one yet.
        ///
        /// Most buttons are wired where they are built, but some are made with a bare
        /// <c>AddComponent&lt;Button&gt;</c> in a screen far away from here, and a
        /// button with no sound is a button the player cannot hear. Rather than trust
        /// every screen to remember, this sweeps for them once a second. It is cheap:
        /// a screen holds tens of buttons, not thousands.
        /// </summary>
        private void Update()
        {
            _sweepTimer += Time.unscaledDeltaTime;
            if (_sweepTimer < 1f) return;
            _sweepTimer = 0f;

            AttachMissingClickSounds();
        }

        private static void AttachMissingClickSounds()
        {
            var buttons = Object.FindObjectsByType<Button>(FindObjectsSortMode.None);
            for (int i = 0; i < buttons.Length; i++)
            {
                var button = buttons[i];
                if (button == null) continue;
                if (button.GetComponent<ButtonClickSound>() != null) continue;
                AudioFeedback.AttachClickSound(button, AudioCue.UiClick);
            }
        }
    }
}
