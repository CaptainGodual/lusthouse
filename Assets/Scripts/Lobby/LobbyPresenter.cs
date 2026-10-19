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
    ///   1. Top bar        — avatar + level, LVL, Coins (+), Gems, gear      [mockup, §2.5]
    ///   2. Carousel       — daily bonus / newest / jackpot slides          [mockup, §2.6]
    ///   3. Featured row   — large tiles driven by promo config            [mockup]
    ///   4. Category tabs  — All / Slots / Jackpots / New / Favorites / High Vol  [§2.7]
    ///   5. Game grid      — dense 224x280 tiles                           [§2.8]
    ///   6. Bottom nav     — Home / Shop / Missions / Social / Leaderboard [mockup, §2.11]
    ///
    /// The carousel and featured row are built by <see cref="PromoCarousel"/> and
    /// <see cref="FeaturedRow"/>, both data-driven from <see cref="PromoService"/>.
    ///
    /// The plan's desktop side rail (§2.9) is not built yet and remains M4.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public LobbyController controller;
        public GameCatalog catalog;
        public PromoService promo;

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
        private Text _offlineChip;
        private LobbyGrid _grid;
        private PromoCarousel _carousel;
        private FeaturedRow _featuredRow;
        private ModalSheet _sheet;
        private RectTransform _root;

        private void Awake()
        {
            ServicesBootstrap.EnsureExists();

            if (controller == null) controller = GetComponent<LobbyController>();
            if (controller != null && catalog != null && controller.catalog == null)
                controller.catalog = catalog;

            EnsurePromoService();
            UiFactory.EnsureEventSystem();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WalletChanged>(OnWalletChanged);
            Build();
            Refresh();

            // The lobby is already usable; the override only ever improves it in place.
            promo?.FetchRemoteOverride();
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

        private void EnsurePromoService()
        {
            if (promo != null) return;

            promo = GetComponent<PromoService>();
            if (promo != null) return;

            promo = FindFirstObjectByType<PromoService>();
            if (promo != null) return;

            var host = new GameObject("PromoService");
            DontDestroyOnLoad(host);
            promo = host.AddComponent<PromoService>();
            promo.catalog = catalog;
        }

        // =====================================================================
        // Layout
        //
        // One vertical rhythm, top to bottom, in 1920x1080 reference pixels. Every rect
        // is positioned by its top-left corner via UiFactory.PlaceTopLeft /
        // PlaceTopCentre, so nothing depends on a hand-computed pivot.
        //
        //    0.. 88   top bar
        //  100..232   carousel banner (PromoCarousel.BannerY / BannerHeight)
        //  244..544   featured row
        //  556..960   library: heading / tabs / grid
        //  976..1080  bottom nav
        // =====================================================================

        private const float Margin = 40f;
        private const float TopBarHeight = 88f;
        private const float FeaturedY = 244f, FeaturedH = 300f;
        private const float LibraryTop = 556f, LibraryBottom = 120f;
        private const float NavHeight = 104f;

        private void Build()
        {
            if (_root != null) return;   // build once

            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null) canvas = UiFactory.CreateCanvas(transform, "LobbyCanvas");
            _root = canvas.transform as RectTransform;

            var backdrop = UiFactory.NewImage("Backdrop", _root, UiFactory.VelvetBase);
            UiFactory.Stretch(backdrop.rectTransform);

            BuildTopBar(_root);
            BuildCarousel(_root);
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
            UiFactory.PlaceTopLeft(coinPlus.GetComponent<RectTransform>(), coinsX + 244f, chipY + 4f, 48f, chipH - 8f);
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

            // Persistent OFFLINE chip (§2.13) — hidden until a fetch actually fails.
            _offlineChip = UiFactory.NewText("OfflineChip", root, "OFFLINE", 13,
                FontStyle.Bold, UiFactory.StateWarn, TextAnchor.MiddleCenter);
            UiFactory.PlaceTopLeft(_offlineChip.rectTransform, gemsX + 236f, chipY, 100f, chipH);
            _offlineChip.gameObject.SetActive(false);

            var gear = UiFactory.NewButton("Gear", root, "⚙", 28, UiFactory.TextSecondary, out Text gearText);
            UiFactory.PlaceTopLeft(gear.GetComponent<RectTransform>(), 1920f - Margin - 56f, chipY, 56f, chipH);
            UiFactory.Stretch(gearText.rectTransform);
            gear.onClick.AddListener(() => ShowSheet("SETTINGS",
                "Audio, graphics, responsible play, and age re-confirmation ship in M4.\n\n" +
                "This sheet exists so the gear is never a dead end.", "CLOSE"));
        }

        // ---- 2. carousel ------------------------------------------------------

        private void BuildCarousel(RectTransform root)
        {
            var host = new GameObject("PromoArea", typeof(RectTransform));
            host.transform.SetParent(root, false);
            UiFactory.PlaceTopLeft(host.GetComponent<RectTransform>(), 0f, 0f, 1920f,
                PromoCarousel.BannerY + PromoCarousel.BannerHeight + 28f);

            _carousel = host.AddComponent<PromoCarousel>();
            _carousel.promo = promo;
            _carousel.controller = controller;
            _carousel.DailyBonusRequested += ShowDailyBonusSheet;
        }

        // ---- 3. featured row --------------------------------------------------

        private void BuildFeaturedRow(RectTransform root)
        {
            var host = new GameObject("FeaturedArea", typeof(RectTransform));
            host.transform.SetParent(root, false);
            UiFactory.PlaceTopLeft(host.GetComponent<RectTransform>(), Margin, FeaturedY,
                1920f - Margin * 2f, FeaturedH);

            _featuredRow = host.AddComponent<FeaturedRow>();
            _featuredRow.promo = promo;
            _featuredRow.controller = controller;
            _featuredRow.tileHeight = FeaturedH;
            _featuredRow.rightMargin = Margin;
            _featuredRow.TeaserTapped += (headline, body) => ShowSheet(headline, body, "GOT IT");
        }

        // ---- 4 + 5. library ---------------------------------------------------

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
            gridRect.offsetMin = Vector2.zero;
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

            if (promo != null && _offlineChip != null)
                _offlineChip.gameObject.SetActive(promo.IsOffline);
        }

        private void SetStatus(string message, Color color)
        {
            if (_status == null) return;
            _status.text = message;
            _status.color = color;
        }

        private void OnLockedTapped(string title) =>
            ShowSheet(title.ToUpperInvariant(), "This title is marked coming soon.", "GOT IT");

        private void ShowDailyBonusSheet() => ShowSheet("DAILY BONUS",
            "The daily gift and its escalation ladder ship in M2/M4.\n\n" +
            "It guarantees enough Coins for at least 20 minutes at the lowest bet (plan §4.3).",
            "CLOSE");

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