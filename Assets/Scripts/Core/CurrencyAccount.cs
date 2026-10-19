using System;
using System.Collections.Generic;

namespace LustHouse.Core
{
    [Serializable]
    public class CurrencyAccount
    {
        public Currency currency;
        public long balance;
        public List<LedgerEntry> ledger = new List<LedgerEntry>();

        public void SetBalance(long value) => balance = value;

        public void Apply(long delta, LedgerReason reason, string note = "")
        {
            balance += delta;
            ledger.Add(new LedgerEntry
            {
                timestamp = DateTime.UtcNow,
                currency = currency,
                delta = delta,
                reason = reason,
                balanceAfter = balance,
                note = note
            });
        }
    }
}