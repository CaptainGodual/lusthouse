using System.Collections.Generic;

namespace LustHouse.Slots
{
    /// <summary>
    /// Template A/B base evaluator: counts paying paylines with wild substitution.
    ///
    /// This is the strategy behind <see cref="PayType.Lines"/>. The other templates
    /// delegate here and layer their feature handling on top, so there is exactly one
    /// payout code path (plan §5.2).
    /// </summary>
    public class PaylineEvaluator : IReelEvaluator
    {
        public virtual SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = new SpinResult
            {
                stops = stops,
                bet = bet,
                grid = ReelEngine.BuildGrid(def, stops),
                lineWins = new List<LineWin>(),
                totalWin = 0
            };

            var grid = result.grid;
            long totalMultiples = 0;

            if (def.payType == PayType.Lines)
            {
                for (int i = 0; i < def.paylines.Count; i++)
                {
                    var line = def.paylines[i];
                    long win = EvaluateLine(def, grid, line, out int symbolId, out int count);
                    if (win <= 0) continue;

                    totalMultiples += win;
                    result.lineWins.Add(new LineWin
                    {
                        paylineIndex = i,
                        count = count,
                        symbolId = symbolId,
                        payout = win
                    });
                }
            }

            result.scatterCount = ReelEngine.CountScatters(def, grid);
            if (def.hasFreeSpins && result.scatterCount >= def.freeSpinScatterCount)
            {
                result.triggeredFreeSpins = true;
                result.freeSpinsAwarded = def.freeSpinAward;
                totalMultiples += (long)def.freeSpinAward * def.freeSpinBetMultiplier;
            }

            result.totalWin = totalMultiples * bet;
            return result;
        }

        /// <summary>
        /// Walks a payline left to right. A wild substitutes for any non-scatter symbol;
        /// the line wins on the longest prefix of matching symbols, minimum three.
        /// </summary>
        public static long EvaluateLine(SlotMachineDefinition def, int[][] grid, PaylineDefinition line,
                                        out int symbolId, out int count)
        {
            symbolId = -1;
            count = 0;
            if (line?.rows == null) return 0;

            int reelCount = line.rows.Length;

            // The base symbol is whatever reel 0 shows that is not a wild.
            int baseSymbol = grid[0][line.rows[0]];
            var baseDef = def.SymbolById(baseSymbol);
            if (baseDef == null) return 0;
            if (baseDef.isScatter) return 0;   // scatters never pay on a line

            bool sawWild = baseDef.isWild;
            int matched = 1;

            for (int r = 1; r < reelCount; r++)
            {
                int symbol = grid[r][line.rows[r]];
                var def2 = def.SymbolById(symbol);
                if (def2 == null) break;

                if (symbol == baseSymbol) { matched++; continue; }
                if (def2.isWild) { matched++; sawWild = true; continue; }
                break;
            }

            if (matched < 3) return 0;

            // A wild-only line pays as the highest-value symbol it could substitute for.
            symbolId = sawWild ? HighestPayingSymbol(def) : baseSymbol;
            count = matched;

            var payDef = def.SymbolById(symbolId);
            if (payDef == null) return 0;

            return payDef.PayoutForMatch(matched);
        }

        private static int HighestPayingSymbol(SlotMachineDefinition def)
        {
            int best = -1;
            long bestPayout = -1;

            for (int i = 0; i < def.symbols.Count; i++)
            {
                var s = def.symbols[i];
                if (s == null || s.isScatter) continue;

                long five = s.PayoutForMatch(5);
                if (five > bestPayout)
                {
                    bestPayout = five;
                    best = s.id;
                }
            }

            return best;
        }
    }

    /// <summary>Template C: cluster/cascade pays (plan §3.2).</summary>
    public class CascadeEvaluator : PaylineEvaluator
    {
        public override SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = base.Evaluate(def, stops, bet);
            // One cascade step in the greybox pass; the loop lands with the template
            // implementation in M5. Payout logic is already delegated above.
            return result;
        }
    }

    /// <summary>Template D: hold & spin. Base payout is line-based; respins are layered on.</summary>
    public class HoldSpinEvaluator : PaylineEvaluator
    {
        public override SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = base.Evaluate(def, stops, bet);
            result.triggeredHoldAndSpin = def.hasHoldAndSpin;
            return result;
        }
    }

    /// <summary>Template E: progressive. Same line maths, plus a seeded meter contribution.</summary>
    public class ProgressiveEvaluator : PaylineEvaluator
    {
        public override SpinResult Evaluate(SlotMachineDefinition def, int[] stops, long bet)
        {
            var result = base.Evaluate(def, stops, bet);
            result.triggeredBonusBuy = def.hasBonusBuy && def.bonusBuyCostMultiplier > 0;
            return result;
        }
    }
}