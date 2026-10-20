using System;
using System.Collections;
using LustHouse.Core;
using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Drives one game inside the single shared <c>Slot.unity</c> scene (plan §5.2).
    /// Betting, payout, Lust Points and progression stats all route through here so no
    /// game code can move currency on its own.
    /// </summary>
    [DisallowMultipleComponent]
    public class SlotGameRunner : MonoBehaviour
    {
        [Header("Wiring")]
        public SlotMachineDefinition definition;
        public ReelEngine reelEngine;

        [Header("Economy")]
        [Tooltip("Index into the bet ladder the player's level has unlocked.")]
        public int betLevel = 0;

        [Tooltip("Lust Points awarded per 10,000 Coins wagered.")]
        public long lustPerTenThousandWagered = 1;

        [Header("Autoplay / turbo (plan §3.2)")]
        public int autoplayCount = 0;
        public int turboMode = 0;

        /// <summary>Raised when a spin needs a top-up sheet — never a dead end (plan §2.8).</summary>
        public event System.Action<long> InsufficientFunds;

        public event System.Action<SpinResult> SpinResolved;

        private bool _busy;

        /// <summary>
        /// The bet the player is on, which is the ladder step their level has unlocked.
        /// </summary>
        public long CurrentBet =>
            definition != null ? definition.BetForLevel(betLevel, Economy.BetLimits.PlayerLevel) : 0;

        /// <summary>How many denominations this player can choose between.</summary>
        public int BetOptionCount =>
            definition != null ? definition.BetOptions(Economy.BetLimits.PlayerLevel).Count : 1;

        /// <summary>
        /// One spin. Debits first, evaluates, then credits any win. Every path writes a
        /// ledger entry; there is no way to change a balance without one.
        /// </summary>
        public SpinResult Spin()
        {
            var result = new SpinResult();

            if (_busy || definition == null || reelEngine == null) return result;
            if (WalletService.Instance == null) return result;

            long bet = CurrentBet;
            if (bet <= 0) return result;

            if (!WalletService.Instance.CanAfford(Currency.Coins, bet))
            {
                InsufficientFunds?.Invoke(bet);
                return result;
            }

            _busy = true;
            try
            {
                if (!WalletService.Instance.TryDebit(Currency.Coins, bet, LedgerReason.Spin, definition.id))
                {
                    InsufficientFunds?.Invoke(bet);
                    return result;
                }

                result = reelEngine.Spin(definition, bet);

                if (result.totalWin > 0)
                {
                    WalletService.Instance.TryGrant(Currency.Coins, result.totalWin,
                        LedgerReason.SpinPayout, definition.id);
                }

                // Lust is non-spendable and accrues purely from volume.
                if (PlayerProgression.Instance != null)
                {
                    long lust = bet * lustPerTenThousandWagered / 10000;
                    PlayerProgression.Instance.RecordSpin(bet, result.totalWin);
                    PlayerProgression.Instance.AddLustPoints(lust);
                    // XP comes only from Coin wins, never from the bet itself.
                    long xp = PlayerProgression.Instance.XpForCoinsWon(result.totalWin);
                    if (xp > 0) PlayerProgression.Instance.AddXp(xp);
                }

                SpinResolved?.Invoke(result);
                return result;
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// Coroutine entry point for the presentation layer. Kept separate from
        /// <see cref="Spin"/> so the simulator can call the maths without any animation.
        /// </summary>
        public IEnumerator SpinRoutine()
        {
            var result = Spin();
            if (definition == null) yield break;

            float duration = turboMode > 0
                ? definition.spinDurationSeconds * 0.35f
                : definition.spinDurationSeconds;
            yield return new WaitForSeconds(duration);
        }

        public void StepBet(int delta)
        {
            if (definition == null) return;
            int count = BetOptionCount;
            betLevel = Mathf.Clamp(betLevel + delta, 0, Mathf.Max(0, count - 1));
        }
    }
}