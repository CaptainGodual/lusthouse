using System;
using System.Collections;
using LustHouse.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.UI
{
    /// <summary>
    /// The face of the age gate (plan §2.3a): the full-screen overlay the player
    /// answers the age question on.
    ///
    /// One component serves both places the gate appears — the blocking first-launch
    /// overlay in the boot, and the re-confirmation the top bar's <c>18+</c> chip
    /// opens — because a gate that is written twice is a gate that will be written
    /// differently the second time, and the second version is the one nobody tests.
    ///
    /// The overlay is built in code rather than authored into a scene, like the rest of
    /// this game's UI (see <see cref="Boot.BootPresenter"/>): a scene-authored overlay
    /// that loses a reference fails silently, and a gate that fails silently is a gate
    /// that lets a minor through.
    /// </summary>
    [DisallowMultipleComponent]
    public class AgeGateOverlay : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("How long the refusal screen is held before the game closes, seconds.")]
        public float refusalSeconds = 3f;

        private RectTransform _question;
        private RectTransform _refusal;
        private Action _onConfirmed;
        private Action _onRefused;
        private bool _answered;

        /// <summary>
        /// Builds the gate.
        ///
        /// <paramref name="onConfirmed"/> runs once the player has confirmed and the
        /// answer has been kept; <paramref name="onRefused"/> runs when they have said
        /// they are under 18, just before the game closes.
        /// </summary>
        public static AgeGateOverlay Create(Transform parent, Action onConfirmed,
                                            Action onRefused = null)
        {
            var go = new GameObject("AgeGateOverlay", typeof(RectTransform));
            if (parent != null) go.transform.SetParent(parent, false);

            var overlay = go.AddComponent<AgeGateOverlay>();
            overlay.Build(onConfirmed, onRefused);
            return overlay;
        }

        /// <summary>True once the player has answered, either way.</summary>
        public bool Answered => _answered;

        private void Build(Action onConfirmed, Action onRefused)
        {
            _onConfirmed = onConfirmed;
            _onRefused = onRefused;

            var root = (RectTransform)transform;

            // The overlay fills its parent. A RectTransform that is merely parented
            // keeps its own hundred-pixel square, so the backdrop behind the panel
            // would be a hundred-pixel square too — the game underneath would still
            // be on screen and still be clickable, and the gate would be a sticker
            // on it rather than a wall.
            UiFactory.Stretch(root);

            // The backdrop is opaque, not a scrim: behind this screen there must be no
            // game to look at, even in the moment before the panel fades in.
            var backdrop = UiFactory.NewImage("Backdrop", root, UiFactory.VelvetBase);
            UiFactory.Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;

            // A soft bloom behind the heading, the same beat the boot uses for the
            // wordmark.
            var bloom = UiFactory.NewImage("Glow", root,
                new Color(UiFactory.NeonMagenta.r, UiFactory.NeonMagenta.g, UiFactory.NeonMagenta.b, 0.16f));
            bloom.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            bloom.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            bloom.rectTransform.sizeDelta = new Vector2(900f, 420f);

            var panel = UiFactory.NewImage("Panel", root, UiFactory.VelvetPanel);
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(920f, 620f), Vector2.zero);

            BuildQuestion(panel.rectTransform);
            BuildRefusal(panel.rectTransform);
        }

        // ---- the question ------------------------------------------------------

        private void BuildQuestion(RectTransform panel)
        {
            var host = new GameObject("Question", typeof(RectTransform));
            host.transform.SetParent(panel, false);
            _question = (RectTransform)host.transform;
            UiFactory.Stretch(_question);

            var heading = UiFactory.NewText("Heading", _question, "A G E   V E R I F I C A T I O N",
                40, FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(840f, 56f),
                new Vector2(0f, -56f));

            var rule = UiFactory.NewImage("Rule", _question, UiFactory.GoldPrimary);
            UiFactory.Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(520f, 2f),
                new Vector2(0f, -96f));

            // The wording is the requirement, not decoration. It says what the game is,
            // that no real money is involved, that nothing can be redeemed, and where
            // the prize tables and the return-to-player rate live (plan §5.7).
            var body = UiFactory.NewText("Body", _question,
                "Lust House is a simulated gambling game for adults aged 18 and over.\n\n" +
                "No real money is used, nothing is bought with real money, and no prize " +
                "can be redeemed. Every game publishes its prize tables and its " +
                "return-to-player rate in its info panel before you play.\n\n" +
                "You must be 18 or older to enter.",
                21, FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 300f),
                new Vector2(0f, -140f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.alignment = TextAnchor.UpperCenter;

            // The invited answer is the loud one. Declining is a legitimate answer and
            // is given a button that reads as a button, not as a warning.
            var confirm = UiFactory.NewButton("ConfirmAge", _question, "I AM 18 OR OLDER",
                22, UiFactory.VelvetBase, out Text confirmText);
            UiFactory.Place(confirm.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(340f, 72f), new Vector2(-190f, 56f));
            confirm.GetComponent<Image>().color = UiFactory.GoldPrimary;
            confirm.onClick.AddListener(OnConfirmClicked);

            var refuse = UiFactory.NewButton("RefuseAge", _question, "I AM UNDER 18",
                20, UiFactory.TextSecondary, out Text refuseText);
            UiFactory.Place(refuse.GetComponent<RectTransform>(), new Vector2(0.5f, 0f),
                new Vector2(340f, 72f), new Vector2(190f, 56f));
            refuse.GetComponent<Image>().color = UiFactory.VelvetRaised;
            refuse.onClick.AddListener(OnRefuseClicked);
        }

        // ---- the refusal ------------------------------------------------------

        private void BuildRefusal(RectTransform panel)
        {
            var host = new GameObject("Refusal", typeof(RectTransform));
            host.transform.SetParent(panel, false);
            _refusal = (RectTransform)host.transform;
            UiFactory.Stretch(_refusal);

            var heading = UiFactory.NewText("Heading", _refusal, "N O T   A V A I L A B L E",
                40, FontStyle.Bold, UiFactory.StateWarn);
            UiFactory.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(840f, 56f),
                new Vector2(0f, -56f));

            var rule = UiFactory.NewImage("Rule", _refusal, UiFactory.StateWarn);
            UiFactory.Place(rule.rectTransform, new Vector2(0.5f, 1f), new Vector2(520f, 2f),
                new Vector2(0f, -96f));

            var body = UiFactory.NewText("Body", _refusal,
                "You must be 18 or older to play.\n\n" +
                "Lust House is a simulated gambling game for adults aged 18 and over, and " +
                "it cannot be played by anyone who is under 18.\n\n" +
                "This game will now close.",
                21, FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(760f, 260f),
                new Vector2(0f, -150f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            body.alignment = TextAnchor.UpperCenter;

            _refusal.gameObject.SetActive(false);
        }

        // ---- the answers ------------------------------------------------------

        private void OnConfirmClicked()
        {
            if (_answered) return;
            _answered = true;

            // If the answer could not be kept, the player is asked again next launch.
            // Letting them in anyway would mean asking the same question forever.
            if (!AgeGate.Confirm())
            {
                _answered = false;
                return;
            }

            Hide();
            _onConfirmed?.Invoke();
        }

        private void OnRefuseClicked()
        {
            if (_answered) return;
            _answered = true;

            _question.gameObject.SetActive(false);
            _refusal.gameObject.SetActive(true);

            _onRefused?.Invoke();
            StartCoroutine(CloseAfterDelay());
        }

        private IEnumerator CloseAfterDelay()
        {
            float elapsed = 0f;
            while (elapsed < refusalSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            AgeGate.Leave();
        }

        /// <summary>Takes the gate off the screen without answering it.</summary>
        public void Hide() => gameObject.SetActive(false);
    }
}
