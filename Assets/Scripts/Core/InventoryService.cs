using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LustHouse.Core;
using LustHouse.Economy;

namespace LustHouse.Core
{
    public class InventoryService : MonoBehaviour
    {
        public static InventoryService Instance { get; private set; }

        private readonly HashSet<string> _owned = new HashSet<string>();
        private readonly Dictionary<CosmeticCategory, string> _equipped
            = new Dictionary<CosmeticCategory, string>();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public bool IsOwned(string id) => _owned.Contains(id);
        public string Equipped(CosmeticCategory c) =>
            _equipped.TryGetValue(c, out var id) ? id : null;

        public void Equip(CosmeticCategory c, string id)
        {
            if (!_owned.Contains(id)) return;
            _equipped[c] = id;
            EventBus.Publish(new InventoryEquipped { itemId = id });
        }

        // Atomic two-service transaction. See plan §5.3.
        public bool TryPurchase(CosmeticDefinition def, WalletService wallet)
        {
            if (def == null || _owned.Contains(def.id)) return false;
            if (def.unlockCondition != UnlockCondition.Purchase) return false;
            if (def.priceGems <= 0) return false;

            if (!wallet.TryDebit(Currency.Gems, def.priceGems, LedgerReason.Purchase, def.id))
                return false; // insufficient funds

            _owned.Add(def.id);
            EventBus.Publish(new InventoryItemAdded { itemId = def.id });
            return true;
        }

        public void Grant(string id)
        {
            if (_owned.Add(id))
                EventBus.Publish(new InventoryItemAdded { itemId = id });
        }

        public List<string> Owned() => _owned.ToList();

        public void LoadFromSave(HashSet<string> owned, Dictionary<CosmeticCategory, string> equipped)
        {
            _owned.Clear();
            foreach (var id in owned) _owned.Add(id);
            _equipped.Clear();
            foreach (var kv in equipped) _equipped[kv.Key] = kv.Value;
        }
    }
}