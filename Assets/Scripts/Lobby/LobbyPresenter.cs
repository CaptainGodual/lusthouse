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

        // =====================================================================
        // Layout
        // =====================================================================

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

            BuildTabsAndGrid(_root);
            BuildBottomNav(_root);

            if (showDevTools) BuildDevRow(_root);
        }

        // ---- 1. top bar -------------------------------------------------------

        private void BuildTopBar(RectTransform root)
        {
            var bar = UiFactory.NewImage("TopBar", root, UiFactory.VelvetPanel);
            var barRect = bar.rectTransform;
            barRect.anchorMin = new Vector2(0f, 1f);
            barRect.anchorMax = new Vector2(1f, 1f);
            barRect.pivot = new Vector2(0.5f, 1f);
            barRect.sizeDelta = new Vector2(0f, 88f);

            // Avatar + level shield (mockup, left).
            var avatar = UiFactory.NewImage("Avatar", bar.transform, UiFactory.VelvetRaised);
            UiFactory.Place(avatar.rectTransform, new Vector2(0f, 0.5f), new Vector2(56f, 56f), new Vector2(24f, 0f));
            avatar.raycastTarget = true;
            var avatarHit = avatar.gameObject.AddComponent<Button>();
            avatarHit.onClick.AddListener(() => ShowSheet("CLUB",
                "The Club screen — avatar editor, wardrobe, and stats — is M4 work.\n\n" +
                "Everything you see here is a placeholder.", "CLOSE"));

            var avatarInitial = UiFactory.NewText("Initial", avatar.transform, "P", 28,
                FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Stretch(avatarInitial.rectTransform);

            // LVL chip.
            var lvlChip = UiFactory.NewImage("LevelChip", bar.transform, UiFactory.VelvetRaised);
            UiFactory.Place(lvlChip.rectTransform, new Vector2(0f, 0.5f), new Vector2(150f, 52f), new Vector2(96f, 0f));
            var lvlCaption = UiFactory.NewText("Caption", lvlChip.transform, "LVL", 13,
                FontStyle.Bold, UiFactory.TextSecondary, TextAnchor.UpperCenter);
            UiFactory.Place(lvlCaption.rectTransform, new Vector2(0.5f, 1f), new Vector2(140f, 22f), new Vector2(0f, -4f));
            _level = UiFactory.NewText("Value", lvlChip.transform, "1", 28,
                FontStyle.Bold, UiFactory.TextPrimary);
            UiFactory.Place(_level.rectTransform, new Vector2(0.5f, 0f), new Vector2(140f, 34f), new Vector2(0f, 2f));

            // Coins — the only currency with a + entry point in the mockup.
            var coinChip = UiFactory.NewImage("CoinChip", bar.transform, UiFactory.VelvetRaised);
            UiFactory.Place(coinChip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(300f, 56f), new Vector2(60f, 0f));
            _coins = UiFactory.NewText("Value", coinChip.transform, "◎  0", 26,
                FontStyle.Bold, UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            UiFactory.Place(_coins.rectTransform, new Vector2(0f, 0.5f), new Vector2(220f, 56f), new Vector2(18f, 0f));

            var coinPlus = UiFactory.NewButton("CoinPlus", coinChip.transform, "+", 32,
                UiFactory.VelvetBase, out Text coinPlusText);
            UiFactory.Place(coinPlus.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(52f, 44f), new Vector2(-8f, 0f));
            UiFactory.Stretch(coinPlusText.rectTransform);
            coinPlus.GetComponent<Image>().color = UiFactory.GoldPrimary;
            coinPlus.onClick.AddListener(() => controller?.OnCurrencyPlusPressed(Currency.Coins));

            // Gems — no + in the mockup, matching §2.5 (Gems + is wired in M4).
            var gemChip = UiFactory.NewImage("GemChip", bar.transform, UiFactory.VelvetRaised);
            UiFactory.Place(gemChip.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(220f, 56f), new Vector2(400f, 0f));
            _gems = UiFactory.NewText("Value", gemChip.transform, "⬡  0", 26,
                FontStyle.Bold, UiFactory.NeonCyan, TextAnchor.MiddleLeft);
            UiFactory.Place(_gems.rectTransform, new Vector2(0f, 0.5f), new Vector2(200f, 56f), new Vector2(18f, 0f));

            // Settings gear.
            var gear = UiFactory.NewButton("Gear", bar.transform, "⚙", 30, UiFactory.TextSecondary, out Text gearText);
            UiFactory.Place(gear.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(56f, 56f), new Vector2(-24f, 0f));
            UiFactory.Stretch(gearText.rectTransform);
            gear.onClick.AddListener(() => ShowSheet("SETTINGS",
                "Audio, graphics, responsible play, and age re-confirmation ship in M4.\n\n" +
                "This sheet exists so the gear is never a dead end.", "CLOSE"));
        }

        // ---- 2. daily bonus banner -------------------------------------------

        private void BuildDailyBonus(RectTransform root)
        {
            var banner = UiFactory.NewImage("DailyBonus", root, UiFactory.VelvetPanel);
            UiFactory.Place(banner.rectTransform, new Vector2(0.5f, 1f), new Vector2(1000f, 132f), new Vector2(0f, -104f));
            banner.raycastTarget = true;

            var edge = UiFactory.NewImage("Edge", banner.transform, UiFactory.NeonMagenta);
            UiFactory.Stretch(edge.rectTransform, 2f);

            var heading = UiFactory.NewText("Heading", banner.transform, "D A I L Y   B O N U S", 34,
                FontStyle.Bold, UiFactory.GoldGlow);
            UiFactory.Place(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(520f, 56f), new Vector2(0f, 18f));

            var claim = UiFactory.NewButton("Claim", banner.transform, "CLAIM", 24, UiFactory.VelvetBase, out Text claimText);
            UiFactory.Place(claim.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(200f, 56f), new Vector2(240f, -34f));
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
                var dotSize = i == 0 ? 12f : 8f;
                UiFactory.Place(dot.rectTransform, new Vector2(0.5f, 0f),
                    new Vector2(dotSize, dotSize), new Vector2((i - 1.5f) * 26f, 16f));
            }
        }

        // ---- 3. featured row (mockup) ----------------------------------------

        private void BuildFeaturedRow(RectTransform root)
        {
            // Featured tiles mirror the mockup: one large featured slot tile plus the
            // category tiles that exist. Blackjack/Roulette/Poker are shown as locked
            // teasers rather than shipped, because plan §3.4 is slots-only.
            var row = new GameObject("FeaturedRow", typeof(RectTransform));
            row.transform.SetParent(root, false);
            var rowRect = (RectTransform)row.transform;
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0f, 1f);
            rowRect.anchoredPosition = new Vector2(0f, -252f);
            rowRect.sizeDelta = new Vector2(0f, 232f);

            var layout = new List<(string label, string body, Color accent, bool locked, string gameId)>
            {
                ("FEATURED SLOTS", "Featured slot games from the library.", UiFactory.NeonMagenta, false, null),
                ("BLACKJACK",  "Table games are not in the v1 roster (plan §3.4 is slots-only).", UiFactory.StateSuccess, true, null),
                ("ROULETTE",   "Table games are not in the v1 roster (plan §3.4 is slots-only).", UiFactory.NeonMagenta, true, null),
                ("POKER",      "Table games are not in the v1 roster (plan §3.4 is slots-only).", UiFactory.NeonCyan, true, null)
            };

            const float tileW = 260f, tileH = 232f, gap = 24f;
            const int maxVisible = 4;

            for (int i = 0; i < maxVisible; i++)
            {
                var spec = layout[i];
                float x = i * (tileW + gap);

                var tile = UiFactory.NewImage("Featured_" + spec.label.Replace(' ', '_'), rowRect,
                    new Color(spec.accent.r * 0.30f, spec.accent.g * 0.30f, spec.accent.b * 0.30f, 1f));
                tile.raycastTarget = true;
                UiFactory.Place(tile.rectTransform, new Vector2(0f, 1f), new Vector2(tileW, tileH), new Vector2(x, 0f));

                var edge = UiFactory.NewImage("Edge", tile.transform, spec.accent);
                UiFactory.Stretch(edge.rectTransform, 3f);

                var label = UiFactory.NewText("Label", tile.transform, spec.label, 26,
                    FontStyle.Bold, spec.locked ? UiFactory.TextDisabled : UiFactory.TextPrimary,
                    TextAnchor.LowerCenter);
                var labelRect = label.rectTransform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 0f);
                labelRect.pivot = new Vector2(0.5f, 0f);
                labelRect.offsetMin = new Vector2(8f, 14f);
                labelRect.offsetMax = new Vector2(-8f, 62f);

                var captured = spec;
                tile.gameObject.AddComponent<Button>().onClick.AddListener(() =>
                {
                    if (captured.locked) { ShowSheet(captured.label, captured.body, "GOT IT"); return; }

                    // Featured slots = the grid's first entry.
                    var first = catalog != null && catalog.games.Count > 0 ? catalog.games[0] : null;
                    if (first != null) controller?.OnGameTilePressed(first.id);
                });
            }

            // Right chevron affordance, as in the mockup.
            var chevron = UiFactory.NewText("Chevron", rowRect, "›", 64, FontStyle.Bold,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Place(chevron.rectTransform, new Vector2(1f, 1f), new Vector2(60f, 232f), new Vector2(-6f, 0f));
        }

        // ---- 4 + 5. tabs and grid --------------------------------------------

        private void BuildTabsAndGrid(RectTransform root)
        {
            var container = new GameObject("Library", typeof(RectTransform));
            container.transform.SetParent(root, false);
            var rect = (RectTransform)container.transform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(40f, 130f);   // above the bottom nav
            rect.offsetMax = new Vector2(-40f, -500f); // below the featured row

            var heading = UiFactory.NewText("Heading", rect, "L I B R A R Y", 20,
                FontStyle.Bold, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.Place(heading.rectTransform, new Vector2(0f, 1f), new Vector2(600f, 32f), new Vector2(0f, -8f));

            _grid.BuildTabs(rect, Categories);

            var gridHost = new GameObject("GridArea", typeof(RectTransform));
            gridHost.transform.SetParent(rect, false);
            var gridRect = (RectTransform)gridHost.transform;
            gridRect.anchorMin = Vector2.zero;
            gridRect.anchorMax = Vector2.one;
            gridRect.offsetMin = new Vector2(0f, 44f);
            gridRect.offsetMax = new Vector2(0f, -48f);

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
            navRect.sizeDelta = new Vector2(0f, 104f);

            // Mockup order: HOME · SHOP · MISSIONS · SOCIAL · LEADERBOARD
            var destinations = new (string label, string body)[]
            {
                ("HOME",       null),
                ("SHOP",       "The Boutique — Gems-only cosmetics — ships in M6 with the cosmetic catalogue."),
                ("MISSIONS",   "Daily and weekly missions ship in M2/M4."),
                ("SOCIAL",     "Social features (friends, chat, emotes) are deferred to 1.1 — plan §6."),
                ("LEADERBOARD","Tournaments are unresolved (plan §2.9); remote leaderboards are deferred to 1.1.")
            };

            int count = destinations.Length;
            float slotWidth = 1920f / count;

            for (int i = 0; i < count; i++)
            {
                var destination = destinations[i];
                bool selected = i == 0;

                var item = UiFactory.NewText("Nav_" + destination.label, nav.transform, destination.label, 20,
                    FontStyle.Bold, selected ? UiFactory.GoldPrimary : UiFactory.TextSecondary);
                UiFactory.Place(item.rectTransform, new Vector2((i + 0.5f) / count, 0.5f),
                    new Vector2(slotWidth, 72f), new Vector2(0f, 6f));

                if (selected)
                {
                    var underline = UiFactory.NewImage("ActiveUnderline", nav.transform, UiFactory.GoldPrimary);
                    UiFactory.Place(underline.rectTransform, new Vector2((i + 0.5f) / count, 0f),
                        new Vector2(96f, 4f), new Vector2(0f, 22f));
                }

                var hit = item.gameObject.AddComponent<Button>();
                var captured = destination;
                hit.onClick.AddListener(() =>
                {
                    if (captured.body != null) ShowSheet(captured.label, captured.body, "CLOSE");
                    else SetStatus("Home is this screen.", UiFactory.TextSecondary);
                });
            }
        }

        // ---- dev row ----------------------------------------------------------

        private void BuildDevRow(RectTransform root)
        {
            var grant = UiFactory.NewButton("DevGrant", root, "+5,000 COINS", 14,
                UiFactory.TextSecondary, out Text grantText);
            UiFactory.Place(grant.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(190f, 34f), new Vector2(-24f, 112f));
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