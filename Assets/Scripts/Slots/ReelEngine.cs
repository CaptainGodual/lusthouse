using System;
using System.Collections.Generic;
using LustHouse.Core;
using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// One evaluated spin: which symbols landed where, what paid, and which features fired.
    /// </summary>
    public struct SpinResult
    {
        public int[] stops;              // stop index per reel
        public int[][] grid;             // grid[reel][row] -> symbol id
        public long totalWin;            // in currency units (already multiplied by bet)
        public long bet;
        public List<LineWin> lineWins;
        public int scatterCount;
        public bool triggeredFreeSpins;
        public bool triggeredBonusBuy;
        public bool triggeredHoldAndSpin;
        public int freeSpinsAwarded;

        /// <summary>Ways-based wins (PayType.Ways) or clusters cleared (PayType.Cluster).</summary>
        public long waysWon;

        public int clustersWon;
        public int cascadeSteps;

        /// <summary>
        /// Draw source for evaluators that must refill the grid (cluster pays).
        /// Set by ReelEngine before evaluation; null means the evaluator seeds its own.
        /// </summary>
        public SeededRng? rng;

        public bool IsWin => totalWin > 0;

        public float Multiplier(long bet) => bet <= 0 ? 0f : (float)((double)totalWin / bet);
    }

    public struct LineWin
    {
        public int paylineIndex;
        public int count;       // matching symbols
        public int symbolId;
        public long payout;     // in bet-multiples
    }

    /// <summary>Common shape for all five mechanic templates (plan §5.2: strategy classes).</summary>
    public interface IReelEvaluator
    {
        SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet);
    }

    /// <summary>
    /// Spins reels from a seeded stream. Deterministic: the same definition, seed and bet
    /// always produce the same grid, which is what makes the RTP simulation in
    /// <c>MathSimulator</c> meaningful and guardrail §6 satisfiable.
    /// </summary>
    public class ReelEngine : MonoBehaviour
    {
        [Tooltip("Base seed for the session. Overridden by QA repro entry.")]
        public int sessionSeed = 0;

        private SeededRng _rng;
        private bool _seeded;

        public SeededRng Rng => _rng;

        public void Seed(int seed)
        {
            _rng = SeededRng.FromSeed(seed);
            _seeded = true;
        }

        public void SeedFromState(ulong s0, ulong s1)
        {
            _rng = SeededRng.FromState(s0, s1);
            _seeded = true;
        }

        public (ulong s0, ulong s1) CaptureState()
        {
            EnsureSeeded();
            return (_rng.State0, _rng.State1);
        }

        private void EnsureSeeded()
        {
            if (_seeded) return;
            _rng = SeededRng.FromSeed(sessionSeed != 0 ? sessionSeed : Environment.TickCount);
            _seeded = true;
        }

        /// <summary>Headless spin used by the simulator — no MonoBehaviour required.</summary>
        public static SpinResult SpinStatic(SlotMachineDefinition def, ref SeededRng rng, long bet)
        {
            if (def == null) return default;

            var evaluator = EvaluatorFor(def);
            var stops = Stop(def, ref rng);
            var result = evaluator.Evaluate(def, stops, bet);
            result.rng = rng;
            return result;
        }

        public SpinResult Spin(SlotMachineDefinition def, long bet)
        {
            EnsureSeeded();
            if (def == null) return default;

            var evaluator = EvaluatorFor(def);
            var stops = Stop(def, ref _rng);
            var result = evaluator.Evaluate(def, stops, bet);
            result.rng = _rng;
            return result;
        }

        /// <summary>One uniform stop per reel, drawn from that reel's own strip.</summary>
        private static int[] Stop(SlotMachineDefinition def, ref SeededRng rng)
        {
            var stops = new int[def.reelCount];
            for (int r = 0; r < def.reelCount; r++)
            {
                var strip = def.reelStrips[r]?.symbols;
                stops[r] = strip == null || strip.Count <= 1 ? 0 : rng.NextInt(strip.Count);
            }
            return stops;
        }

        /// <summary>Builds the grid from the stops, honouring per-reel strip length.</summary>
        public static int[][] BuildGrid(SlotMachineDefinition def, int[] stops)
        {
            var grid = new int[def.reelCount][];
            for (int r = 0; r < def.reelCount; r++)
            {
                var strip = def.reelStrips[r].symbols;
                grid[r] = new int[def.rowCount];
                for (int row = 0; row < def.rowCount; row++)
                    grid[r][row] = strip[(stops[r] + row) % strip.Count];
            }
            return grid;
        }

        private static IReelEvaluator EvaluatorFor(SlotMachineDefinition def)
        {
            // payType decides how a win is measured; template layers features on top.
                // A cluster-pays game must not be scored as paylines, and vice versa.
                switch (def.payType)
                {
                    case PayType.Ways: return new WaysEvaluator();
                    case PayType.Cluster: return new ClusterEvaluator();
                }

                switch (def.template)
                {
                    case SlotTemplate.Cascade: return new CascadeEvaluator();
                    case SlotTemplate.HoldAndSpin: return new HoldSpinEvaluator();
                    case SlotTemplate.Progressive: return new ProgressiveEvaluator();
                    default: return new PaylineEvaluator();
                }
        }

        /// <summary>Counts scatters anywhere on the grid.</summary>
        public static int CountScatters(SlotMachineDefinition def, int[][] grid)
        {
            int count = 0;
            for (int r = 0; r < grid.Length; r++)
            {
                for (int row = 0; row < grid[r].Length; row++)
                {
                    var sym = def.SymbolById(grid[r][row]);
                    if (sym != null && sym.isScatter) count++;
                }
            }
            return count;
        }
    }
}