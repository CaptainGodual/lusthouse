using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// Captures the lobby to a PNG so the layout can be looked at rather than guessed at.
    ///
    /// An overlay canvas is not drawn by any camera, so the capture switches the canvas to
    /// ScreenSpaceCamera, renders the camera into a RenderTexture and reads the pixels
    /// back. That works in batch mode, where no window is ever opened.
    ///
    /// This is a development tool, not a test: it asserts nothing and always passes.
    /// </summary>
    public class LobbyScreenshotTests
    {
        [UnityTest]
        public IEnumerator CaptureLobby()
        {
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Lobby");
            yield return null;
            yield return null;
            yield return new WaitForSecondsRealtime(1.5f);

            var canvas = Object.FindFirstObjectByType<Canvas>();
            var camera = Camera.main;
            Assert.IsNotNull(canvas, "The lobby has no Canvas.");
            Assert.IsNotNull(camera, "The lobby has no Main Camera.");

            var previousMode = canvas.renderMode;
            var previousCamera = canvas.worldCamera;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            Canvas.ForceUpdateCanvases();
            yield return null;

            const int width = 1920, height = 1080;
            var target = new RenderTexture(width, height, 24) { antiAliasing = 1 };
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;

            camera.targetTexture = target;
            RenderTexture.active = target;
            camera.Render();

            var shot = new Texture2D(width, height, TextureFormat.RGB24, false);
            shot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            shot.Apply();

            RenderTexture.active = previousActive;
            camera.targetTexture = previousTarget;
            canvas.renderMode = previousMode;
            canvas.worldCamera = previousCamera;

            string path = Path.Combine(Application.dataPath, "..", "Logs", "lobby.png");
            File.WriteAllBytes(path, shot.EncodeToPNG());
            Debug.Log($"[LustHouse] lobby screenshot written to {path}");

            yield return null;
        }
    }
}
