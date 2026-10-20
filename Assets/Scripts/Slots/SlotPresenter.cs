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

        /// <summary>The reel grid. Exposed so a test can assert what the player is shown.</summary>
        public ReelsView Reels => _reels;

        private Text _title;
        private Text _balance;
        private Text _bet;
        private Text _betLevel;
        private Text _totalWin;
        private Text _result;
        private Text _lifetime;
        private ReelsView _reels;
        private Button _spinButton;
        private Button _betDownButton;
        private Button _betUpButton;
        private Button _maxBetButton;
        private Button _backButton;

        private bool _spinning;
        private bool _built;

        private void Awake()
        {
            Core.ServicesBootstrap.EnsureExists();
            UiFactory.EnsureEventSystem();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WalletChanged>(OnWalletChanged);
            if (!_built)
            {
                Build();
                _built = true;
            }
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

            // ---- session stats (§2.10) ----
            _lifetime = UiFactory.NewText("Lifetime", bar.transform, "", 12, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.MiddleRight);
            var lifetimeRect = _lifetime.rectTransform;
            lifetimeRect.anchorMin = new Vector2(1f, 0.5f);
            lifetimeRect.anchorMax = new Vector2(1f, 0.5f);
            lifetimeRect.pivot = new Vector2(1f, 0.5f);
            lifetimeRect.sizeDelta = new Vector2(150f, 96f);
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

            controller.runner.betLevel = Mathf.Max(0, controller.Definition.betLevels - 1);
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

        private IEnumerator SpinRoutine()
        {
            _spinning = true;
            _result.text = "SPINNING...";
            _result.color = UiFactory.TextSecondary;
            _totalWin.color = UiFactory.TextSecondary;
            Refresh();

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

            RenderLifetime(betBefore, lustBefore);

            // Refresh before clearing _spinning would keep the spin button disabled, so clear
            // the guard first and let Refresh restore every control in one place.
            _spinning = false;
            Refresh();
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
                $"SPINS {Economy.PlayerProgression.Instance?.TotalSpins ?? 0}\n" +
                $"NET {coinsNow - coinsBefore:+#;-#;0}\n" +
                $"LUST +{lustGained}";
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
                _bet.text = UiFactory.FormatAmount(controller.runner.CurrentBet);
                _betLevel.text = $"LEVEL {controller.runner.betLevel + 1}/{def.betLevels}";

                bool canBet = !_spinning;
                SetInteractable(_betDownButton, canBet && controller.runner.betLevel > 0);
                SetInteractable(_betUpButton,
                    canBet && controller.runner.betLevel < def.betLevels - 1);
                SetInteractable(_maxBetButton,
                    canBet && controller.runner.betLevel < def.betLevels - 1);
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
