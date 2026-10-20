using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Hold &amp; spin (plan §3.2 template D): the triggering special symbols lock, the grid
    /// respins, and every new special symbol locks and adds its <c>holdSpinValue</c>. The respin
    /// counter resets on a new lock and the round ends when it runs out — or when the grid fills,
    /// which awards the top jackpot tier.
    ///
    /// Locked symbols are an overlay, not a second reel set. Each respin is a real base-game
    /// spin; only cells that are not already held are considered, so a prize can never be
    /// counted twice. The maths is pure and seeded, and respins deliberately use
    /// <see cref="ReelEngine.SpinBaseStatic"/> so no other feature can trigger inside the round.
    /// </summary>
    public sealed class HoldAndSpinHandler : ISlotFeatureHandler
    {
        public SlotFeature Definition { get; set; }

        private HoldAndSpinFeature Config => Definition as HoldAndSpinFeature;

        public bool CanTrigger(SlotMachineDefinition game, SpinResult baseResult)
        {
            var c = Config;
            if (c == null || baseResult.grid == null) return false;

            return CountLockable(game, baseResult.grid, c) >= Mathf.Max(1, c.initialLocked);
        }

        public FeatureResult Run(FeatureContext ctx)
        {
            var c = Config;
            var result = new FeatureResult { triggeredHoldAndSpin = true };
            if (c == null || ctx == null || ctx.game == null || ctx.baseResult.grid == null)
                return result;

            var game = ctx.game;

            // locked[reel][row] is the held-cell overlay. It survives every respin and is what
            // stops a cell (and its prize) being locked twice.
            var locked = NewLockedGrid(game);
            long pot = LockNew(game, ctx.baseResult.grid, c, locked, result.steps, out _);

            int respins = Mathf.Max(1, c.maxRespins);
            int guard = 0;

            if (IsFull(locked))
            {
                AwardJackpot(c, result, ref pot);
            }
            else
            {
                while (respins > 0 && guard++ < 1024)
                {
                    var spin = ReelEngine.SpinBaseStatic(game, ref ctx.rng, ctx.bet);

                    // A hold & spin respin pays locked prizes, not the base-game line win.
                    // Clear it from the presentation result so the presenter cannot highlight
                    // a win the player is not being paid.
                    spin.totalWin = 0;
                    spin.lineWins = null;
                    spin.waysWon = 0;
                    spin.clustersWon = 0;
                    spin.cascadeSteps = 0;

                    result.spinsRun++;
                    result.steps.Add(new FeatureStep { kind = FeatureStepKind.Spin, spin = spin });

                    long value = LockNew(game, spin.grid, c, locked, result.steps, out int newLocks);
                    if (newLocks > 0)
                    {
                        pot += value;
                        respins = Mathf.Max(1, c.maxRespins);   // a new lock resets the counter
                    }
                    else
                    {
                        respins--;
                    }

                    if (IsFull(locked))
                    {
                        AwardJackpot(c, result, ref pot);
                        break;
                    }
                }
            }

            result.totalWin = pot * ctx.bet;
            return result;
        }

        private static bool[][] NewLockedGrid(SlotMachineDefinition game)
        {
            int reels = Mathf.Max(0, game.reelCount);
            int rows = Mathf.Max(0, game.rowCount);
            var locked = new bool[reels][];
            for (int r = 0; r < reels; r++) locked[r] = new bool[rows];
            return locked;
        }

        /// <summary>Every current cell is held.</summary>
        private static bool IsFull(bool[][] locked)
        {
            if (locked == null || locked.Length == 0) return false;

            for (int r = 0; r < locked.Length; r++)
            {
                if (locked[r] == null || locked[r].Length == 0) return false;
                for (int row = 0; row < locked[r].Length; row++)
                    if (!locked[r][row]) return false;
            }

            return true;
        }

        /// <summary>Counts lockable symbols without allocating an overlay (the trigger check).</summary>
        private static int CountLockable(SlotMachineDefinition game, int[][] grid, HoldAndSpinFeature c)
        {
            int count = 0;
            if (grid == null) return 0;

            for (int r = 0; r < grid.Length; r++)
            {
                var cells = grid[r];
                if (cells == null) continue;

                for (int row = 0; row < cells.Length; row++)
                    if (IsLockable(game.SymbolById(cells[row]), c)) count++;
            }

            return count;
        }

        /// <summary>
        /// Locks every lockable symbol in a cell that is not already held, emitting one step per
        /// new lock, and returns the value added. <paramref name="steps"/> may be null.
        /// </summary>
        private static long LockNew(SlotMachineDefinition game, int[][] grid,
                                    HoldAndSpinFeature c, bool[][] locked,
                                    List<FeatureStep> steps, out int count)
        {
            long value = 0;
            count = 0;
            if (grid == null || locked == null) return 0;

            int reels = Mathf.Min(grid.Length, locked.Length);
            for (int r = 0; r < reels; r++)
            {
                var cells = grid[r];
                var held = locked[r];
                if (cells == null || held == null) continue;

                int rows = Mathf.Min(cells.Length, held.Length);
                for (int row = 0; row < rows; row++)
                {
                    if (held[row]) continue;

                    var symbol = game.SymbolById(cells[row]);
                    if (!IsLockable(symbol, c)) continue;

                    held[row] = true;
                    count++;
                    value += symbol.holdSpinValue;

                    steps?.Add(new FeatureStep
                    {
                        kind = FeatureStepKind.Lock,
                        reel = r,
                        row = row,
                        symbolId = symbol.id,
                        amount = symbol.holdSpinValue
                    });
                }
            }

            return value;
        }

        /// <summary>Filling the grid pays the top tier of <see cref="HoldAndSpinFeature.jackpotValues"/>.</summary>
        private static void AwardJackpot(HoldAndSpinFeature c, FeatureResult result, ref long pot)
        {
            if (!c.jackpotTiers || c.jackpotValues == null || c.jackpotValues.Length == 0) return;

            long jackpot = c.jackpotValues[c.jackpotValues.Length - 1];
            if (jackpot <= 0) return;

            pot += jackpot;
            result.steps.Add(new FeatureStep { kind = FeatureStepKind.Award, amount = jackpot });
        }

        private static bool IsLockable(SymbolDefinition symbol, HoldAndSpinFeature c)
        {
            if (symbol == null) return false;
            if (c.specialSymbolId >= 0) return symbol.id == c.specialSymbolId;
            return symbol.isSpecial;
        }
    }
}
