using UnityEngine;

namespace LustHouse.Economy
{
    public enum CosmeticCategory { Base, Hair, Outfit, Accessory, Emote }
    public enum CosmeticRarity { Common, Uncommon, Rare, Epic, VIP }

    public enum UnlockCondition { None, Purchase, VipTier, Level, SeasonRank, LifetimeSpins }

    [CreateAssetMenu(menuName = "LustHouse/Cosmetic", fileName = "Cosmetic_")]
    public class CosmeticDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public CosmeticCategory category;
        public CosmeticRarity rarity;
        public Sprite previewSprite;
        public Sprite[] layerSprites;
        public int priceGems;                 // 0 = not purchasable
        public UnlockCondition unlockCondition = UnlockCondition.Purchase;
        public int unlockValue;               // tier/level/rank threshold
        public string addressableKey;
    }
}