using System.Collections;
using LustHouse.Core;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Slots
{
    /// <summary>
    /// Greybox slot view. Proves the full M0.5 loop: bet debits the wallet, a win credits
    /// it, and both land in the ledger so they survive a restart.
    ///
    /// Not the real reel presentation — that is M5. What matters here is that the maths,
    /// the wallet, and the routing are demonstrably connected.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public SlotSceneController controller;

        [Header("Presentation")]
        public float spinHoldSeconds = 0.35f;

        private Text _title;
        private Text _balance;
        private Text _bet;
        private Text _result;
        private Text _grid;
        private Text _lifetime;
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
            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null) canvas = UiFactory.CreateCanvas(transform, "SlotCanvas");
            var root = canvas.transform;

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

            // ---- reel readout ----
            _grid = UiFactory.NewText("Grid", root, "", 44, FontStyle.Bold,
                UiFactory.TextPrimary, TextAnchor.MiddleCenter);
            UiFactory.Place(_grid.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1200f, 320f), Vector2.zero);

            var frame = UiFactory.NewImage("ReelFrame", root, new Color(0f, 0f, 0f, 0.25f));
            UiFactory.Place(frame.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(1240f, 360f), Vector2.zero);

            // ---- result line ----
            _result = UiFactory.NewText("Result", root, "", 28, FontStyle.Bold,
                UiFactory.TextSecondary);
            UiFactory.Place(_result.rectTransform, new Vector2(0.5f, 0f), new Vector2(1200f, 48f), new Vector2(0f, 300f));

            // ---- bet stepper ----
            _bet = UiFactory.NewText("Bet", root, "", 24, FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Place(_bet.rectTransform, new Vector2(0.5f, 0f), new Vector2(420f, 40f), new Vector2(0f, 210f));

            var minusBtn = UiFactory.NewButton("BetDown", root, "−", 40, UiFactory.TextPrimary, out Text minusText);
            UiFactory.Place(minusBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(72f, 64f), new Vector2(-300f, 140f));
            UiFactory.Stretch(minusText.rectTransform);

            var plusBtn = UiFactory.NewButton("BetUp", root, "+", 40, UiFactory.TextPrimary, out Text plusText);
            UiFactory.Place(plusBtn.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(72f, 64f), new Vector2(300f, 140f));
            UiFactory.Stretch(plusText.rectTransform);

            minusBtn.onClick.AddListener(() => StepBet(-1));
            plusBtn.onClick.AddListener(() => StepBet(1));

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

            yield return new WaitForSeconds(spinHoldSeconds);

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

            _grid.text = RenderGrid(result);
            RenderLifetime(betBefore, lustBefore);
            Refresh();
            _spinning = false;
            _spinButton.interactable = true;
        }

        private static string RenderGrid(SpinResult result)
        {
            if (result.grid == null) return "";

            var sb = new System.Text.StringBuilder();
            for (int row = 0; row < result.grid[0].Length; row++)
            {
                for (int reel = 0; reel < result.grid.Length; reel++)
                {
                    var def = SymbolName(result.grid[reel][row]);
                    sb.Append(def).Append(' ');
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string SymbolName(int symbolId)
        {
            return symbolId switch
            {
                0 => "[W] ",
                1 => "[S] ",
                10 => " A  ",
                11 => " K  ",
                12 => " Q  ",
                13 => " J  ",
                14 => "10  ",
                15 => " 9  ",
                16 => "**  ",   // Filigree (the premium symbol)
                _ => " ?  "
            };
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