using System;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>
    /// A simple modal sheet (§2.3). One sheet visible at a time; opening another replaces
    /// this one. Used for "Coming Soon", dev notices, and placeholder destinations.
    ///
    /// Deliberately plain — this is a greybox, not the §2.9 sheet design.
    /// </summary>
    [DisallowMultipleComponent]
    public class ModalSheet : MonoBehaviour
    {
        [Header("Timing (plan §2.1)")]
        public float riseSeconds = 0.32f;

        private CanvasGroup _group;
        private RectTransform _panel;
        private Text _body;
        private Button _close;
        private Action _onClosed;

        public bool IsOpen => _group != null && _group.alpha > 0.01f;

        public static ModalSheet Create(Transform parent, string title, string body, string closeLabel)
        {
            var go = new GameObject("Sheet", typeof(RectTransform));
            go.transform.SetParent(parent, false);

            var sheet = go.AddComponent<ModalSheet>();
            sheet.Build(title, body, closeLabel);
            return sheet;
        }

        private void Build(string title, string body, string closeLabel)
        {
            UiFactory.Stretch((RectTransform)transform);

            // Scrim. Raycast state is driven by Rise() so a closing sheet stops eating input.
            var scrim = gameObject.AddComponent<Image>();
            scrim.sprite = UiFactory.Solid;
            scrim.color = new Color(0f, 0f, 0f, 0.65f);
            scrim.raycastTarget = false;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var panel = UiFactory.NewImage("Panel", transform, UiFactory.VelvetPanel);
            UiFactory.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(640f, 340f), Vector2.zero);
            _panel = panel.rectTransform;

            var heading = UiFactory.NewText("Heading", _panel, title, 30, FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(560f, 48f), new Vector2(0f, -36f));

            _body = UiFactory.NewText("Body", _panel, body, 18, FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.Place(_body.rectTransform, new Vector2(0.5f, 1f), new Vector2(560f, 160f), new Vector2(0f, -180f));
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Truncate;

            _close = UiFactory.NewButton("Close", _panel, closeLabel, 22, UiFactory.VelvetBase, out Text closeText);
            UiFactory.Place(_close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(240f, 60f), new Vector2(0f, 44f));
            UiFactory.Stretch(closeText.rectTransform);
            _close.GetComponent<Image>().color = UiFactory.GoldPrimary;
            _close.onClick.AddListener(Close);
        }

        public void Open(Action onClosed = null)
        {
            _onClosed = onClosed;
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
            var scrim = GetComponent<Image>();
            scrim.raycastTarget = !closing;

            float elapsed = 0f;
            float startAlpha = _group.alpha;
            float startY = _panel.anchoredPosition.y;
            float endY = closing ? startY - 40f : 0f;

            while (elapsed < riseSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / riseSeconds);
                // ease-out cubic, per §2.1
                float eased = 1f - Mathf.Pow(1f - t, 3f);

                _group.alpha = Mathf.Lerp(startAlpha, target, eased);
                var pos = _panel.anchoredPosition;
                pos.y = Mathf.Lerp(startY, endY, eased);
                _panel.anchoredPosition = pos;

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
    }
}