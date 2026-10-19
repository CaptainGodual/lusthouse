using System;
using System.Collections.Generic;
using LustHouse.Avatar;
using LustHouse.Economy;
using UnityEngine;

namespace LustHouse.Core
{
    /// <summary>
    /// Owns owned cosmetics and the equipped loadout. Purchases are a two-service
    /// transaction with WalletService and are never partially applied (plan §5.3).
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryService : MonoBehaviour
    {
        public static InventoryService Instance { get; private set; }

        private readonly HashSet<string> _owned = new HashSet<string>();
        private readonly Dictionary<CosmeticCategory, string> _equipped =
            new Dictionary<CosmeticCategory, string>();

        private bool _starterGrantApplied;

        /// <summary>Set by the bootstrap so a mutation marks the save dirty.</summary>
        public Action MarkedDirty;

        /// <summary>
        /// Set by the bootstrap once the cosmetic catalog is loaded. Used to evaluate
        /// non-purchase unlock conditions.
        /// </summary>
        public Func<CosmeticDefinition, bool> UnlockEvaluator;

        public event Action<string> OwnedChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---- reads ------------------------------------------------------------

        public bool IsOwned(string id) => !string.IsNullOrEmpty(id) && _owned.Contains(id);

        public string Equipped(CosmeticCategory c) =>
            _equipped.TryGetValue(c, out var id) ? id : null;

        public int OwnedCount => _owned.Count;

        public IEnumerable<string> Owned()
        {
            foreach (var id in _owned) yield return id;
        }

        public AvatarLoadout GetLoadout()
        {
            return new AvatarLoadout
            {
                baseId = Equipped(CosmeticCategory.Base),
                hairId = Equipped(CosmeticCategory.Hair),
                outfitId = Equipped(CosmeticCategory.Outfit),
                accessoryId = Equipped(CosmeticCategory.Accessory),
                emoteId = Equipped(CosmeticCategory.Emote)
            };
        }

        // ---- equip ------------------------------------------------------------

        public bool Equip(CosmeticDefinition def)
        {
            if (def == null || !IsOwned(def.id)) return false;
            return Equip(def.category, def.id);
        }

        public bool Equip(CosmeticCategory category, string id)
        {
            if (!IsOwned(id)) return false;

            // Equipping is instant and free — never charge for a re-equip (§5.3 rule 4).
            _equipped[category] = id;
            MarkedDirty?.Invoke();
            EventBus.Publish(new InventoryEquipped { itemId = id, category = category.ToString() });
            EventBus.Publish(new AvatarChanged { loadoutHash = LoadoutHash() });
            return true;
        }

        public string LoadoutHash() => GetLoadout().Hash();

        // ---- grant ------------------------------------------------------------

        /// <summary>
        /// Grants an item for free (starter grant, VIP unlock, mission reward). Never
        /// charges and never debits.
        /// </summary>
        public bool Grant(CosmeticDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.id)) return false;
            if (!_owned.Add(def.id)) return false;

            MarkedDirty?.Invoke();
            OwnedChanged?.Invoke(def.id);
            EventBus.Publish(new InventoryItemAdded { itemId = def.id, category = def.category.ToString() });

            // First acquisition auto-equips so the avatar is never half-dressed.
            if (!_equipped.ContainsKey(def.category))
                Equip(def.category, def.id);

