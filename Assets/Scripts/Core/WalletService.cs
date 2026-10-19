using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Core
{
    public class WalletService : MonoBehaviour
    {
        public static WalletService Instance { get; private set; }

        private readonly Dictionary<Currency, CurrencyAccount> _accounts
            = new Dictionary<Currency, CurrencyAccount>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            foreach (Currency c in Enum.GetValues(typeof(Currency)))
                _accounts[c] = new CurrencyAccount { currency = c };
        }

        public long Balance(Currency c) => _accounts[c].balance;

        public void Grant(Currency c, long amount, LedgerReason reason, string note = "")
        {
            if (amount < 0) throw new System.ArgumentException("Use Debit for negative");
            _accounts[c].Apply(amount, reason, note);
            EventBus.Publish(new WalletChanged { currency = c, balance = _accounts[c].balance });
        }

        public bool TryDebit(Currency c, long amount, LedgerReason reason, string note = "")
        {
            if (amount <= 0) return false;
            if (_accounts[c].balance < amount) return false;
            _accounts[c].Apply(-amount, reason, note);
            EventBus.Publish(new WalletChanged { currency = c, balance = _accounts[c].balance });
            return true;
        }

        public List<CurrencyAccount> All() => new List<CurrencyAccount>(_accounts.Values);

        public void LoadFromSave(Dictionary<Currency, long> balances)
        {
            foreach (var kv in balances)
                _accounts[kv.Key].SetBalance(kv.Value);
        }
    }
}