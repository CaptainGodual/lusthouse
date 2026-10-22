using System;
using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// The first-run offer (plan §1.3): 50,000 Coins and 25 Gems, free.
    ///
    /// It is a gift, not a sale, and the copy says so — there is no price, no
    /// "continue" upsell and no route to a store. The player can decline it.
    ///
    /// Declining is not a dead end. A player who says no still gets a starting stake of
    /// one minimum bet, so nobody is ever left holding zero Coins and no way to play.
    /// Both answers are recorded in the ledger with their own note, which is what makes
    /// the offer once-ever: there is no flag to drift out of sync and no button to
    /// press twice.
    /// </summary>
    [DisallowMultipleComponent]
    public class FirstRunPackPrompt : MonoBehaviour
    {
        [Header("Layout")]
        public float panelWidth = 720f;
        public float panelHeight = 460f;

        private CanvasGroup _group;
        private RectTransform _panel;
        private Text _title;
        private Text _body;
        private Action _onClosed;
        private long _minimumBet;
        private bool _answered;

        public static FirstRunPackPrompt Create(Transform parent, long minimumBet, Action onClosed = null)
        {
            var go = new GameObject("FirstRunPackPrompt", typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var prompt = go.AddComponent<FirstRunPackPrompt>();
            prompt._minimumBet = Math.Max(1L, minimumBet);
            prompt._onClosed = onClosed;
            prompt.Build();
            return prompt;
        }

        /// <summary>
        /// The smallest bet a player can place — the amount a decliner is given.
        ///
        /// Read from the content rather than hard-coded, so the stake always matches
        /// the games that actually ship. When there is no catalogue to read, the house
        /// ladder's first rung is used, which is the floor of every bet in the game.
        /// </summary>
        public static long MinimumBetFor(Catalog.GameCatalog catalog)
        {
            long smallest = long.MaxValue;

            if (catalog != null)
            {
                foreach (var entry in catalog.games)
                {
                    if (entry == null || entry.locked || entry.definition == null) continue;
                    if (entry.definition.minBet <= 0) continue;
                    if (entry.definition.minBet < smallest) smallest = entry.definition.minBet;
                }
            }

            if (smallest == long.MaxValue)
                smallest = Economy.BetLimits.Ladder.Length > 0 ? Economy.BetLimits.Ladder[0] : 1000L;

            return smallest;
        }

        private void Build()
        {
            UiFactory.Stretch((RectTransform)transform);

            var scrim = gameObject.AddComponent<Image>();
            scrim.sprite = UiFactory.Solid;
            scrim.color = new Color(0f, 0f, 0f, 0.72f);
            scrim.raycastTarget = true;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var panel = UiFactory.NewImage("Panel", transform, UiFactory.VelvetPanel);
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f),
                new Vector2(panelWidth, panelHeight), Vector2.zero);
            _panel = panel.rectTransform;

            _title = UiFactory.NewText("Heading", _panel, "F R E E   S T A R T   P A C K", 32,
                FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.PlaceTopLeft(_title.rectTransform, 40f, 26f, panelWidth - 80f, 44f);

            var rule = UiFactory.NewImage("Rule", _panel, UiFactory.GoldPrimary);
            UiFactory.PlaceTopLeft(rule.rectTransform, 40f, 78f, panelWidth - 80f, 2f);

            _body = UiFactory.NewText("Body", _panel, "", 19, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(_body.rectTransform, 40f, 100f, panelWidth - 80f, 200f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            Fill();

            AddButton("TAKE IT", 320f, UiFactory.SpinGreen, TakeIt);
            AddButton("NO THANKS", 386f, UiFactory.VelvetRaised, Decline);
        }

        public void Open()
        {
            Core.LustLog.Info($"[LustHouse] FirstRunPackPrompt.Open: active={gameObject.activeInHierarchy} " +
                              $"parent={transform.parent?.name}");
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(Rise(1f));
        }

        public void Close()
        {
            Core.LustLog.Info("[LustHouse] FirstRunPackPrompt.Close.");
            StopAllCoroutines();
            StartCoroutine(Rise(0f, true));
        }

        private System.Collections.IEnumerator Rise(float target, bool closing = false)
        {
            // An absolute clock rather than an accumulated frame delta: the fade has
            // to finish even when the frame delta is not a clock, or the flow behind
            // it waits forever.
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

        private void Fill()
        {
            _body.text =
                "50,000 Coins and 25 Gems — free, and nothing to buy.\n\n" +
                "Coins cannot be purchased in this game and no prize can be redeemed, so " +
                "this is simply a starting balance.\n\n" +
                "If you would rather not take it, say no. You will still get enough to " +
                "place a bet, and the offer will not come back.";
        }

        private void TakeIt()
        {
            if (_answered) return;
            _answered = true;

            var services = ServicesBootstrap.Instance;
            if (services != null) services.GrantFirstRunPack();

            _title.text = "T A K E N";
            _body.text =
                "50,000 Coins and 25 Gems are in your account.\n\n" +
                "Coins can be earned from the daily gift, from level-ups and from the " +
                "games themselves. They cannot be bought, and they cannot be redeemed " +
                "for anything.";

            ClearButtons();
            AddButton("THANK YOU", 320f, UiFactory.GoldPrimary, Close);
        }

        private void Decline()
        {
            if (_answered) return;
            _answered = true;

            var services = ServicesBootstrap.Instance;
            if (services != null) services.GrantStartingStake(_minimumBet);

            _title.text = "N O   T H I N G   T A K E N";
            _body.text =
                $"No problem — you have been given {_minimumBet:N0} Coins, which is one " +
                "minimum bet, so you can start playing straight away.\n\n" +
                "Coins are earned from the daily gift, from level-ups and from the games " +
                "themselves. They cannot be bought.";

            ClearButtons();
            AddButton("LET'S PLAY", 320f, UiFactory.GoldPrimary, Close);
        }

        private void ClearButtons()
        {
            for (int i = _panel.childCount - 1; i >= 0; i--)
            {
                var child = _panel.GetChild(i);
                if (child.name == "Action") Destroy(child.gameObject);
            }
        }

        private void AddButton(string label, float y, Color color, Action onClick)
        {
            var button = UiFactory.NewButton("Action", _panel, label, 22,
                UiFactory.TextPrimary, out Text text);
            UiFactory.PlaceTopLeft(button.GetComponent<RectTransform>(), 40f, y, panelWidth - 80f, 60f);
            UiFactory.Stretch(text.rectTransform);
            button.GetComponent<Image>().color = color;
            button.onClick.AddListener(() => onClick?.Invoke());
        }
    }
}
