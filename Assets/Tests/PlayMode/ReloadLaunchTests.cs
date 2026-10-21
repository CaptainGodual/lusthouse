using System.Collections;
using System.Collections.Generic;
using LustHouse.Lobby;
using LustHouse.Slots;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// The launch round trip: lobby → game → lobby → game.
    ///
    /// The second launch is the one that used to hang. Coming back from a game tears the
    /// lobby scene down and builds it again, so every piece of the launch path is a fresh
    /// instance — including the content service, which is where the Addressables
    /// initialisation lives. Addressables itself is a process-wide singleton, and asking it
    /// to initialise twice is not a no-op: with no catalogue in the build it throws, the
    /// exception killed the launch coroutine, and the player was left looking at the
    /// loading screen for ever.
    ///
    /// The editor cannot reproduce that exact failure — it always has a catalogue — so this
    /// test asserts the behaviour that matters either way: a second launch works, the loader
    /// comes down, and the guard that stops a second tap being ignored is released.
    /// </summary>
    public class ReloadLaunchTests
    {
        private const string LobbyScene = "Lobby";
        private const string SlotScene = "Slot";

        private readonly List<Object> _spawned = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = false;

            for (int i = 0; i < _spawned.Count; i++)
                if (_spawned[i] != null) Object.Destroy(_spawned[i]);
            _spawned.Clear();

            SlotLaunchPayload.Payload = null;
            yield return null;
        }

        private IEnumerator LoadLobby()
        {
            yield return SceneManager.LoadSceneAsync(LobbyScene);
            yield return null;
            yield return null;   // Build() + RebuildGrid() land on the presenter's OnEnable
        }

        /// <summary>Waits for a scene to finish loading, or gives up after a frame budget.</summary>
        private static IEnumerator WaitForScene(string sceneName, int maxFrames = 900)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (SceneManager.GetActiveScene().name == sceneName) yield break;
                yield return null;
            }
        }

        /// <summary>The first game tile. Looked up under the Canvas rather than the controller.</summary>
        private static Image FirstTile()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return null;

            foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                if (image.name.StartsWith("Tile_")) return image;
            return null;
        }

        private static Button FindButton(string name)
        {
            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
                if (button.name == name) return button;
            return null;
        }

        /// <summary>The loading overlay, if one has been built in this lobby.</summary>
        private static GameObject LoadingOverlay()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return null;

            foreach (var transform in canvas.GetComponentsInChildren<Transform>(true))
                if (transform.name == "LoadingOverlay") return transform.gameObject;
            return null;
        }

        private static void Click(GameObject target)
        {
            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };
            ExecuteEvents.Execute(target, pointer, ExecuteEvents.pointerClickHandler);
        }

        /// <summary>
        /// Puts coins in the wallet. The lobby rebuilds the whole grid on every wallet
        /// change, so anything grabbed before this point is destroyed by it — and clicking a
        /// destroyed GameObject does nothing at all.
        /// </summary>
        private static void Fund()
        {
            Core.ServicesBootstrap.EnsureExists();

            var wallet = Core.WalletService.Instance;
            Assert.IsNotNull(wallet, "There is no WalletService, so the lobby cannot tell " +
                                     "whether a game is affordable.");

            wallet.TryGrant(Core.Currency.Coins, 1_000_000, Core.LedgerReason.Admin,
                "reload launch test");

            Assert.Greater(wallet.Balance(Core.Currency.Coins), 0,
                "The wallet has no coins, so the tile would refuse to launch.");
        }

        /// <summary>Closes any modal sheet so the lobby is clickable again.</summary>
        private static void CloseSheets()
        {
            foreach (var sheet in Object.FindObjectsByType<ModalSheet>(FindObjectsSortMode.None))
                sheet.Close();
        }

        [UnityTest]
        public IEnumerator LaunchingAgainAfterComingBackFromAGame_Works()
        {
            // ---- first launch ----------------------------------------------------
            yield return LoadLobby();
            Fund();
            yield return null;
            yield return null;

            var tile = FirstTile();
            Assert.IsNotNull(tile, "The lobby built no game tile, so there is nothing to launch.");

            Click(tile.gameObject);
            yield return WaitForScene(SlotScene);

            Assert.AreEqual(SlotScene, SceneManager.GetActiveScene().name,
                "The first launch never reached the slot scene.");

            var controller = Object.FindFirstObjectByType<SlotSceneController>();
            Assert.IsNotNull(controller, "The Slot scene has no SlotSceneController.");
            Assert.IsNotNull(controller.Definition,
                "The Slot scene loaded with no definition, so it bounced straight back to " +
                "the lobby.");

            // ---- back to the lobby ------------------------------------------------
            var back = FindButton("Back");
            Assert.IsNotNull(back, "The slot screen has no back button, so the player cannot " +
                                   "return to the lobby.");

            Click(back.gameObject);
            yield return WaitForScene(LobbyScene);

            Assert.AreEqual(LobbyScene, SceneManager.GetActiveScene().name,
                "The back button did not return to the lobby.");

            yield return null;
            yield return null;

            // ---- second launch ----------------------------------------------------
            Fund();
            yield return null;
            yield return null;

            var secondTile = FirstTile();
            Assert.IsNotNull(secondTile, "The rebuilt lobby has no game tile.");

            Click(secondTile.gameObject);
            yield return null;
            yield return null;

            // The loader is built the first time a launch shows it, so finding it here
            // proves the second launch actually started rather than being swallowed.
            var overlay = LoadingOverlay();
            Assert.IsNotNull(overlay,
                "The second launch never showed the loading screen, so it never started.");

            // Give the launch plenty of room: the scene load plus the pack check. The
            // failure this guards against was a launch that never finished at all.
            yield return WaitForScene(SlotScene, 1800);

            Assert.AreEqual(SlotScene, SceneManager.GetActiveScene().name,
                "The second launch never reached the slot scene. Coming back from a game " +
                "rebuilds the lobby, and the rebuild is what broke the launch: the content " +
                "service asked Addressables to initialise a second time, which threw with " +
                "no catalogue in the build, and the exception killed the launch coroutine " +
                "with the loading screen still up.");

            var secondController = Object.FindFirstObjectByType<SlotSceneController>();
            Assert.IsNotNull(secondController, "The Slot scene has no SlotSceneController.");
            Assert.IsNotNull(secondController.Definition,
                "The second launch reached the slot scene with no definition.");
        }

        [UnityTest]
        public IEnumerator AFailedLaunch_HidesTheLoaderAndReleasesTheGuard()
        {
            // The failure this provokes is deliberate, and it is noisy: Unity logs an error
            // for a scene that is not in the build, and the lobby logs the launch failure.
            LogAssert.ignoreFailingMessages = true;

            yield return LoadLobby();

            var controller = Object.FindFirstObjectByType<LobbyController>();
            Assert.IsNotNull(controller, "No LobbyController in the lobby.");

            // A scene name that is not in the build makes the launch fail in the one place
            // it can: the scene load. The loader must still come down, and the launch guard
            // must be released, or the next tap on a tile would be ignored for ever.
            controller.slotSceneName = "NoSuchSceneExists";

            Fund();
            yield return null;
            yield return null;

            var tile = FirstTile();
            Assert.IsNotNull(tile, "The lobby built no game tile.");

            Click(tile.gameObject);
            yield return null;
            yield return null;
            yield return null;

            var overlay = LoadingOverlay();
            Assert.IsNotNull(overlay,
                "The launch never showed the loading screen, so it never started.");
            Assert.IsFalse(overlay.activeInHierarchy,
                "The loading screen is still up after a launch that failed. A player left " +
                "looking at the loader has no way forward.");

            controller.slotSceneName = "Slot";

            // The guard must be released: a second tap has to be able to start a launch.
            CloseSheets();
            yield return new WaitForSeconds(0.5f);

            Fund();
            yield return null;
            yield return null;

            var secondTile = FirstTile();
            Assert.IsNotNull(secondTile, "The rebuilt lobby has no game tile.");

            Click(secondTile.gameObject);
            yield return WaitForScene(SlotScene, 1800);

            Assert.AreEqual(SlotScene, SceneManager.GetActiveScene().name,
                "The launch guard was never released after a failed launch, so the next tap " +
                "was ignored and the game could not be opened at all.");
        }

        [UnityTest]
        public IEnumerator AddressablesIsInitialisedAtMostOncePerProcess()
        {
            // The lobby scene is torn down and rebuilt between a game and the lobby, so the
            // content service is a new instance every time. Addressables is a process-wide
            // singleton: initialising it twice reaches into a locator list that is empty
            // when the first attempt failed, and throws. That exception is what stranded
            // the player on the loading screen, so the guard has to be static, not per
            // instance.
            int before = SlotContentService.InitAttempts;

            var first = new GameObject("ContentServiceA");
            _spawned.Add(first);
            var firstService = first.AddComponent<SlotContentService>();
            yield return firstService.EnsureDownloaded("Slots/velvet_room", _ => { });

            var second = new GameObject("ContentServiceB");
            _spawned.Add(second);
            var secondService = second.AddComponent<SlotContentService>();
            yield return secondService.EnsureDownloaded("Slots/velvet_room", _ => { });

            Assert.LessOrEqual(SlotContentService.InitAttempts - before, 1,
                "Addressables was initialised more than once in the process. The second " +
                "call is not a no-op — it throws when the catalogue could not be loaded, " +
                "which kills the launch coroutine and leaves the loading screen up.");
        }
    }
}
