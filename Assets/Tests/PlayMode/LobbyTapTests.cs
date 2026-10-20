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
    /// The lobby tap, end to end: raycast a tile, click it, and land in a slot scene with a
    /// definition.
    ///
    /// This is the only test that covers the seam between the lobby and the game. Every other
    /// suite tests a component in isolation and all of them pass while the player cannot
    /// launch anything — the one failure mode that makes the whole project unplayable and is
    /// invisible to any unit test. A tap is not a unit; it is a raycast through a canvas, a
    /// listener firing, and a static surviving a scene load, and every link of that chain can
    /// break alone.
    /// </summary>
    public class LobbyTapTests
    {
        private const string LobbyScene = "Lobby";
        private const string SlotScene = "Slot";

        private readonly List<Object> _spawned = new List<Object>();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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

        /// <summary>
        /// The first game tile's screen position, and a description of what a raycast at that
        /// point actually hits. A tap fails for one of three reasons — nothing is there,
        /// something else is on top, or the handler swallows it — and each needs a different
        /// fix, so the diagnostics name which.
        /// </summary>
        private static string DescribeRaycastAt(Vector2 screenPoint)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
            {
                position = screenPoint
            }, hits);

            if (hits.Count == 0) return "nothing at all — the tile is not raycastable here";

            var top = hits[0].gameObject;
            string names = string.Join(" > ", hits.ConvertAll(h => h.gameObject.name));
            return $"top hit '{top.name}' (raycastTarget={DescribeRaycast(top)}), stack: {names}";
        }

        private static string DescribeRaycast(GameObject go)
        {
            var graphic = go.GetComponent<Graphic>();
            if (graphic == null) return "no Graphic";
            if (!graphic.raycastTarget) return "raycastTarget=false";

            bool masked = go.GetComponentInParent<Mask>() != null ||
                          go.GetComponentInParent<RectMask2D>() != null;
            return masked ? "ok, but inside a mask that may clip it" : "ok";
        }

        /// <summary>
        /// The first game tile. Looked up under the Canvas rather than the controller: the
        /// screen's UI is parented to the scene's canvas, which is a scene root rather than a
        /// child of the controller, so walking from the controller finds nothing.
        /// </summary>
        private static Image FirstTile()
        {
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null) return null;

            foreach (var image in canvas.GetComponentsInChildren<Image>(true))
                if (image.name.StartsWith("Tile_")) return image;
            return null;
        }

        [UnityTest]
        public IEnumerator Lobby_BuildsAtLeastOneTappableTile()
        {
            yield return LoadLobby();

            var grid = Object.FindFirstObjectByType<LobbyGrid>();
            Assert.IsNotNull(grid, "The lobby built no LobbyGrid; nothing can be tapped.");

            var tiles = new List<Button>();
            var canvas = Object.FindFirstObjectByType<Canvas>();
            Assert.IsNotNull(canvas, "The lobby has no Canvas.");

            foreach (var button in canvas.GetComponentsInChildren<Button>(true))
            {
                if (button.name.StartsWith("Tile_")) tiles.Add(button);
            }

            Assert.IsNotEmpty(tiles, "The lobby built no game tiles. The grid is empty, so " +
                                     "there is nothing to tap and no error to explain why.");
        }

        [UnityTest]
        public IEnumerator TileTap_IsReachableByARaycast()
        {
            yield return LoadLobby();

            var grid = Object.FindFirstObjectByType<LobbyGrid>();
            var canvas = Object.FindFirstObjectByType<Canvas>();
            Assert.IsNotNull(canvas, "The lobby has no Canvas.");

            var graphicRaycaster = canvas.GetComponent<GraphicRaycaster>();
            Assert.IsNotNull(graphicRaycaster,
                "The lobby Canvas has no GraphicRaycaster, so no UI can receive a click at all. " +
                "This is the single component that makes an entire screen untappable.");
            Assert.IsTrue(graphicRaycaster.enabled, "The GraphicRaycaster is disabled.");

            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            Assert.IsNotNull(eventSystem, "No EventSystem in the lobby; clicks go nowhere.");
            Assert.IsNotNull(eventSystem.currentInputModule,
                "The EventSystem has no input module, so it never produces pointer events.");

            var tile = FirstTile();
            Assert.IsNotNull(tile, "No tile image found to raycast against.");

            var rt = (RectTransform)tile.transform;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rt.position);
            Log($"[LobbyTap] tile '{tile.name}' at screen {screen}, raycast: {DescribeRaycastAt(screen)}");

            var results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current)
            {
                position = screen
            }, results);

            Assert.IsNotEmpty(results,
                $"A raycast at the centre of tile '{tile.name}' hit nothing, so it cannot be " +
                "tapped. Something is wrong with the canvas, the rect, or an overlay.");
            Assert.AreEqual(tile.gameObject, results[0].gameObject,
                "Something is drawn on top of the tile and is eating every click: " +
                DescribeRaycastAt(screen));
        }

        [UnityTest]
        public IEnumerator NoScreenFillingGraphicEatsThePointer()
        {
            // The bug this guards: a full-quad Graphic with raycastTarget on (the leftover
            // SceneLabel) is returned as the top hit for every pixel, so the whole screen goes
            // dead while looking completely normal. Asserted across all three scenes rather
            // than just the lobby, because the same label shipped in each of them.
            foreach (var sceneName in new[] { LobbyScene, SlotScene, "BootLoader" })
            {
                yield return SceneManager.LoadSceneAsync(sceneName);
                yield return null;
                yield return null;

                var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
                Assert.IsNotNull(canvas, $"{sceneName} has no Canvas.");

                var canvasRect = (RectTransform)canvas.transform;
                foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(true))
                {
                    if (!graphic.raycastTarget) continue;

                    // A full-screen Button is a deliberate control, not a blocker: BootLoader's
                    // "tap anywhere to enter" is exactly that. Only untargeted decoration
                    // covering the screen is the bug.
                    if (graphic.GetComponentInParent<Button>() != null) continue;

                    var rt = graphic.rectTransform;
                    bool covers = rt.rect.width >= canvasRect.rect.width * 0.98f &&
                                  rt.rect.height >= canvasRect.rect.height * 0.98f;

                    Assert.IsFalse(covers,
                        $"'{graphic.name}' in {sceneName} covers the whole canvas and has " +
                        "raycastTarget on, so it swallows every click in the game. It needs " +
                        "raycastTarget off, or LustHouse > Setup > Repair input blocking.");
                }
            }
        }

        [UnityTest]
        public IEnumerator TappingATile_LaunchesTheSlotSceneWithADefinition()
        {
            yield return LoadLobby();

            var controller = Object.FindFirstObjectByType<LobbyController>();
            Assert.IsNotNull(controller, "No LobbyController in the scene.");
            Assert.IsNotNull(controller.catalog, "LobbyController.catalog is null, so the grid " +
                                               "has no games to tap. The controller warns about " +
                                               "this at Awake; check the console.");

            // Fund the wallet first. LobbyPresenter rebuilds the entire grid on every wallet
            // change, so anything grabbed before this point is destroyed by it — and clicking
            // a destroyed GameObject does nothing at all, which looks exactly like a dead
            // button. Grant, settle, then go looking for the tile.
            long before = Core.WalletService.Instance?.Balance(Core.Currency.Coins) ?? 0;
            Core.WalletService.Instance?.TryGrant(Core.Currency.Coins, 1_000_000,
                Core.LedgerReason.Admin, "lobby tap test");
            Log($"[LustHouse] coins before={before} after={Core.WalletService.Instance?.Balance(Core.Currency.Coins)}");

            yield return null;
            yield return null;

            var grid = Object.FindFirstObjectByType<LobbyGrid>();
            Assert.IsNotNull(grid, "The lobby built no LobbyGrid.");

            var tile = FirstTile();
            Assert.IsNotNull(tile, "No tile to tap.");

            var button = tile.GetComponent<Button>();
            Assert.IsNotNull(button, "The tile has no Button component.");
            Assert.IsTrue(button.isActiveAndEnabled, "The tile's Button is disabled.");
            Assert.IsTrue(button.interactable, "The tile's Button is marked non-interactable.");

            // Go through the dispatcher rather than clicking the Button directly: the tile's
            // listener calls straight into it, and calling it ourselves would prove only that
            // the method runs, not that the click reaches it.
            Log($"[LustHouse] tapping tile '{tile.name}'");
            Log($"[LustHouse] tile active={tile.gameObject.activeInHierarchy}");

            var pointer = new PointerEventData(EventSystem.current)
            {
                button = PointerEventData.InputButton.Left
            };
            ExecuteEvents.Execute(tile.gameObject, pointer, ExecuteEvents.pointerClickHandler);

            // The launch itself loads the Slot scene, and SlotSceneController.Start consumes
            // the static and clears it. So asserting on the payload here would be a race
            // against a field that is designed to be transient — it can be null because the
            // handoff succeeded. Assert on the destination instead.
            yield return WaitForScene(SlotScene);

            Assert.AreEqual(SlotScene, SceneManager.GetActiveScene().name,
                "The tap fired but never reached LobbyController.Launch, so no slot scene " +
                "loaded. Either OnGameTilePressed refused it (it now opens a shortfall sheet " +
                "rather than failing silently) or the tile's listener is not wired.");

            var slotController = Object.FindFirstObjectByType<SlotSceneController>();
            Assert.IsNotNull(slotController, "The Slot scene has no SlotSceneController.");
            Assert.IsNotNull(slotController.Definition,
                "The Slot scene loaded with no definition, so it bounces straight back to the " +
                "lobby. The payload was lost in the handoff.");

            var presenter = Object.FindFirstObjectByType<SlotPresenter>();
            Assert.IsNotNull(presenter, "The Slot scene has no SlotPresenter.");
            Assert.IsNotNull(presenter.Reels, "The slot screen built no ReelsView.");
            Assert.AreSame(slotController.Definition, presenter.Reels.Definition,
                "The reels were configured with a different definition than the one the maths " +
                "evaluated — the player would be shown a grid that was never scored.");
            Assert.Greater(presenter.Reels.ReelCount, 0,
                "The reels built no columns, so the slot screen is blank.");
        }

        /// <summary>Waits for a scene to finish loading, or gives up after a fixed budget.</summary>
        private static IEnumerator WaitForScene(string sceneName, int maxFrames = 300)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (SceneManager.GetActiveScene().name == sceneName) yield break;
                yield return null;
            }
        }

        private static void Log(string message)
        {
            TestContext.Out.WriteLine(message);
            Debug.Log(message);
        }
    }
}