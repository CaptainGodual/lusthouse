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
    }
}
