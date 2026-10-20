using System;
using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The lobby, laid out like a casino floor rather than a settings screen.
    ///
    ///   0.. 80   top tier       — the house wordmark, and the Gems store widget
    ///  80..152   gold strip     — Rewards · Coins · Gems · promo ribbon · VIP · menu
    /// 158..202   category chips — All / Slots / Jackpots / New / Favorites / High Vol
    /// 210..556   feature row    — portrait cards, the promo slide first
    /// 570..620   shelf header   — the TOP GAMES ribbon
    /// 628..958   library shelf  — the whole catalogue, paged
    /// 966..992   page dots
    /// 996..1080  bottom bar     — Friends · daily-gift countdown · Inbox
    ///
    /// The shape follows the House of Fun reference: a bright top tier over a gold status
    /// strip, portrait feature cards, and a paged shelf of the library under a ribbon
    /// header. Two things are deliberately not copied. The reference sells Coins directly
    /// and this product never does (guardrail §2) — the store widget sells Gems, which is
    /// the only monetised flow (§4.6). And the reference's bottom timer is a giveaway
    /// countdown; this one is the daily gift, because that is a real reward with a real
    /// deadline rather than a decoration.
    ///
    /// The chrome is procedural, like every other screen here: <see cref="UiChrome"/> makes
    /// the gold rims, angled ribbons and neon glows, and <see cref="LobbyBackdrop"/> makes
    /// the hall behind it all.
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

        // ---- layout (1920x1080 reference pixels) --------------------------------

        private const float ReferenceWidth = 1920f;
        private const float Margin = 48f;

        private const float TopTierHeight = 80f;
        private const float StripY = 80f, StripHeight = 72f;
        private const float TabsY = 158f, TabsHeight = 44f;
        private const float FeaturedY = 210f, FeaturedHeight = 346f;
        private const float ShelfHeaderY = 570f, ShelfHeaderHeight = 50f;
        private const float ShelfY = 628f, ShelfHeight = 340f;
        private const float DotsY = 966f;
        private const float BottomY = 996f, BottomHeight = 84f;

        // ---- state ---------------------------------------------------------------

        private Text _coins;
        private Text _gems;
        private Text _level;
        private Text _vipTier;
        private Text _vipProgress;
        private Image _xpFill;
        private Text _countdown;
        private Image _inboxDot;
        private DailyBonusChip _dailyChip;
        private Text _status;
        private Text _offlineChip;
        private LobbyGrid _grid;
        private FeaturedRow _featuredRow;
        private ModalSheet _sheet;
        private RectTransform _root;

        private void Awake()
        {
            ServicesBootstrap.EnsureExists();

            if (controller == null) controller = GetComponent<LobbyController>();
            if (controller != null)
            {
                // The scene carries the catalogue on the controller and the presenter is
                // added at runtime, so this is the only chance to pick it up. Without it the
                // backdrop and the feature row build empty, and nothing rebuilds them.
                if (catalog == null) catalog = controller.catalog;
                if (controller.catalog == null) controller.catalog = catalog;
            }

            EnsurePromoService();
            UiFactory.EnsureEventSystem();
        }

        private void OnEnable()
        {
            EventBus.Subscribe<WalletChanged>(OnWalletChanged);
            EventBus.Subscribe<LevelUp>(OnLevelUp);
            Build();
            Refresh();
            AnnounceNewUnlocks();

            // The lobby is already usable; the override only ever improves it in place.
            promo?.FetchRemoteOverride();
        }

        private void OnDisable()
        {
            EventBus.Unsubscribe<WalletChanged>(OnWalletChanged);
            EventBus.Unsubscribe<LevelUp>(OnLevelUp);
        }

        private void Update()
        {
            RefreshCountdown();
        }

        private void OnWalletChanged(WalletChanged change)
        {
            if (change.currency == Currency.LustPoints) return;  // non-wagerable
            Refresh();
            _grid?.RebuildGrid();
        }

        private void EnsurePromoService()
        {
            if (promo == null) promo = GetComponent<PromoService>();

            if (promo == null) promo = FindFirstObjectByType<PromoService>();

            if (promo == null)
            {
                var host = new GameObject("PromoService");
                DontDestroyOnLoad(host);
                promo = host.AddComponent<PromoService>();
            }

            // The service resolves the feature row's tiles through the catalogue, so it
            // needs the same one the lobby has.
            if (promo.catalog == null) promo.catalog = catalog;
        }

        // =====================================================================
        // Layout
        // =====================================================================

        private void Build()
        {
            if (_root != null) return;   // build once

            var canvas = UiFactory.ResolveCanvas(transform, "LobbyCanvas");
            _root = canvas.transform as RectTransform;

            LobbyBackdrop.Create(_root, catalog);

            BuildTopTier(_root);
            BuildStrip(_root);

            // The grid owns the category chips and the shelf, so it has to exist before
            // either of them is built.
            _grid = GetComponent<LobbyGrid>() ?? gameObject.AddComponent<LobbyGrid>();
            _grid.controller = controller;
            _grid.catalog = catalog;
            _grid.RaiseLockNotice += OnLockedTapped;

            BuildCategoryChips(_root);
            BuildFeatureRow(_root);
            BuildShelf(_root);
            BuildBottomBar(_root);

            if (showDevTools) BuildDevRow(_root);
        }

        // ---- 1. top tier ---------------------------------------------------------

        private void BuildTopTier(RectTransform root)
        {
            // The house wordmark, with a magenta bloom behind it.
            var logoGlow = UiChrome.NewGlow("LogoGlow", root, new Color(1f, 0.24f, 0.55f, 0.35f));
            UiFactory.PlaceTopLeft(logoGlow.rectTransform, 12f, -18f, 280f, 130f);

            var lust = UiFactory.NewText("Lust", root, "LUST", 36, FontStyle.Bold,
                UiFactory.TextPrimary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(lust.rectTransform, Margin + 8f, 4f, 120f, 44f);

            var house = UiFactory.NewText("House", root, "HOUSE", 46, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(house.rectTransform, Margin + 104f, 0f, 200f, 50f);

            var casino = UiFactory.NewText("Casino", root, "C A S I N O", 12, FontStyle.Bold,
                UiFactory.TextSecondary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(casino.rectTransform, Margin + 12f, 52f, 160f, 20f);

            BuildPlayerChip(root);
            BuildStoreWidget(root);
        }

        /// <summary>
        /// The player chip (plan §2.5): who is playing and what level they are. The
        /// reference puts nothing there, but the plan wants the player's identity on the
        /// lobby, and the gap under the wordmark is the only quiet space on the top tier.
        /// </summary>
        private void BuildPlayerChip(RectTransform root)
        {
            var avatar = UiFactory.NewImage("Avatar", root, UiFactory.VelvetRaised);
            UiFactory.PlaceTopLeft(avatar.rectTransform, 520f, 16f, 48f, 48f);
            avatar.raycastTarget = true;
            avatar.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                ShowSheet("CLUB",
                    "The Club screen — avatar editor, wardrobe, and stats — is M4 work.\n\n" +
                    "Everything you see here is a placeholder.", "CLOSE"));

            var initial = UiFactory.NewText("Initial", avatar.transform, "P", 24,
                FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Stretch(initial.rectTransform);

            var name = UiFactory.NewText("PlayerName", root, "PLAYER", 14, FontStyle.Bold,
                UiFactory.TextSecondary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(name.rectTransform, 580f, 12f, 200f, 20f);

            _level = UiFactory.NewText("Level", root, "LVL 1", 22, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(_level.rectTransform, 580f, 32f, 200f, 30f);
        }

        /// <summary>
        /// The store widget. The reference sells Coins here; this product sells Gems, because
        /// Coins are never purchasable (guardrail §2) and Gems are the only monetised flow
        /// (§4.6). The button opens the Gems sheet and is never a dead end.
        /// </summary>
        private void BuildStoreWidget(RectTransform root)
        {
            var buy = UiFactory.NewButton("BuyGems", root, "BUY", 22,
                UiFactory.TextPrimary, out Text buyText);
            UiFactory.PlaceTopLeft(buy.GetComponent<RectTransform>(), 1776f, 16f, 96f, 48f);
            UiFactory.Stretch(buyText.rectTransform);
            buy.GetComponent<Image>().color = new Color32(0x22, 0xB2, 0x4C, 0xFF);
            buy.onClick.AddListener(ShowGemsStore);

            // The reference points at the buy button with a red arrow. Same beat, house colour.
            var arrow = UiFactory.NewText("Arrow", root, "◀", 26, FontStyle.Bold,
                UiFactory.NeonMagenta, TextAnchor.MiddleCenter);
            UiFactory.PlaceTopLeft(arrow.rectTransform, 1730f, 16f, 40f, 48f);

            var offer = UiChrome.NewPill("GemOffer", root, "25 ⬡", 22,
                UiFactory.NeonCyan, out Text offerText);
            UiFactory.PlaceTopLeft(offer.rectTransform, 1540f, 16f, 176f, 48f);

            var label = UiFactory.NewText("GetGemsLabel", root, "GET GEMS", 24, FontStyle.Bold,
                UiFactory.GoldPrimary, TextAnchor.MiddleRight);
            UiFactory.PlaceTopLeft(label.rectTransform, 1300f, 16f, 230f, 48f);
        }

        // ---- 2. the gold strip ----------------------------------------------------

        private void BuildStrip(RectTransform root)
        {
            var strip = UiFactory.NewImage("GoldStrip", root, new Color32(0x2A, 0x10, 0x2E, 0xFF));
            UiFactory.PlaceTopStrip(strip.rectTransform, StripY, StripHeight);

            // A gold edge top and bottom, so the strip reads as a bar of the house.
            var goldTop = UiFactory.NewImage("GoldEdge", strip.transform, UiFactory.GoldPrimary);
            UiFactory.PlaceTopStrip(goldTop.rectTransform, 0f, 3f);
            var goldBottom = UiFactory.NewImage("GoldEdge", strip.transform, UiFactory.GoldPrimary);
            UiFactory.PlaceBottomLeft(goldBottom.rectTransform, 0f, 0f, ReferenceWidth, 3f);

            float chipY = StripY + 12f;
            const float chipH = 48f;

            // Rewards: the daily gift, with its own countdown ring.
            var dailyHost = new GameObject("DailyBonusHost", typeof(RectTransform));
            dailyHost.transform.SetParent(root, false);
            _dailyChip = dailyHost.AddComponent<DailyBonusChip>();
            _dailyChip.service = DailyBonusService.Instance;
            _dailyChip.OnOpen = ShowDailyBonusSheet;
            if (_dailyChip.service == null)
                LustLog.Warn("[LustHouse] DailyBonusService.Instance is null; the rewards chip will be inert.");
            UiFactory.PlaceTopLeft(dailyHost.GetComponent<RectTransform>(), Margin, chipY, 190f, chipH);
            _dailyChip.Build(dailyHost.GetComponent<RectTransform>());

            // Coins.
            var coinChip = UiChrome.NewPill("CoinChip", root, "◎  0", 24,
                UiFactory.GoldPrimary, out _coins);
            UiFactory.PlaceTopLeft(coinChip.rectTransform, 256f, chipY, 210f, chipH);

            // Gems.
            var gemChip = UiChrome.NewPill("GemChip", root, "⬡  0", 24,
                UiFactory.NeonCyan, out _gems);
            UiFactory.PlaceTopLeft(gemChip.rectTransform, 482f, chipY, 176f, chipH);

            // The promo ribbon in the middle, the way the reference carries its SALE tab.
            var ribbon = UiChrome.NewRibbon("PromoRibbon", root, "B O N U S", 24,
                UiFactory.NeonMagenta, UiFactory.TextPrimary, out Text ribbonText);
            UiFactory.PlaceTopCentre(ribbon.rectTransform, chipY + 2f, 300f, 44f);
            var ribbonGlow = UiChrome.NewGlow("RibbonGlow", root, new Color(1f, 0.24f, 0.55f, 0.30f));
            UiFactory.PlaceTopCentre(ribbonGlow.rectTransform, chipY - 8f, 340f, 60f);
            ribbonGlow.transform.SetAsFirstSibling();

            // VIP tier and progress, right of centre.
            var vip = UiChrome.NewPill("VipChip", root, "★ BRONZE", 20,
                UiFactory.GoldPrimary, out _vipTier);
            UiFactory.PlaceTopLeft(vip.rectTransform, 1400f, chipY, 210f, chipH);

            var xp = UiChrome.NewPill("XpChip", root, "0%", 20,
                UiFactory.TextSecondary, out _vipProgress);
            UiFactory.PlaceTopLeft(xp.rectTransform, 1622f, chipY, 152f, chipH);

            var xpTrack = UiFactory.NewImage("XpTrack", xp.transform, UiFactory.VelvetBase);
            UiFactory.PlaceBottomCentre(xpTrack.rectTransform, 5f, 130f, 5f);
            _xpFill = UiFactory.NewImage("XpFill", xpTrack.transform, UiFactory.GoldPrimary);
            _xpFill.type = Image.Type.Filled;
            _xpFill.fillMethod = Image.FillMethod.Horizontal;
            _xpFill.fillAmount = 0f;
            UiFactory.Stretch(_xpFill.rectTransform);

            // The menu.
            var menu = UiFactory.NewButton("Menu", root, "☰", 30,
                UiFactory.GoldPrimary, out Text menuText);
            UiFactory.PlaceTopLeft(menu.GetComponent<RectTransform>(), 1796f, chipY, 76f, chipH);
            UiFactory.Stretch(menuText.rectTransform);
            menu.GetComponent<Image>().color = UiFactory.VelvetRaised;
            menu.onClick.AddListener(() => ShowSheet("MENU",
                "Missions, tournaments, the rewards pass, the boutique and settings all ship " +
                "in M4.\n\nThis menu exists so it is never a dead end.", "CLOSE"));

            // Persistent OFFLINE chip (§2.13) — hidden until a fetch actually fails.
            _offlineChip = UiFactory.NewText("OfflineChip", root, "OFFLINE", 13,
                FontStyle.Bold, UiFactory.StateWarn, TextAnchor.MiddleCenter);
            UiFactory.PlaceTopLeft(_offlineChip.rectTransform, 674f, chipY, 110f, chipH);
            _offlineChip.gameObject.SetActive(false);
        }

        // ---- 3. category chips ----------------------------------------------------

        private void BuildCategoryChips(RectTransform root)
        {
            var band = new GameObject("CategoryChips", typeof(RectTransform));
            band.transform.SetParent(root, false);
            UiFactory.PlaceTopLeft(band.GetComponent<RectTransform>(), Margin, TabsY,
                ReferenceWidth - Margin * 2f, TabsHeight);

            _grid.BuildTabs(band.transform, Categories);
        }

        // ---- 4. feature row -------------------------------------------------------

        private void BuildFeatureRow(RectTransform root)
        {
            var host = new GameObject("FeaturedArea", typeof(RectTransform));
            host.transform.SetParent(root, false);
            UiFactory.PlaceTopLeft(host.GetComponent<RectTransform>(), Margin, FeaturedY,
                ReferenceWidth - Margin * 2f, FeaturedHeight);

            _featuredRow = host.AddComponent<FeaturedRow>();
            _featuredRow.tileHeight = FeaturedHeight - 32f;
            _featuredRow.SetPromo(promo, controller);
            _featuredRow.PromoTapped += OnPromoTapped;
            _featuredRow.TeaserTapped += (headline, body) => ShowSheet(headline, body, "GOT IT");
        }

        // ---- 5. the shelf ---------------------------------------------------------

        private void BuildShelf(RectTransform root)
        {
            // The ribbon header, centred above the row.
            var ribbon = UiChrome.NewRibbon("ShelfRibbon", root, "T O P   G A M E S", 26,
                UiFactory.NeonMagenta, UiFactory.TextPrimary, out Text ribbonText);
            UiFactory.PlaceTopCentre(ribbon.rectTransform, ShelfHeaderY, 380f, ShelfHeaderHeight);
            var glow = UiChrome.NewGlow("ShelfGlow", root, new Color(1f, 0.24f, 0.55f, 0.28f));
            UiFactory.PlaceTopCentre(glow.rectTransform, ShelfHeaderY - 10f, 440f, 70f);
            glow.transform.SetAsFirstSibling();

            var host = new GameObject("Shelf", typeof(RectTransform));
            host.transform.SetParent(root, false);
            UiFactory.PlaceTopLeft(host.GetComponent<RectTransform>(), Margin, ShelfY,
                ReferenceWidth - Margin * 2f, ShelfHeight);

            _grid.BuildShelf(host.GetComponent<RectTransform>(), ShelfHeight);
            _grid.RebuildGrid();
        }

        // ---- 6. bottom bar --------------------------------------------------------

        private void BuildBottomBar(RectTransform root)
        {
            var bar = UiFactory.NewImage("BottomBar", root, new Color32(0x14, 0x08, 0x1A, 0xCC));
            UiFactory.PlaceTopStrip(bar.rectTransform, BottomY, BottomHeight);

            var goldEdge = UiFactory.NewImage("GoldEdge", bar.transform, UiFactory.GoldPrimary);
            UiFactory.PlaceTopStrip(goldEdge.rectTransform, 0f, 3f);

            float buttonY = BottomY + 12f;
            const float buttonH = 60f;

            var friends = UiChrome.NewPillButton("Friends", root, "F R I E N D S", 20,
                UiFactory.TextPrimary, out Text friendsText);
            UiFactory.PlaceTopLeft(friends.GetComponent<RectTransform>(), Margin, buttonY, 190f, buttonH);
            friends.onClick.AddListener(() => ShowSheet("FRIENDS",
                "Friends, chat and emotes are deferred to 1.1 (plan §6).\n\n" +
                "This button exists so it is never a dead end.", "CLOSE"));

            // The daily-gift countdown, the reference's giveaway timer.
            var timer = UiChrome.NewPill("GiftTimer", root, "—", 24,
                UiFactory.GoldPrimary, out _countdown);
            UiFactory.PlaceTopCentre(timer.rectTransform, buttonY + 4f, 460f, buttonH - 8f);
            timer.raycastTarget = true;
            var timerButton = timer.gameObject.AddComponent<Button>();
            timerButton.targetGraphic = timer;
            timerButton.onClick.AddListener(ShowDailyBonusSheet);

            var inbox = UiChrome.NewPillButton("Inbox", root, "I N B O X", 20,
                UiFactory.TextPrimary, out Text inboxText);
            UiFactory.PlaceTopLeft(inbox.GetComponent<RectTransform>(), ReferenceWidth - Margin - 190f,
                buttonY, 190f, buttonH);
            inbox.onClick.AddListener(() => ShowSheet("INBOX",
                "Missions and tournament invitations arrive here (plan §2.9).\n\n" +
                "The badge lights up when the daily gift is ready to claim.", "CLOSE"));

            _inboxDot = UiFactory.NewImage("Dot", inbox.transform, UiFactory.StateDanger);
            UiFactory.PlaceTopLeft(_inboxDot.rectTransform, 156f, 8f, 16f, 16f);
            _inboxDot.gameObject.SetActive(false);
        }

        // ---- dev row --------------------------------------------------------------

        private void BuildDevRow(RectTransform root)
        {
            var grant = UiFactory.NewButton("DevGrant", root, "+5,000 COINS", 14,
                UiFactory.TextSecondary, out Text grantText);
            UiFactory.PlaceTopLeft(grant.GetComponent<RectTransform>(),
                ReferenceWidth - Margin - 380f, BottomY + 16f, 180f, 52f);
            UiFactory.Stretch(grantText.rectTransform);
            grant.onClick.AddListener(() =>
            {
                DevGrantCoins(5000);
                SetStatus("Dev grant: +5,000 Coins (ledger reason Admin).", UiFactory.StateDanger);
            });

            _status = UiFactory.NewText("Status", root, "", 15, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(_status.rectTransform, Margin, DotsY, 420f, 26f);
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
            if (progression != null)
            {
                if (_level != null) _level.text = $"LVL {progression.Level}";

                if (_xpFill != null)
                    _xpFill.fillAmount = Mathf.Clamp01((float)progression.Xp / Mathf.Max(1, progression.XpForNextLevel()));

                if (_vipProgress != null)
                    _vipProgress.text = $"{Mathf.RoundToInt(Mathf.Clamp01((float)progression.Xp / Mathf.Max(1, progression.XpForNextLevel())) * 100f)}%";

                if (_vipTier != null)
                {
                    var tier = progression.CurrentVipTier;
                    _vipTier.text = $"★ {tier.name.ToUpperInvariant()}";
                    _vipTier.color = tier.accent.a > 0.01f ? tier.accent : UiFactory.GoldPrimary;
                }
            }

            if (promo != null && _offlineChip != null)
                _offlineChip.gameObject.SetActive(promo.IsOffline);

            RefreshCountdown();
        }

        /// <summary>
        /// The bottom-bar timer. It is the daily gift, because that is a real reward with a
        /// real deadline — the reference's equivalent is a giveaway countdown.
        /// </summary>
        private void RefreshCountdown()
        {
            if (_countdown == null) return;

            var service = DailyBonusService.Instance;
            if (service == null)
            {
                _countdown.text = "D A I L Y   G I F T";
                return;
            }

            if (service.IsClaimable)
            {
                var reward = service.NextReward;
                long amount = reward != null ? reward.coins : 0;
                _countdown.text = amount > 0
                    ? $"{CurrencyRules.Symbol(Currency.Coins)}  {UiFactory.FormatAmount(amount)}   C L A I M   N O W"
                    : "D A I L Y   G I F T   R E A D Y";
                _countdown.color = UiFactory.GoldGlow;
            }
            else
            {
                var remaining = service.TimeUntilNextClaim;
                _countdown.text = $"{CurrencyRules.Symbol(Currency.Coins)}  {UiFactory.FormatAmount(ServiceRewardCoins(service))}   IN   {FormatClock(remaining)}";
                _countdown.color = UiFactory.GoldPrimary;
            }

            if (_inboxDot != null)
                _inboxDot.gameObject.SetActive(service.IsClaimable);
        }

        private static long ServiceRewardCoins(DailyBonusService service)
        {
            var reward = service.NextReward;
            return reward != null ? reward.coins : 0;
        }

        private static string FormatClock(TimeSpan span)
        {
            if (span <= TimeSpan.Zero) return "00:00:00";
            return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
        }

        private void SetStatus(string message, Color color)
        {
            if (_status == null) return;
            _status.text = message;
            _status.color = color;
        }

        private void OnLockedTapped(string title) =>
            ShowSheet(title.ToUpperInvariant(), "This title is marked coming soon.", "GOT IT");

        private void OnPromoTapped(PromoSlide slide)
        {
            if (slide == null) return;

            if (slide.kind == PromoSlideKind.DailyBonus)
            {
                ShowDailyBonusSheet();
                return;
            }

            ShowSheet(string.IsNullOrEmpty(slide.headline) ? "FEATURED" : slide.headline,
                string.IsNullOrEmpty(slide.subline) ? "This promotion is coming soon." : slide.subline,
                "GOT IT");
        }

        private void ShowDailyBonusSheet()
        {
            if (DailyBonusService.Instance == null)
            {
                ShowSheet("DAILY BONUS", "Daily bonus is not ready yet.", "CLOSE");
                return;
            }
            DailyBonusSheet.Create(_root, DailyBonusService.Instance);
        }

        /// <summary>
        /// The Gems store. There is no store implementation yet, so the sheet says so plainly
        /// rather than pretending — a button that leads nowhere is the one thing a player
        /// cannot act on.
        /// </summary>
        private void ShowGemsStore()
        {
            int remaining = WalletService.Instance != null
                ? WalletService.Instance.ConversionsRemainingToday()
                : 0;

            ShowSheet("GET GEMS",
                "Gems are the premium currency, and the only thing this product ever sells.\n\n" +
                "The store ships in M6. Until then, Gems arrive on day 7 of the daily gift, " +
                "and Coins can be converted from Gems " +
                $"({remaining} of {CurrencyRules.MaxConversionsPerDay} conversions left today).\n\n" +
                "Coins are never sold directly.", "GOT IT");
        }

        private void OnLevelUp(LevelUp evt)
        {
            Refresh();
            string unlocked = UnlockedNamesAt(evt.newLevel);
            if (!string.IsNullOrEmpty(unlocked))
                ShowSheet($"LEVEL {evt.newLevel}", $"New game unlocked: {unlocked}\n\n+{evt.xpReward} Coins", "GOT IT");
            else
                SetStatus($"Level {evt.newLevel}! +{evt.xpReward} Coins", UiFactory.GoldPrimary);
        }

        private string UnlockedNamesAt(int level)
        {
            if (catalog == null) return null;
            var progression = Economy.PlayerProgression.Instance;
            var names = new System.Collections.Generic.List<string>();
            foreach (var entry in catalog.games)
            {
                if (entry == null || entry.locked) continue;
                if (entry.unlockLevel == level) names.Add(entry.Title);
            }
            return names.Count > 0 ? string.Join(", ", names) : null;
        }

        private void AnnounceNewUnlocks()
        {
            var progression = Economy.PlayerProgression.Instance;
            if (progression == null || catalog == null) return;
            if (progression.Level <= progression.LastAnnouncedLevel) return;

            var names = new System.Collections.Generic.List<string>();
            foreach (var entry in catalog.games)
            {
                if (entry == null || entry.locked) continue;
                if (entry.unlockLevel > progression.LastAnnouncedLevel && entry.unlockLevel <= progression.Level)
                    names.Add(entry.Title);
            }

            progression.MarkLastAnnouncedLevel(progression.Level);

            if (names.Count > 0)
                ShowSheet($"LEVEL {progression.Level}", $"New games unlocked: {string.Join(", ", names)}", "GOT IT");
        }

        /// <summary>
        /// Opens the top-up sheet for a game the player cannot enter. A tile that refuses to
        /// launch must say why on screen: a tap that silently does nothing is indistinguishable
        /// from a broken build, which is the one reading a player cannot act on.
        /// </summary>
        public void ShowEntryBlocked(string title, string reason) =>
            ShowSheet(title.ToUpperInvariant(), reason, "CLOSE");

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
