using System.Collections.Generic;
using LustHouse.Lobby;
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
        private ProbabilityDisclosureSheet _disclosure;
        private FirstRunPackPrompt _prompt;

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
                    _ageGate = UI.AgeGateOverlay.Create(_canvas.transform, OnAgeConfirmed,
                        null, OpenDisclosureOverGate);
                    _ageGate.transform.SetAsLastSibling();
                }
                return;
            }

            RunFirstRunSequence();
        }

        /// <summary>The player answered the age question. On to the disclosure and the offer.</summary>
        private void OnAgeConfirmed()
        {
            if (_ageGate != null)
            {
                _ageGate.Hide();
                _ageGate = null;
            }

            RunFirstRunSequence();
        }

        /// <summary>
        /// The first-run sequence (plan §1.1, §1.3): the probability disclosure, then
        /// the free start pack, then the lobby.
        ///
        /// Both steps are once-only, and both are derived from persisted state rather
        /// than from a flag that could drift: the disclosure from the player's
        /// disclosure flag, the offer from the ledger. A returning player goes straight
        /// to the lobby.
        /// </summary>
        private void RunFirstRunSequence()
        {
            var progression = Economy.PlayerProgression.Instance;
            bool disclosurePending = progression == null || !progression.DisclosureSeen;

            var services = Core.ServicesBootstrap.Instance;
            bool promptPending = services == null || !services.FirstRunPromptAnswered;

            Core.LustLog.Info($"[LustHouse] First-run sequence: disclosurePending={disclosurePending} " +
                         $"promptPending={promptPending} " +
                         $"disclosureSeen={progression?.DisclosureSeen} " +
                         $"offerAnswered={services?.FirstRunPromptAnswered}");

            if (disclosurePending)
            {
                OpenDisclosure(AfterDisclosure);
                return;
            }

            if (promptPending)
            {
                OpenPrompt(EnterLobby);
                return;
            }

            EnterLobby();
        }

        /// <summary>
        /// Opens the probability disclosure.
        ///
        /// The sheet measures the games it publishes, so it is opened with the catalogue
        /// rather than with a list of names: a disclosure that lists a game the build
        /// does not contain is a disclosure that lies.
        /// </summary>
        private void OpenDisclosure(System.Action onClosed)
        {
            var services = Core.ServicesBootstrap.Instance;
            var catalog = services != null ? services.catalog : null;
            var games = ProbabilityDisclosureSheet.ShippableGames(catalog);

            _disclosure = ProbabilityDisclosureSheet.Create(_canvas.transform, games, onClosed);
            _disclosure.transform.SetAsLastSibling();
            _disclosure.Open();
            Core.LustLog.Info($"[LustHouse] The probability disclosure is up ({games.Count} games).");
        }

        /// <summary>The gate's link. The gate stays up behind the sheet.</summary>
        private void OpenDisclosureOverGate()
        {
            OpenDisclosure(null);
        }

        private void AfterDisclosure()
        {
            _disclosure = null;

            var progression = Economy.PlayerProgression.Instance;
            if (progression != null)
            {
                progression.MarkDisclosureSeen();
                Core.ServicesBootstrap.Instance?.SaveNow();
            }

            // On to the offer, if it is still outstanding.
            RunFirstRunSequence();
        }

        /// <summary>
        /// Opens the free start pack.
        ///
        /// Declining it is a real answer: the player still gets a stake they can bet,
        /// and the offer is never asked again.
        /// </summary>
        private void OpenPrompt(System.Action onClosed)
        {
            var services = Core.ServicesBootstrap.Instance;
            long minimumBet = FirstRunPackPrompt.MinimumBetFor(services != null ? services.catalog : null);

            _prompt = FirstRunPackPrompt.Create(_canvas.transform, minimumBet, onClosed);
            _prompt.transform.SetAsLastSibling();
            _prompt.Open();
            Core.LustLog.Info("[LustHouse] The free start pack offer is up.");
        }

        private void EnterLobby()
        {
            Core.LustLog.Info($"[LustHouse] EnterLobby: sequence={sequence != null} " +
                              $"ready={sequence != null && sequence.ReadyToEnter}");
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
