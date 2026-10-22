using UnityEngine;

namespace LustHouse.UI
{
    /// <summary>
    /// Pure layout helpers for responsive scaling (plan §2.1).
    /// </summary>
    public static class ScreenLayout
    {
        public struct SafeArea
        {
            public Rect safeRect;
            public Vector2 referenceResolution;

            public SafeArea(Rect rect, Vector2 reference)
            {
                safeRect = rect;
                referenceResolution = reference;
            }
        }

        public static SafeArea GetSafeArea(Vector2 referenceResolution)
        {
            Rect safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0)
                safe = new Rect(0, 0, Screen.width, Screen.height);

            return new SafeArea(safe, referenceResolution);
        }

        public static bool IsCompactWidth(float width)
        {
            return width < 1400f;
        }

        public static float FitAspect(float containerWidth, float containerHeight, float contentWidth, float contentHeight)
        {
            if (contentWidth <= 0 || contentHeight <= 0) return 1f;
            float scaleX = containerWidth / contentWidth;
            float scaleY = containerHeight / contentHeight;
            return Mathf.Min(scaleX, scaleY);
        }
    }
}

