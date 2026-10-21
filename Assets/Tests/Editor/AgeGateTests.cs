using System;
using System.IO;
using System.Text.RegularExpressions;
using LustHouse.Core;
using LustHouse.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LustHouse.Tests
{
    /// <summary>
    /// The age gate (plan §2.3a): the store requirement that this game is 18+.
    ///
    /// Two things are worth protecting. The first is that the gate is **closed by
    /// default** — a player who has not answered cannot be let in, and a gate that
    /// fails open is worse than no gate at all. The second is that the answer is
    /// **kept**: a gate that asks the same question on every launch is a gate that
    /// has been built wrong.
    ///
    /// The wording is protected too. It is not decoration: the text on this screen
    /// is part of what the store reviews, and "no real money" and "18" have to be on
    /// it.
    /// </summary>
    public class AgeGateTests
    {
        // ---- the gate is closed by default ------------------------------------

        [Test]
        public void TheGateIsClosedUntilSomebodyConfirms()
        {
            // No player has answered, so there is no answer to read.
            Assert.IsFalse(AgeGate.IsConfirmed,
                "The age gate is open for a player who has never confirmed they are 18.");
        }

        [Test]
        public void TheGateWillNotOpenWhenTheAnswerCannotBeKept()
        {
            // The answer has to be recorded against a player. If there is no player
            // to record it against, the gate must stay shut rather than open on an
            // answer that will be forgotten.
            LogAssert.Expect(LogType.Error, new Regex("age gate was answered with no player"));

            Assert.IsFalse(AgeGate.Confirm(),
                "The gate opened even though the answer could not be kept. The player " +
                "would be asked the same question on every launch.");
        }

        // ---- the answer is kept -----------------------------------------------

        [Test]
        public void TheConfirmationSurvivesASave()
        {
            // The whole point of the gate is that it is asked once. The answer lives
            // in the save, and the save is the only thing that survives a launch.
            string dir = Path.Combine(Path.GetTempPath(),
                "lusthouse_agegate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);

            try
            {
                var save = new SaveSystem(Path.Combine(dir, "save.json"));

                var doc = SaveDocument.CreateNew(new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc));
                doc.player.ageConfirmed = true;
                save.Save(doc);

                var loaded = save.Load();
                Assert.IsTrue(loaded.ok, "The save did not load: " + loaded.warning);
                Assert.IsTrue(loaded.document.player.ageConfirmed,
                    "The age confirmation was thrown away by a save and reload, so the " +
                    "player would be asked their age on every launch.");
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
                catch { /* best effort */ }
            }
        }

        // ---- what the player is shown ----------------------------------------

        [Test]
        public void TheGateOffersBothAnswers()
        {
            var host = new GameObject("AgeGateHost");
            try
            {
                AgeGateOverlay.Create(host.transform, null);

                var confirm = host.GetComponentsInChildren<Button>(true);
                Assert.IsTrue(HasButton(confirm, "ConfirmAge"),
                    "The age gate has no way for the player to say they are 18.");
                Assert.IsTrue(HasButton(confirm, "RefuseAge"),
                    "The age gate has no way for the player to say they are under 18. " +
                    "A gate that only offers one answer is not a gate.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheGateBlocksWhateverIsBehindIt()
        {
            // The gate has to be a wall, not a suggestion. If the backdrop does not
            // take the click, the player can reach the lobby underneath it.
            var host = new GameObject("AgeGateHost");
            try
            {
                AgeGateOverlay.Create(host.transform, null);

                Image backdrop = null;
                foreach (var image in host.GetComponentsInChildren<Image>(true))
                    if (image.name == "Backdrop") backdrop = image;

                Assert.IsNotNull(backdrop, "The age gate has no backdrop.");
                Assert.IsTrue(backdrop.raycastTarget,
                    "The age gate's backdrop does not take the click, so the game " +
                    "underneath it is still reachable while the gate is up.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheGateCoversTheWholeScreen()
        {
            // The overlay has to fill its parent. A RectTransform that is merely
            // parented keeps its own hundred-pixel square, so the backdrop behind
            // the panel would be a hundred-pixel square too — the game underneath
            // would still be on screen and still be clickable, and the gate would
            // be a sticker on it rather than a wall.
            var host = new GameObject("AgeGateHost", typeof(RectTransform));
            try
            {
                var hostRect = (RectTransform)host.transform;
                UiFactory.Stretch(hostRect);
                hostRect.sizeDelta = new Vector2(1920f, 1080f);

                var overlay = AgeGateOverlay.Create(host.transform, null);
                var rect = (RectTransform)overlay.transform;

                Assert.AreEqual(Vector2.zero, rect.anchorMin,
                    "The age gate's overlay is not anchored to the top-left of its parent, " +
                    "so it does not fill the screen.");
                Assert.AreEqual(Vector2.one, rect.anchorMax,
                    "The age gate's overlay is not anchored to the bottom-right of its " +
                    "parent, so it does not fill the screen.");

                Image backdrop = null;
                foreach (var image in host.GetComponentsInChildren<Image>(true))
                    if (image.name == "Backdrop") backdrop = image;

                Assert.IsNotNull(backdrop, "The age gate has no backdrop.");
                Assert.AreEqual(rect.rect.width, backdrop.rectTransform.rect.width, 1f,
                    "The age gate's backdrop is narrower than the gate, so the game is " +
                    "still visible down each side of it.");
                Assert.AreEqual(rect.rect.height, backdrop.rectTransform.rect.height, 1f,
                    "The age gate's backdrop is shorter than the gate, so the game is " +
                    "still visible above and below it.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void TheGateSaysWhatTheGameIsAndWhoMayPlay()
        {
            // The wording is the requirement. A store reviewer reads this text, and
            // "18" and "no real money" have to be on it.
            var host = new GameObject("AgeGateHost");
            try
            {
                AgeGateOverlay.Create(host.transform, null);

                string all = "";
                foreach (var text in host.GetComponentsInChildren<Text>(true))
                    all += " " + text.text;

                Assert.IsTrue(all.Contains("18"),
                    "The age gate never says what age the player has to be.");
                Assert.IsTrue(all.ToLowerInvariant().Contains("real money"),
                    "The age gate never says that no real money is involved.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static bool HasButton(Button[] buttons, string name)
        {
            for (int i = 0; i < buttons.Length; i++)
                if (buttons[i].name == name) return true;
            return false;
        }
    }
}
