using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The lobby, laid out to the approved mockup with the plan's tab bar and dense grid
    /// beneath the featured row.
    ///
    /// Structure, top to bottom:
    ///   1. Top bar        — avatar + level, LVL, Coins (+), Gems (+), gear      [mockup, §2.5]
    ///   2. Daily bonus    — promo banner with carousel dots                       [mockup, §2.6]
    ///   3. Featured row   — large tiles with a right chevron                     [mockup]
    ///   4. Category tabs  — All / Slots / Jackpots / New / Favorites / High Vol  [§2.7]
    ///   5. Game grid      — dense 224x280 tiles                                  [§2.8]
    ///   6. Bottom nav     — Home / Shop / Missions / Social / Leaderboard        [mockup, §2.11]
    ///
    /// SOCIAL and LEADERBOARD open a "coming soon" sheet rather than doing nothing —
    /// a nav item that goes nowhere is a dead end, which the plan forbids (§2.8).
    /// The plan's desktop side rail (§2.9) is not built yet and remains M4.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public LobbyController controller;
        public GameCatalog catalog;

        [Header("Dev")]
        [Tooltip("Shows a QA row for granting currency. Never enable in a release build.")]
        public bool showDevTools = true;

        private static readonly GameCategory[] Categories =
        {
            GameCategory.All,
            GameCategory.Slots,
            GameCategory.Jackpots,
            GameCategory.New,
            GameCategory.Favorites,
            GameCategory.HighVolatility
        };

        private Text _coins;
        private Text _gems;
        private Text _level;
        private Text _status;
        private LobbyGrid _grid;
        private ModalSheet _sheet;
        private RectTransform _root;

        private void Awake()
        {
            ServicesBootstrap.EnsureExists();

            if (controller == null) controller = GetComponent<LobbyController>();
            if (controller != null && catalog != null && controller.catalog == null)
                controller.catalog = catalog;

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
            if (change.currency == Currency.LustPoints) return;  // non-wagerable
            Refresh();
            _grid?.RebuildGrid();
        }
        // Layout
        //
        // One vertical rhythm for the whole screen, top to bottom, in 1920x1080
        // reference pixels. Every rect is positioned by its top-left corner via
        // UiFactory.PlaceTopLeft / PlaceTopCentre, so nothing depends on a hand-computed
        // pivot and nothing can drift off the left edge.
        //
        //    0.. 88   top bar
        //  100..232   daily bonus banner
        //  244..544   featured row
        //  556..960   library: heading / tabs / grid
        //  976..1080  bottom nav
        // =====================================================================

        private const float Margin = 40f;
        private const float TopBarHeight = 88f;
        private const float BonusY = 100f, BonusH = 132f;
        private const float FeaturedY = 244f, FeaturedH = 300f;
        private const float LibraryTop = 556f, LibraryBottom = 120f;
        private const float NavHeight = 104f;

        private void Build()
        {
            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null) canvas = UiFactory.CreateCanvas(transform, "LobbyCanvas");
            _root = canvas.transform as RectTransform;

            var backdrop = UiFactory.NewImage("Backdrop", _root, UiFactory.VelvetBase);
            UiFactory.Stretch(backdrop.rectTransform);

            BuildTopBar(_root);
            BuildDailyBonus(_root);
            BuildFeaturedRow(_root);

            _grid = GetComponent<LobbyGrid>() ?? gameObject.AddComponent<LobbyGrid>();
            _grid.controller = controller;
            _grid.catalog = catalog;
            _grid.RaiseLockNotice += OnLockedTapped;

            BuildLibrary(_root);
            BuildBottomNav(_root);

            if (showDevTools) BuildDevRow(_root);
        }

        // ---- 1. top bar -------------------------------------------------------

        private void BuildTopBar(RectTransform root)
        {
            var bar = UiFactory.NewImage("TopBar", root, UiFactory.VelvetPanel);
            UiFactory.PlaceTopStrip(bar.rectTransform, 0f, TopBarHeight);

            float chipY = 18f;
            const float chipH = 52f;

            // Avatar + level shield (mockup, far left).
            var avatar = UiFactory.NewImage("Avatar", root, UiFactory.VelvetRaised);
            UiFactory.PlaceTopLeft(avatar.rectTransform, Margin, chipY, chipH, chipH);
            avatar.raycastTarget = true;
            avatar.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                ShowSheet("CLUB",
                    "The Club screen — avatar editor, wardrobe, and stats — is M4 work.\n\n" +
                    "Everything you see here is a placeholder.", "CLOSE"));

            var initial = UiFactory.NewText("Initial", avatar.transform, "P", 26,
                FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Stretch(initial.rectTransform);

            // LVL chip.
            var lvlChip = UiFactory.NewImage("LevelChip", root, UiFactory.VelvetRaised);
            UiFactory.PlaceTopLeft(lvlChip.rectTransform, Margin + chipH + 8f, chipY, 130f, chipH);
            var lvlCaption = UiFactory.NewText("Caption", lvlChip.transform, "LVL", 12,
                FontStyle.Bold, UiFactory.TextSecondary, TextAnchor.UpperCenter);
            UiFactory.PlaceTopCentre(lvlCaption.rectTransform, 2f, 120f, 18f);
            _level = UiFactory.NewText("Value", lvlChip.transform, "1", 26,
                FontStyle.Bold, UiFactory.TextPrimary);
            UiFactory.PlaceTopCentre(_level.rectTransform, 18f, 120f, 32f);

            // Coins — the only currency with a + entry point in the mockup.
            const float coinsX = Margin + chipH + 8f + 130f + 16f;
            var coinChip = UiFactory.NewImage("CoinChip", root, UiFactory.VelvetRaised);
            UiFactory.PlaceTopLeft(coinChip.rectTransform, coinsX, chipY, 300f, chipH);
            _coins = UiFactory.NewText("Value", coinChip.transform, "◎  0", 24,
                FontStyle.Bold, UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            UiFactory.PlaceLeftMid(_coins.rectTransform, 18f, 220f, chipH);

            var coinPlus = UiFactory.NewButton("CoinPlus", root, "+", 30, UiFactory.VelvetBase, out Text coinPlusText);
            UiFactory.PlaceTopLeft(coinPlus.GetComponent<RectTransform>(), coinsX + 300f - 56f, chipY + 4f, 48f, chipH - 8f);
            UiFactory.Stretch(coinPlusText.rectTransform);
            coinPlus.GetComponent<Image>().color = UiFactory.GoldPrimary;
            coinPlus.onClick.AddListener(() => controller?.OnCurrencyPlusPressed(Currency.Coins));

            // Gems — no + in the mockup, matching §2.5 (the Gems + button is M4).
            const float gemsX = coinsX + 300f + 16f;
            var gemChip = UiFactory.NewImage("GemChip", root, UiFactory.VelvetRaised);
            UiFactory.PlaceTopLeft(gemChip.rectTransform, gemsX, chipY, 220f, chipH);
            _gems = UiFactory.NewText("Value", gemChip.transform, "⬡  0", 24,
                FontStyle.Bold, UiFactory.NeonCyan, TextAnchor.MiddleLeft);
            UiFactory.PlaceLeftMid(_gems.rectTransform, 18f, 200f, chipH);

            // Settings gear, far right.
            var gear = UiFactory.NewButton("Gear", root, "⚙", 28, UiFactory.TextSecondary, out Text gearText);
            UiFactory.PlaceTopLeft(gear.GetComponent<RectTransform>(), 1920f - Margin - 56f, chipY, 56f, chipH);
            UiFactory.Stretch(gearText.rectTransform);
            gear.onClick.AddListener(() => ShowSheet("SETTINGS",
                "Audio, graphics, responsible play, and age re-confirmation ship in M4.\n\n" +
                "This sheet exists so the gear is never a dead end.", "CLOSE"));
        }

        // ---- 2. daily bonus banner -------------------------------------------

        private void BuildDailyBonus(RectTransform root)
        {
            var banner = UiFactory.NewImage("DailyBonus", root, UiFactory.VelvetPanel);
            UiFactory.PlaceTopCentre(banner.rectTransform, BonusY, 1000f, BonusH);
            banner.raycastTarget = true;

            var edge = UiFactory.NewImage("Edge", banner.transform, UiFactory.NeonMagenta);
            UiFactory.Stretch(edge.rectTransform, 2f);

            var heading = UiFactory.NewText("Heading", banner.transform, "D A I L Y   B O N U S", 32,
                FontStyle.Bold, UiFactory.GoldGlow);
            UiFactory.PlaceTopCentre(heading.rectTransform, 26f, 520f, 48f, -40f);

            var claim = UiFactory.NewButton("Claim", banner.transform, "CLAIM", 22,
                UiFactory.VelvetBase, out Text claimText);
            UiFactory.PlaceTopLeft(claim.GetComponent<RectTransform>(), 1000f / 2f + 40f, 70f, 200f, 48f);
            UiFactory.Stretch(claimText.rectTransform);
            claim.GetComponent<Image>().color = UiFactory.StateSuccess;
            claim.onClick.AddListener(() => ShowSheet("DAILY BONUS",
                "The daily gift and its escalation ladder ship in M2/M4.\n\n" +
                "It guarantees enough Coins for at least 20 minutes at the lowest bet (plan §4.3).",
                "CLOSE"));

            // Carousel dots (§2.6).
            for (int i = 0; i < 4; i++)
            {
                var dot = UiFactory.NewImage("Dot" + i, banner.transform,
                    i == 0 ? UiFactory.GoldPrimary : UiFactory.TextDisabled);
                float size = i == 0 ? 12f : 8f;
                UiFactory.PlaceTopCentre(dot.rectTransform, BonusH - 22f, size, size,
                    (i - 1.5f) * 24f);
            }
        }

        // ---- 3. featured row (mockup) ----------------------------------------

        private void BuildFeaturedRow(RectTransform root)
        {
            var host = new GameObject("FeaturedRow", typeof(RectTransform));
            host.transform.SetParent(root, false);
            var row = (RectTransform)host.transform;
            UiFactory.PlaceTopLeft(row, Margin, FeaturedY, 1920f - Margin * 2f, FeaturedH);

            var layout = new (string label, string body, Color accent, bool locked)[]
            {
                ("FEATURED SLOTS", "Featured slot games from the library.", UiFactory.NeonMagenta, false),
                ("BLACKJACK",  "Table games are not in the v1 roster — plan §3.4 is slots-only.", UiFactory.StateSuccess, true),
                ("ROULETTE",   "Table games are not in the v1 roster — plan §3.4 is slots-only.", UiFactory.NeonMagenta, true),
                ("POKER",      "Table games are not in the v1 roster — plan §3.4 is slots-only.", UiFactory.NeonCyan, true)
            };

            // Four tiles across the content width, chevron overlapping the right edge.
            float chevronW = 44f;
            float usable = 1920f - Margin * 2f - chevronW;
            float tileW = (usable - 3f * 24f) / 4f;

            for (int i = 0; i < layout.Length; i++)
            {
                var spec = layout[i];
                float x = i * (tileW + 24f);

                var tile = UiFactory.NewImage("Featured_" + spec.label.Replace(' ', '_'), row,
                    new Color(spec.accent.r * 0.30f, spec.accent.g * 0.30f, spec.accent.b * 0.30f, 1f));
                tile.raycastTarget = true;
                UiFactory.PlaceTopLeft(tile.rectTransform, x, 0f, tileW, FeaturedH);

                var edge = UiFactory.NewImage("Edge", tile.transform, spec.accent);
                UiFactory.Stretch(edge.rectTransform, 3f);

                var label = UiFactory.NewText("Label", tile.transform, spec.label, 26,
                    FontStyle.Bold, spec.locked ? UiFactory.TextDisabled : UiFactory.TextPrimary,
                    TextAnchor.LowerCenter);
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.offsetMin = new Vector2(8f, 16f);
                labelRect.offsetMax = new Vector2(-8f, 66f);
                label.horizontalOverflow = HorizontalWrapMode.Wrap;

                var captured = spec;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    if (captured.locked) { ShowSheet(captured.label, captured.body, "GOT IT"); return; }

                    var first = catalog != null && catalog.games.Count > 0 ? catalog.games[0] : null;
                    if (first != null) controller?.OnGameTilePressed(first.id);
                });
            }

            var chevron = UiFactory.NewText("Chevron", row, "›", 60, FontStyle.Bold,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(chevron.rectTransform, usable, 0f, chevronW, FeaturedH);
        }

        // ---- 4 + 5. library: tabs and grid ------------------------------------

        private void BuildLibrary(RectTransform root)
        {
            var host = new GameObject("Library", typeof(RectTransform));
            host.transform.SetParent(root, false);
            var band = (RectTransform)host.transform;
            band.anchorMin = new Vector2(0f, 0f);
            band.anchorMax = new Vector2(1f, 1f);
            band.offsetMin = new Vector2(Margin, LibraryBottom);
            band.offsetMax = new Vector2(-Margin, -LibraryTop);

            var heading = UiFactory.NewText("Heading", band, "L I B R A R Y", 20,
                FontStyle.Bold, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(heading.rectTransform, 0f, 0f, 600f, 30f);

            _grid.BuildTabs(band, Categories);

            var gridHost = new GameObject("GridArea", typeof(RectTransform));
            gridHost.transform.SetParent(band, false);
            var gridRect = (RectTransform)gridHost.transform;
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(0f, 0f);
            gridRect.offsetMax = new Vector2(0f, -84f);

            _grid.BuildGridArea(gridRect);
            _grid.RebuildGrid();
        }

        // ---- 6. bottom nav ----------------------------------------------------

        private void BuildBottomNav(RectTransform root)
        {
            var nav = UiFactory.NewImage("BottomNav", root, UiFactory.VelvetPanel);
            var navRect = nav.rectTransform;
            navRect.anchorMin = new Vector2(0f, 0f);
            navRect.anchorMax = new Vector2(1f, 0f);
            navRect.pivot = new Vector2(0.5f, 0f);
            navRect.sizeDelta = new Vector2(0f, NavHeight);

            // Mockup order: HOME · SHOP · MISSIONS · SOCIAL · LEADERBOARD
            var destinations = new (string label, string body)[]
            {
                ("HOME",       null),
                ("SHOP",       "The Boutique — Gems-only cosmetics — ships in M6 with the cosmetic catalogue."),
                ("MISSIONS",   "Daily and weekly missions ship in M2/M4."),
                ("SOCIAL",     "Social features (friends, chat, emotes) are deferred to 1.1 — plan §6."),
                ("LEADERBOARD","Tournaments are unresolved (plan §2.9); remote leaderboards are deferred to 1.1.")
            };

            float slotWidth = 1920f / destinations.Length;

            for (int i = 0; i < destinations.Length; i++)
            {
                var destination = destinations[i];
                bool selected = i == 0;

                // Label sits above the underline; the underline is at y=20 from the nav's
                // bottom, the label baseline above it.
                var item = UiFactory.NewText("Nav_" + destination.label, nav.transform, destination.label, 20,
                    FontStyle.Bold, selected ? UiFactory.GoldPrimary : UiFactory.TextSecondary);
                UiFactory.PlaceBottomCentre(item.rectTransform, 34f, slotWidth, 44f,
                    -1920f / 2f + slotWidth * (i + 0.5f));

                if (selected)
                {
                    var underline = UiFactory.NewImage("ActiveUnderline", nav.transform, UiFactory.GoldPrimary);
                    UiFactory.PlaceBottomCentre(underline.rectTransform, 20f, 96f, 4f,
                        -1920f / 2f + slotWidth * (i + 0.5f));
                }

                var hit = item.gameObject.AddComponent<Button>();
                var captured = destination;
                hit.onClick.AddListener(() =>
                {
                    if (captured.body != null) ShowSheet(captured.label, captured.body, "CLOSE");
                    else SetStatus("Home is this screen.", UiFactory.TextSecondary);
                });
            }

            // Status line above the nav, used by dev tools and nav feedback.
            _status = UiFactory.NewText("Status", root, "", 16, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceBottomLeft(_status.rectTransform, Margin, NavHeight + 6f, 1200f, 28f);
        }

        // ---- dev row ----------------------------------------------------------

        private void BuildDevRow(RectTransform root)
        {
            var grant = UiFactory.NewButton("DevGrant", root, "+5,000 COINS", 14,
                UiFactory.TextSecondary, out Text grantText);
            UiFactory.PlaceBottomLeft(grant.GetComponent<RectTransform>(),
                1920f - Margin - 200f, NavHeight + 4f, 200f, 32f);
            UiFactory.Stretch(grantText.rectTransform);
            grant.onClick.AddListener(() =>
            {
                DevGrantCoins(5000);
                SetStatus("Dev grant: +5,000 Coins (ledger reason Admin).", UiFactory.StateDanger);
            });
        }
        // =====================================================================
        // State
        // =====================================================================

        private void Refresh()
        {
            if (_coins == null) return;

            var wallet = WalletService.Instance;
            _coins.text = $"{CurrencyRules.Symbol(Currency.Coins)}  {UiFactory.FormatAmount(wallet != null ? wallet.Balance(Currency.Coins) : 0)}";
            _gems.text = $"{CurrencyRules.Symbol(Currency.Gems)}  {UiFactory.FormatAmount(wallet != null ? wallet.Balance(Currency.Gems) : 0)}";

            var progression = Economy.PlayerProgression.Instance;
            if (progression != null && _level != null) _level.text = progression.Level.ToString();
        }

        private void SetStatus(string message, Color color)
        {
            if (_status == null) return;
            _status.text = message;
            _status.color = color;
        }

        private void OnLockedTapped(string title) =>
            ShowSheet(title.ToUpperInvariant(), "This title is marked coming soon.", "GOT IT");

        private void ShowSheet(string title, string body, string closeLabel)
        {
            if (_sheet != null) _sheet.Close();

            _sheet = ModalSheet.Create(_root, title, body, closeLabel);
            _sheet.Open();
        }

        /// <summary>QA helper: grants Coins through the real ledger path.</summary>
        public void DevGrantCoins(long amount)
        {
            if (WalletService.Instance == null) return;
            WalletService.Instance.TryGrant(Currency.Coins, amount, LedgerReason.Admin, "greybox dev grant");
            Refresh();
        }
    }
}
