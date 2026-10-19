using System.Collections.Generic;
using UnityEngine;

namespace LustHouse.Slots
{
    public class PaylineEvaluator : IReelEvaluator
    {
        public SpinResult Evaluate(SlotMachineDefinition def, int[] stops)
        {
            // Stub: production implementation walks paylines and symbol payouts.
            return new SpinResult { totalWin = 0, triggeredFreeSpins = false };
        }
    }
}