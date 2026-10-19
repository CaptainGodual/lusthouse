using System;
using UnityEngine;

namespace LustHouse.Slots
{
    public interface IReelEvaluator
    {
        SpinResult Evaluate(SlotMachineDefinition def, int[] stops);
    }

    public struct SpinResult
    {
        public long totalWin;
        public int[][] winningLines;
        public bool triggeredFreeSpins;
        public bool triggeredHoldAndSpin;
    }

    public class ReelEngine : MonoBehaviour
    {
        public IReelEvaluator Evaluator { get; set; }

        public SpinResult Spin(SlotMachineDefinition def)
        {
            int[] stops = new int[def.reelCount];
            for (int i = 0; i < stops.Length; i++)
                stops[i] = UnityEngine.Random.Range(0, def.symbolStrip.Length / def.reelCount);
            return Evaluator != null ? Evaluator.Evaluate(def, stops) : default;
        }
    }
}