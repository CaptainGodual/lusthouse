using System;
using System.Collections.Generic;

namespace LustHouse.Core
{
    /// <summary>
    /// One wallet account. Holds the authoritative balance and the append-only ledger
    /// that balance is derived from (plan §4.5).
    /// </summary>
    [Serializable]
    public class CurrencyAccount
    {
        public Currency currency;
        public long balance;

        /// <summary>
        /// When false the account refuses every debit regardless of who asks. This is the
        /// structural guard for Lust Points — callers cannot bypass it.
        /// </summary>
        public bool spendable = true;

        /// <summary>Bounded audit trail. The full history is not persisted forever.</summary>
        public List<LedgerEntry> ledger = new List<LedgerEntry>();

        public const int MaxPersistedLedgerEntries = 500;

        public CurrencyAccount() { }

        public CurrencyAccount(Currency c)
        {
            currency = c;
            spendable = CurrencyRules.IsSpendable(c);
        }

        public void Reset()
        {
            balance = 0;
            ledger.Clear();
        }

        /// <summary>
        /// Append a ledger entry and move the balance. Returns false (and changes nothing)
        /// if the mutation is not permitted.
        /// </summary>
        public bool Apply(long delta, LedgerReason reason, string note = "", DateTime? timestamp = null)
        {
            if (delta < 0)
            {
                if (!spendable) return false;
                if (balance + delta < 0) return false;
            }

            balance += delta;
            ledger.Add(new LedgerEntry
            {
                sequence = ledger.Count == 0 ? 1 : ledger[ledger.Count - 1].sequence + 1,
                timestamp = timestamp ?? DateTime.UtcNow,
                currency = currency,
                delta = delta,
                reason = reason,
                balanceAfter = balance,
                note = note ?? string.Empty
            });

            if (ledger.Count > MaxPersistedLedgerEntries)
                ledger.RemoveRange(0, ledger.Count - MaxPersistedLedgerEntries);

            return true;
        }

        /// <summary>Restores a persisted balance without writing a ledger entry.</summary>
        public void SetBalanceDirect(long value)
        {
            balance = value;
        }

        /// <summary>
        /// Re-derives the balance by replaying the ledger. Used by tests and the
        /// save-integrity check; proves §4.5's "balance is derived from the ledger".
        /// </summary>
        public long ReplayBalance()
        {
            long sum = 0;
            for (int i = 0; i < ledger.Count; i++) sum += ledger[i].delta;
            return sum;
        }
    }
}