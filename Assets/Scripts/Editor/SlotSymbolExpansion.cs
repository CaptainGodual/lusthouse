#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using LustHouse.Core;
using LustHouse.Slots;
using UnityEditor;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Adds symbols to a game that has room for them, puts them on the reels, and puts the
    /// maths back where the game says it is.
    ///
    /// The three steps have to travel together, and a tool that does only one of them is
    /// worse than no tool at all:
    ///
    /// <list type="bullet">
    /// <item>A paytable entry no reel can reach is dead weight that still looks live in
    /// the paytable — <c>SlotContentTests</c> fails it.</item>
    /// <item>Reel stops added without retuning move the exact RTP away from the declared
    /// target, and the ship gate fails the build.</item>
    /// <item>Retuning without re-deriving <c>maxWinMultiplier</c> leaves the honest cap
    /// below what the reels can now actually pay.</item>
    /// </list>
    ///
    /// So this does the whole job, and the two generic halves are exposed as their own
    /// menu items for the next game that needs them.
    /// </summary>
    public static class SlotSymbolExpansion
    {
        private const string ArtFolder = ContentBootstrap.ArtFolder;

        /// <summary>Close enough to the ship gate's ±0.5% that rounding cannot push it out.</summary>
        private const double TightTolerance = 0.0005;

        /// <summary>Residual this small is beneath what one unit of payout can resolve.</summary>
        private const double ResidualFloor = 0.0002;

        /// <summary>Unit steps allowed while closing the gap between measured and target.</summary>
        private const int MaxTotalNudges = 400;

        // ------------------------------------------------------------------ content --

        /// <summary>One symbol to add: identity, payout shape and the placeholder's look.</summary>
        private sealed class NewSymbol
        {
            public int id;
            public string symbolName;
            public long[] payouts;
            public Color tint;
        }

        /// <summary>
        /// Velvet Room's six extra symbols.
        ///
        /// Ids start at 18 because 17 is the shared hold &amp; spin Diamond: a game that
        /// grows a hold &amp; spin feature later must not find the id already taken by a
        /// fruit. Payouts are a starting shape, not a tuned figure — the retune scales
        /// them with everything else so the ladder keeps its shape.
        ///
        /// The two ranks extend the ladder downward (A K Q J 10 is complete, so the only
        /// room left is below Ten), and the four fruits fill the gap the game already had
        /// between Grape at the bottom and Mango at the top.
        /// </summary>
        private static readonly NewSymbol[] VelvetRoomAdditions =
        {
            new NewSymbol { id = 18, symbolName = "Nine",   payouts = new long[] { 1, 3,  8 },
                            tint = ColorFromHex(0x8C, 0x9A, 0xB8) },
            new NewSymbol { id = 19, symbolName = "Eight",  payouts = new long[] { 1, 2,  6 },
                            tint = ColorFromHex(0xA8, 0x8C, 0xB8) },
            new NewSymbol { id = 20, symbolName = "Cherry", payouts = new long[] { 1, 3, 10 },
                            tint = ColorFromHex(0xC7, 0x4C, 0x63) },
            new NewSymbol { id = 21, symbolName = "Lemon",  payouts = new long[] { 2, 4, 12 },
                            tint = ColorFromHex(0xD9, 0xBF, 0x4A) },
            new NewSymbol { id = 22, symbolName = "Orange", payouts = new long[] { 2, 6, 18 },
                            tint = ColorFromHex(0xD9, 0x8A, 0x3E) },
            new NewSymbol { id = 23, symbolName = "Plum",   payouts = new long[] { 3, 8, 24 },
                            tint = ColorFromHex(0x96, 0x5C, 0x8C) }
        };

        private static Color ColorFromHex(byte r, byte g, byte b) =>
            new Color32(r, g, b, 0xFF);

        /// <summary>
        /// Velvet Room's Bonus symbol: the trigger for the pick-a-prize round, and a symbol
        /// that pays nothing on any line.
        ///
        /// Id 2 — the shared set uses 0/1 and 10-17, so the low ids are free keys. Zero
        /// payouts are the point, not a placeholder: the round is the only way it pays, and
        /// a trigger symbol that also paid on lines would double-count its own feature in
        /// the RTP. It is a normal, reachable symbol (not special or scatter) so it blocks
        /// lines like any other and the strips above are honest about where it sits.
        /// </summary>
        private static readonly NewSymbol VelvetRoomBonus = new NewSymbol
        {
            id = 2,
            symbolName = "Bonus",
            payouts = new long[] { 0, 0, 0 },
            tint = ColorFromHex(0xE2, 0xB5, 0x4A)
        };

        /// <summary>
        /// Velvet Room's pick-a-prize round, as designed: three triggers open twelve hidden
        /// options, the player takes three, and the prizes are weighted toward the small
        /// end so the round feels generous without carrying the whole RTP.
        ///
        /// Weighted mean is 3.30 bet-multiples, so a trigger pays 9.9 on average; the
        /// trigger lands on roughly 1 spin in 188, which is about 5.5% of the return. The
        /// retune scales the paytable against target minus that constant.
        /// </summary>
        private static BonusGameFeature VelvetRoomBonusFeature() => new BonusGameFeature
        {
            id = "bonus_game",
            displayName = "Pick a Prize",
            kind = BonusKind.PickPrize,
            triggerSymbolId = 2,
            triggerCount = 3,
            choices = 12,
            picks = 3,
            interactive = true,
            prizes = new List<BonusPrize>
            {
                new BonusPrize { payout = 1,  weight = 40f },
                new BonusPrize { payout = 2,  weight = 25f },
                new BonusPrize { payout = 3,  weight = 15f },
                new BonusPrize { payout = 5,  weight = 10f },
                new BonusPrize { payout = 10, weight = 7f  },
                new BonusPrize { payout = 25, weight = 3f  }
            }
        };

        // ------------------------------------------------------------------ menu -----

        [MenuItem("LustHouse/Content/Add six symbols to Velvet Room", priority = 50)]
        public static void ExtendVelvetRoom()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);
            if (def == null)
            {
                LustLog.Error($"[LustHouse] Velvet Room not found at {ContentBootstrap.VelvetRoomPath}.");
                return;
            }

            // Reset to the authored shapes and the original stops first, so re-running this
            // gives the same answer instead of scaling an already-scaled paytable on top of
            // itself.
            ResetToAuthoredBaselines(def);

            int added = AddSymbols(def, VelvetRoomAdditions);
            int placed = PlaceOffStripSymbols(def);

            string report;
            bool tuned = RetuneToDeclaredTarget(def, out report);
            ReconcileMaxWin(def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();

            string status = def.IsValid(out var error) ? "valid" : $"INVALID ({error})";
            LustLog.Info($"[LustHouse] Velvet Room: +{added} symbol(s), +{placed} reel stop(s) " +
                         $"per reel, {def.reelStrips[0].symbols.Count} stops, max win " +
                         $"{def.maxWinMultiplier}x, {status}. {report}");

            if (!tuned)
                LustLog.Error("[LustHouse] Velvet Room did not land on its declared target " +
                              "RTP — it is no longer ship-gated. " + report);
        }

        /// <summary>
        /// Gives Velvet Room its pick-a-prize bonus: the Bonus symbol, one stop of it per
        /// reel (reels 1-4, baked into the authored strip table), and the feature that
        /// pays when three of them land. Then retunes the paytable against the target
        /// minus the bonus's own expected contribution, and reconciles the max-win cap.
        ///
        /// Everything here is idempotent — re-running restores the authored baselines and
        /// measures again rather than compounding the last run's scale — so it is the
        /// menu to reach for after any strip or prize-table edit.
        /// </summary>
        [MenuItem("LustHouse/Content/Author Velvet Room bonus game", priority = 54)]
        public static void AuthorVelvetRoomBonus()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);
            if (def == null)
            {
                LustLog.Error($"[LustHouse] Velvet Room not found at {ContentBootstrap.VelvetRoomPath}.");
                return;
            }

            // Back to authored shapes and strips first — the strip table already carries
            // the Bonus stops, so a reset restores them rather than stacking a second set.
            ResetToAuthoredBaselines(def);

            AddSymbols(def, new[] { VelvetRoomBonus });

            // Features are data: the game carries its own bonus definition, and this only
            // adds it if the asset has none yet. Editing the prizes later is an asset
            // edit, not a code change.
            if (def.features == null) def.features = new List<SlotFeature>();
            if (def.Find<BonusGameFeature>() == null)
                def.features.Add(VelvetRoomBonusFeature());

            string report;
            bool tuned = RetuneToDeclaredTarget(def, out report);
            ReconcileMaxWin(def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string status = def.IsValid(out var error) ? "valid" : $"INVALID ({error})";
            var bonus = def.Find<BonusGameFeature>();
            LustLog.Info($"[LustHouse] Velvet Room bonus authored: trigger " +
                         $"{bonus?.triggerCount}x id {bonus?.triggerSymbolId}, " +
                         $"{bonus?.choices} choices / {bonus?.picks} picks, max win " +
                         $"{def.maxWinMultiplier}x, {status}. {report}");

            if (!tuned)
                LustLog.Error("[LustHouse] Velvet Room did not land on its declared target " +
                              "RTP — it is no longer ship-gated. " + report);
        }

        /// <summary>
        /// Gives Velvet Room its 2x2 mega block on Mango: the symbol opts in with a
        /// multiplier, and every exact 2x2 of Mango pays four-of-a-kind x that multiplier
        /// on top of the lines. Then retunes, since the block's RTP is linear in the same
        /// paytable the retune scales.
        ///
        /// The multiplier is the whole design knob. Mango sits one stop per reel on reels
        /// 0-1 and one adjacent pair on reels 2-4, so blocks can only form on the reel
        /// pairs (2,3) and (3,4): about one per 180 spins. At 0.15 the feature carries
        /// roughly four percentage points of RTP pre-retune — a real win without turning
        /// the paytable into the block's appendix. Idempotent like the others: re-running
        /// restores the authored baselines and re-applies the same multiplier.
        /// </summary>
        [MenuItem("LustHouse/Content/Author Velvet Room mega block", priority = 55)]
        public static void AuthorVelvetRoomMegaBlock()
        {
            const int mangoId = 16;
            const float multiplier = 0.15f;

            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);
            if (def == null)
            {
                LustLog.Error($"[LustHouse] Velvet Room not found at {ContentBootstrap.VelvetRoomPath}.");
                return;
            }

            // Back to authored shapes and strips first — the strips already carry the
            // adjacent Mango pairs a block needs, so a reset restores them.
            ResetToAuthoredBaselines(def);

            var mango = def.SymbolById(mangoId);
            if (mango == null)
            {
                LustLog.Error("[LustHouse] Velvet Room has no Mango (id 16) to opt into blocks.");
                return;
            }
            mango.megaBlockMultiplier = multiplier;

            string report;
            bool tuned = RetuneToDeclaredTarget(def, out report);
            ReconcileMaxWin(def);

            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string status = def.IsValid(out var error) ? "valid" : $"INVALID ({error})";
            double blocks = SimulationEngine.BlockRtp(def);
            LustLog.Info($"[LustHouse] Velvet Room mega block authored: {mango.symbolName} " +
                         $"x{multiplier}, block RTP {blocks:P3}, max win {def.maxWinMultiplier}x, " +
                         $"{status}. {report}");

            if (!tuned)
                LustLog.Error("[LustHouse] Velvet Room did not land on its declared target " +
                              "RTP — it is no longer ship-gated. " + report);
        }

        /// <summary>
        /// Puts the paytable and the strips back to the shape the designer authored, so the
        /// retune always starts from the same place.
        ///
        /// Without this the menu is only safe to run once: every later run would scale a
        /// paytable that had already been scaled, and leave the new symbols' stops on the
        /// reels on top of the ones it was about to place.
        /// </summary>
        private static void ResetToAuthoredBaselines(SlotMachineDefinition def)
        {
            foreach (var symbol in def.symbols)
            {
                if (symbol?.payoutsByMatch == null) continue;

                long[] shape = AuthoredShape(symbol.id, symbol.payoutsByMatch.Length);
                if (shape == null) continue;

                if (shape.Length != symbol.payoutsByMatch.Length)
                    Array.Resize(ref shape, symbol.payoutsByMatch.Length);

                symbol.payoutsByMatch = shape;
            }

            // Back to the authored stops. Removing the new symbols' stops is not enough on
            // its own: placing them replaces a donor rather than inserting, so a strip that
            // only had the new symbols taken off would shrink a little on every run.
            //
            // The authored shape is per-reel (24/25/26/27/28), so each reel resets to its
            // own table entry rather than to one shared strip. A reel with no authored
            // entry — a definition with more reels than the table — falls back to the first
            // entry rather than being left empty, because an empty strip throws in
            // ReelEngine.Stop the first time the game spins.
            var authored = ReelStripAuthoring.VelvetRoomStrips;
            for (int r = 0; r < def.reelStrips.Count; r++)
            {
                var reel = def.reelStrips[r];
                if (reel == null) continue;

                var table = authored[Math.Min(r, authored.Length - 1)];
                reel.symbols = new List<int>(table);
            }
        }

        /// <summary>The payout shape a symbol was authored with, before any retune.</summary>
        private static long[] AuthoredShape(int symbolId, int length)
        {
            long[] shape;
            switch (symbolId)
            {
                case 0:  shape = new long[] { 12, 36, 90 }; break;   // Wild
                case 1:  shape = new long[] {  0,  0,  0 }; break;   // Scatter
                case 10: shape = new long[] {  3, 12, 36 }; break;   // Ace
                case 11: shape = new long[] {  2,  9, 30 }; break;   // King
                case 12: shape = new long[] {  2,  7, 24 }; break;   // Queen
                case 13: shape = new long[] {  1,  5, 15 }; break;   // Jack
                case 14: shape = new long[] {  1,  3,  9 }; break;   // Ten
                case 15: shape = new long[] {  1,  2,  7 }; break;   // Grape
                case 16: shape = new long[] { 12, 48, 150 }; break;  // Mango
                default:
                {
                    foreach (var addition in VelvetRoomAdditions)
                        if (addition.id == symbolId) return (long[])addition.payouts.Clone();
                    return null;
                }
            }

            return shape.Length == length ? shape : (long[])shape.Clone();
        }

        /// <summary>
        /// Puts every paytable symbol that no reel carries onto every reel of the selected
        /// games, one stop each.
        ///
        /// One stop per reel rather than a share of the strip: a symbol a designer has just
        /// added has no measured weight yet, and giving it a lot of stops at once swings
        /// the RTP further than a retune can comfortably absorb. Raising its frequency is
        /// then a strip edit followed by another retune, both one click.
        /// </summary>
        [MenuItem("LustHouse/Content/Place off-strip symbols on reels", priority = 51)]
        public static void PlaceOffStripSymbolsMenu()
        {
            var defs = SelectedDefinitions();
            if (defs.Count == 0)
            {
                LustLog.Warn("[LustHouse] Select one or more slot definitions in the Project " +
                             "window first.");
                return;
            }

            foreach (var def in defs)
            {
                int placed = PlaceOffStripSymbols(def);
                if (placed == 0)
                {
                    LustLog.Info($"[LustHouse] {def.id}: every paytable symbol is already on a reel.");
                    continue;
                }

                EditorUtility.SetDirty(def);
                LustLog.Info($"[LustHouse] {def.id}: placed {placed} symbol(s), one stop per " +
                             $"reel; strips are now {def.reelStrips[0].symbols.Count} stops.");
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Scales a game's paytable until its exact RTP lands back on its declared target.
        ///
        /// A uniform scale is used rather than solving the new symbols alone, because that
        /// is what keeps the ladder intact: every symbol moves by the same proportion, so
        /// the tiers the designer chose survive the retune. Solving only the new symbols
        /// would have to make them pay more than the Ace to cover the stops taken from the
        /// old ones.
        /// </summary>
        [MenuItem("LustHouse/Content/Retune paytable to declared target RTP", priority = 52)]
        public static void RetuneMenu()
        {
            var defs = SelectedDefinitions();
            if (defs.Count == 0)
            {
                LustLog.Warn("[LustHouse] Select one or more slot definitions in the Project " +
                             "window first.");
                return;
            }

            bool allOk = true;
            foreach (var def in defs)
            {
                string report;
                bool ok = RetuneToDeclaredTarget(def, out report);
                if (ok) ReconcileMaxWin(def);
                allOk &= ok;
                LustLog.Info($"[LustHouse] {report}");
                EditorUtility.SetDirty(def);
            }

            AssetDatabase.SaveAssets();

            if (!allOk)
                LustLog.Error("[LustHouse] At least one game is outside the ship gate's ±0.5%. " +
                              "Its maths is not signed off until that is fixed.");
        }

        private static List<SlotMachineDefinition> SelectedDefinitions()
        {
            var found = new List<SlotMachineDefinition>();
            foreach (var obj in Selection.objects)
                if (obj is SlotMachineDefinition def) found.Add(def);
            return found;
        }

        // ------------------------------------------------------------- paytable ------

        /// <summary>Adds the symbols that are not already present. Idempotent.</summary>
        private static int AddSymbols(SlotMachineDefinition def, IEnumerable<NewSymbol> additions)
        {
            if (def.symbols == null) def.symbols = new List<SymbolDefinition>();

            int added = 0;
            foreach (var spec in additions)
            {
                if (def.SymbolById(spec.id) == null)
                {
                    def.symbols.Add(new SymbolDefinition
                    {
                        id = spec.id,
                        symbolName = spec.symbolName,
                        payoutsByMatch = (long[])spec.payouts.Clone(),
                        animatesOnWin = true,
                        winAnimationFramesPerSecond = 12f,
                        winAnimationSeconds = 0.9f
                    });
                    added++;
                }

                // Re-attach art even for a symbol that is already present. Skipping it on
                // the grounds that the symbol exists would leave a dangling reference
                // whenever the artwork is moved or deleted — which is exactly when this
                // menu is the thing to reach for.
                var symbol = def.SymbolById(spec.id);
                if (symbol != null && symbol.sprite == null)
                    symbol.sprite = EnsurePlaceholderArt(spec);
            }

            return added;
        }

        // ------------------------------------------------------------------- reels ---

        /// <summary>
        /// Gives each paytable symbol no reel carries one stop on every reel, taking that
        /// stop from the most common paying symbol on that reel.
        ///
        /// The strip stays the same length, which matters more than it looks: reel
        /// probability is a three-to-five power in the RTP, so lengthening the strip to
        /// make room costs roughly a third of the game's return, and the paytable then has
        /// to be inflated by half again to get it back. Giving the new symbol a stop that
        /// already existed costs a fraction of that.
        ///
        /// The Wild and the Scatter are never donors. Neither is touched here even though
        /// a Scatter stop buys nothing in a game with no free spins — how often the feature
        /// symbol turns up is a design decision, not a spare slot.
        /// </summary>
        public static int PlaceOffStripSymbols(SlotMachineDefinition def)
        {
            if (def == null || def.reelStrips == null || def.symbols == null) return 0;

            var onStrips = new HashSet<int>();
            foreach (var reel in def.reelStrips)
            {
                if (reel?.symbols == null) continue;
                foreach (int id in reel.symbols) onStrips.Add(id);
            }

            var missing = new List<int>();
            foreach (var symbol in def.symbols)
            {
                if (symbol == null) continue;

                // Wilds, scatters and specials may legitimately sit off-strip: a feature
                // symbol belongs to a mode that does not run on every spin.
                if (symbol.isWild || symbol.isScatter || symbol.isSpecial) continue;

                if (!onStrips.Contains(symbol.id)) missing.Add(symbol.id);
            }

            if (missing.Count == 0) return 0;

            foreach (var reel in def.reelStrips)
            {
                var strip = reel?.symbols;
                if (strip == null) continue;

                foreach (int newId in missing)
                {
                    int donor = HeaviestPayingStop(strip, def);
                    if (donor < 0) break;

                    int index = strip.IndexOf(donor);
                    if (index < 0) break;

                    strip[index] = newId;
                }
            }

            return missing.Count;
        }

        /// <summary>
        /// The paying symbol with the most stops on this reel. Recounted after every
        /// replacement, so the stops come off the fattest symbol repeatedly rather than
        /// stripping several at once from the same one.
        /// </summary>
        private static int HeaviestPayingStop(List<int> strip, SlotMachineDefinition def)
        {
            var counts = new Dictionary<int, int>();
            foreach (int id in strip)
            {
                counts.TryGetValue(id, out int c);
                counts[id] = c + 1;
            }

            int bestId = -1, bestCount = -1;
            foreach (var symbol in def.symbols)
            {
                if (symbol == null || symbol.isWild || symbol.isScatter || symbol.isSpecial)
                    continue;

                // A trigger symbol's stop is not spare capacity: taking it would lower the
                // feature's trigger rate behind the design's back, which no retune repairs.
                if (symbol.payoutsByMatch != null)
                {
                    bool pays = false;
                    for (int k = 0; k < symbol.payoutsByMatch.Length; k++)
                        if (symbol.payoutsByMatch[k] > 0) { pays = true; break; }
                    if (!pays) continue;
                }

                if (!counts.TryGetValue(symbol.id, out int c) || c <= bestCount) continue;
                bestCount = c;
                bestId = symbol.id;
            }

            return bestId;
        }

        // -------------------------------------------------------------------- maths --

        /// <summary>
        /// One (symbol, match count) entry's share of the RTP, measured by giving it a
        /// payout of one and everything else nothing.
        ///
        /// The measurement rather than a closed form is deliberate: this mirrors the engine
        /// exactly, so wild substitution and the leftmost-match rule are whatever
        /// <c>PaylineEvaluator</c> says they are. RTP is linear in the payouts — the
        /// probabilities that decide a win do not depend on what it pays — so these weights
        /// let the retune compute a new RTP without re-enumerating it every time.
        /// </summary>
        private struct Weight
        {
            public int symbolIndex;
            public int matchIndex;
            public double w;
        }

        private static List<Weight> MeasureWeights(SlotMachineDefinition def, out double baseline)
        {
            var saved = new long[def.symbols.Count][];
            for (int i = 0; i < def.symbols.Count; i++)
                saved[i] = def.symbols[i]?.payoutsByMatch != null
                    ? (long[])def.symbols[i].payoutsByMatch.Clone()
                    : new long[0];

            var weights = new List<Weight>();

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                // A scatter pays on no line, so its weight is zero however it is scored.
                if (symbol == null || symbol.isScatter || symbol.payoutsByMatch == null) continue;

                // A trigger-only symbol — the bonus, say — has no positive payout on purpose: it
                // pays through its feature, not on a line. Measuring it anyway would give it a
                // non-zero weight, and the nudge walk below would then walk its zero up to one
                // and quietly turn it into a paying symbol, inflating the RTP it was never meant
                // to contribute to. Skip any symbol that pays nothing anywhere.
                //
                // Read the saved copy, not the live arrays: the k-loop below leaves every
                // symbol's payouts cleared after it finishes measuring the previous symbol, so
                // by this point the live arrays read as all-zero and every symbol after the
                // first would be skipped — which is exactly the bug this check caused when it
                // read symbol.payoutsByMatch directly.
                bool paysAnything = false;
                for (int k = 0; k < saved[i].Length; k++)
                    if (saved[i][k] > 0) { paysAnything = true; break; }
                if (!paysAnything) continue;

                for (int k = 0; k < symbol.payoutsByMatch.Length; k++)
                {
                    for (int j = 0; j < def.symbols.Count; j++)
                    {
                        var other = def.symbols[j]?.payoutsByMatch;
                        if (other != null) Array.Clear(other, 0, other.Length);
                    }

                    symbol.payoutsByMatch[k] = 1;
                    // Line only: the bonus term is a constant, so including it would add it
                    // to every weight and the retune would count it once per paytable entry.
                    double w = SimulationEngine.LineRtp(def);
                    if (w > 0) weights.Add(new Weight { symbolIndex = i, matchIndex = k, w = w });
                    symbol.payoutsByMatch[k] = 0;
                }
            }

            for (int i = 0; i < def.symbols.Count; i++)
                def.symbols[i].payoutsByMatch = saved[i];

            baseline = SimulationEngine.LineRtp(def);
            return weights;
        }

        private static double SumWeights(List<Weight> weights, SlotMachineDefinition def)
        {
            double total = 0;
            foreach (var weight in weights)
                total += weight.w * def.symbols[weight.symbolIndex].payoutsByMatch[weight.matchIndex];
            return total;
        }

        /// <summary>
        /// A bigger match must never out-pay a smaller one: a four-of-a-kind that pays less
        /// than a three-of-a-kind reads as a bug to the player, and is one in the paytable.
        /// </summary>
        private static bool KeepsLadder(SymbolDefinition symbol, int index, long candidate)
        {
            var payouts = symbol.payoutsByMatch;
            if (index > 0 && candidate < payouts[index - 1]) return false;
            if (index + 1 < payouts.Length && candidate > payouts[index + 1]) return false;
            return true;
        }

        /// <summary>
        /// Scales every payout by the ratio of declared target to measured RTP, then walks
        /// unit steps to absorb the damage the rounding did.
        ///
        /// The walk is not a nicety. RTP is linear in the payouts, so scaling every one of
        /// them by the same ratio gets the expectation exactly on target — and then
        /// rounding each back to a whole number of bet-multiples throws that away, because
        /// a four-of-a-kind weight on a common symbol is thousands of times coarser than a
        /// five-of-a-kind weight on a rare one. On this content that rounding cost seven
        /// and a half percentage points of RTP, which is fifteen times the gate's tolerance.
        /// So the gap is closed one unit at a time on whichever entry moves it most, and
        /// stops the moment it is finer than a unit of payout.
        /// </summary>
        public static bool RetuneToDeclaredTarget(SlotMachineDefinition def, out string report)
        {
            report = $"{def?.id}: ";

            if (def == null) { report += "no definition."; return false; }

            if (!SimulationEngine.SupportsExactRtp(def))
            {
                report += $"no closed-form RTP for {def.payType}; retune by hand.";
                return false;
            }

            double target = def.targetRtp;
            var weights = MeasureWeights(def, out double baseline);

            if (weights.Count == 0 || baseline <= 0)
            {
                report += "nothing in the paytable pays, so there is no RTP to scale.";
                return false;
            }

            // The bonus round is a constant per spin the paytable does not affect, so the
            // line game alone has to land on target minus that constant. A negative result
            // means the feature alone promises more than the game's declared RTP.
            double bonusTerm = SimulationEngine.BonusRtp(def);
            double progressiveTerm = SimulationEngine.ProgressiveRtp(def);
            double lineTarget = target - bonusTerm - progressiveTerm;

            if (lineTarget <= 0)
            {
                report += $"bonus promises {bonusTerm:P4}, at or above the whole " +
                          $"target {target:P4}; there is nothing left for the paylines.";
                return false;
            }

            double scale = lineTarget / baseline;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0)
            {
                report += $"scale {scale} is not usable (baseline {baseline:P4}).";
                return false;
            }

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var payouts = def.symbols[i]?.payoutsByMatch;
                if (payouts == null) continue;

                for (int k = 0; k < payouts.Length; k++)
                    if (payouts[k] > 0)
                        payouts[k] = Math.Max(1, (long)Math.Round(payouts[k] * scale));
            }

            double running = SumWeights(weights, def);
            double residual = lineTarget - running;

            // Walk the single unit step that closes the gap most, wherever it is. A big
            // weight is the only thing that can move a big residual, and a fine weight is
            // the only thing that can resolve a small one, so letting the residual choose
            // which entry to touch converges in far fewer steps than sweeping the list —
            // and it stops as soon as rounding is beneath what one unit can resolve.
            for (int step = 0; step < MaxTotalNudges && Math.Abs(residual) > ResidualFloor; step++)
            {
                Weight best = default;
                long bestDelta = 0;
                double bestAfter = Math.Abs(residual);
                bool found = false;

                foreach (var weight in weights)
                {
                    var symbol = def.symbols[weight.symbolIndex];

                    for (int delta = -1; delta <= 1; delta += 2)
                    {
                        long next = symbol.payoutsByMatch[weight.matchIndex] + delta;
                        if (next < 1) continue;
                        if (!KeepsLadder(symbol, weight.matchIndex, next)) continue;

                        double after = Math.Abs(residual - weight.w * delta);
                        if (after >= bestAfter - 1e-12) continue;

                        bestAfter = after;
                        best = weight;
                        bestDelta = delta;
                        found = true;
                    }
                }

                if (!found) break;

                var touched = def.symbols[best.symbolIndex];
                long current = touched.payoutsByMatch[best.matchIndex];
                touched.payoutsByMatch[best.matchIndex] = current + bestDelta;
                running += best.w * bestDelta;
                residual = lineTarget - running;
            }

            double exact = SimulationEngine.ExactRtp(def);
            double drift = Math.Abs(exact - target);
            bool ok = drift <= TightTolerance;

            report += $"measured {baseline:P4} -> scaled x{scale:F3} -> " +
                      $"line {exact - bonusTerm - progressiveTerm:P4} + bonus {bonusTerm:P4} + progressive {progressiveTerm:P4} = {exact:P4} " +
                      $"against target {target:P4} (drift {drift:P4}, " +
                      $"{(ok ? "inside" : "OUTSIDE")} ±0.5%).";

            return ok;
        }

        /// <summary>
        /// The honest cap: however many paylines there are, a spin cannot pay more than the
        /// biggest line win times the number of lines. Recomputed after a retune because a
        /// scaled-up paytable can otherwise pay past its own declared ceiling, which the
        /// simulation reports as a breach.
        ///
        /// Public so <see cref="ReelStripAuthoring"/>'s per-reel authoring menu can run it
        /// alongside the retune it triggers — a strip change scales the paytable, and the
        /// cap has to follow that scaling or the gate reports a phantom max-win breach.
        /// </summary>
        public static void ReconcileMaxWin(SlotMachineDefinition def)
        {
            if (def?.paylines == null || def.paylines.Count == 0) return;

            long top = 0;
            foreach (var symbol in def.symbols)
                if (symbol != null)
                    top = Math.Max(top, symbol.PayoutForMatch(def.reelCount));

            // A pick-a-prize round can land on the same spin as the best line win, so the
            // honest cap is both, not either. It is bounded: the round pays the sum of its
            // picks, at most picks x the top prize.
            long bonus = 0;
            var feature = def.Find<BonusGameFeature>();
            if (feature != null && feature.picks > 0)
            {
                long topPrize = 0;
                if (feature.prizes != null)
                    foreach (var prize in feature.prizes)
                        if (prize != null) topPrize = Math.Max(topPrize, prize.payout);
                bonus = feature.picks * topPrize;
            }

            // Mega blocks too. Every 2x2 window on the grid is a block at most once, so the
            // bound is windows x the largest single block pay — again additive, since a
            // block pays on top of whatever the paylines through its cells also won.
            long blocks = 0;
            if (def.reelCount > 1 && def.rowCount > 1)
            {
                long windows = (def.reelCount - 1) * (def.rowCount - 1);
                long topBlock = 0;
                foreach (var symbol in def.symbols)
                    if (symbol != null)
                        topBlock = Math.Max(topBlock,
                            (long)Math.Ceiling(symbol.MegaBlockPayoutExact()));
                blocks = windows * topBlock;
            }

            def.maxWinMultiplier = (int)Math.Max(1, def.paylines.Count * top + bonus + blocks);
        }

        // --------------------------------------------------------------------- art ---

        /// <summary>
        /// The placeholder tile a symbol wears until its artwork lands: a tinted square
        /// with the symbol's first letter on it, matching what the reels draw for a symbol
        /// with no sprite at all.
        ///
        /// It has to be a real sprite rather than a missing one because Velvet Room is
        /// ship-gated, and the gate says every symbol on its reels is arted. A placeholder
        /// is a look, not a hole.
        /// </summary>
        private static Sprite EnsurePlaceholderArt(NewSymbol spec)
        {
            string path = $"{ArtFolder}/{spec.symbolName.ToLowerInvariant()}.png";

            if (!File.Exists(path))
            {
                const int size = 256;
                var tile = BuildPlaceholderTile(spec.tint, Letter(spec.symbolName), size);
                File.WriteAllBytes(path, tile.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tile);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }

            if (AssetImporter.GetAtPath(path) is TextureImporter importer &&
                importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                LustLog.Warn($"[LustHouse] Placeholder art at {path} did not import as a " +
                             "sprite; the symbol will draw as a flat tile.");

            return sprite;
        }

        private static char Letter(string name) =>
            string.IsNullOrEmpty(name) ? '?' : char.ToUpperInvariant(name[0]);

        private static Texture2D BuildPlaceholderTile(Color tint, char letter, int size)
        {
            var pixels = new Color32[size * size];

            var fill = (Color32)tint;
            var edge = (Color32)(tint * 0.55f);
            const int border = 10;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool onEdge = x < border || x >= size - border ||
                                  y < border || y >= size - border;
                    pixels[y * size + x] = onEdge ? edge : fill;
                }
            }

            DrawGlyph(pixels, size, letter, new Color32(255, 255, 255, 255));

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Draws one character onto the tile from a five-by-seven bitmap font.
        ///
        /// A bitmap rather than the editor's own text because the editor has none in a
        /// batch run: GUI text needs a GUI context, and this runs headless. The font is a
        /// table of bytes, so the letter is the same on a workstation as it is on a build
        /// machine — and the placeholder never depends on which font a machine happens to
        /// have installed.
        /// </summary>
        private static void DrawGlyph(Color32[] pixels, int size, char letter, Color32 ink)
        {
            if (!TinyFont.TryGetValue(letter, out var rows)) return;

            const int glyphWidth = 5;
            const int glyphHeight = 7;

            int scale = Mathf.Max(1, size / 14);
            int ox = (size - glyphWidth * scale) / 2;
            int oy = (size - glyphHeight * scale) / 2;

            for (int row = 0; row < glyphHeight; row++)
            {
                for (int col = 0; col < glyphWidth; col++)
                {
                    if (((rows[row] >> (glyphWidth - 1 - col)) & 1) == 0) continue;

                    for (int sy = 0; sy < scale; sy++)
                    {
                        // Rows are drawn top-down; pixel rows run bottom-up.
                        int py = oy + (glyphHeight - 1 - row) * scale + sy;
                        for (int sx = 0; sx < scale; sx++)
                            pixels[py * size + ox + col * scale + sx] = ink;
                    }
                }
            }
        }

        /// <summary>A five-by-seven bitmap font: one byte per row, most significant bit left.</summary>
        private static readonly Dictionary<char, byte[]> TinyFont = new Dictionary<char, byte[]>
        {
            ['A'] = new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 },
            ['B'] = new byte[] { 0x1E, 0x11, 0x1E, 0x11, 0x11, 0x11, 0x1E },
            ['C'] = new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E },
            ['D'] = new byte[] { 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E },
            ['E'] = new byte[] { 0x1F, 0x10, 0x1E, 0x10, 0x10, 0x10, 0x1F },
            ['F'] = new byte[] { 0x1F, 0x10, 0x1E, 0x10, 0x10, 0x10, 0x10 },
            ['G'] = new byte[] { 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F },
            ['H'] = new byte[] { 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11, 0x11 },
            ['I'] = new byte[] { 0x0E, 0x04, 0x04, 0x04, 0x04, 0x04, 0x0E },
            ['J'] = new byte[] { 0x07, 0x02, 0x02, 0x02, 0x02, 0x12, 0x0C },
            ['K'] = new byte[] { 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11 },
            ['L'] = new byte[] { 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F },
            ['M'] = new byte[] { 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11 },
            ['N'] = new byte[] { 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11 },
            ['O'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
            ['P'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 },
            ['Q'] = new byte[] { 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D },
            ['R'] = new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11 },
            ['S'] = new byte[] { 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E },
            ['T'] = new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 },
            ['U'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E },
            ['V'] = new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x04 },
            ['W'] = new byte[] { 0x11, 0x11, 0x11, 0x15, 0x15, 0x1B, 0x11 },
            ['X'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11 },
            ['Y'] = new byte[] { 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04 },
            ['Z'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F },
            ['0'] = new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E },
            ['1'] = new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E },
            ['2'] = new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F },
            ['3'] = new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E },
            ['4'] = new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 },
            ['5'] = new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E },
            ['6'] = new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E },
            ['7'] = new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 },
            ['8'] = new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E },
            ['9'] = new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C }
        };
    }
}
#endif
