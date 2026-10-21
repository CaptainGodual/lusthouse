using System;
using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    public enum SlotTemplate { Classic3, Video5, Cascade, HoldAndSpin, Progressive }
    public enum Volatility { Low, Medium, High }
    public enum PayType { Lines, Ways, Cluster }

    /// <summary>Whether a game's maths has been signed off (plan §3.7).</summary>
    public enum RtpStatus
    {
        /// <summary>
        /// The maths is not signed off. The target is a design value, not a claim: the
        /// game still has to validate and be playable, but nothing is asserted about its
        /// RTP beyond that.
        /// </summary>
        Unmeasured,

        /// <summary>
        /// The game claims to be shippable. The RTP gate then asserts the claim holds —
        /// the exact RTP must land within ±0.5% of the target, or the gate fails until
        /// the maths is re-measured.
        /// </summary>
        Measured
    }

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

        [Tooltip("Prize this symbol carries when it locks in a hold & spin feature, in " +
                 "bet-multiples. 0 = no value. Only special symbols (isSpecial) are used.")]
        public long holdSpinValue = 0;

        [Header("Win animation")]
        [Tooltip("Whether this symbol animates when it wins. Turn it off for a symbol whose " +
                 "artwork has nothing to animate: a static emblem that jumps reads as a " +
                 "glitch, not a win.")]
        public bool animatesOnWin = true;

        [Tooltip("The animation played on a winning symbol, if it has one. Leave it empty and " +
                 "the symbol gets the standard pulse.")]
        public AnimationClip winAnimation;

        [Tooltip("The frames of a sprite-sheet animation, in order. This is the simple path: " +
                 "import the sheet as Multiple sprites, then drag the frames straight in " +
                 "here and they play on a win. Leave it empty unless the symbol has its own " +
                 "artwork to animate.")]
        public Sprite[] winAnimationFrames = new Sprite[0];

        [Tooltip("How many frames a second the sprite-sheet animation runs at.")]
        public float winAnimationFramesPerSecond = 12f;

        [Tooltip("How long the win animation runs, in seconds.")]
        public float winAnimationSeconds = 0.9f;

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
        [Tooltip("The smallest bet this game takes. The floor never moves with level.")]
        public long minBet = 1000;

        [Tooltip("The largest bet this game ever takes. The player's level caps what " +
                 "they can actually reach (see BetLimits).")]
        public long maxBet = 2400000;

        [Tooltip("Whether this game's maths has been signed off. An unmeasured game is " +
                 "explicitly deferred: it must validate and be playable, but its target " +
                 "is a design value rather than a claim. The RTP gate checks this field " +
                 "instead of a list of names, so adding a game needs no code change at all " +
                 "(plan §3.7).")]
        public RtpStatus rtpStatus = RtpStatus.Unmeasured;

        [Range(0.80f, 1.00f)]
        public float targetRtp = 0.955f;
        public int maxWinMultiplier = 2500;
        public bool progressive = false;
        public long progressiveSeed = 250000;

        [Header("Features")]
        [Tooltip("Features this game can trigger, in evaluation order. Data, not code — adding a " +
                 "feature to a game is a content change (plan §3.5). The engine dispatches each to " +
                 "its handler by type.")]
        // SerializeReference, not the default inline path: SlotFeature is abstract, and inline
        // serialization of an abstract field writes only the base fields and reads back as an
        // empty entry — silently. A feature authored here would vanish on the next save, the
        // game would quietly stop triggering it, and nothing in the YAML would look wrong.
        [SerializeReference]
        public List<SlotFeature> features = new List<SlotFeature>();

        [Tooltip("Minimum cluster size for PayType.Cluster games (plan §3.2 template C).")]
        public int cascadeClusterSize = 5;

        // ---- legacy feature flags (deprecated) ------------------------------
        // Kept so existing assets keep their behaviour until they are migrated to
        // <see cref="features"/>. The engine honours these only when <see cref="features"/> is
        // empty, and they are dropped from the data model once content is migrated.
        [Tooltip("Deprecated: add a FreeSpinsFeature to features instead.")]
        public bool hasFreeSpins = false;
        public int freeSpinScatterCount = 3;
        public int freeSpinAward = 10;
        [Tooltip("Deprecated: the FreeSpinsFeature betMultiplier.")]
        public int freeSpinBetMultiplier = 1;

        [Tooltip("Deprecated: cluster pays is PayType.Cluster now.")]
        public bool hasCascade = false;

        [Tooltip("Deprecated: add a HoldAndSpinFeature to features instead.")]
        public bool hasHoldAndSpin = false;
        public int holdSpinInitialLocked = 3;
        public int holdSpinMaxRespins = 10;

        [Tooltip("Deprecated: bonus buy is a BonusGameFeature now.")]
        public bool hasBonusBuy = false;
        [Tooltip("Deprecated: the BonusGameFeature cost.")]
        public int bonusBuyCostMultiplier = 80;

        [Header("Presentation")]
        public float spinDurationSeconds = 1.6f;
        public float anticipationSeconds = 0.9f;
        public long[] winCelebrationThresholds = { 10, 50, 200, 1000, 5000 };

        [Tooltip("The symbol whose artwork represents this game in the lobby — its face. " +
                 "0 = the highest-paying symbol. Every game shares one symbol set, so this " +
                 "is what tells two lobby tiles apart until they have real key art.")]
        public int faceSymbolId = 0;

        // ---- derived helpers --------------------------------------------------

        /// <summary>
        /// Every bet this game can take, ignoring who is playing: the house ladder
        /// clipped to the game's own range.
        ///
        /// The maths does not care who is playing, so this is what the RTP simulator
        /// walks. A level cap is a rule about the player, not about the game.
        /// </summary>
        public List<long> AllBetOptions()
        {
            var options = new List<long>();
            foreach (long step in Economy.BetLimits.Ladder)
            {
                if (step < minBet) continue;
                if (step > maxBet) break;
                options.Add(step);
            }

            // A game whose range does not meet the ladder still has to be playable.
            if (options.Count == 0) options.Add(Math.Max(1L, minBet));
            return options;
        }

        /// <summary>
        /// The bets a player of the given level can take: the game's own bets, cut off
        /// at the ceiling that level has unlocked.
        /// </summary>
        public List<long> BetOptions(int playerLevel)
        {
            long ceiling = Math.Min(maxBet, Economy.BetLimits.MaxBetForLevel(playerLevel));

            var options = new List<long>();
            foreach (long step in Economy.BetLimits.Ladder)
            {
                if (step < minBet) continue;
                if (step > ceiling) break;
                options.Add(step);
            }

            if (options.Count == 0) options.Add(Math.Max(1L, minBet));
            return options;
        }

        /// <summary>The largest bet a player of the given level can take here.</summary>
        public long MaxBetFor(int playerLevel)
        {
            var options = BetOptions(playerLevel);
            return options[options.Count - 1];
        }

        /// <summary>The bet at a position in the ladder a player of this level can reach.</summary>
        public long BetForLevel(int levelIndex, int playerLevel)
        {
            var options = BetOptions(playerLevel);
            if (options.Count == 0) return minBet;
            int index = Mathf.Clamp(levelIndex, 0, options.Count - 1);
            return options[index];
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

        /// <summary>
        /// The features this game can trigger. New content populates <see cref="features"/>
        /// directly; the legacy flags are still honoured so existing assets keep their behaviour
        /// until they are migrated.
        /// </summary>
        public List<SlotFeature> GetFeatures()
        {
            if (features != null && features.Count > 0) return features;

            var derived = new List<SlotFeature>();
            if (hasFreeSpins)
                derived.Add(new FreeSpinsFeature
                {
                    id = "free_spins",
                    displayName = "Free Spins",
                    scatterCount = freeSpinScatterCount,
                    award = freeSpinAward,
                    betMultiplier = freeSpinBetMultiplier
                });
            if (hasHoldAndSpin)
                derived.Add(new HoldAndSpinFeature
                {
                    id = "hold_spin",
                    displayName = "Hold & Spin",
                    initialLocked = holdSpinInitialLocked,
                    maxRespins = holdSpinMaxRespins
                });
            return derived;
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