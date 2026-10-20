using System;
using LustHouse.Core;
using UnityEngine;

namespace LustHouse.Economy
{
    [Serializable]
    public class DailyBonusDay
    {
        public long coins = 0;
        public long gems = 0;
    }

    /// <summary>
    /// Owns the daily bonus: claimability, streak, rewards and the ledger write.
    /// Created by <see cref="Core.ServicesBootstrap"/> and persisted through
    /// <see cref="PlayerProgression"/> (streak + last claim date).
    /// </summary>
    public class DailyBonusService : MonoBehaviour
    {
        public static DailyBonusService Instance { get; private set; }

        [Header("Reward ladder (7 days)")]
        public DailyBonusDay[] ladder = new DailyBonusDay[]
        {
            new DailyBonusDay { coins = 10000, gems = 0 },
            new DailyBonusDay { coins = 12000, gems = 0 },
            new DailyBonusDay { coins = 15000, gems = 0 },
            new DailyBonusDay { coins = 18000, gems = 0 },
            new DailyBonusDay { coins = 20000, gems = 0 },
            new DailyBonusDay { coins = 25000, gems = 0 },
            new DailyBonusDay { coins = 30000, gems = 25 },
        };

        [Header("Rules")]
        public int requiredSpins = 100;
        public long fallbackMinBet = 100;

        /// <summary>Raised when claimability or countdown changes.</summary>
        public event Action StateChanged;

        private bool _wasClaimable;
        private float _nextPoll;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _wasClaimable = IsClaimable;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + 1f;

            bool now = IsClaimable;
            if (now != _wasClaimable)
            {
                _wasClaimable = now;
                if (now) EventBus.Publish(new DailyGiftReady());
                StateChanged?.Invoke();
            }
        }

        public PlayerProgression Progression => PlayerProgression.Instance;

        // ---- reads ---------------------------------------------------------

        public bool IsClaimable => CanClaim(DateTime.UtcNow,
            Progression != null ? Progression.LastDailyGiftUtc : "",
            out _);

        public int NextRewardDay
        {
            get
            {
                int streak = Progression != null ? Progression.LoginStreak : 0;
                string last = Progression != null ? Progression.LastDailyGiftUtc : "";
                DateTime now = DateTime.UtcNow;
                if (!string.IsNullOrEmpty(last) && TryParseDate(last, out var lastDate) && lastDate.Date == now.Date)
                    return ((streak % 7) + 1); // already claimed today -> next cycle day
                // preview the streak that *would* result
                var prog = PlayerProgression.Instance;
                int nextStreak = ComputeNextStreak(prog != null ? prog.LoginStreak : 0, last, now);
                return ((nextStreak - 1) % 7) + 1;
            }
        }

        public TimeSpan TimeUntilNextClaim
        {
            get
            {
                string last = Progression != null ? Progression.LastDailyGiftUtc : "";
                if (string.IsNullOrEmpty(last)) return TimeSpan.Zero;
                if (!TryParseDate(last, out var lastDate)) return TimeSpan.Zero;
                DateTime now = DateTime.UtcNow;
                if (lastDate.Date < now.Date) return TimeSpan.Zero;
                DateTime tomorrow = now.Date.AddDays(1);
                return tomorrow - now;
            }
        }

        public DailyBonusDay PreviewReward(int day)
        {
            if (ladder == null || ladder.Length == 0) return new DailyBonusDay();
            int idx = Mathf.Clamp(day - 1, 0, ladder.Length - 1);
            return ladder[idx];
        }

        public DailyBonusDay NextReward => PreviewReward(NextRewardDay);

        // ---- claim --------------------------------------------------------

        public bool TryClaim(out string message)
        {
            message = null;
            var progression = Progression;
            if (progression == null)
            {
                message = "Progression is not ready yet.";
                return false;
            }

            DateTime now = DateTime.UtcNow;
            string last = progression.LastDailyGiftUtc;

            if (!CanClaim(now, last, out string blockReason))
            {
                message = blockReason ?? "Already claimed today.";
                return false;
            }

            // Ledger-derived guard: a persisted grant for today must not be re-claimed.
            var coinsAccount = WalletService.Instance != null ? WalletService.Instance.Account(Currency.Coins) : null;
            string todayNote = "daily gift day " + NextRewardDay + " " + now.ToString("yyyy-MM-dd");
            if (coinsAccount != null && coinsAccount.ContainsEntry(LedgerReason.DailyGift, todayNote))
            {
                progression.RecordDailyGiftClaim(now.ToString("o"), progression.LoginStreak > 0 ? progression.LoginStreak : 1);
                message = "Already claimed today.";
                return false;
            }

            int nextStreak = ComputeNextStreak(progression.LoginStreak, last, now);
            int rewardDay = ((nextStreak - 1) % 7) + 1;
            var reward = PreviewReward(rewardDay);

            // Grant coins.
            if (WalletService.Instance != null)
            {
                if (reward.coins > 0)
                    WalletService.Instance.TryGrant(Currency.Coins, reward.coins, LedgerReason.DailyGift, todayNote);
                if (reward.gems > 0)
                    WalletService.Instance.TryGrant(Currency.Gems, reward.gems, LedgerReason.DailyGift,
                        "daily gift day 7 gems " + now.ToString("yyyy-MM-dd"));
            }

            progression.RecordDailyGiftClaim(now.ToString("o"), nextStreak);

            _wasClaimable = IsClaimable;
            StateChanged?.Invoke();

            if (ServicesBootstrap.Instance != null) ServicesBootstrap.Instance.SaveNow();

            message = $"+{reward.coins} Coins" + (reward.gems > 0 ? $" and {reward.gems} Gems" : "");
            EventBus.Publish(new DailyGiftClaimed { streakDay = rewardDay, coins = reward.coins, gems = reward.gems });
            return true;
        }

        // ---- pure helpers (testable) --------------------------------------

        public static bool CanClaim(DateTime now, string lastClaimUtc, out string blockReason)
        {
            blockReason = null;
            if (string.IsNullOrWhiteSpace(lastClaimUtc)) return true;
            if (!TryParseDate(lastClaimUtc, out var last)) return true;

            DateTime today = now.Date;
            if (last.Date > today)
            {
                blockReason = "Check your device clock.";
                return false;
            }
            if (last.Date == today)
            {
                blockReason = "Already claimed today.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Computes the new streak given the previous streak value.
        /// </summary>
        public static int ComputeNextStreak(int currentStreak, string lastClaimUtc, DateTime now)
        {
            if (string.IsNullOrWhiteSpace(lastClaimUtc)) return 1;
            if (!TryParseDate(lastClaimUtc, out var last)) return 1;

            DateTime today = now.Date;
            if (last.Date > today) return Mathf.Max(1, currentStreak); // blocked elsewhere
            if (last.Date == today) return Mathf.Max(1, currentStreak);
            if (last.Date == today.AddDays(-1)) return Mathf.Max(1, currentStreak) + 1;
            return 1;
        }

        public static bool TryParseDate(string value, out DateTime date)
        {
            date = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal |
                System.Globalization.DateTimeStyles.AssumeUniversal, out date);
        }
    }
}