            return true;
        }

        public bool Grant(string id) => Grant(new CosmeticDefinition { id = id });

        /// <summary>
        /// Grants the one-per-category starter set. Runs once per account, ever
        /// (plan §5.4). Guarantees a complete, renderable avatar from the first frame.
        /// </summary>
        public void GrantStarterSet(IReadOnlyList<CosmeticDefinition> catalog)
        {
            if (_starterGrantApplied || catalog == null) return;

            foreach (CosmeticCategory category in Enum.GetValues(typeof(CosmeticCategory)))
            {
                for (int i = 0; i < catalog.Count; i++)
                {
                    var def = catalog[i];
                    if (def == null || def.category != category) continue;
                    if (def.unlockCondition != UnlockCondition.Starter && def.priceGems > 0) continue;
                    Grant(def);
                    break;
                }
            }

            _starterGrantApplied = true;
            MarkedDirty?.Invoke();
        }

        // ---- purchase (plan §5.3) --------------------------------------------

        public bool CanPurchase(CosmeticDefinition def, WalletService wallet, out string failure)
        {
            failure = null;

            if (def == null) { failure = "Missing item."; return false; }
            if (IsOwned(def.id)) { failure = "Already owned."; return false; }
            if (def.unlockCondition != UnlockCondition.Purchase)
            {
                failure = "Not purchasable — unlock condition only.";
                return false;
            }
            if (def.priceGems <= 0) { failure = "Item has no Gem price."; return false; }
            if (wallet == null) { failure = "Wallet unavailable."; return false; }
            if (!wallet.CanAfford(Currency.Gems, def.priceGems))
            {
                failure = "Not enough Gems.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// The atomic two-service transaction. Debit, then grant; if the grant fails the
        /// debit is compensated with a <see cref="LedgerReason.PurchaseRefund"/> entry so
        /// the audit trail is never rewritten (§5.3).
        /// </summary>
        public bool TryPurchase(CosmeticDefinition def, WalletService wallet)
        {
            if (!CanPurchase(def, wallet, out var failure))
            {
                EventBus.Publish(new InventoryPurchaseFailed { itemId = def?.id, reason = failure });
                return false;
            }

            long price = def.priceGems;

            if (!wallet.TryDebit(Currency.Gems, price, LedgerReason.Purchase, def.id))
            {
                EventBus.Publish(new InventoryPurchaseFailed { itemId = def.id, reason = "Debit failed." });
                return false;
            }

            if (!GrantInternal(def))
            {
                // Compensate with a new ledger entry. Never delete the Purchase entry.
                wallet.TryGrant(Currency.Gems, price, LedgerReason.PurchaseRefund, def.id);
                EventBus.Publish(new InventoryPurchaseFailed { itemId = def.id, reason = "Grant failed; refunded." });
                return false;
            }

            return true;
        }

        /// <summary>Grant that does not auto-equip — used inside the purchase transaction.</summary>
        private bool GrantInternal(CosmeticDefinition def)
        {
            if (!_owned.Add(def.id)) return false;

            MarkedDirty?.Invoke();
            OwnedChanged?.Invoke(def.id);
            EventBus.Publish(new InventoryItemAdded { itemId = def.id, category = def.category.ToString() });
            return true;
        }

        // ---- persistence ------------------------------------------------------

        public InventoryState CaptureState()
        {
            var state = new InventoryState { starterGrantApplied = _starterGrantApplied };
            state.ownedIds.AddRange(_owned);

            foreach (var kv in _equipped)
            {
                state.equipped.Add(new EquippedSlotState
                {
                    category = kv.Key.ToString(),
                    itemId = kv.Value
                });
            }

            return state;
        }

        public void RestoreState(InventoryState state)
        {
            _owned.Clear();
            _equipped.Clear();

            if (state == null) return;

            if (state.ownedIds != null)
                foreach (var id in state.ownedIds)
                    if (!string.IsNullOrEmpty(id)) _owned.Add(id);

            if (state.equipped != null)
            {
                foreach (var slot in state.equipped)
                {
                    if (slot == null || string.IsNullOrEmpty(slot.itemId)) continue;
                    if (!Enum.TryParse(slot.category, out CosmeticCategory category)) continue;

                    // Equipping something we do not own would render a phantom layer.
                    if (_owned.Contains(slot.itemId)) _equipped[category] = slot.itemId;
                }
            }

            _starterGrantApplied = state.starterGrantApplied;
        }
    }
}