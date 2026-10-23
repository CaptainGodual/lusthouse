using System;
using System.Collections.Generic;
using LustHouse.Core;
using LustHouse.Audio;
using LustHouse.UI;
using UnityEngine;
using UnityEngine.UI;

namespace LustHouse.Slots
{
    /// <summary>
    /// A live jackpot meter: one row per tier, each showing tier name + current pot.
    /// Values come from <see cref="ProgressiveJackpotService"/>, so they stay live across
    /// spins and scene changes. On a hit the meter flashes and the banner shows the
    /// winning tier.
    ///
    /// Values always snap (no smoothing) — which is exactly what reduced motion asks for.
    /// </summary>
    [DisallowMultipleComponent]
    public class JackpotMeter : MonoBehaviour
    {
        [Serializable]
        public class TierRow
        {
            public string tierName;
            public Text label;
            public Text value;
        }

        /// <summary>Game id whose pots are shown. Empty falls back to the seed.</summary>
        public string gameId = "";

        public List<TierRow> rows = new List<TierRow>();

        [Tooltip("Shown briefly when a tier pays out. Leave empty for no banner.")]
        public Text banner;

        [Tooltip("Full-screen flash image. Leave empty for no flash.")]
        public Image flash;

        [Header("Timing")]
        public float flashSeconds = 0.25f;

        private float _flashElapsed;
        private Color _flashColor;

        private void Awake()
        {
            if (flash != null)
            {
                _flashColor = flash.color;
                flash.gameObject.SetActive(false);
            }

            if (banner != null) banner.gameObject.SetActive(false);
        }

        private void Update()
        {
            var service = ProgressiveJackpotService.Instance;
            if (service == null) return;

            foreach (var row in rows)
            {
                if (row.label == null || row.value == null || string.IsNullOrEmpty(row.tierName))
                    continue;

                long pot = service.PotValue(gameId, row.tierName);
                row.value.text = UiFactory.FormatAmount(pot);
            }

            if (_flashElapsed > 0f)
            {
                _flashElapsed -= Time.unscaledDeltaTime;
                if (flash != null)
                {
                    float k = Mathf.Clamp01(_flashElapsed / flashSeconds);
                    flash.color = new Color(_flashColor.r, _flashColor.g, _flashColor.b, k);
                    if (_flashElapsed <= 0f) flash.gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Called when a tier pays out: flash the meter and show the banner.</summary>
        public void OnHit(string tierName, long amount)
        {
            if (banner != null)
            {
                banner.text = $"{tierName.ToUpperInvariant()} JACKPOT  {UiFactory.FormatAmount(amount)}";
                banner.gameObject.SetActive(true);
            }

            if (flash != null)
            {
                flash.gameObject.SetActive(true);
                flash.color = _flashColor;
                _flashElapsed = flashSeconds;
            }

            AudioFeedback.Play(AudioCue.JackpotWin);
        }

        /// <summary>Hide the banner.</summary>
        public void ClearBanner()
        {
            if (banner != null) banner.gameObject.SetActive(false);
        }
    }
}
