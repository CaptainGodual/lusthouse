using UnityEngine;

namespace LustHouse.Slots
{
    /// <summary>
    /// Measured window rects for reel mask geometry (plan §2.3).
    /// </summary>
    public static class SlotFrameGeometry
    {
        // Measured window rects (168–492, 535–859, 901–1225, 1268–1592, 1634–1958 × 114–954 of 2132×1070)
        public static readonly Rect[] WindowRects = new Rect[]
        {
            new Rect(168f/2132f, 114f/1070f, (492f-168f)/2132f, (954f-114f)/1070f),
            new Rect(535f/2132f, 114f/1070f, (859f-535f)/2132f, (954f-114f)/1070f),
            new Rect(901f/2132f, 114f/1070f, (1225f-901f)/2132f, (954f-114f)/1070f),
            new Rect(1268f/2132f, 114f/1070f, (1592f-1268f)/2132f, (954f-114f)/1070f),
            new Rect(1634f/2132f, 114f/1070f, (1958f-1634f)/2132f, (954f-114f)/1070f),
        };

        public static Rect GetWindowRect(int index, Rect containerRect)
        {
            if (index < 0 || index >= WindowRects.Length) return containerRect;
            Rect r = WindowRects[index];
            return new Rect(
                containerRect.x + r.x * containerRect.width,
                containerRect.y + r.y * containerRect.height,
                r.width * containerRect.width,
                r.height * containerRect.height
            );
        }
    }
}

