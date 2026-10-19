using UnityEngine;

namespace LustHouse.Economy
{
    public enum CosmeticCategory { Base, Hair, Outfit, Accessory, Emote }

    public enum CosmeticRarity { Common, Uncommon, Rare, Epic, VIP }

    /// <summary>
    /// How an item becomes owned. Only <see cref="Purchase"/> involves money.
    /// </summary>
    public enum UnlockCondition
    {
        /// <summary>Granted by the one-per-category starter set on account creation.</summary>
        Starter,
        /// <summary>Bought with Gems. Never with Coins.</summary>
        Purchase,
        /// <summary>Granted for reaching a VIP tier. Never purchasable.</summary>
        VipTier,
        /// <summary>Granted for reaching an account level.</summary>
        Level,
        /// <summary>Granted for reaching a season rank.</summary>
        SeasonRank,
        /// <summary>Granted for lifetime spin count.</summary>
        LifetimeSpins
    }

    /// <summary>
    /// One cosmetic item (plan §5.3).
    ///
    /// Guardrail: there is deliberately no coin price field. <see cref="priceGems"/> is
    /// the only price in the model, which makes it structurally impossible for the lobby
    /// to violate guardrail §2 / plan §5.3 rule 1. There is also no gameplay field of any
    /// kind, so cosmetics cannot affect RTP, bet range, or odds (§5.3 rule 3).
    /// </summary>
    [CreateAssetMenu(menuName = "LustHouse/Cosmetic", fileName = "Cosmetic_")]
    public class CosmeticDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable save-data key. Never rename once shipped.")]
        public string id = "";

        public string displayName = "";

        [TextArea(1, 2)]
        public string description = "";

        public CosmeticCategory category = CosmeticCategory.Base;
        public CosmeticRarity rarity = CosmeticRarity.Common;

        [Header("Art")]
        [Tooltip("The single layer sprite composited into the bust portrait.")]
        public Sprite layerSprite;

        public Vector2 layerOffset = Vector2.zero;
        public Vector2 layerScale = Vector2.one;

        [Tooltip("Thumbnail for wardrobe grids. Cropped from the layer if left empty.")]
        public Sprite previewSprite;

        [Tooltip("Lazy-load key for high-fx variants.")]
        public string addressableKey = "";

        [Header("Unlock")]
        public UnlockCondition unlockCondition = UnlockCondition.Purchase;

        [Tooltip("Gem price. 0 means not purchasable. The ONLY price field.")]
        public int priceGems = 0;

        [Tooltip("VIP tier / level / season rank / spin count threshold.")]
        public int unlockValue = 0;

        // ---- helpers ----------------------------------------------------------

        public bool IsPurchasable => unlockCondition == UnlockCondition.Purchase && priceGems > 0;

        public bool IsFreeUnlock => unlockCondition != UnlockCondition.Purchase;

        /// <summary>
        /// Editor/validator hook backing the guardrail test: a cosmetic must never be
        /// reachable with Coins.
        /// </summary>
        public bool WouldRequireCoins => false;

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (unlockCondition == UnlockCondition.Purchase && priceGems <= 0)
            {
                Debug.LogWarning($"[LustHouse] Cosmetic '{id}' is marked Purchase but has no Gem price. " +
                                 "Use Starter/VipTier/Level if it should be granted.", this);
            }
        }
#endif
    }
}