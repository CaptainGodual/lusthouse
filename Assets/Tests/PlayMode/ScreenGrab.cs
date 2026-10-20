using System.IO;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// Captures the screen to a PNG.
    ///
    /// An overlay canvas is not drawn by any camera, so the capture switches the canvas to
    /// ScreenSpaceCamera, renders that camera into a RenderTexture and reads the pixels
    /// back. That works in batch mode, where no window is ever opened — which is the only
    /// way to see a UI change without a human in front of the screen.
    /// </summary>
    public static class ScreenGrab
    {
        public const int Width = 1920;
        public const int Height = 1080;

        /// <summary>
        /// Renders the canvas through the camera and writes the PNG.
        ///
        /// The canvas is put back the way it was afterwards, so a capture never leaves the
        /// scene in a state the next test would trip over.
        /// </summary>
        public static void Capture(Canvas canvas, Camera camera, string path)
        {
            if (canvas == null || camera == null) return;

            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            Canvas.ForceUpdateCanvases();

            var target = new RenderTexture(Width, Height, 24) { antiAliasing = 1 };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            camera.targetTexture = target;
            RenderTexture.active = target;
            camera.Render();

            var shot = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            shot.Apply();

            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;
            canvas.renderMode = previousMode;
            canvas.worldCamera = previousCamera;

            File.WriteAllBytes(path, shot.EncodeToPNG());
            Object.DestroyImmediate(shot);
            Object.DestroyImmediate(target);
        }

        /// <summary>The project's Logs folder, which is where the lobby capture goes too.</summary>
        public static string LogPath(string fileName) =>
            Path.Combine(Application.dataPath, "..", "Logs", fileName);
    }
}
