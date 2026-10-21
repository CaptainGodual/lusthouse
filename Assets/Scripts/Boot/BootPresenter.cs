using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LustHouse.Boot
{
    /// <summary>
    /// Builds and drives the loading screen UI (plan §1.2, §1.5).
    ///
    /// The UI is constructed in code rather than authored into the scene on purpose:
    /// scene-authored UI silently fails to appear when a reference is missing, which is
    /// exactly how the first greybox ended up as a black screen with no way forward.
    /// Building it here means the only way to get a blank screen is for this component to
    /// be absent from the scene entirely.
    ///
    /// Layout is anchored from a 1920x1080 reference (§2.1) and scaled by CanvasScaler.
    /// </summary>
    [DisallowMultipleComponent]
    public class BootPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public BootSequence sequence;

        [Header("Palette (plan §2.1)")]
        public Color velvetBase = UI.UiFactory.VelvetBase;
        public Color velvetRaised = UI.UiFactory.VelvetRaised;
        public Color goldPrimary = UI.UiFactory.GoldPrimary;
        public Color goldGlow = UI.UiFactory.GoldGlow;
        public Color neonMagenta = UI.UiFactory.NeonMagenta;
        public Color textPrimary = UI.UiFactory.TextPrimary;
        public Color textSecondary = UI.UiFactory.TextSecondary;

        [Header("Timing")]
        [Tooltip("Soft-pulse period for the TAP TO ENTER prompt, seconds.")]
        public float pulsePeriod = 1.4f;

        private Image _fillImage;
        private Text _stageLabel;
        private Text _title;
        private Text _subtitle;
        private CanvasGroup _enterGroup;
        private Button _enterButton;
        private Canvas _canvas;
        private UI.AgeGateOverlay _ageGate;

        private bool _ready;

        // ---- construction -----------------------------------------------------

        private void Awake()
        {
            if (sequence == null) sequence = GetComponent<BootSequence>();

            // The age gate asks the player whether they are 18, and it can only know
            // the answer if the save has been read. The services are the save: without
            // them there is no player to record an answer against, and the gate would
            // ask the same question on every launch.
            Core.ServicesBootstrap.EnsureExists();

            UI.UiFactory.EnsureEventSystem();
            _canvas = UI.UiFactory.ResolveCanvas(transform, "BootCanvas", 100);
            BuildUI();
        }

        private void OnEnable()
        {
            if (sequence == null) return;

            sequence.ProgressChanged += OnProgress;
            sequence.Ready += OnReady;

            // Catch up if the sequence finished before this subscribed.
            OnProgress(sequence.Progress01, sequence.StageLabel);
            if (sequence.ReadyToEnter) OnReady();
        }

        private void OnDisable()
        {
            if (sequence == null) return;
            sequence.ProgressChanged -= OnProgress;
            sequence.Ready -= OnReady;
        }



        private void BuildUI()
        {
            var root = _canvas.transform as RectTransform;

            // Backdrop.
            var bg = UI.UiFactory.NewImage("Backdrop", root, velvetBase);
            UI.UiFactory.Stretch(bg.rectTransform);

            // A soft magenta bloom behind the wordmark (plan §1.2, neon ignition).
            var bloom = UI.UiFactory.NewImage("NeonBloom", root, new Color(neonMagenta.r, neonMagenta.g, neonMagenta.b, 0.18f));
            var bloomRect = bloom.rectTransform;
            bloomRect.anchorMin = new Vector2(0.5f, 0.62f);
            bloomRect.anchorMax = new Vector2(0.5f, 0.62f);
            bloomRect.sizeDelta = new Vector2(900, 220);

            _title = UI.UiFactory.NewText("Title", root, "L U S T   H O U S E", 64, FontStyle.Bold, textPrimary);
            var titleRect = _title.rectTransform;
            titleRect.anchorMin = new Vector2(0.5f, 0.62f);
            titleRect.anchorMax = new Vector2(0.5f, 0.62f);
            titleRect.sizeDelta = new Vector2(1000, 100);
            // +4% tracking on display type (§2.1).
            _title.supportRichText = false;

            _subtitle = UI.UiFactory.NewText("Subtitle", root, "Play money only.  18+", 20, FontStyle.Normal, textSecondary);
            var subRect = _subtitle.rectTransform;
            subRect.anchorMin = new Vector2(0.5f, 0.53f);
            subRect.anchorMax = new Vector2(0.5f, 0.53f);
            subRect.sizeDelta = new Vector2(900, 40);

            // ---- progress bar, bottom-centre, 420px wide (§1.2 beat 3) ----
            const float barWidth = 420f;
            const float barHeight = 8f;
            const float barY = 180f;

            var track = UI.UiFactory.NewImage("ProgressTrack", root, velvetRaised);
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0.5f, 0f);
            trackRect.anchorMax = new Vector2(0.5f, 0f);
            trackRect.pivot = new Vector2(0.5f, 0f);
            trackRect.anchoredPosition = new Vector2(0, barY);
            trackRect.sizeDelta = new Vector2(barWidth, barHeight);

            _fillImage = UI.UiFactory.NewImage("ProgressFill", trackRect, goldPrimary);
            UI.UiFactory.Stretch(_fillImage.rectTransform, 2f);
            _fillImage.type = Image.Type.Filled;
            _fillImage.fillMethod = Image.FillMethod.Horizontal;
            _fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fillImage.fillAmount = 0f;

            _stageLabel = UI.UiFactory.NewText("StageLabel", root, "Starting…", 18, FontStyle.Normal, textSecondary);
            var stageRect = _stageLabel.rectTransform;
            stageRect.anchorMin = new Vector2(0.5f, 0f);
            stageRect.anchorMax = new Vector2(0.5f, 0f);
            stageRect.pivot = new Vector2(0.5f, 0f);
            stageRect.anchoredPosition = new Vector2(0, barY - 44f);
            stageRect.sizeDelta = new Vector2(barWidth + 240, 30);

            // ---- TAP TO ENTER ----
            var enterGo = new GameObject("TapToEnter", typeof(RectTransform));
            enterGo.transform.SetParent(root, false);
            var enterRect = (RectTransform)enterGo.transform;
            UI.UiFactory.Stretch(enterRect);

            _enterGroup = enterGo.AddComponent<CanvasGroup>();
            _enterGroup.alpha = 0f;
            _enterGroup.interactable = false;
            _enterGroup.blocksRaycasts = false;

            // Full-screen transparent button: a tap anywhere advances, so the greybox
            // never dead-ends. The visible prompt is a child of it.
            var buttonImage = enterGo.AddComponent<Image>();
            buttonImage.sprite = UI.UiFactory.Solid;
            buttonImage.color = new Color(0f, 0f, 0f, 0f);   // invisible hit area
            buttonImage.raycastTarget = true;

            _enterButton = enterGo.AddComponent<Button>();
            _enterButton.targetGraphic = buttonImage;
            _enterButton.onClick.AddListener(OnEnterClicked);

            var prompt = UI.UiFactory.NewText("Prompt", enterRect, "T A P   T O   E N T E R", 40, FontStyle.Bold, goldGlow);
            var promptRect = prompt.rectTransform;
            promptRect.anchorMin = new Vector2(0.5f, 0.5f);
            promptRect.anchorMax = new Vector2(0.5f, 0.5f);
            promptRect.sizeDelta = new Vector2(900, 80);
        }

        // ---- events -----------------------------------------------------------

        private void OnProgress(float progress, string label)
        {
            if (_fillImage != null) _fillImage.fillAmount = Mathf.Clamp01(progress);

            if (_stageLabel == null) return;

            // Compose the string fresh each time. Assigning to .text twice would append
            // the percentage to the previous frame's text.
            if (string.IsNullOrEmpty(label)) label = "Starting…";
            _stageLabel.text = Mathf.RoundToInt(Mathf.Clamp01(progress) * 100f) + "%  ·  " + label;
        }

        private void OnReady()
        {
            if (_ready) return;
            _ready = true;

            if (_enterGroup != null)
            {
                _enterGroup.alpha = 1f;
                _enterGroup.interactable = true;
                _enterGroup.blocksRaycasts = true;
            }
        }

        private void OnEnterClicked()
        {
            if (!_ready || sequence == null) return;
            _enterButton.interactable = false;

            // The age gate comes before the lobby, not inside it (plan §2.3a). Nothing
            // in the game — not the lobby, not a game, not a purchase — is reachable
            // until the player has said they are 18 or over, and the only way into
            // the lobby is through here.
            if (!Core.AgeGate.IsConfirmed)
            {
                if (_ageGate == null)
                {
                    _ageGate = UI.AgeGateOverlay.Create(_canvas.transform, OnAgeConfirmed);
                    _ageGate.transform.SetAsLastSibling();
                }
                return;
            }

            EnterLobby();
        }

        /// <summary>The player answered the age question. On to the lobby.</summary>
        private void OnAgeConfirmed()
        {
            if (_ageGate != null)
            {
                _ageGate.Hide();
                _ageGate = null;
            }

            EnterLobby();
        }

        private void EnterLobby()
        {
            if (sequence != null) sequence.EnterLobby();
        }

        private void Update()
        {
            float t = Time.unscaledTime;

            // Beat 1 — the neon sign flickers on in two stutters, then holds (§1.2).
            if (_title != null)
            {
                float ignition = Mathf.Clamp01(t / 1.2f);
                bool stutter = ignition < 1f &&
                               (Mathf.PingPong(t * 9f, 1f) > 0.62f || ignition < 0.35f);
                var c = goldGlow;
                c.a = stutter ? 0.35f : Mathf.Lerp(0.6f, 1f, ignition);
                _title.color = c;
            }

            // Soft pulse on the prompt (§1.2 beat 4). Cheap: one ping-pong, no allocation.
            if (!_ready || _enterGroup == null) return;

            float pulse = Mathf.PingPong(t / pulsePeriod, 1f);
            _enterGroup.alpha = Mathf.Lerp(0.55f, 1f, pulse);
        }

        // ---- helpers ----------------------------------------------------------





    }
}
