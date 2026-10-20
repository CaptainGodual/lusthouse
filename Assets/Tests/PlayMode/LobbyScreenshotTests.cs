using System.Collections;
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

            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 10f;
            Canvas.ForceUpdateCanvases();
            yield return null;

            ScreenGrab.Capture(canvas, camera, ScreenGrab.LogPath("lobby.png"));
            Debug.Log("[LustHouse] lobby screenshot written to Logs/lobby.png");

            yield return null;
        }
    }
}
