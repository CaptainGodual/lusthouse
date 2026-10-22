using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Pure layout computation for slot machine (plan §2.2).
    /// </summary>
    public static class SlotLayout
    {
        public struct LayoutResult
        {
            public Rect machineRect;
            public Rect[] reelColumnRects;
            public Rect headerRect;
            public Rect controlBarRect;
            public Rect[] buttonSlots;
        }

        public static LayoutResult Compute(RectTransform canvas, Rect safeArea, int reelCount, int rowCount)
        {
            LayoutResult result = new LayoutResult();
            result.reelColumnRects = new Rect[reelCount];
            result.buttonSlots = new Rect[6];

            // Machine centered, scaled to fit
            float machineWidth = 1700f;
            float machineHeight = 900f;
            result.machineRect = new Rect((canvas.rect.width - machineWidth) * 0.5f, (canvas.rect.height - machineHeight) * 0.5f, machineWidth, machineHeight);

            // Header at top
            result.headerRect = new Rect(result.machineRect.x, result.machineRect.yMax - 80f, result.machineRect.width, 80f);

            // Control bar at bottom
            float barHeight = Mathf.Clamp(safeArea.height * 0.14f, 120f, 170f);
            result.controlBarRect = new Rect(safeArea.x, safeArea.y, safeArea.width, barHeight);

            // Button slots spaced proportionally
            float spacing = safeArea.width / 7f;
            for (int i = 0; i < 6; i++)
            {
                result.buttonSlots[i] = new Rect(safeArea.x + spacing * (i + 1) - 64f, safeArea.y + barHeight * 0.5f - 64f, 128f, 128f);
            }

            // Reel columns
            float reelAreaY = result.machineRect.y + 100f;
            float reelAreaHeight = result.machineRect.height - 200f;
            float reelWidth = 300f;
            float reelSpacing = 20f;
            float totalReelWidth = reelCount * reelWidth + (reelCount - 1) * reelSpacing;
            float startX = result.machineRect.x + (result.machineRect.width - totalReelWidth) * 0.5f;
            for (int i = 0; i < reelCount; i++)
            {
                result.reelColumnRects[i] = new Rect(startX + i * (reelWidth + reelSpacing), reelAreaY, reelWidth, reelAreaHeight);
            }

            return result;
        }
    }
}

