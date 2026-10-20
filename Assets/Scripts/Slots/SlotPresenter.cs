using System.Collections;
using LustHouse.Core;
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
    /// Still greybox in its chrome — the win celebration, paytable and features are M5.
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

        /// <summary>The reel grid. Exposed so a test can assert what the player is shown.</summary>
        public ReelsView Reels => _reels;

        private Text _title;
        private Text _balance;
        private Text _bet;
        private Text _result;
        private Text _lifetime;
        private ReelsView _reels;
        private Button _spinButton;
        private Button _backButton;

        private bool _spinning;

        private void Awake()
        {
            Core.ServicesBootstrap.EnsureExists();
            UiFactory.EnsureEventSystem();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WalletChanged>(OnWalletChanged);
            Build();
            Refresh();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WalletChanged>(OnWalletChanged);
        }

        private void OnWalletChanged(WalletChanged change)
        {
            if (change.currency == Currency.LustPoints) return;
            Refresh();
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

            _title = UiFactory.NewText("Title", header.transform, "—", 28, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            var titleRect = _title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0f, 0f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.anchoredPosition = new Vector2(28f, 0f);
            titleRect.sizeDelta = new Vector2(700f, 72f);

            _balance = UiFactory.NewText("Balance", header.transform, "", 22, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.MiddleRight);
            var balanceRect = _balance.rectTransform;
            balanceRect.anchorMin = new Vector2(1f, 0f);
            balanceRect.anchorMax = new Vector2(1f, 0f);
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
            UiFactory.Place(uiFrame.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(1720f, 1000f), new Vector2(0f, 20f));
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
                new Vector2(1720f, 1000f), new Vector2(0f, 20f));

            var reelsGo = new GameObject("Reels", typeof(RectTransform));
            reelsGo.transform.SetParent(maskGo.transform, false);
            _reels = reelsGo.AddComponent<ReelsView>();
            _reels.cellGap = 0f;
            UiFactory.Place((RectTransform)reelsGo.transform, new Vector2(0.5f, 0.5f),
                new Vector2(1502f, 1070f), new Vector2(0f, 0f));
            // ---- result line ----
            _result = UiFactory.NewText("Result", root, "", 28, FontStyle.Bold,
                UiFactory.TextSecondary);
            UiFactory.Place(_result.rectTransform, new Vector2(0.5f, 0f), new Vector2(260f, 64f), new Vector2(-580f, 60f));

            // ---- bet stepper ----
            _bet = UiFactory.NewText("Bet", root, "", 24, FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Place(_bet.rectTransform, new Vector2(0.5f, 0f), new Vector2(260f, 64f), new Vector2(-180f, 60f));

            var minusBtn = UiFactory.NewButton("BetDown", root, "−", 40, UiFactory.TextPrimary, out Text minusText);
            UiFactory.Place(minusBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(72f, 64f), new Vector2(280f, 60f));
            UiFactory.Stretch(minusText.rectTransform);

            var plusBtn = UiFactory.NewButton("BetUp", root, "+", 40, UiFactory.TextPrimary, out Text plusText);
            UiFactory.Place(plusBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(72f, 64f), new Vector2(380f, 60f));
            UiFactory.Stretch(plusText.rectTransform);

            minusBtn.onClick.AddListener(() => StepBet(-1));
            plusBtn.onClick.AddListener(() => StepBet(1));

            var maxBetBtn = UiFactory.NewButton("MaxBet", root, "MAX", 24, UiFactory.TextPrimary, out Text maxBetText);
            UiFactory.Place(maxBetBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(97f, 64f), new Vector2(-360f, 60f));
            UiFactory.Stretch(maxBetText.rectTransform);
            maxBetBtn.onClick.AddListener(() =>
            {
                if (controller == null || controller.runner == null || controller.Definition == null) return;
                controller.runner.betLevel = Mathf.Max(0, controller.Definition.betLevels - 1);
                Refresh();
            });

            // ---- spin ----
            _spinButton = UiFactory.NewButton("Spin", root, "S P I N", 40, UiFactory.VelvetBase, out _);
            UiFactory.Place(_spinButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(340f, 84f), new Vector2(0f, 60f));
            _spinButton.GetComponent<Image>().color = UiFactory.GoldPrimary;
            _spinButton.onClick.AddListener(OnSpinClicked);

            // ---- back ----
            _backButton = UiFactory.NewButton("Back", root, "← LOBBY", 22, UiFactory.TextSecondary, out _);
            UiFactory.Place(_backButton.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(200f, 56f), new Vector2(28f, 24f));
            _backButton.onClick.AddListener(OnBackClicked);

            // ---- lifetime stats (§2.10) ----
            _lifetime = UiFactory.NewText("Lifetime", root, "", 16, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.LowerRight);
            UiFactory.Place(_lifetime.rectTransform, new Vector2(1f, 0f), new Vector2(760f, 40f), new Vector2(-28f, 28f));
        }

        private void StepBet(int delta)
        {
            if (controller == null || controller.runner == null) return;
            controller.runner.StepBet(delta);
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
            if (_spinning || controller == null || controller.runner == null) return;
            StartCoroutine(SpinRoutine());
        }

        private IEnumerator SpinRoutine()
        {
            _spinning = true;
            _spinButton.interactable = false;

            long betBefore = WalletService.Instance != null ? WalletService.Instance.Balance(Currency.Coins) : 0;
            long lustBefore = WalletService.Instance != null ? WalletService.Instance.Balance(Currency.LustPoints) : 0;

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
                yield return StartCoroutine(_reels.PlaySpin(result.stops, turbo ? 0.35f : 1f));
                _reels.ShowWinHighlights(result);
            }
            else
            {
                yield return new WaitForSeconds(spinHoldSeconds);
            }

            // Feature rounds (free spins, hold & spin, and later bonus games) play after the
            // base spin, from the exact steps the engine produced. The maths already scored
            // them; this only animates what was decided.
            if (result.featureSteps != null && _reels != null)
            {
                bool turbo = controller.runner.turboMode > 0;
                _reels.ClearHighlights();
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
                            yield return StartCoroutine(
                                _reels.PlaySpin(step.spin.stops, turbo ? 0.35f : 1f));
                            _reels.ShowWinHighlights(step.spin);
                            break;

                        case FeatureStepKind.Award:
                            // Already folded into the round total; the result line shows it.
                            break;
                    }
                }
            }

            if (result.bet > 0)
            {
                _result.color = result.IsWin ? UiFactory.StateSuccess : UiFactory.TextSecondary;
                _result.text = result.IsWin
                    ? $"WIN  {UiFactory.FormatAmount(result.totalWin)}  ({result.Multiplier(result.bet):0.#}×)"
                    : "No win this spin";
            }
            else
            {
                _result.color = UiFactory.StateWarn;
                _result.text = "Not enough Coins for the minimum bet. Still not a dead end — " +
                               "earn more or lower the bet.";
            }

            RenderLifetime(betBefore, lustBefore);
            Refresh();
            _spinning = false;
            _spinButton.interactable = true;
        }

        private void RenderLifetime(long coinsBefore, long lustBefore)
        {
            if (_lifetime == null) return;

            var wallet = WalletService.Instance;
            if (wallet == null) return;

            long coinsNow = wallet.Balance(Currency.Coins);
            long lustNow = wallet.Balance(Currency.LustPoints);

            // Lust is non-spendable and never debited, so any change is pure accrual.
            long lustGained = lustNow - lustBefore;

            _lifetime.text =
                $"Spins {Economy.PlayerProgression.Instance?.TotalSpins ?? 0}   ·   " +
                $"Net this spin {coinsNow - coinsBefore:+#;-#;0}   ·   " +
                $"Lust +{lustGained}";
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

            if (def != null)
                _bet.text = $"BET  {UiFactory.FormatAmount(controller.runner.CurrentBet)}   " +
                            $"(level {controller.runner.betLevel + 1}/{def.betLevels})";

            if (_spinButton != null)
                _spinButton.interactable = !_spinning && def != null;
        }
    }
}