using System;
using System.Collections;
using System.Collections.Generic;
using LustHouse.Slots;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The probability disclosure (plan §1.1, §5.7).
    ///
    /// The age gate already told the player that every game "publishes its prize tables
    /// and its return-to-player rate in its info panel" — and there was no info panel.
    /// This is that panel, and the numbers in it are *measured*: the return-to-player
    /// figure comes from the same exact-RTP engine the ship gate uses, and the hit rate
    /// comes from a fixed-seed run. Nothing here is typed by hand, so the sheet cannot
    /// claim a number the maths does not support.
    ///
    /// It also carries the statement the store listing has to make: this is a simulation,
    /// no real money is involved, and nothing can be redeemed.
    /// </summary>
    [DisallowMultipleComponent]
    public class ProbabilityDisclosureSheet : MonoBehaviour
    {
        [Header("Measurement")]
        [Tooltip("Spins in the fixed-seed run behind the hit rate.")]
        public int sampleSpins = 20000;

        [Tooltip("Spins measured per frame, so opening the sheet never freezes the game.")]
        public int spinsPerFrame = 4000;

        [Tooltip("The seed the sampled figures come from. Fixed, so the numbers are reproducible.")]
        public int seed = 20261022;

        [Header("Layout")]
        public float panelWidth = 1180f;
        public float maxPanelHeight = 940f;

        private CanvasGroup _group;
        private RectTransform _panel;
        private RectTransform _body;
        private Text _status;
        private Button _close;
        private Action _onClosed;
        private bool _open;

        /// <summary>The games whose maths this sheet publishes.</summary>
        private readonly List<SlotMachineDefinition> _games = new List<SlotMachineDefinition>();

        public static ProbabilityDisclosureSheet Create(Transform parent,
                                                        IEnumerable<SlotMachineDefinition> games,
                                                        Action onClosed = null)
        {
            var go = new GameObject("ProbabilityDisclosureSheet", typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var sheet = go.AddComponent<ProbabilityDisclosureSheet>();
            sheet.Build(games, onClosed);
            return sheet;
        }

        /// <summary>
        /// The games a disclosure has to cover: the ones a player can actually play.
        ///
        /// A title that is coming soon is not disclosed, because there is nothing to
        /// play yet — publishing the odds for a game that cannot be opened is noise.
        /// </summary>
        public static List<SlotMachineDefinition> ShippableGames(Catalog.GameCatalog catalog)
        {
            var result = new List<SlotMachineDefinition>();
            if (catalog == null) return result;

            foreach (var entry in catalog.games)
            {
                if (entry == null || entry.locked) continue;
                if (entry.definition == null) continue;
                result.Add(entry.definition);
            }
            return result;
        }

        private void Build(IEnumerable<SlotMachineDefinition> games, Action onClosed)
        {
            _onClosed = onClosed;

            if (games != null)
            {
                foreach (var game in games)
                    if (game != null) _games.Add(game);
            }

            UiFactory.Stretch((RectTransform)transform);

            // The scrim swallows the taps behind the sheet, so a sheet that is open is
            // the only thing the player can act on.
            var scrim = gameObject.AddComponent<Image>();
            scrim.sprite = UiFactory.Solid;
            scrim.color = new Color(0f, 0f, 0f, 0.72f);
            scrim.raycastTarget = true;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var panel = UiFactory.NewImage("Panel", transform, UiFactory.VelvetPanel);
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(panelWidth, 640f), Vector2.zero);
            _panel = panel.rectTransform;

            var heading = UiFactory.NewText("Heading", _panel, "P R O B A B I L I T Y   D I S C L O S U R E",
                34, FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.PlaceTopLeft(heading.rectTransform, 40f, 22f, panelWidth - 80f, 46f);

            var rule = UiFactory.NewImage("Rule", _panel, UiFactory.GoldPrimary);
            UiFactory.PlaceTopLeft(rule.rectTransform, 40f, 78f, panelWidth - 80f, 2f);

            var sub = UiFactory.NewText("Sub", _panel,
                "Every figure below is measured from the shipping maths, not typed in by hand.",
                18, FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.PlaceTopLeft(sub.rectTransform, 40f, 90f, panelWidth - 80f, 30f);

            var body = new GameObject("Body", typeof(RectTransform));
            body.transform.SetParent(_panel, false);
            _body = (RectTransform)body.transform;
            UiFactory.PlaceTopLeft(_body, 40f, 132f, panelWidth - 80f, 400f);

            _status = UiFactory.NewText("Status", _panel, "M E A S U R I N G…", 18,
                FontStyle.Bold, UiFactory.NeonCyan, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(_status.rectTransform, 40f, 132f, panelWidth - 80f, 30f);

            // The statement the store listing has to make, printed where the player
            // cannot miss it.
            var footer = UiFactory.NewText("Footer", _panel, ResponsiblePlayStatement, 16,
                FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(footer.rectTransform, 40f, 552f, panelWidth - 80f, 84f);
            footer.horizontalOverflow = HorizontalWrapMode.Wrap;
            footer.verticalOverflow = VerticalWrapMode.Truncate;

            _close = UiFactory.NewButton("Close", _panel, "CLOSE", 22, UiFactory.VelvetBase, out Text closeText);
            UiFactory.PlaceBottomCentre(_close.GetComponent<RectTransform>(), 24f, 260f, 60f);
            UiFactory.Stretch(closeText.rectTransform);
            _close.GetComponent<Image>().color = UiFactory.GoldPrimary;
            _close.onClick.AddListener(Close);
        }

        /// <summary>
        /// The responsible-play statement, in one place so every surface that shows it
        /// shows the same words.
        /// </summary>
        public const string ResponsiblePlayStatement =
            "Lust House is a simulated gambling game for adults aged 18 and over. No real " +
            "money is used, nothing is bought with real money, and no prize can be redeemed. " +
            "Coins cannot be purchased. The random number generator runs on your device from " +
            "a fixed seed so results can be reproduced; it is not certified.";

        public void Open()
        {
            _open = true;
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(Rise(1f));
            StartCoroutine(MeasureAndFill());
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            StopAllCoroutines();
            StartCoroutine(Rise(0f, true));
        }

        private System.Collections.IEnumerator Rise(float target, bool closing = false)
        {
            // An absolute clock, not an accumulated frame delta. A sheet that is opened
            // and closed in the same frame must still finish its fade, and a headless
            // test host — where the frame delta is not a clock — must not be able to
            // leave the sheet stuck half-open with the flow behind it waiting.
            float deadline = Time.unscaledTime + 0.18f;
            float startAlpha = _group.alpha;

            while (Time.unscaledTime < deadline)
            {
                _group.alpha = Mathf.Lerp(startAlpha, target,
                    Mathf.Clamp01(1f - (deadline - Time.unscaledTime) / 0.18f));
                yield return null;
            }

            _group.alpha = target;
            _group.blocksRaycasts = !closing && target > 0.5f;

            if (closing)
            {
                gameObject.SetActive(false);
                _onClosed?.Invoke();
                _onClosed = null;
            }
        }

        /// <summary>
        /// Measures the games and fills the sheet in.
        ///
        /// The sampling is chunked across frames: a 20,000-spin run is quick on a
        /// desktop and noticeable on a phone, and a sheet that freezes the game while it
        /// measures is a sheet the player thinks has hung.
        /// </summary>
        private IEnumerator MeasureAndFill()
        {
            if (_games.Count == 0)
            {
                if (_status != null) _status.text = "There is no published game yet.";
                yield break;
            }

            var reports = new List<DisclosureReport>();

            foreach (var game in _games)
            {
                var report = new DisclosureReport
                {
                    gameId = game.id,
                    title = game.title,
                    tagline = game.tagline,
                    volatility = game.volatility,
                    maxWinMultiplier = game.maxWinMultiplier,
                    minBet = game.minBet,
                    maxBet = game.maxBet,
                    payType = game.payType,
                    measured = game.rtpStatus == RtpStatus.Measured,
                    sampleSeed = seed
                };

                // The exact figure is closed form and instant.
                report.exactRtp = SimulationEngine.ExactRtp(game);

                // The sampled figure is chunked so the frame never stalls.
                long totalBet = 0, totalWin = 0, spins = 0;
                double hits = 0;
                int chunkSeed = seed;

                while (spins < sampleSpins)
                {
                    int chunk = Mathf.Min(spinsPerFrame, sampleSpins - (int)spins);
                    var sampled = SimulationEngine.Run(game, chunk, game.minBet > 0 ? game.minBet : 1L, chunkSeed);
                    totalBet += sampled.totalBet;
                    totalWin += sampled.totalWin;
                    hits += sampled.hitRate * sampled.spins;
                    spins += sampled.spins;
                    chunkSeed++;

                    if (_status != null)
                        _status.text = $"M E A S U R I N G…  {spins:N0} / {sampleSpins:N0} spins";

                    yield return null;
                }

                report.sampleSpins = spins;
                report.sampledRtp = totalBet > 0 ? totalWin / (double)totalBet : 0.0;
                report.hitRate = spins > 0 ? hits / spins : 0.0;

                reports.Add(report);
            }

            if (_status != null) _status.gameObject.SetActive(false);
            Fill(reports);
        }

        /// <summary>Builds the content blocks, one per published game.</summary>
        private void Fill(List<DisclosureReport> reports)
        {
            float y = 0f;

            for (int i = 0; i < reports.Count; i++)
            {
                y = BuildBlock(reports[i], y);
                y += 18f;
            }

            // The panel is sized around what was built, so the footer and the close
            // button stay where they belong however many games are published.
            float contentBottom = 132f + y + 12f;
            float panelHeight = Mathf.Clamp(contentBottom + 176f, 520f, maxPanelHeight);
            _panel.sizeDelta = new Vector2(panelWidth, panelHeight);

            var footer = _panel.Find("Footer");
            if (footer is RectTransform footerRect)
                UiFactory.PlaceTopLeft(footerRect, 40f, panelHeight - 140f, panelWidth - 80f, 84f);
        }

        /// <summary>One game's block: the figures, then the paytable, then the notes.</summary>
        private float BuildBlock(DisclosureReport report, float y)
        {
            var title = UiFactory.NewText("Title", _body, report.title.ToUpperInvariant(), 26,
                FontStyle.Bold, UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(title.rectTransform, 0f, y, 1000f, 32f);
            y += 36f;

            var rtp = UiFactory.NewText("Rtp", _body,
                $"Return to player: {report.RtpText}", 21, FontStyle.Bold,
                UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(rtp.rectTransform, 0f, y, 1000f, 28f);
            y += 30f;

            var hit = UiFactory.NewText("HitRate", _body, $"Hit rate: {report.HitRateText}", 17,
                FontStyle.Normal, UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(hit.rectTransform, 0f, y, 1000f, 24f);
            y += 26f;

            var facts = UiFactory.NewText("Facts", _body,
                $"Volatility {report.VolatilityText}   ·   Max win {report.MaxWinText}   ·   " +
                $"Bets {report.BetRangeText}", 17, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(facts.rectTransform, 0f, y, 1000f, 24f);
            y += 30f;

            if (!report.measured)
            {
                var warn = UiFactory.NewText("Unmeasured", _body,
                    "This game's maths has not been signed off yet.", 16, FontStyle.Italic,
                    UiFactory.StateWarn, TextAnchor.MiddleLeft);
                UiFactory.PlaceTopLeft(warn.rectTransform, 0f, y, 1000f, 24f);
                y += 26f;
            }

            var paytableHeading = UiFactory.NewText("PaytableHeading", _body, "P R I Z E   T A B L E",
                18, FontStyle.Bold, UiFactory.NeonCyan, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(paytableHeading.rectTransform, 0f, y, 1000f, 26f);
            y += 28f;

            // Three columns, so a 27-row table is nine rows tall.
            const int columns = 3;
            const float columnWidth = 360f;
            float rowHeight = 22f;
            int rows = Mathf.CeilToInt(report.paytable.Count / (float)columns);

            for (int i = 0; i < report.paytable.Count; i++)
            {
                int column = i / Mathf.Max(1, rows);
                int row = i % Mathf.Max(1, rows);

                var entry = UiFactory.NewText("Pay", _body, report.paytable[i].Label, 16,
                    FontStyle.Normal,
                    report.paytable[i].isWild ? UiFactory.NeonCyan : UiFactory.TextPrimary,
                    TextAnchor.MiddleLeft);
                UiFactory.PlaceTopLeft(entry.rectTransform, column * columnWidth, y + row * rowHeight,
                    columnWidth - 8f, rowHeight);
            }

            y += rows * rowHeight + 10f;

            foreach (var note in report.specialNotes)
            {
                var text = UiFactory.NewText("Note", _body, note, 16, FontStyle.Normal,
                    UiFactory.TextSecondary, TextAnchor.MiddleLeft);
                UiFactory.PlaceTopLeft(text.rectTransform, 0f, y, 1080f, 22f);
                y += 22f;
            }

            return y;
        }
    }
}
