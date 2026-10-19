using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// Owns the three <see cref="CurrencyAccount"/>s and is the only thing allowed to move
    /// a balance (plan §4.5). Every mutation appends a ledger entry first-class.
    /// </summary>
    [DisallowMultipleComponent]
    public class WalletService : MonoBehaviour
    {
        public static WalletService Instance { get; private set; }

        private readonly Dictionary<Currency, CurrencyAccount> _accounts =
            new Dictionary<Currency, CurrencyAccount>();

        private int _conversionsToday;
        private string _conversionDateUtc = "";

        /// <summary>Raised after any successful mutation. Consumers also get Wallet.Changed.</summary>
        public event Action<Currency, long, long> BalanceChanged;

        /// <summary>Set by the bootstrap so a mutation marks the save dirty.</summary>
        public Action MarkedDirty;

        public IReadOnlyDictionary<Currency, CurrencyAccount> Accounts => _accounts;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            foreach (Currency c in Enum.GetValues(typeof(Currency)))
                _accounts[c] = new CurrencyAccount(c);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- reads ------------------------------------------------------------

        public long Balance(Currency c) => _accounts.TryGetValue(c, out var a) ? a.balance : 0L;

        public CurrencyAccount Account(Currency c) => _accounts.TryGetValue(c, out var a) ? a : null;

        public bool CanAfford(Currency c, long amount) => Balance(c) >= amount && amount > 0;

        // ---- writes -----------------------------------------------------------

        /// <summary>Adds currency. Returns false for a non-positive amount.</summary>
        public bool TryGrant(Currency c, long amount, LedgerReason reason, string note = "")
        {
            if (amount <= 0) return false;
            if (!_accounts.TryGetValue(c, out var account)) return false;
            if (!account.Apply(amount, reason, note)) return false;

            Publish(c, account, amount, reason);
            return true;
        }

        /// <summary>
        /// Removes currency. Refused outright for non-spendable currencies (Lust Points)
        /// and when the balance is short.
        /// </summary>
        public bool TryDebit(Currency c, long amount, LedgerReason reason, string note = "")
        {
            if (!CurrencyRules.CanBeDebited(c, amount))
            {
                if (amount > 0 && !CurrencyRules.IsSpendable(c))
                    LustLog.Warn($"[LustHouse] {c} is non-spendable; debit refused.");
                return false;
            }

            if (!_accounts.TryGetValue(c, out var account)) return false;
            if (!account.Apply(-amount, reason, note)) return false;

            Publish(c, account, -amount, reason);
            return true;
        }

        /// <summary>
        /// Adjusts by a signed delta. Negative deltas route through the same guards as
        /// <see cref="TryDebit"/>.
        /// </summary>
        public bool TryAdjust(Currency c, long delta, LedgerReason reason, string note = "")
        {
            return delta >= 0
                ? TryGrant(c, delta, reason, note)
                : TryDebit(c, -delta, reason, note);
        }

        private void Publish(Currency c, CurrencyAccount account, long delta, LedgerReason reason)
        {
            BalanceChanged?.Invoke(c, delta, account.balance);
            EventBus.Publish(new WalletChanged
            {
                currency = c,
                balance = account.balance,
                delta = delta,
                reason = reason
            });
            MarkedDirty?.Invoke();
        }

        // ---- conversion (plan §4.3 rule 2) -----------------------------------

        public int ConversionsRemainingToday()
        {
            RollConversionDate();
            return Mathf.Max(0, CurrencyRules.MaxConversionsPerDay - _conversionsToday);
        }

        /// <summary>
        /// One-directional Gems → Coins. There is deliberately no inverse method; coin
        /// washing is impossible by construction.
        /// </summary>
        public bool TryConvertGemsToCoins(long gems, out long coins)
        {
            coins = 0;
            RollConversionDate();

            if (gems <= 0)
            {
                Block("Conversion amount must be positive.");
                return false;
            }

            if (_conversionsToday >= CurrencyRules.MaxConversionsPerDay)
            {
                Block($"Daily conversion cap reached ({CurrencyRules.MaxConversionsPerDay}/day).");
                return false;
            }

            coins = gems * CurrencyRules.GemsToCoinsRate;

            if (!TryDebit(Currency.Gems, gems, LedgerReason.Conversion, "gems→coins"))
            {
                Block("Not enough Gems to convert.");
                coins = 0;
                return false;
            }

            if (!TryGrant(Currency.Coins, coins, LedgerReason.Conversion, "gems→coins"))
            {
                // Grant failed after the debit succeeded — refund rather than rewrite history.
                TryGrant(Currency.Gems, gems, LedgerReason.ConversionRefund, "conversion rolled back");
                coins = 0;
                return false;
            }

            _conversionsToday++;
            return true;
        }

        private void Block(string reason)
        {
            LustLog.Info($"[LustHouse] Conversion blocked: {reason}");
            EventBus.Publish(new WalletConversionBlocked { reason = reason });
        }

        private void RollConversionDate()
        {
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");
            if (_conversionDateUtc == today) return;
            _conversionDateUtc = today;
            _conversionsToday = 0;
        }

        // ---- persistence ------------------------------------------------------

        public WalletState CaptureState()
        {
            var state = new WalletState
            {
                conversionsToday = _conversionsToday,
                conversionDateUtc = _conversionDateUtc
            };

            foreach (var kv in _accounts)
            {
                state.accounts.Add(new AccountState
                {
                    currency = kv.Key,
                    balance = kv.Value.balance,
                    ledger = new List<LedgerEntry>(kv.Value.ledger)
                });
            }

            return state;
        }

        public void RestoreState(WalletState state)
        {
            if (state == null) return;

            _conversionsToday = state.conversionsToday;
            _conversionDateUtc = state.conversionDateUtc ?? "";

            foreach (var saved in state.accounts)
            {
                if (!_accounts.TryGetValue(saved.currency, out var account)) continue;

                account.ledger.Clear();
                if (saved.ledger != null) account.ledger.AddRange(saved.ledger);

                account.SetBalanceDirect(saved.balance);

                // Owned ids are never invalidated, but a hand-edited save could drive the
                // ledger and balance out of sync. Trust the balance, keep the ledger.
                account.spendable = CurrencyRules.IsSpendable(saved.currency);
            }
        }

        /// <summary>
        /// Save-integrity check: replays every ledger and compares against the stored
        /// balance. Surfaced in QA builds.
        /// </summary>
        public bool LedgerConsistent(out string report)
        {
            bool ok = true;
            var sb = new System.Text.StringBuilder();
            foreach (var kv in _accounts)
            {
                long replayed = kv.Value.ReplayBalance();
                sb.AppendLine($"{kv.Key}: stored={kv.Value.balance} replayed={replayed}");
                if (replayed != kv.Value.balance) ok = false;
            }
            report = sb.ToString();
            return ok;
        }
    }
}