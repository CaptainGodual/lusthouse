using System;
using LustHouse.Core;
using LustHouse.Economy;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Lobby
{
    /// <summary>Top-bar daily bonus chip: state + countdown, opens the claim sheet.</summary>
    public class DailyBonusChip : MonoBehaviour
    {
        public DailyBonusService service;
        public Action OnOpen;

        private Text _status;
        private Image _dot;

        public void Build(RectTransform parent)
        {
            var chip = UiFactory.NewImage("DailyBonusChip", parent, UiFactory.VelvetPanel);
            chip.sprite = UiFactory.RoundedPanel;
            chip.type = Image.Type.Sliced;
            chip.raycastTarget = true;
            UiFactory.PlaceTopLeft(chip.rectTransform, 0f, 0f, 190f, 48f);

            var rim = UiChrome.NewRim("Rim", chip.transform, UiFactory.GoldPrimary);
            UiFactory.Stretch(rim.rectTransform, -2f);

            var label = UiFactory.NewText("Label", chip.transform, "REWARDS", 15,
                FontStyle.Bold, UiFactory.TextPrimary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(label.rectTransform, 14f, 5f, 120f, 20f);

            _status = UiFactory.NewText("Status", chip.transform, "READY", 15,
                FontStyle.Bold, UiFactory.GoldPrimary, TextAnchor.MiddleLeft);
            UiFactory.PlaceTopLeft(_status.rectTransform, 14f, 24f, 130f, 20f);

            _dot = UiFactory.NewImage("Dot", chip.transform, UiFactory.StateDanger);
            UiFactory.PlaceTopLeft(_dot.rectTransform, 164f, 18f, 12f, 12f);

            var button = chip.gameObject.AddComponent<Button>();
            button.targetGraphic = chip;
            UiFactory.StyleButton(button);
            button.onClick.AddListener(() => OnOpen?.Invoke());

            if (service != null) service.StateChanged += Refresh;
            Refresh();
        }

        private void Update()
        {
            if (service == null) return;
            // Refresh once per second for the countdown.
            if (Time.frameCount % 60 == 0) Refresh();
        }

        private void OnDestroy()
        {
            if (service != null) service.StateChanged -= Refresh;
        }

        public void Refresh()
        {
            if (_status == null || service == null) return;

            if (service.IsClaimable)
            {
                _status.text = "READY";
                _status.color = UiFactory.GoldPrimary;
                if (_dot != null) _dot.gameObject.SetActive(true);
            }
            else
            {
                _status.text = FormatTime(service.TimeUntilNextClaim);
                _status.color = UiFactory.TextSecondary;
                if (_dot != null) _dot.gameObject.SetActive(false);
            }
        }

        private static string FormatTime(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) return "READY";
            if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
            return $"{t.Minutes}m {t.Seconds}s";
        }
    }
}
