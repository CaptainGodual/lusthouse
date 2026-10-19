using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    public enum SlotTemplate { Classic3, Video5, Cascade, HoldAndSpin, Progressive }
    public enum Volatility { Low, Medium, High }
    public enum PayType { Lines, Ways, Cluster }

    /// <summary>One symbol in a game's paytable (plan §3.5).</summary>
    [Serializable]
    public class SymbolDefinition
    {
        public int id = 0;
        public string symbolName = "";

        public Sprite sprite;

        [Tooltip("Payout in bet-multiples for 3, 4, 5, ... matching symbols. Index 0 == three of a kind.")]
        public long[] payoutsByMatch = new long[0];

        [Tooltip("Payout in bet-multiples by cluster size, index 0 == cascadeClusterSize " +
                 "symbols. Required for PayType.Cluster: a 9-cell cluster would otherwise " +
                 "index 6 into payoutsByMatch and pay nothing at all.")]
        public long[] clusterPayouts = new long[0];

        [Tooltip("Substitutes for any non-scatter symbol when counting lines.")]
        public bool isWild = false;

        [Tooltip("Pays anywhere and triggers features; never substitutes.")]
        public bool isScatter = false;

        public bool isSpecial = false;

        public long PayoutForMatch(int count)
        {
            if (payoutsByMatch == null || payoutsByMatch.Length == 0) return 0;
            int index = count - 3;
            if (index < 0 || index >= payoutsByMatch.Length) return 0;
            return payoutsByMatch[index];
        }

        /// <summary>
        /// Payout for a cluster of <paramref name="size"/> symbols. Keyed off
        /// <c>cascadeClusterSize</c>, not off three-of-a-kind, so a nine-cell cluster does
        /// not index six entries into the line paytable and pay nothing.
        /// </summary>
        public long PayoutForCluster(int size, int minSize)
        {
            if (clusterPayouts != null && clusterPayouts.Length > 0)
            {
                int index = size - minSize;
                if (index >= 0 && index < clusterPayouts.Length) return clusterPayouts[index];
                return 0;
            }

            return PayoutForMatch(size);   // fall back for hand-built fixtures
        }
    }

    /// <summary>One reel's strip: symbol ids in stop order.</summary>
    [Serializable]
    public class ReelStrip
    {
        public List<int> symbols = new List<int>();
    }

    /// <summary>One payline: a row index per reel.</summary>
    [Serializable]
    public class PaylineDefinition
    {
        public string lineName = "";
        /// <summary>rows[reel] — the row this line visits on each reel.</summary>
        public int[] rows = new int[0];
    }

    /// <summary>
    /// A complete game as data. Adding a game must require zero new C# (plan §3.5).
    /// </summary>
    [CreateAssetMenu(menuName = "LustHouse/Slot Definition", fileName = "Slot_")]
    public class SlotMachineDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string id = "";
        public string title = "";
        [TextArea(1, 3)]
        public string tagline = "";
        public SlotTemplate template = SlotTemplate.Classic3;
        public Volatility volatility = Volatility.Medium;
        public Color themeTint = Color.white;
        public Sprite keyArt;
        public string addressableKey = "";
        public string audioBank = "";

        [Header("Grid")]
        public int reelCount = 3;
        public int rowCount = 3;

        [Header("Symbols")]
        public List<SymbolDefinition> symbols = new List<SymbolDefinition>();

        [Header("Reel strips")]
        [Tooltip("One entry per reel. Each is a list of symbol ids in stop order.")]
        // ReelStrip wrapper, not int[][] and not List<List<int>>. Unity's serializer
        // supports neither jagged arrays nor nested generic containers: both are silently
        // dropped on save, so the field never reaches the asset and always reads back
        // empty. ReelEngine.Stop then indexes a zero-length list and throws on every
        // spin. This cost a full milestone to find -- do not "simplify" it back.
        public List<ReelStrip> reelStrips = new List<ReelStrip>();

        [Header("Paylines")]
        public PayType payType = PayType.Lines;
        public List<PaylineDefinition> paylines = new List<PaylineDefinition>();

        [Header("Economy")]
        public long minBet = 100;
        public long maxBet = 5000;
        public int betLevels = 6;
        [Range(0.80f, 1.00f)]
        public float targetRtp = 0.955f;
        public int maxWinMultiplier = 2500;
        public bool progressive = false;
        public long progressiveSeed = 250000;

        [Header("Features")]
        public bool hasFreeSpins = false;
        public int freeSpinScatterCount = 3;
        public int freeSpinAward = 10;
        [Tooltip("Multiplier applied to the base bet for a free-spin award.")]
        public int freeSpinBetMultiplier = 1;

        public bool hasCascade = false;
        public int cascadeClusterSize = 5;

        public bool hasHoldAndSpin = false;
        public int holdSpinInitialLocked = 3;
        public int holdSpinMaxRespins = 10;

        public bool hasBonusBuy = false;
        [Tooltip("Cost as a multiple of the base bet.")]
        public int bonusBuyCostMultiplier = 80;

        [Header("Presentation")]
        public float spinDurationSeconds = 1.6f;
        public float anticipationSeconds = 0.9f;
        public long[] winCelebrationThresholds = { 10, 50, 200, 1000, 5000 };

        // ---- derived helpers --------------------------------------------------

        public long BetForLevel(int levelIndex)
        {
            if (betLevels <= 1) return minBet;
            int steps = betLevels - 1;
            double t = (double)Mathf.Clamp(levelIndex, 0, steps) / steps;
            // Round to a "round number" (plan §4.3 rule 5) by stepping through clean values.
            long raw = (long)Math.Round(minBet + (maxBet - minBet) * t);
            return RoundToNiceNumber(raw, minBet, maxBet);
        }

        private static long RoundToNiceNumber(long value, long min, long max)
        {
            if (value <= min) return min;
            if (value >= max) return max;

            // Largest power of ten at or below value.
            long magnitude = 1;
            while (magnitude * 10 <= value) magnitude *= 10;

            // Floor onto a 1/2/5 step so every rung is a round number (§4.3 rule 5).
            foreach (long step in new long[] { 1, 2, 5 })
            {
                long candidate = (value / (magnitude * step)) * (magnitude * step);
                if (candidate >= min && candidate <= max && candidate > 0) return candidate;
            }

            return Math.Clamp(value, min, max);
        }

        public SymbolDefinition SymbolById(int symbolId)
        {
            for (int i = 0; i < symbols.Count; i++)
                if (symbols[i] != null && symbols[i].id == symbolId) return symbols[i];
            return null;
        }

        public int SymbolIndexById(int symbolId)
        {
            for (int i = 0; i < symbols.Count; i++)
                if (symbols[i] != null && symbols[i].id == symbolId) return i;
            return -1;
        }

        public bool IsValid(out string error)
        {
            error = null;

            if (string.IsNullOrWhiteSpace(id)) { error = "id is required (it is the save-data key)."; return false; }
            if (symbols == null || symbols.Count == 0) { error = "No symbols in paytable."; return false; }
            if (reelStrips == null || reelStrips.Count != reelCount)
            {
                error = $"reelStrips has {reelStrips?.Count ?? 0} entries but reelCount is {reelCount}.";
                return false;
            }
            if (minBet <= 0 || maxBet < minBet)
            {
                error = "Bet range is invalid (minBet must be > 0 and <= maxBet).";
                return false;
            }
            if (minBet % 100 != 0)
            {
                error = "minBet must be a round number (§4.3 rule 5).";
                return false;
            }

            for (int r = 0; r < reelCount; r++)
            {
                var reel = reelStrips[r];
                var strip = reel?.symbols;
                if (strip == null || strip.Count == 0)
                {
                    error = $"Reel {r} has an empty strip.";
                    return false;
                }
                for (int i = 0; i < strip.Count; i++)
                {
                    if (SymbolById(strip[i]) == null)
                    {
                        error = $"Reel {r} position {i} references unknown symbol id {strip[i]}.";
                        return false;
                    }
                }
            }

            if (payType == PayType.Lines)
            {
                if (paylines == null || paylines.Count == 0)
                {
                    error = "Paytype is Lines but no paylines are defined.";
                    return false;
                }
                for (int i = 0; i < paylines.Count; i++)
                {
                    var line = paylines[i];
                    if (line?.rows == null || line.rows.Length != reelCount)
                    {
                        error = $"Payline {i} must have exactly {reelCount} row indices.";
                        return false;
                    }
                    for (int r = 0; r < line.rows.Length; r++)
                    {
                        if (line.rows[r] < 0 || line.rows[r] >= rowCount)
                        {
                            error = $"Payline {i} references row {line.rows[r]} outside 0..{rowCount - 1}.";
                            return false;
                        }
                    }
                }
            }
            else if (payType == PayType.Cluster)
            {
                // Cluster pays need a usable cluster size, and no paylines -- leftover
                // paylines on a cluster game mean the wrong maths is silently running.
                if (cascadeClusterSize < 3)
                {
                    error = "Cluster pays need cascadeClusterSize of at least 3.";
                    return false;
                }

                if (paylines != null && paylines.Count > 0)
                {
                    error = "Cluster pays have no paylines; clear them or the payline maths will run instead.";
                    return false;
                }

                bool anyClusterPay = false;
                for (int i = 0; i < symbols.Count && !anyClusterPay; i++)
                {
                    var s = symbols[i];
                    if (s == null || s.clusterPayouts == null) continue;
                    for (int k = 0; k < s.clusterPayouts.Length; k++)
                        if (s.clusterPayouts[k] > 0) { anyClusterPay = true; break; }
                }

                if (!anyClusterPay)
                {
                    error = "Cluster pays need at least one symbol with a non-zero " +
                            "clusterPayouts entry, otherwise every cluster pays zero.";
                    return false;
                }
            }
            else if (payType == PayType.Ways && paylines != null && paylines.Count > 0)
            {
                error = "Ways pays has no paylines; clear them or the payline maths will run instead.";
                return false;
            }

            // Every payType needs at least one symbol that can actually pay. Ways with a
            // single-tier paytable, or a paytable with no paying symbol, silently returns 0.
            bool anyPaying = false;
            for (int i = 0; i < symbols.Count; i++)
            {
                var s = symbols[i];
                if (s == null || s.isScatter) continue;
                var pays = s.payoutsByMatch;
                if (pays == null) continue;
                for (int k = 0; k < pays.Length; k++)
                    if (pays[k] > 0) { anyPaying = true; break; }
                if (anyPaying) break;
            }

            if (!anyPaying)
            {
                error = "No symbol pays anything; every spin would return zero.";
                return false;
            }

            return true;
        }
    }
}