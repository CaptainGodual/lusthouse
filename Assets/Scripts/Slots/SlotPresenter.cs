using System.Collections;
using LustHouse.Audio;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Slots
{
    /// <summary>
    /// Slot screen. Proves the full M0.5 loop: bet debits the wallet, a win credits it,
    /// and both land in the ledger so they survive a restart. The reels themselves are drawn
    /// by <see cref="ReelsView"/> from the same stops the engine evaluated.
    ///
    /// The bottom control bar is built here: bet stepper, total bet, total win, max bet and
    /// spin. Reels, features and maths stay data-driven; this class only presents them.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public SlotSceneController controller;

        [Header("Presentation")]
        [Tooltip("Fallback hold for a spin with nothing to animate — a bet the wallet refused. " +
                 "A real spin is timed by the definition's spinDurationSeconds.")]
        public float spinHoldSeconds = 0.35f;

        [Header("Feature round")]
        [Tooltip("How long one spin of a feature round takes to show. A free-spins round " +
                 "runs dozens of spins, and each one at the full spin length is minutes " +
                 "of the player watching reels turn.")]
        public float featureSpinSeconds = 0.35f;

        [Tooltip("How many feature spins are animated before the round is resolved to its " +
                 "final grid. A retriggering strip can run to hundreds of spins.")]
        public int maxAnimatedFeatureSpins = 10;

        [Header("Safety")]
        [Tooltip("How long a spin may take before the game takes the controls back. A spin " +
                 "that never finishes leaves every button disabled, and a player with no " +
                 "button to press has no way out.")]
        public float spinWatchdogSeconds = 60f;

        [Header("Music")]
        [Tooltip("How long the bed stays up after the reels stop before it fades away, " +
                 "seconds. The bed comes up with the reels and goes down once the " +
                 "player has been still for a moment.")]
        public float musicIdleSeconds = 6f;

        /// <summary>The reel grid. Exposed so a test can assert what the player is shown.</summary>
        public ReelsView Reels => _reels;

        private Text _title;
        private Text _balance;
        private Text _bet;
        private Text _betLevel;
        private Text _totalWin;
        private Text _result;
        private Text _lifetime;
        private Text _levelBanner;
        private ReelsView _reels;
        private Button _spinButton;
        private Button _betDownButton;
        private Button _betUpButton;
        private Button _maxBetButton;
        private Button _backButton;

        private bool _spinning;
        private bool _built;
        private Coroutine _bannerRoutine;
        private float _spinElapsed;
        private float _musicIdleElapsed;
        private bool _musicBedUp;

        /// <summary>True while a spin is in flight. The controls are disabled until it ends.</summary>
        public bool IsSpinning => _spinning;

        /// <summary>
        /// The longest a feature round of <paramref name="stepCount"/> steps can take to
        /// show, in seconds.
        ///
        /// Bounded by the animation budget, so no matter how long the maths ran the player
        /// is not left watching reels turn for minutes. Measured on the shipped content: an
        /// average feature round is 27 spins and the worst case seen was over 160.
        /// </summary>
        public float FeatureRoundSeconds(int stepCount) =>
            Mathf.Min(stepCount, maxAnimatedFeatureSpins) * featureSpinSeconds;

        private void Awake()
        {
            Core.ServicesBootstrap.EnsureExists();
            UiFactory.EnsureEventSystem();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WalletChanged>(OnWalletChanged);
            EventBus.Subscribe<LevelUp>(OnLevelUp);
            if (!_built)
            {
                Build();
                _built = true;
            }
            Refresh();

            // The room has a bed, and it fades away on its own if the player never
            // touches it. The reels bring it back the moment they turn.
            BringTheBedUp();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WalletChanged>(OnWalletChanged);
            EventBus.Unsubscribe<LevelUp>(OnLevelUp);

            // Leaving the screen mid-spin takes the reels with it, so it takes the
            // loop with it too.
            SlotAudio.SpinEnded();

            // And the bed goes with it. The music channel outlives the scene, so a bed
            // left behind would follow the player back into the lobby.
            TakeTheBedDown();
        }

        private void OnWalletChanged(WalletChanged change)
        {
            if (change.currency == Currency.LustPoints) return;
            Refresh();
        }

        private void OnLevelUp(LevelUp evt)
        {
            if (_levelBanner == null) return;
            _levelBanner.text = $"LEVEL {evt.newLevel}!  +{evt.xpReward} COINS";
            _levelBanner.gameObject.SetActive(true);

            // Only the banner's own timer is restarted. StopAllCoroutines here also killed
            // the spin in flight, and a level-up is published from inside the spin — the
            // maths raises it the moment the win is credited. The game froze with every
            // control disabled and the reels never turning.
            if (_bannerRoutine != null) StopCoroutine(_bannerRoutine);
            _bannerRoutine = StartCoroutine(HideLevelBanner());
        }

        private System.Collections.IEnumerator HideLevelBanner()
        {
            yield return new WaitForSeconds(3f);
            if (_levelBanner != null) _levelBanner.gameObject.SetActive(false);
        }

        private void Build()
        {
            var canvas = UiFactory.ResolveCanvas(transform, "SlotCanvas");
            var root = canvas.transform;

            // ---- background UI (background part of the game) ----
            var backgroundSprite = Resources.Load<Sprite>("Art/UI/Opulent Crimson Gold Casino Interface");
            var background = UiFactory.NewImage("Background", root, UiFactory.VelvetBase);
            if (backgroundSprite != null)
            {
                background.sprite = backgroundSprite;
                background.color = Color.white;
                background.preserveAspect = false;
                background.type = Image.Type.Simple;
            }
            UiFactory.Stretch(background.rectTransform);
            background.transform.SetAsFirstSibling();
            background.raycastTarget = false;

            // ---- slots frame UI (drawn on top of the reels) ----
            // Loaded here so it can be parented after the reels are created below; the actual
            // GameObject is built once the reels exist so the layering reads background ->
            // reels (masked) -> frame.
            var frameSprite = Resources.Load<Sprite>("Art/UI/Slots");
            // ---- header ----
            var header = UiFactory.NewImage("Header", root, UiFactory.VelvetPanel);
            var headerRect = header.rectTransform;
            headerRect.anchorMin = new Vector2(0f, 1f);
            headerRect.anchorMax = new Vector2(1f, 1f);
            headerRect.pivot = new Vector2(0.5f, 1f);
            headerRect.sizeDelta = new Vector2(0f, 72f);

            _backButton = UiFactory.NewButton("Back", header.transform, "← LOBBY", 18,
                UiFactory.TextSecondary, out _);
            _backButton.GetComponent<Image>().color = UiFactory.VelvetRaised;
            UiFactory.PlaceLeftMid(_backButton.GetComponent<RectTransform>(), 20f, 120f, 44f);
            _backButton.onClick.AddListener(OnBackClicked);

            _title = UiFactory.NewText("Title", header.transform, "—", 28, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            UiFactory.PlaceLeftMid(_title.rectTransform, 156f, 700f, 72f);

            _balance = UiFactory.NewText("Balance", header.transform, "", 22, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.MiddleRight);
            var balanceRect = _balance.rectTransform;
            balanceRect.anchorMin = new Vector2(1f, 0.5f);
            balanceRect.anchorMax = new Vector2(1f, 0.5f);
            balanceRect.pivot = new Vector2(1f, 0.5f);
            balanceRect.anchoredPosition = new Vector2(-28f, 0f);
            balanceRect.sizeDelta = new Vector2(420f, 72f);

            // ---- slots frame UI (drawn behind the reels so they stay visible) ----
            var uiFrame = UiFactory.NewImage("UiFrame", root, UiFactory.VelvetBase);
            if (frameSprite != null)
            {
                uiFrame.sprite = frameSprite;
                uiFrame.color = Color.white;
                uiFrame.preserveAspect = false;
                uiFrame.type = Image.Type.Simple;
            }
            // The mask has five 327px-wide slots in a 2132x1070 image. At 1546x900 the
            // on-screen slot pitch stays close to the 270px reel cells, and the bottom
            // 180 pixels are left clear for the control bank.
            UiFactory.Place(uiFrame.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(1546f, 900f), new Vector2(0f, 90f));
            uiFrame.raycastTarget = false;

            // ---- reels ----
            // Everything about the reels — cell size, gap, icon padding, turns, stagger — is a
            // field on ReelsView. Icons themselves are SymbolDefinition.sprite in the
            // definition asset; symbols with no sprite yet fall back to a tinted tile.
            // ---- reel mask (Slots-mask.png) ----
            var maskSprite = Resources.Load<Sprite>("Art/UI/Slots-mask");
            var maskGo = new GameObject("ReelMask", typeof(RectTransform));
            maskGo.transform.SetParent(root, false);
            var maskImage = maskGo.AddComponent<Image>();
            if (maskSprite != null)
            {
                maskImage.sprite = maskSprite;
                maskImage.color = Color.white;
                maskImage.type = Image.Type.Simple;
            }
            maskImage.raycastTarget = false;
            var mask = maskGo.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            UiFactory.Place((RectTransform)maskGo.transform, new Vector2(0.5f, 0.5f),
                new Vector2(1546f, 900f), new Vector2(0f, 90f));

            var reelsGo = new GameObject("Reels", typeof(RectTransform));
            reelsGo.transform.SetParent(maskGo.transform, false);
            _reels = reelsGo.AddComponent<ReelsView>();
            _reels.cellGap = 0f;
            UiFactory.Place((RectTransform)reelsGo.transform, new Vector2(0.5f, 0.5f),
                new Vector2(1502f, 810f), new Vector2(0f, 0f));

            BuildControlBar(root);

            _levelBanner = UiFactory.NewText("LevelBanner", root, "", 26, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.MiddleCenter);
            UiFactory.PlaceTopCentre(_levelBanner.rectTransform, 90f, 600f, 40f);
            _levelBanner.gameObject.SetActive(false);

            header.transform.SetAsLastSibling();
        }

        /// <summary>
        /// The bottom bar. It is deliberately built from the shared procedural sprites so the
        /// screen keeps working without imported art, while the colours and layered highlights
        /// match the gold/blue/purple/green control bank in the reference.
        /// </summary>
        private void BuildControlBar(Transform root)
        {
            var bar = UiFactory.NewImage("ControlBar", root, UiFactory.BarBrown);
            UiFactory.PlaceBottomCentre(bar.rectTransform, 20f, 1720f, 150f);

            var barOutline = bar.gameObject.AddComponent<Outline>();
            barOutline.effectColor = new Color(0.75f, 0.57f, 0.24f, 0.85f);
            barOutline.effectDistance = new Vector2(2f, 2f);

            var goldEdge = UiFactory.NewImage("GoldEdge", bar.transform, UiFactory.GoldPrimary);
            var edgeRect = goldEdge.rectTransform;
            edgeRect.anchorMin = new Vector2(0f, 1f);
            edgeRect.anchorMax = new Vector2(1f, 1f);
            edgeRect.pivot = new Vector2(0.5f, 1f);
            edgeRect.sizeDelta = new Vector2(0f, 4f);
            edgeRect.anchoredPosition = Vector2.zero;

            // ---- total bet ----
            _betDownButton = NewRoundButton("BetDown", bar.transform, "−", 46, UiFactory.BetBlue, 96f);
            UiFactory.Place(_betDownButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(96f, 96f), new Vector2(-636f, 0f));
            _betDownButton.onClick.AddListener(() => StepBet(-1));

            var betFill = NewInsetPanel("BetPanel", bar.transform, new Vector2(300f, 112f),
                UiFactory.BetPanelBlue);
            UiFactory.Place((RectTransform)betFill.parent, new Vector2(0.5f, 0.5f),
                new Vector2(300f, 112f), new Vector2(-426f, 0f));

            var betLabel = UiFactory.NewText("Label", betFill, "TOTAL BET", 15, FontStyle.Bold,
                UiFactory.NeonCyan);
            UiFactory.PlaceTopCentre(betLabel.rectTransform, 8f, 270f, 20f);

            _bet = UiFactory.NewText("Amount", betFill, "0", 36, FontStyle.Bold, UiFactory.TextPrimary);
            UiFactory.Place(_bet.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(270f, 42f), Vector2.zero);

            _betLevel = UiFactory.NewText("Level", betFill, "", 13, FontStyle.Normal,
                UiFactory.TextSecondary);
            UiFactory.PlaceBottomCentre(_betLevel.rectTransform, 8f, 270f, 18f);

            _betUpButton = NewRoundButton("BetUp", bar.transform, "+", 46, UiFactory.BetBlue, 96f);
            UiFactory.Place(_betUpButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(96f, 96f), new Vector2(-216f, 0f));
            _betUpButton.onClick.AddListener(() => StepBet(1));

            // ---- total win ----
            var winFill = NewInsetPanel("WinPanel", bar.transform, new Vector2(380f, 112f),
                UiFactory.WinPanelBlue);
            UiFactory.Place((RectTransform)winFill.parent, new Vector2(0.5f, 0.5f),
                new Vector2(380f, 112f), new Vector2(54f, 0f));

            var winLabel = UiFactory.NewText("Label", winFill, "TOTAL WIN", 15, FontStyle.Bold,
                UiFactory.NeonCyan);
            UiFactory.PlaceTopCentre(winLabel.rectTransform, 8f, 340f, 20f);

            _totalWin = UiFactory.NewText("Amount", winFill, "0", 40, FontStyle.Bold,
                UiFactory.TextPrimary);
            UiFactory.Place(_totalWin.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(340f, 46f), Vector2.zero);

            _result = UiFactory.NewText("Status", winFill, "GOOD LUCK", 13, FontStyle.Bold,
                UiFactory.TextSecondary);
            UiFactory.PlaceBottomCentre(_result.rectTransform, 8f, 340f, 18f);

            // ---- max bet ----
            _maxBetButton = NewRoundButton("MaxBet", bar.transform, "MAX\nBET", 20,
                UiFactory.MaxPurple, 110f);
            UiFactory.Place(_maxBetButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(110f, 110f), new Vector2(324f, 0f));
            _maxBetButton.onClick.AddListener(SetMaxBet);

            // ---- spin ----
            _spinButton = NewGlossyButton("Spin", bar.transform, "SPIN", 42, UiFactory.SpinGreen,
                new Vector2(280f, 124f), out Text spinLabel);
            UiFactory.Place(_spinButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f),
                new Vector2(280f, 124f), new Vector2(544f, 0f));

            // Leave room for the small hint along the bottom edge of the green button.
            spinLabel.rectTransform.anchorMin = new Vector2(0f, 0.42f);
            spinLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            spinLabel.rectTransform.offsetMin = Vector2.zero;
            spinLabel.rectTransform.offsetMax = Vector2.zero;

            var spinHint = UiFactory.NewText("Hint", _spinButton.transform, "TAP TO SPIN", 12,
                FontStyle.Bold, new Color(1f, 1f, 1f, 0.72f));
            UiFactory.PlaceBottomCentre(spinHint.rectTransform, 12f, 240f, 18f);

            _spinButton.onClick.AddListener(OnSpinClicked);

            // The spin press is heavier than a button click. It is the one press that
            // spends the player's money, and it should not sound like tapping a label.
            AudioFeedback.AttachClickSound(_spinButton, AudioCue.SpinPress);

            // ---- session stats (§2.10) ----
            _lifetime = UiFactory.NewText("Lifetime", bar.transform, "", 12, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.MiddleRight);
            var lifetimeRect = _lifetime.rectTransform;
            lifetimeRect.anchorMin = new Vector2(1f, 0.5f);
            lifetimeRect.anchorMax = new Vector2(1f, 0.5f);
            lifetimeRect.pivot = new Vector2(1f, 0.5f);
            lifetimeRect.sizeDelta = new Vector2(150f, 112f);
            lifetimeRect.anchoredPosition = new Vector2(-16f, 0f);
        }

        private static Button NewRoundButton(string name, Transform parent, string label,
                                             int fontSize, Color fill, float size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = new Vector2(size, size);

            var ring = go.AddComponent<Image>();
            ring.sprite = UiFactory.Circle;
            ring.color = UiFactory.GoldPrimary;
            ring.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = ring;
            UiFactory.StyleButton(button);
            go.AddComponent<CanvasGroup>();
            AddShadow(ring, -3f);

            var inner = UiFactory.NewImage("Fill", go.transform, fill);
            inner.sprite = UiFactory.Circle;
            button.targetGraphic = inner;
            UiFactory.Stretch(inner.rectTransform, 4f);

            var shine = UiFactory.NewImage("Shine", go.transform, new Color(1f, 1f, 1f, 0.16f));
            shine.sprite = UiFactory.Circle;
            var shineRect = shine.rectTransform;
            shineRect.anchorMin = new Vector2(0.5f, 1f);
            shineRect.anchorMax = new Vector2(0.5f, 1f);
            shineRect.pivot = new Vector2(0.5f, 1f);
            shineRect.sizeDelta = new Vector2(size * 0.62f, size * 0.28f);
            shineRect.anchoredPosition = new Vector2(0f, -size * 0.12f);

            var text = UiFactory.NewText("Label", go.transform, label, fontSize, FontStyle.Bold,
                UiFactory.TextPrimary);
            UiFactory.Stretch(text.rectTransform);

            AudioFeedback.AttachClickSound(button);

            return button;
        }

        private static Button NewGlossyButton(string name, Transform parent, string label,
                                              int fontSize, Color fill, Vector2 size,
                                              out Text labelText)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            ((RectTransform)go.transform).sizeDelta = size;

            var ring = go.AddComponent<Image>();
            ring.sprite = UiFactory.RoundedPanel;
            ring.color = UiFactory.GoldPrimary;
            ring.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = ring;
            UiFactory.StyleButton(button);
            go.AddComponent<CanvasGroup>();
            AddShadow(ring, -4f);

            var inner = UiFactory.NewImage("Fill", go.transform, fill);
            inner.sprite = UiFactory.RoundedPanel;
            button.targetGraphic = inner;
            UiFactory.Stretch(inner.rectTransform, 4f);

            var shine = UiFactory.NewImage("Shine", go.transform, new Color(1f, 1f, 1f, 0.14f));
            shine.sprite = UiFactory.RoundedPanel;
            var shineRect = shine.rectTransform;
            shineRect.anchorMin = new Vector2(0f, 0.55f);
            shineRect.anchorMax = new Vector2(1f, 1f);
            shineRect.offsetMin = new Vector2(10f, -6f);
            shineRect.offsetMax = new Vector2(-10f, -8f);

            labelText = UiFactory.NewText("Label", go.transform, label, fontSize, FontStyle.Bold,
                UiFactory.TextPrimary);
            UiFactory.Stretch(labelText.rectTransform);

            AudioFeedback.AttachClickSound(button);

            return button;
        }

        private static RectTransform NewInsetPanel(string name, Transform parent, Vector2 size,
                                                   Color fill)
        {
            var outer = UiFactory.NewImage(name, parent, UiFactory.GoldPrimary);
            outer.sprite = UiFactory.RoundedPanel;
            outer.rectTransform.sizeDelta = size;
            AddShadow(outer, -4f);

            var inner = UiFactory.NewImage("Fill", outer.transform, fill);
            inner.sprite = UiFactory.RoundedPanel;
            UiFactory.Stretch(inner.rectTransform, 3f);

            var shine = UiFactory.NewImage("Shine", inner.transform, new Color(1f, 1f, 1f, 0.05f));
            shine.sprite = UiFactory.RoundedPanel;
            var shineRect = shine.rectTransform;
            shineRect.anchorMin = new Vector2(0f, 0.55f);
            shineRect.anchorMax = new Vector2(1f, 1f);
            shineRect.offsetMin = new Vector2(8f, -6f);
            shineRect.offsetMax = new Vector2(-8f, -6f);

            return inner.rectTransform;
        }

        private static void AddShadow(Image image, float y)
        {
            var shadow = image.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.35f);
            shadow.effectDistance = new Vector2(0f, y);
        }

        private static void SetInteractable(Button button, bool interactable)
        {
            if (button == null) return;

            button.interactable = interactable;
            var group = button.GetComponent<CanvasGroup>();
            if (group != null) group.alpha = interactable ? 1f : 0.42f;
        }

        private void StepBet(int delta)
        {
            if (_spinning || controller == null || controller.runner == null) return;
            controller.runner.StepBet(delta);
            Refresh();
        }

        private void SetMaxBet()
        {
            if (_spinning || controller == null || controller.runner == null ||
                controller.Definition == null) return;

            // The top of the ladder this player's level has unlocked, not the house max.
            controller.runner.betLevel = Mathf.Max(0, controller.runner.BetOptionCount - 1);
            Refresh();
        }

        private void OnBackClicked()
        {
            Core.ServicesBootstrap services = Core.ServicesBootstrap.EnsureExists();
            services?.SaveNow();
            UnityEngine.SceneManagement.SceneManager.LoadScene("Lobby");
        }

        private void OnSpinClicked()
        {
            if (_spinning || controller == null || controller.runner == null ||
                controller.Definition == null) return;
            StartCoroutine(SpinRoutine());
        }

        /// <summary>
        /// The backstop. Every part of a spin has its own timeout, but a bug anywhere in it
        /// must not be able to leave the player with no button to press: a spin that
        /// overran its budget has the reels cut short and the controls released.
        ///
        /// This runs in Update rather than in a coroutine, so nothing that stops a
        /// coroutine can stop the backstop.
        /// </summary>
        private void Update()
        {
            // The bed's clock runs whether or not a spin is in flight, so it comes
            // first: a bed that only counted down during a spin would never fade.
            TickMusicIdle();

            if (!_spinning) return;

            _spinElapsed += Time.unscaledDeltaTime;
            if (_spinElapsed < spinWatchdogSeconds) return;

            LustLog.Error($"[LustHouse] A spin ran for {_spinElapsed:F0}s; the reels were " +
                          "stopped and the controls released.");

            // The reel sound has to stop with the reels, or it plays on over a
            // machine that has just given up.
            SlotAudio.SpinEnded();

            _spinning = false;
            if (_reels != null) _reels.StopSpin();
            Refresh();

            // The bed follows the reels down, the same way.
            ScheduleMusicIdle();
        }

        private IEnumerator SpinRoutine()
        {
            _spinning = true;
            _spinElapsed = 0f;
            _result.text = "SPINNING...";
            _result.color = UiFactory.TextSecondary;
            _totalWin.color = UiFactory.TextSecondary;
            Refresh();

            long betBefore = WalletService.Instance != null ? WalletService.Instance.Balance(Currency.Coins) : 0;
            long lustBefore = WalletService.Instance != null ? WalletService.Instance.Balance(Currency.LustPoints) : 0;
            long xpBefore = Economy.PlayerProgression.Instance != null ? Economy.PlayerProgression.Instance.Xp : 0;

            var result = controller.runner.Spin();

            // The reels are driven from the stops the engine returned, so the grid the player
            // watches land is the grid the maths scored. A refused spin has no stops and
            // nothing to animate, so it falls back to the plain hold.
            if (_reels != null)
            {
                _reels.ClearHighlights();
                _reels.ClearLocks();       // held cells belong to one round only
            }

            if (result.stops != null && _reels != null)
            {
                bool turbo = controller.runner.turboMode > 0;

                // The reels are turning: the loop runs until they stop, across the base
                // spin and every feature spin that follows it. Restarting it per spin
                // would turn a feature round into a stutter.
                SlotAudio.SpinStarted();

                // The bed comes up with the reels. If it was on its way out — the
                // player spun again before it had faded — it is brought back rather
                // than left in a gap.
                BringTheBedUp();

                yield return StartCoroutine(_reels.PlaySpin(result.stops, turbo ? 0.35f : 1f));
                _reels.ShowWinHighlights(result);

                // The feature is announced now, when the trigger is lit up on screen.
                // The maths found it a second ago; the player has only just seen it.
                SlotAudio.FeatureTriggered(result);
            }
            else
            {
                yield return new WaitForSeconds(spinHoldSeconds);
            }

            // Feature rounds (free spins, hold & spin, and later bonus games) play after the
            // base spin, from the exact steps the engine produced. The maths already scored
            // them; this only animates what was decided.
            //
            // A feature round is long. A free-spins round is dozens of spins and a
            // retriggering strip runs to hundreds — measured at an average of 27 and a
            // worst case over 160 on the shipped content. Animating each one at the full
            // spin length is forty seconds to four minutes of the player watching reels
            // turn with every control disabled, which reads as a freeze rather than a
            // bonus. So the steps play fast, and a round longer than the animation budget
            // is resolved to its final grid instead of being dragged out spin by spin.
            // Nothing about the maths changes: the payout was already credited.
            if (result.featureSteps != null && _reels != null)
            {
                _reels.ClearHighlights();

                float spinSeconds = controller.runner.definition != null
                    ? controller.runner.definition.spinDurationSeconds
                    : 1.5f;
                float scale = featureSpinSeconds / Mathf.Max(0.05f, spinSeconds);
                if (controller.runner.turboMode > 0) scale *= 0.35f;

                int animated = 0;
                var lastSpin = default(SpinResult);
                bool haveLastSpin = false;

                foreach (var step in result.featureSteps)
                {
                    switch (step.kind)
                    {
                        case FeatureStepKind.Lock:
                            // Locks accumulate across respins; ClearLocks runs at the start of
                            // the next round, not between the steps of this one.
                            _reels.ShowLock(step.reel, step.row, step.symbolId, step.amount);
                            break;

                        case FeatureStepKind.Spin:
                            if (step.spin.stops == null) break;
                            lastSpin = step.spin;
                            haveLastSpin = true;

                            if (animated >= maxAnimatedFeatureSpins)
                            {
                                // Past the budget: the reel lands on this spin's stops and
                                // the round moves on.
                                _reels.ShowStops(step.spin.stops);
                                break;
                            }

                            animated++;
                            yield return StartCoroutine(_reels.PlaySpin(step.spin.stops, scale));
                            _reels.ShowWinHighlights(step.spin);
                            break;

                        case FeatureStepKind.Pick:
                        case FeatureStepKind.Reveal:
                            // The picks were made before this round resolved (the engine took
                            // the player's choice, or seeded the same draw), so this is a
                            // playback of a result that already exists. Each reveal is folded
                            // into the round total the result line shows below; a dedicated
                            // pick-grid view would render them as a separate overlay.
                            break;

                        case FeatureStepKind.Award:
                            // Already folded into the round total; the result line shows it.
                            break;
                    }
                }

                // The round resolved to its final grid, so the last thing the player sees is
                // what the maths paid out on.
                if (haveLastSpin && animated < maxAnimatedFeatureSpins)
                    _reels.ShowWinHighlights(lastSpin);
            }

            if (result.bet > 0)
            {
                _totalWin.text = UiFactory.FormatAmount(result.totalWin);
                _totalWin.color = result.IsWin ? UiFactory.GoldGlow : UiFactory.TextSecondary;
                _result.color = result.IsWin ? UiFactory.StateSuccess : UiFactory.TextSecondary;
                _result.text = result.IsWin
                    ? $"{result.Multiplier(result.bet):0.#}× WIN"
                    : "NO WIN";
            }
            else
            {
                _totalWin.text = "—";
                _totalWin.color = UiFactory.StateWarn;
                _result.color = UiFactory.StateWarn;
                _result.text = "NOT ENOUGH COINS";
            }

            // The payout is announced once the round has resolved and the numbers on
            // screen are the ones that were actually paid.
            SlotAudio.WinResolved(result, controller != null ? controller.Definition : null);

            RenderLifetime(betBefore, lustBefore, xpBefore);

            // Refresh before clearing _spinning would keep the spin button disabled, so clear
            // the guard first and let Refresh restore every control in one place.
            SlotAudio.SpinEnded();
            _spinning = false;
            Refresh();

            // The reels have stopped, but the bed does not go the instant they do: the
            // player is still in the middle of a round they are about to play again.
            // It fades away once they have been still for a moment.
            ScheduleMusicIdle();
        }

        // ---- the music bed -------------------------------------------------------

        /// <summary>
        /// Brings the bed up and restarts its clock.
        ///
        /// The bed is asked for, not restarted: asking for a bed that is already up
        /// leaves it up, so a player who spins again before it has faded away gets
        /// the bed back rather than a gap where it used to be.
        /// </summary>
        private void BringTheBedUp()
        {
            _musicBedUp = true;
            _musicIdleElapsed = 0f;
            SlotAudio.MusicStarted();
        }

        /// <summary>Takes the bed down, if it was up.</summary>
        private void TakeTheBedDown()
        {
            if (!_musicBedUp) return;
            _musicBedUp = false;
            SlotAudio.MusicIdle();
        }

        /// <summary>
        /// Starts the clock on the bed fading away.
        ///
        /// The clock is counted down in <see cref="Update"/> rather than in a
        /// coroutine, so it cannot outlive the screen and so a test can shorten it.
        /// </summary>
        private void ScheduleMusicIdle()
        {
            _musicIdleElapsed = 0f;
        }

        /// <summary>
        /// Counts the bed down to nothing.
        ///
        /// The clock only runs while the reels are still. A spin takes the bed up and
        /// holds it there for as long as the round lasts, and the clock starts again
        /// when the round ends.
        /// </summary>
        private void TickMusicIdle()
        {
            if (_spinning) return;
            if (!_musicBedUp) return;

            _musicIdleElapsed += Time.unscaledDeltaTime;
            if (_musicIdleElapsed < Mathf.Max(0.01f, musicIdleSeconds)) return;

            TakeTheBedDown();
        }

        private void RenderLifetime(long coinsBefore, long lustBefore, long xpBefore)
        {
            if (_lifetime == null) return;

            var wallet = WalletService.Instance;
            if (wallet == null) return;

            long coinsNow = wallet.Balance(Currency.Coins);
            long lustNow = wallet.Balance(Currency.LustPoints);

            // Lust is non-spendable and never debited, so any change is pure accrual.
            long lustGained = lustNow - lustBefore;
            long xpNow = Economy.PlayerProgression.Instance != null ? Economy.PlayerProgression.Instance.Xp : xpBefore;
            long xpGained = xpNow - xpBefore;
            if (xpGained < 0) xpGained = 0;

            _lifetime.text =
                $"SPINS {Economy.PlayerProgression.Instance?.TotalSpins ?? 0}\n" +
                $"NET {coinsNow - coinsBefore:+#;-#;0}\n" +
                $"LUST +{lustGained}\n" +
                $"XP +{xpGained}";
        }

        /// <summary>Re-reads the wallet and definition. Called after the definition arrives.</summary>
        public void Refresh()
        {
            if (_title == null) return;

            var def = controller != null ? controller.Definition : null;
            _title.text = def != null ? def.title.ToUpperInvariant() : "NO GAME LOADED";

            // The presenter's OnEnable runs before the definition arrives, so the reels are
            // built here rather than in Build(). Configure is a no-op layout pass once the
            // reels exist, which matters because every wallet change lands in here.
            if (_reels != null) _reels.Configure(def);

            var wallet = WalletService.Instance;
            long coins = wallet != null ? wallet.Balance(Currency.Coins) : 0;
            _balance.text = $"{CurrencyRules.Symbol(Currency.Coins)} {UiFactory.FormatAmount(coins)}";

            if (def != null && controller.runner != null)
            {
                int steps = controller.runner.BetOptionCount;
                int level = Economy.BetLimits.PlayerLevel;
                long cap = Economy.BetLimits.MaxBetForLevel(level);
                int next = Economy.BetLimits.NextCapLevel(level);

                _bet.text = UiFactory.FormatAmount(controller.runner.CurrentBet);

                // The ladder only means something if the player can see what is behind the
                // next rung, so the ceiling is on screen and the level that lifts it.
                _betLevel.text = next > 0
                    ? $"BET {controller.runner.betLevel + 1}/{steps}  ·  LVL {next} UNLOCKS {UiFactory.FormatAmount(Economy.BetLimits.MaxBetForLevel(next))}"
                    : $"BET {controller.runner.betLevel + 1}/{steps}  ·  MAX {UiFactory.FormatAmount(cap)}";

                bool canBet = !_spinning;
                SetInteractable(_betDownButton, canBet && controller.runner.betLevel > 0);
                SetInteractable(_betUpButton, canBet && controller.runner.betLevel < steps - 1);
                SetInteractable(_maxBetButton, canBet && controller.runner.betLevel < steps - 1);
            }
            else
            {
                _bet.text = "—";
                _betLevel.text = "";
                SetInteractable(_betDownButton, false);
                SetInteractable(_betUpButton, false);
                SetInteractable(_maxBetButton, false);
            }

            SetInteractable(_spinButton, !_spinning && def != null);
        }
    }
}
