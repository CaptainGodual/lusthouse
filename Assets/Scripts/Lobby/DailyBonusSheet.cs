using System;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>Code-built daily bonus sheet with a 7-day ladder.</summary>
    public class DailyBonusSheet : MonoBehaviour
    {
        private DailyBonusService _service;
        private CanvasGroup _group;
        private RectTransform _panel;
        private Text _title;
        private Text _subtitle;
        private Text _status;
        private Button _claim;
        private Text _claimLabel;

        public static DailyBonusSheet Create(Transform parent, DailyBonusService service)
        {
            var go = new GameObject("DailyBonusSheet", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var sheet = go.AddComponent<DailyBonusSheet>();
            sheet._service = service;
            sheet.Build();
            sheet.Open();
            return sheet;
        }

        private void Build()
        {
            var rt = (RectTransform)transform;
            UiFactory.Stretch(rt);

            var scrim = gameObject.AddComponent<Image>();
            scrim.sprite = UiFactory.Solid;
            scrim.color = new Color(0f, 0f, 0f, 0.65f);
            scrim.raycastTarget = true;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            var panelImg = UiFactory.NewImage("Panel", transform, UiFactory.VelvetPanel);
            UiFactory.Place(panelImg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(920f, 520f), Vector2.zero);
            _panel = panelImg.rectTransform;

            _title = UiFactory.NewText("Title", _panel, "DAILY BONUS", 32, FontStyle.Bold, UiFactory.GoldPrimary);
            UiFactory.PlaceTopCentre(_title.rectTransform, 24f, 400f, 40f);

            _subtitle = UiFactory.NewText("Subtitle", _panel, "", 16, FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.PlaceTopCentre(_subtitle.rectTransform, 70f, 400f, 24f);

            // 7 day cells.
            float cellW = 112f, gap = 10f;
            float total = 7 * cellW + 6 * gap;
            float startX = (920f - total) / 2f;
            int nextDay = _service != null ? _service.NextRewardDay : 1;
            bool claimedToday = _service != null && !_service.IsClaimable;

            for (int i = 0; i < 7; i++)
            {
                int day = i + 1;
                var reward = _service != null ? _service.PreviewReward(day) : new DailyBonusDay();
                bool isNext = day == nextDay && !claimedToday;
                bool isClaimed = claimedToday && day < nextDay;

                var cell = UiFactory.NewImage($"Day{day}", _panel,
                    isNext ? UiFactory.VelvetRaised : UiFactory.VelvetBase);
                UiFactory.PlaceTopLeft(cell.rectTransform, startX + i * (cellW + gap), 110f, cellW, 150f);

                if (isNext)
                {
                    var edge = UiFactory.NewImage("Edge", cell.transform, UiFactory.GoldPrimary);
                    UiFactory.Stretch(edge.rectTransform, 2f);
                }

                var dayText = UiFactory.NewText("Day", cell.transform, $"DAY {day}", 13, FontStyle.Bold,
                    isClaimed ? UiFactory.TextDisabled : UiFactory.TextSecondary, TextAnchor.UpperCenter);
                UiFactory.PlaceTopCentre(dayText.rectTransform, 10f, cellW, 18f);

                var coinText = UiFactory.NewText("Coins", cell.transform, UiFactory.FormatAmount(reward.coins), 16,
                    FontStyle.Bold, isClaimed ? UiFactory.TextDisabled : UiFactory.GoldPrimary, TextAnchor.MiddleCenter);
                UiFactory.Place(coinText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(cellW, 24f), new Vector2(0f, -10f));

                var gemText = UiFactory.NewText("Gems", cell.transform,
                    reward.gems > 0 ? $"+{reward.gems} ⬡" : "", 12, FontStyle.Normal,
                    isClaimed ? UiFactory.TextDisabled : UiFactory.NeonCyan, TextAnchor.LowerCenter);
                UiFactory.PlaceBottomCentre(gemText.rectTransform, 10f, cellW, 18f);

                if (isClaimed)
                {
                    var check = UiFactory.NewText("Check", cell.transform, "✓", 28, FontStyle.Bold,
                        UiFactory.StateSuccess, TextAnchor.MiddleCenter);
                    UiFactory.Stretch(check.rectTransform);
                }
            }

            _status = UiFactory.NewText("Status", _panel, "", 16, FontStyle.Normal, UiFactory.TextSecondary);
            UiFactory.PlaceBottomCentre(_status.rectTransform, 96f, 600f, 40f);

            _claim = UiFactory.NewButton("Claim", _panel, "CLAIM", 22, UiFactory.VelvetBase, out _claimLabel);
            UiFactory.PlaceBottomCentre(_claim.GetComponent<RectTransform>(), 32f, 260f, 56f);
            _claim.GetComponent<Image>().color = UiFactory.GoldPrimary;
            _claim.onClick.AddListener(OnClaimClicked);

            var close = UiFactory.NewButton("Close", _panel, "CLOSE", 18, UiFactory.VelvetBase, out Text closeText);
            PlaceTopRight(close.GetComponent<RectTransform>(), 12f, 12f, 100f, 40f);
            close.onClick.AddListener(Close);

            Refresh();
        }

        private void PlaceTopRight(RectTransform rt, float x, float y, float w, float h)
        {
            UiFactory.PlaceTopLeft(rt, 920f - w - x, y, w, h);
        }

        private void Refresh()
        {
            if (_service == null) return;

            int nextDay = _service.NextRewardDay;
            var reward = _service.NextReward;
            _subtitle.text = $"DAY {nextDay} OF 7  ·  STREAK {(_service.Progression != null ? _service.Progression.LoginStreak : 0)}";

            if (_service.IsClaimable)
            {
                _status.text = $"Claim {UiFactory.FormatAmount(reward.coins)} Coins" +
                    (reward.gems > 0 ? $" and {reward.gems} Gems" : "") + ".";
                _claimLabel.text = $"CLAIM {UiFactory.FormatAmount(reward.coins)}";
                _claim.interactable = true;
            }
            else
            {
                _status.text = $"Already claimed. Next bonus in {FormatTime(_service.TimeUntilNextClaim)}.";
                _claimLabel.text = "CLAIMED";
                _claim.interactable = false;
            }
        }

        private void OnClaimClicked()
        {
            if (_service == null) return;
            if (_service.TryClaim(out string msg))
            {
                _status.text = msg;
                _claimLabel.text = "CLAIMED";
                _claim.interactable = false;

                // Lobby currency refreshes through the WalletChanged event.
            }
            else
            {
                _status.text = msg ?? "Could not claim.";
            }
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
            var scrim = GetComponent<Image>();
            if (scrim != null) scrim.raycastTarget = !closing;

            float elapsed = 0f;
            float startAlpha = _group.alpha;
            while (elapsed < 0.32f)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / 0.32f);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                _group.alpha = Mathf.Lerp(startAlpha, target, eased);
                yield return null;
            }
            _group.alpha = target;
            _group.blocksRaycasts = !closing && target > 0.5f;
            if (closing)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            return $"{t.Minutes}m {t.Seconds}s";
        }
    }
}
