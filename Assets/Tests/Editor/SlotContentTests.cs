using System.Collections.Generic;
using LustHouse.EditorTools;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The content rules for a shipped paytable (plan §3.5 / §3.6).
    ///
    /// A symbol table is data, so nothing stops a designer from giving two symbols the
    /// same id, pointing two symbols at the same artwork, or leaving a symbol that no
    /// reel can ever reach. Each defect is invisible while authoring and fatal later:
    /// a duplicate id makes <c>SymbolById</c> resolve only the first of the two, shared
    /// artwork makes two paytable entries look identical to the player, and an
    /// unreachable symbol is dead weight that still has to be maintained.
    ///
    /// These run over the real definitions on disk rather than a fixture, because the
    /// defect only exists in real content. An art pass is exactly when it shows up.
    /// </summary>
    public class SlotContentTests
    {
        /// <summary>The one game whose maths is ship-gated, so its art set is exact.</summary>
        private const string GatedGame = "velvet_room";

        private static List<SlotMachineDefinition> LoadAll()
        {
            var found = new List<SlotMachineDefinition>();
            foreach (var guid in AssetDatabase.FindAssets("t:SlotMachineDefinition", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(path);
                if (def != null) found.Add(def);
            }
            return found;
        }

        [Test]
        public void EveryDefinition_HasUniqueSymbolIds()
        {
            var problems = new List<string>();

            foreach (var def in LoadAll())
            {
                var seen = new HashSet<int>();
                for (int i = 0; i < def.symbols.Count; i++)
                {
                    var symbol = def.symbols[i];
                    if (symbol == null) continue;

                    if (!seen.Add(symbol.id))
                        problems.Add($"{def.id}: symbol id {symbol.id} is used twice " +
                                     $"(entries '{symbol.symbolName}' and earlier one).");
                }
            }

            Assert.IsEmpty(problems,
                "A duplicated symbol id makes SymbolById resolve only the first entry, so one " +
                "of the two symbols is unreachable on every reel:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EveryDefinition_DoesNotShareOneSpriteBetweenTwoSymbols()
        {
            var problems = new List<string>();

            foreach (var def in LoadAll())
            {
                var owners = new Dictionary<Sprite, string>();
                for (int i = 0; i < def.symbols.Count; i++)
                {
                    var symbol = def.symbols[i];
                    if (symbol == null || symbol.sprite == null) continue;

                    if (owners.TryGetValue(symbol.sprite, out var first))
                        problems.Add($"{def.id}: '{first}' and '{symbol.symbolName}' use the same " +
                                     $"artwork ({symbol.sprite.name}).");
                    else
                        owners[symbol.sprite] = symbol.symbolName;
                }
            }

            Assert.IsEmpty(problems,
                "Two symbols sharing one artwork are indistinguishable on the reel and in the " +
                "paytable:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EveryDefinition_HasNoSymbolTheReelsCanNeverReach()
        {
            var problems = new List<string>();

            foreach (var def in LoadAll())
            {
                var onReels = new HashSet<int>();
                for (int r = 0; r < def.reelStrips.Count; r++)
                {
                    var strip = def.reelStrips[r]?.symbols;
                    if (strip == null) continue;
                    for (int i = 0; i < strip.Count; i++) onReels.Add(strip[i]);
                }

                for (int i = 0; i < def.symbols.Count; i++)
                {
                    var symbol = def.symbols[i];
                    if (symbol == null) continue;

                    // Wilds, scatters and specials are allowed to be off-strip: a feature
                    // symbol can be reserved for a mode that is not on every spin. Anything
                    // else is a paytable entry the player can never see.
                    if (symbol.isWild || symbol.isScatter || symbol.isSpecial) continue;

                    if (!onReels.Contains(symbol.id))
                        problems.Add($"{def.id}: symbol {symbol.id} '{symbol.symbolName}' is in " +
                                     "the paytable but on no reel strip, so it can never appear.");
                }
            }

            Assert.IsEmpty(problems,
                "A symbol no reel can reach is dead weight that still looks live in a " +
                "paytable:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void EverySymbol_HasAName()
        {
            var problems = new List<string>();

            foreach (var def in LoadAll())
            {
                for (int i = 0; i < def.symbols.Count; i++)
                {
                    var symbol = def.symbols[i];
                    if (symbol == null) continue;

                    if (string.IsNullOrWhiteSpace(symbol.symbolName))
                        problems.Add($"{def.id}: symbol {symbol.id} has no name.");
                }
            }

            Assert.IsEmpty(problems,
                "A symbol with no name falls back to a bare id in the paytable and the " +
                "placeholder tile:\n  " + string.Join("\n  ", problems));
        }

        [Test]
        public void TheGatedGame_GivesEverySymbolOnAReelArtAndAName()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);

            Assert.IsNotNull(def, $"Velvet Room missing at {ContentBootstrap.VelvetRoomPath}.");

            var problems = new List<string>();
            var onReels = new HashSet<int>();
            for (int r = 0; r < def.reelStrips.Count; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                if (strip == null) continue;
                for (int i = 0; i < strip.Count; i++) onReels.Add(strip[i]);
            }

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var symbol = def.symbols[i];
                if (symbol == null || !onReels.Contains(symbol.id)) continue;

                if (symbol.sprite == null)
                    problems.Add($"'{symbol.symbolName}' (id {symbol.id}) is on the reels but has " +
                                 "no artwork, so it draws as a placeholder tile.");
            }

            Assert.IsEmpty(problems,
                "The ship-gated game must be fully arted -- a placeholder tile on a live " +
                "symbol is a content bug, not a fallback:\n  " + string.Join("\n  ", problems));
        }

        /// <summary>
        /// Velvet Room's reels are per-reel strips of 20-30 stops, like a commercial slot,
        /// rather than one identical strip copied five times.
        ///
        /// Three things are worth asserting together, because each is invisible on its own.
        /// Length in range: a strip that silently grows back toward the old 51 stops changes
        /// every probability and invalidates the signed-off RTP. Reels differing: five
        /// identical strips would pass a length check while throwing away the whole point of
        /// per-reel weighting. Every symbol reachable: with 20-30 stops it is easy to drop a
        /// symbol while trimming, and a paytable entry no reel carries is a prize the player
        /// can never win -- worse, a symbol missing from reel 0 can never pay at all, since a
        /// line's base is reel 0's cell.
        /// </summary>
        [Test]
        public void VelvetRoom_HasPerReelStripsBetween20And30Stops()
        {
            var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                ContentBootstrap.VelvetRoomPath);

            Assert.IsNotNull(def, $"Velvet Room missing at {ContentBootstrap.VelvetRoomPath}.");
            Assert.IsNotNull(def.reelStrips, "Velvet Room has no reel strips.");

            var problems = new List<string>();
            var shapes = new HashSet<string>();

            for (int r = 0; r < def.reelStrips.Count; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                if (strip == null || strip.Count == 0)
                {
                    problems.Add($"reel {r} has no strip.");
                    continue;
                }

                if (strip.Count < 20 || strip.Count > 30)
                    problems.Add($"reel {r} has {strip.Count} stops; commercial strips run 20-30.");

                shapes.Add(string.Join(",", strip));
            }

            if (problems.Count == 0 && shapes.Count < def.reelStrips.Count)
                problems.Add("every reel carries an identical strip, so the per-reel weighting " +
                             "is not actually doing anything.");

            Assert.IsEmpty(problems,
                "Per-reel strips are the whole basis of the re-tuned maths:\n  " +
                string.Join("\n  ", problems));

            // Every paying symbol has to be on every reel, not merely somewhere: a line's
            // base is reel 0's cell, so a symbol absent from reel 0 can never pay.
            //
            // "Paying" is load-bearing. A trigger-only symbol — the Bonus — carries no line
            // payout and is meant to sit on a subset of reels; requiring it everywhere would
            // force it onto reel 0, where it would block line wins at their most leveraged
            // position and buy nothing.
            var paying = new HashSet<int>();
            foreach (var symbol in def.symbols)
            {
                if (symbol == null || symbol.isWild || symbol.isScatter || symbol.isSpecial)
                    continue;

                bool pays = false;
                if (symbol.payoutsByMatch != null)
                    for (int k = 0; k < symbol.payoutsByMatch.Length; k++)
                        if (symbol.payoutsByMatch[k] > 0) { pays = true; break; }

                if (pays) paying.Add(symbol.id);
            }

            for (int r = 0; r < def.reelStrips.Count; r++)
            {
                var onReel = new HashSet<int>(def.reelStrips[r].symbols);
                var missing = new List<int>();
                foreach (int id in paying)
                    if (!onReel.Contains(id)) missing.Add(id);

                Assert.IsEmpty(missing,
                    $"reel {r} is missing paying symbol(s) {string.Join(", ", missing)}; a " +
                    "symbol absent from a reel is a paytable entry that reel can never pay.");
            }
        }
    }
}
