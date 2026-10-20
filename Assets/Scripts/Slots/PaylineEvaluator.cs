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
            // Features are not scored here: FeatureRunner runs them after the base game so a
            // free-spin round is actually played rather than folded into one lump.
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

            // Report the run length even when the line does not pay, so a near miss is
            // distinguishable from no match at all. Harmless to payout maths: callers
            // gate on win > 0, and symbolId stays -1 when nothing pays.
            count = matched;
            if (matched < 3) return 0;

            // A wild pays as the symbol it stands in for -- the first real symbol on the
            // line -- not as the most expensive symbol in the game. Paying the priciest
            // symbol turned every wild into a Filigree, which put the RTP floor for this
            // paytable at 579% and made the declared target unreachable at all. An
            // all-wild line falls back to the Wild's own payoutsByMatch.
            symbolId = sawWild ? SubstituteSymbol(def, grid, line) : baseSymbol;

            var payDef = def.SymbolById(symbolId);
            if (payDef == null) return 0;

            return payDef.PayoutForMatch(matched);
        }

        /// <summary>
        /// The symbol a wild-assisted line pays as: the first non-wild, non-scatter symbol
        /// the wild covers, or the wild itself when the line is entirely wilds.
        /// </summary>
        private static int SubstituteSymbol(SlotMachineDefinition def, int[][] grid,
                                            PaylineDefinition line)
        {
            for (int r = 0; r < line.rows.Length; r++)
            {
                int id = grid[r][line.rows[r]];
                var symbol = def.SymbolById(id);
                if (symbol == null || symbol.isWild || symbol.isScatter) continue;
                return id;
            }

            for (int i = 0; i < def.symbols.Count; i++)
                if (def.symbols[i] != null && def.symbols[i].isWild)
                    return def.symbols[i].id;

            return -1;
        }
    }

    // Templates C/D/E used to have stub evaluator subclasses here. They are gone: the base game
    // is chosen by payType alone, and each template's mechanic is a SlotFeatureHandler run by
    // FeatureRunner (cluster pays is PayType.Cluster's ClusterEvaluator).
}