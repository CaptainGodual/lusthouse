using System.Collections.Generic;
using LustHouse.Economy;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// The house's bet rules: one shared ladder, and a ceiling that rises with the
    /// player's level.
    ///
    /// These are economy rules, not implementation details. A player who cannot tell
    /// what they are allowed to bet, or who finds the ceiling jumping around between
    /// sessions, has no idea what the game is asking of them.
    /// </summary>
    public class BetLimitsTests
    {
        [Test]
        public void TheLadder_IsAscendingAndEveryStepIsRound()
        {
            long previous = 0;
            foreach (long step in BetLimits.Ladder)
            {
                Assert.Greater(step, previous, "The bet ladder must ascend.");
                Assert.AreEqual(0, step % 100, "Every bet must be a round number (§4.3 rule 5).");
                previous = step;
            }
        }

        [Test]
        public void LevelOne_CapsTheBetAtSeventyFiveThousand()
        {
            // The number the whole curve is anchored on: a new player can reach the
            // 75,000 rung and nothing above it.
            Assert.AreEqual(75_000, BetLimits.MaxBetForLevel(1));
        }

        [Test]
        public void TheCeiling_NeverDropsAsTheLevelRises()
        {
            long previous = 0;
            for (int level = 1; level <= BetLimits.LevelCaps.Length; level++)
            {
                long cap = BetLimits.MaxBetForLevel(level);
                Assert.GreaterOrEqual(cap, previous,
                    $"The bet ceiling must never fall: level {level} is below level {level - 1}.");
                previous = cap;
            }
        }

        [Test]
        public void TheCeiling_TopsOutAtTheHouseMaximum()
        {
            Assert.AreEqual(BetLimits.HouseMaxBet, BetLimits.MaxBetForLevel(BetLimits.LevelCaps.Length));
            Assert.AreEqual(BetLimits.HouseMaxBet, BetLimits.MaxBetForLevel(999),
                "A level past the table must stay at the house maximum, not fall back to the floor.");
        }

        [Test]
        public void TheHouseMaximum_IsTheTopOfTheLadder()
        {
            Assert.AreEqual(BetLimits.Ladder[BetLimits.Ladder.Length - 1], BetLimits.HouseMaxBet);
        }

        [Test]
        public void EveryGame_OffersTheSameFloorAndTheSameCeiling()
        {
            // One house, one ladder. A denomination that means 10,000 in one slot has to
            // mean 10,000 in the next.
            foreach (var guid in AssetDatabase.FindAssets("t:SlotMachineDefinition", new[] { "Assets" }))
            {
                var def = AssetDatabase.LoadAssetAtPath<SlotMachineDefinition>(
                    AssetDatabase.GUIDToAssetPath(guid));
                if (def == null) continue;

                Assert.AreEqual(1_000, def.minBet, $"{def.id} must start at the house floor of 1,000.");
                Assert.AreEqual(BetLimits.HouseMaxBet, def.maxBet,
                    $"{def.id} must top out at the house maximum.");
            }
        }

        [Test]
        public void ALevelUnlocksMoreBetsAndNeverFewer()
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.minBet = 1_000;
            def.maxBet = BetLimits.HouseMaxBet;

            int previous = 0;
            for (int level = 1; level <= BetLimits.LevelCaps.Length; level++)
            {
                var options = def.BetOptions(level);
                Assert.GreaterOrEqual(options.Count, previous,
                    $"Level {level} must not take bets away from level {level - 1}.");
                Assert.IsNotEmpty(options, $"Level {level} must offer at least one bet.");
                previous = options.Count;
            }

            Object.DestroyImmediate(def);
        }

        [Test]
        public void TheFloor_NeverMovesWithLevel()
        {
            // Betting 1,000 at level 1 and 1,000 at level 10 are the same risk, so the
            // bottom of the ladder has to mean the same thing at every level.
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.minBet = 1_000;
            def.maxBet = BetLimits.HouseMaxBet;

            for (int level = 1; level <= BetLimits.LevelCaps.Length; level++)
            {
                var options = def.BetOptions(level);
                Assert.AreEqual(def.minBet, options[0],
                    $"Level {level} must start at the same floor as every other level.");
            }

            Object.DestroyImmediate(def);
        }

        [Test]
        public void TheCeiling_ReachesTheLadderTheLevelUnlocked()
        {
            var def = ScriptableObject.CreateInstance<SlotMachineDefinition>();
            def.minBet = 1_000;
            def.maxBet = BetLimits.HouseMaxBet;

            for (int level = 1; level <= BetLimits.LevelCaps.Length; level++)
            {
                long cap = BetLimits.MaxBetForLevel(level);
                var options = def.BetOptions(level);

                Assert.LessOrEqual(options[options.Count - 1], cap,
                    "A player must not be offered a bet above their ceiling.");
                Assert.AreEqual(cap, options[options.Count - 1],
                    $"Level {level} must be able to reach its ceiling.");
            }

            Object.DestroyImmediate(def);
        }
    }
}
