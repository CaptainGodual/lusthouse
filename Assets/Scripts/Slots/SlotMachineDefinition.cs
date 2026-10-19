using UnityEngine;

namespace LustHouse.Slots
{
    public enum SlotTemplate { Classic3, Video5, Cascade, HoldAndSpin, Progressive }
    public enum Volatility { Low, Medium, High }

    [CreateAssetMenu(menuName = "LustHouse/Slot Definition", fileName = "Slot_")]
    public class SlotMachineDefinition : ScriptableObject
    {
        public string id;
        public string title;
        public SlotTemplate template;
        public Volatility volatility;
        public Sprite keyArt;
        public string addressableKey;

        [Header("Reels")]
        public int reelCount = 5;
        public int rowCount = 3;
        public int[] symbolStrip;   // symbol ids per reel, concatenated

        [Header("Economy")]
        public long minBet = 100;
        public long maxBet = 5000;
        public int betLevels = 6;
        public float targetRtp = 0.955f;
        public int maxWinMultiplier = 2500;
        public bool progressive;

        [Header("Features")]
        public bool hasFreeSpins;
        public int freeSpinScatterCount = 3;
        public bool hasCascade;
        public bool hasHoldAndSpin;
        public bool hasBonusBuy;
        public long bonusBuyCostMultiplier = 80;
    }
}