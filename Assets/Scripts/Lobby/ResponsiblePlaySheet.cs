using System;
using LustHouse.Core;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>Which face of the responsible-play panel to show.</summary>
    public enum ResponsiblePlayMode
    {
        /// <summary>The panel the player opens deliberately: the rules and the exclusion.</summary>
        Panel,

        /// <summary>The check that comes due at 15, 30 and 60 minutes of play.</summary>
        RealityCheck
    }

    /// <summary>
    /// The responsible-play panel (guardrail 4, plan §5.6).
    ///
    /// Two faces, one class:
    ///
    ///   * The **panel**, which the player opens from the lobby and which carries the
    ///     self-exclusion. The exclusion is confirmed before it is applied, because it
    ///     is irreversible and a mis-tap on a permanent exclusion is not something the
    ///     game can undo for the player.
    ///   * The **reality check**, which comes due on its own. It states how long the
    ///     player has been playing and what they have staked this session, and it
    ///     offers two real answers: continue, or end the session. Continuing is
    ///     allowed — a check the player cannot decline is an obstacle, not a check.
    /// </summary>
    [DisallowMultipleComponent]
    public class ResponsiblePlaySheet : MonoBehaviour
    {
        [Header("Layout")]
        public float panelWidth = 760f;
        public float panelHeight = 560f;

        private CanvasGroup _group;
        private RectTransform _panel;
        private RectTransform _content;
        private Text _title;
        private Text _body;
        private Action _onClosed;
        private Action _onEndSession;
        private ResponsiblePlayMode _mode;
        private bool _confirmingPermanent;

        public static ResponsiblePlaySheet Create(Transform parent, ResponsiblePlayMode mode,
                                                 Action onClosed = null, Action onEndSession = null)
        {
            var go = new GameObject("ResponsiblePlaySheet", typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var sheet = go.AddComponent<ResponsiblePlaySheet>();
            sheet._mode = mode;
            sheet._onClosed = onClosed;
            sheet._onEndSession = onEndSession;
            sheet.Build();
            return sheet;
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

            _title = UiFactory.NewText("Heading", _panel, "RESPONSIBLE PLAY", 32,
                FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.PlaceTopLeft(_title.rectTransform, 40f, 26f, panelWidth - 80f, 44f);

            var rule = UiFactory.NewImage("Rule", _panel, UiFactory.GoldPrimary);
            UiFactory.PlaceTopLeft(rule.rectTransform, 40f, 78f, panelWidth - 80f, 2f);

            var host = new GameObject("Content", typeof(RectTransform));
            host.transform.SetParent(_panel, false);
            _content = (RectTransform)host.transform;
            UiFactory.PlaceTopLeft(_content, 40f, 100f, panelWidth - 80f, panelHeight - 180f);

            _body = UiFactory.NewText("Body", _content, "", 18, FontStyle.Normal,
                UiFactory.TextSecondary, TextAnchor.UpperLeft);
            UiFactory.PlaceTopLeft(_body.rectTransform, 0f, 0f, panelWidth - 80f, 300f);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            Fill();
        }

        public void Open()
        {
            gameObject.SetActive(true);
            StopAllCoroutines();
            StartCoroutine(Rise(1f));
        }

        public void Close()
        {
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

        /// <summary>
        /// Builds the panel's content for the current mode.
        ///
        /// The content is rebuilt rather than toggled, because the reality check quotes
        /// the session as it is *now* — a check that says "15 minutes" when the player
        /// has been playing for 47 is worse than no check.
        /// </summary>
        private void Fill()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
                Destroy(_content.GetChild(i).gameObject);

            if (_mode == ResponsiblePlayMode.RealityCheck) FillRealityCheck();
            else FillPanel();
        }

        // ---- the panel ---------------------------------------------------------

        private void FillPanel()
        {
            _title.text = "R E S P O N S I B L E   P L A Y";

            if (_confirmingPermanent)
            {
                _body.text =
                    "Exclude yourself permanently?\n\n" +
                    "This cannot be undone. The game will refuse to open any title " +
                    "afterwards, and there is no way to lift it from inside the game.";

                AddButton("CONFIRM PERMANENT EXCLUSION", 300f, UiFactory.StateDanger, ConfirmPermanent);
                AddButton("CANCEL", 372f, UiFactory.VelvetRaised, () =>
                {
                    _confirmingPermanent = false;
                    Fill();
                });
                return;
            }

            var service = ResponsiblePlayService.Instance;
            string session = service != null
                ? $"This session: {service.SessionMinutes} minutes, {service.SessionSpins:N0} spins, " +
                  $"{service.SessionWagered:N0} Coins wagered."
                : "This session: the timer is not running.";

            _body.text =
                "Lust House is a simulated gambling game. No real money is used, nothing " +
                "is bought with real money, and no prize can be redeemed.\n\n" +
                "A reality check appears every 15 minutes of play. It shows how long " +
                "you have been playing and what you have staked this session. It is a " +
                "reminder, not a limit: you can carry on.\n\n" +
                "Self-exclusion stops the game opening any title. A one-day exclusion " +
                "lifts itself; a permanent one cannot be undone.\n\n" +
                session;

            AddButton("EXCLUDE FOR ONE DAY", 330f, UiFactory.VelvetRaised, () => ExcludeForOneDay());
            AddButton("EXCLUDE PERMANENTLY", 396f, UiFactory.StateDanger, () =>
            {
                _confirmingPermanent = true;
                Fill();
            });
            AddButton("CLOSE", 462f, UiFactory.GoldPrimary, Close);
        }

        // ---- the reality check --------------------------------------------------

        private void FillRealityCheck()
        {
            _title.text = "R E A L I T Y   C H E C K";

            var service = ResponsiblePlayService.Instance;
            string session = service != null
                ? $"You have been playing for {service.SessionMinutes} minutes.\n\n" +
                  $"This session: {service.SessionSpins:N0} spins and {service.SessionWagered:N0} Coins wagered.\n\n" +
                  "Take a moment. You can carry on playing, or end the session here and " +
                  "come back later."
                : "You have been playing for a while.\n\nTake a moment. You can carry on " +
                  "playing, or end the session here and come back later.";

            _body.text = session;

            AddButton("CARRY ON PLAYING", 330f, UiFactory.GoldPrimary, () =>
            {
                service?.AcknowledgeRealityCheck();
                Close();
            });
            AddButton("END SESSION", 396f, UiFactory.VelvetRaised, () =>
            {
                service?.EndSession();
                _onEndSession?.Invoke();
                Close();
            });
        }

        // ---- actions ------------------------------------------------------------

        private void ExcludeForOneDay()
        {
            var service = ResponsiblePlayService.Instance;
            if (service == null) return;

            service.ExcludeForDays(1);
            _body.text =
                "You are excluded for one day.\n\n" +
                "The game will not open any title until the exclusion lifts on its own. " +
                "Nothing here needs to be done — there is nothing to undo and nothing to " +
                "ask for.";

            var close = AddButton("CLOSE", 396f, UiFactory.GoldPrimary, Close);
            close.gameObject.SetActive(true);
        }

        private void ConfirmPermanent()
        {
            var service = ResponsiblePlayService.Instance;
            if (service == null) return;

            service.ExcludePermanently();
            _body.text =
                "You are excluded permanently.\n\n" +
                "The game will not open any title again, and this cannot be undone from " +
                "inside the game.";

            AddButton("CLOSE", 396f, UiFactory.GoldPrimary, Close);
        }

        private Button AddButton(string label, float y, Color color, Action onClick)
        {
            var button = UiFactory.NewButton("Action", _content, label, 20,
                UiFactory.TextPrimary, out Text text);
            UiFactory.PlaceTopLeft(button.GetComponent<RectTransform>(), 0f, y, panelWidth - 80f, 56f);
            UiFactory.Stretch(text.rectTransform);
            button.GetComponent<Image>().color = color;
            button.onClick.AddListener(() => onClick?.Invoke());
            return button;
        }
    }
}
