using System.Collections.Generic;
using LustHouse.Catalog;
using LustHouse.Core;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// Greybox lobby (plan §0.4 P5 / §2). Builds a readable stand-in for the real lobby so
    /// the M0.5 loop is actually testable: BootLoader -> Lobby -> tile tap -> Slot.
    ///
    /// Deliberately minimal. It proves the catalog, the wallet, and the routing; it is not
    /// the §2.4 prefab tree, which is M4 work.
    /// </summary>
    [DisallowMultipleComponent]
    public class LobbyPresenter : MonoBehaviour
    {
        [Header("Wiring")]
        public LobbyController controller;
        public GameCatalog catalog;

        [Header("Dev")]
        [Tooltip("Shows a QA row for granting currency. Never ship with this on.")]
        public bool showDevTools = true;

        private Text _coins;
        private Text _gems;
        private Text _lust;
        private Text _status;
        private RectTransform _tileParent;

        private readonly List<GameCatalog.Entry> _entries = new List<GameCatalog.Entry>();

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
            if (change.currency == Currency.LustPoints) return;  // Lust is not wagerable.
            Refresh();
        }

        // ---- UI ---------------------------------------------------------------

        private void Build()
        {
            var canvas = GetComponentInChildren<Canvas>();
            if (canvas == null) canvas = UiFactory.CreateCanvas(transform, "LobbyCanvas");
            var root = canvas.transform;

            // ---- top bar (§2.5) ----
            var topBar = UiFactory.NewImage("TopBar", root, UiFactory.VelvetPanel);
            var topRect = topBar.rectTransform;
            topRect.anchorMin = new Vector2(0f, 1f);
            topRect.anchorMax = new Vector2(1f, 1f);
            topRect.pivot = new Vector2(0.5f, 1f);
            topRect.sizeDelta = new Vector2(0f, 72f);

            var title = UiFactory.NewText("HouseTitle", topBar.transform,
                "LUST HOUSE", 28, FontStyle.Bold, UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(0f, 0f);
            titleRect.pivot = new Vector2(0f, 0.5f);
            titleRect.anchoredPosition = new Vector2(28f, 0f);
            titleRect.sizeDelta = new Vector2(320f, 72f);

            _coins = AddChip(topBar.transform, "Coins", Currency.Coins, 520f);
            _gems = AddChip(topBar.transform, "Gems", Currency.Gems, 760f);
            _lust = AddChip(topBar.transform, "Lust", Currency.LustPoints, 1000f);

            // ---- category heading + grid ----
            var heading = UiFactory.NewText("Heading", root, "L I B R A R Y", 24, FontStyle.Bold,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            var headRect = heading.rectTransform;
            headRect.anchorMin = new Vector2(0f, 1f);
            headRect.anchorMax = new Vector2(0f, 1f);
            headRect.pivot = new Vector2(0f, 1f);
            headRect.anchoredPosition = new Vector2(48f, -104f);
            headRect.sizeDelta = new Vector2(600f, 40f);

            var gridGo = new GameObject("TileGrid", typeof(RectTransform));
            gridGo.transform.SetParent(root, false);
            _tileParent = (RectTransform)gridGo.transform;
            _tileParent.anchorMin = new Vector2(0f, 0f);
            _tileParent.anchorMax = new Vector2(1f, 1f);
            _tileParent.offsetMin = new Vector2(48f, 96f);
            _tileParent.offsetMax = new Vector2(-48f, -156f);

            // ---- footer status ----
            _status = UiFactory.NewText("Status", root, "", 16, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            var statusRect = _status.rectTransform;
            statusRect.anchorMin = new Vector2(0f, 0f);
            statusRect.anchorMax = new Vector2(0f, 0f);
            statusRect.pivot = new Vector2(0f, 0f);
            statusRect.anchoredPosition = new Vector2(48f, 32f);
            statusRect.sizeDelta = new Vector2(1400f, 32f);

            BuildTiles();

            if (showDevTools) BuildDevRow(root);
        }

        /// <summary>
        /// QA-only row. Grants currency through the real ledger path so the economy can be
        /// exercised without waiting for the daily gift. Never enable in a release build.
        /// </summary>
        private void BuildDevRow(Transform root)
        {
            var label = UiFactory.NewText("DevLabel", root, "DEV", 13, FontStyle.Bold,
                UiFactory.StateDanger, TextAnchor.MiddleRight);
            UiFactory.Place(label.rectTransform, new Vector2(1f, 0f), new Vector2(90f, 44f), new Vector2(-28f, 32f));

            var grant = UiFactory.NewButton("DevGrant", root, "+5,000 COINS", 16,
                UiFactory.TextSecondary, out Text grantText);
            UiFactory.Place(grant.GetComponent<RectTransform>(), new Vector2(1f, 0f),
                new Vector2(220f, 44f), new Vector2(-130f, 32f));
            UiFactory.Stretch(grantText.rectTransform);
            grant.onClick.AddListener(() =>
            {
                DevGrantCoins(5000);
                SetStatus("Dev grant: +5,000 Coins (ledger reason Admin).", UiFactory.StateDanger);
            });

            var back = UiFactory.NewButton("DevBackToBoot", root, "REBOOT", 16,
                UiFactory.TextSecondary, out Text backText);
            UiFactory.Place(back.GetComponent<RectTransform>(), new Vector2(0f, 0f),
                new Vector2(160f, 44f), new Vector2(28f, 32f));
            UiFactory.Stretch(backText.rectTransform);
            back.onClick.AddListener(() =>
            {
                ServicesBootstrap.EnsureExists()?.SaveNow();
                UnityEngine.SceneManagement.SceneManager.LoadScene("BootLoader");
            });
        }

        private static Text AddChip(Transform parent, string label, Currency currency, float x)
        {
            var chip = UiFactory.NewImage(label + "Chip", parent, UiFactory.VelvetRaised);
            var rect = chip.rectTransform;
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(220f, 48f);

            var text = UiFactory.NewText("Value", chip.transform,
                $"{CurrencyRules.Symbol(currency)}  0", 20, FontStyle.Bold,
                currency == Currency.Gems ? UiFactory.NeonCyan
                    : currency == Currency.LustPoints ? UiFactory.NeonMagenta
                    : UiFactory.GoldPrimary,
                TextAnchor.MiddleCenter);
            UiFactory.Stretch(text.rectTransform);

            return text;
        }

        private void BuildTiles()
        {
            _entries.Clear();
            if (catalog != null) _entries.AddRange(catalog.games);

            if (_entries.Count == 0)
            {
                // The grid never empties silently (plan §2.7).
                var empty = UiFactory.NewText("EmptyState", _tileParent,
                    "Catalog is empty. Run LustHouse > Setup > Create starter content.",
                    20, FontStyle.Normal, UiFactory.StateWarn, TextAnchor.UpperLeft);
                UiFactory.Stretch(empty.rectTransform);
                return;
            }

            const float tileW = 224f, tileH = 280f, gap = 24f, top = 8f;
            const int columns = 6;

            for (int i = 0; i < _entries.Count; i++)
            {
                var entry = _entries[i];
                int col = i % columns;
                int row = i / columns;

                var x = 0f + col * (tileW + gap);
                float y = -top - row * (tileH + gap);

                BuildTile(entry, _tileParent, new Vector2(x, y), new Vector2(tileW, tileH));
            }
        }

        private void BuildTile(GameCatalog.Entry entry, Transform parent, Vector2 position, Vector2 size)
        {
            var tile = UiFactory.NewImage("Tile_" + entry.id, parent, UiFactory.VelvetPanel);
            var rect = tile.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            tile.raycastTarget = true;

            var button = tile.gameObject.AddComponent<Button>();
            button.targetGraphic = tile;

            // 4:5 key art is absent in the greybox; a tinted panel stands in.
            if (entry.definition != null)
                tile.color = new Color(
                    entry.definition.themeTint.r * 0.35f,
                    entry.definition.themeTint.g * 0.35f,
                    entry.definition.themeTint.b * 0.35f, 1f);

            var title = UiFactory.NewText("Title", tile.transform, entry.Title, 20,
                FontStyle.Bold, UiFactory.TextPrimary, TextAnchor.LowerCenter);
            var titleRect = title.rectTransform;
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 0f);
            titleRect.offsetMin = new Vector2(8f, 12f);
            titleRect.offsetMax = new Vector2(-8f, 72f);

            // Access badge, top-left (§2.8).
            var badgeText = entry.locked ? "COMING SOON"
                : entry.entryCostGems > 0 ? $"{entry.entryCostGems} GEMS"
                : "FREE";

            var badge = UiFactory.NewText("Badge", tile.transform, badgeText, 13,
                FontStyle.Bold,
                entry.locked ? UiFactory.TextDisabled
                    : entry.entryCostGems > 0 ? UiFactory.NeonMagenta
                    : UiFactory.NeonCyan,
                TextAnchor.UpperLeft);
            var badgeRect = badge.rectTransform;
            badgeRect.anchorMin = new Vector2(0f, 1f);
            badgeRect.anchorMax = new Vector2(0f, 1f);
            badgeRect.pivot = new Vector2(0f, 1f);
            badgeRect.anchoredPosition = new Vector2(10f, -10f);
            badgeRect.sizeDelta = new Vector2(180f, 24f);

            var captured = entry;
            button.onClick.AddListener(() => OnTileClicked(captured, badge));
        }

        private void OnTileClicked(GameCatalog.Entry entry, Text badge)
        {
            if (controller != null) controller.OnGameTilePressed(entry.id);

            // Route the outcome into the footer so a greybox still explains itself.
            if (controller == null || entry.definition == null) return;

            var def = entry.definition;
            long balance = WalletService.Instance != null
                ? WalletService.Instance.Balance(Currency.Coins) : 0;

            if (entry.locked)
                SetStatus($"{entry.Title} is not available yet.", UiFactory.TextDisabled);
            else if (WalletService.Instance != null && balance < def.minBet)
                SetStatus($"{entry.Title}: needs {def.minBet} Coins — you have {balance}. " +
                          "Still tappable, never a dead end.", UiFactory.StateWarn);
            else
                SetStatus($"Launching {entry.Title}...", UiFactory.StateSuccess);
        }

        // ---- refresh ----------------------------------------------------------

        private void Refresh()
        {
            if (_coins == null) return;

            var wallet = WalletService.Instance;
            _coins.text = $"{CurrencyRules.Symbol(Currency.Coins)}  {UiFactory.FormatAmount(wallet != null ? wallet.Balance(Currency.Coins) : 0)}";
            _gems.text = $"{CurrencyRules.Symbol(Currency.Gems)}  {UiFactory.FormatAmount(wallet != null ? wallet.Balance(Currency.Gems) : 0)}";
            _lust.text = $"{CurrencyRules.Symbol(Currency.LustPoints)}  {UiFactory.FormatAmount(wallet != null ? wallet.Balance(Currency.LustPoints) : 0)}";

            UpdateAffordability();
        }

        private void UpdateAffordability()
        {
            var wallet = WalletService.Instance;

            foreach (Transform child in _tileParent)
            {
                var title = child.Find("Title")?.GetComponent<Text>();
                if (title == null) continue;

                var entry = _entries.Find(e => child.name == "Tile_" + e.id);
                if (entry?.definition == null) continue;

                if (entry.locked)
                {
                    var img = child.GetComponent<Image>();
                    if (img != null) img.color = new Color(0.18f, 0.14f, 0.2f, 0.45f);
                    continue;
                }

                long balance = wallet != null ? wallet.Balance(Currency.Coins) : 0;
                bool affordable = balance >= entry.definition.minBet;

                // Unaffordable tiles stay tappable — dimmed, never dead (§2.8).
                var image = child.GetComponent<Image>();
                if (image != null)
                    image.color = affordable
                        ? new Color(entry.definition.themeTint.r * 0.35f,
                                    entry.definition.themeTint.g * 0.35f,
                                    entry.definition.themeTint.b * 0.35f, 1f)
                        : new Color(0.18f, 0.14f, 0.2f, 0.45f);

                var badge = child.Find("Badge")?.GetComponent<Text>();
                if (badge != null && entry.entryCostGems == 0)
                    badge.text = affordable ? "FREE" : $"NEEDS {UiFactory.FormatAmount(entry.definition.minBet)} COINS";
            }
        }

        private void SetStatus(string message, Color color)
        {
            if (_status == null) return;
            _status.text = message;
            _status.color = color;
        }

        // ---- dev tools --------------------------------------------------------

        /// <summary>QA helper: grants Coins with a real ledger entry.</summary>
        public void DevGrantCoins(long amount)
        {
            if (WalletService.Instance == null) return;
            WalletService.Instance.TryGrant(Currency.Coins, amount, LedgerReason.Admin, "greybox dev grant");
            Refresh();
        }
    }
}