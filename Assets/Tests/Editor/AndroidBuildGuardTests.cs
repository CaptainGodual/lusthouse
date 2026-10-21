using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace LustHouse.Tests
{
    /// <summary>
    /// Android build settings that have broken device builds before (plan §5.5).
    /// These are cheap editor-side checks, not a substitute for building the APK.
    /// </summary>
    public class AndroidBuildGuardTests
    {
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string ReadProjectFile(string relativePath) =>
            File.ReadAllText(Path.Combine(ProjectRoot, relativePath));

        [Test]
        public void Manifest_DeclaresAndroidJniModule()
        {
            // Unity.Localization calls AndroidJavaClass under UNITY_ANDROID. Without this
            // built-in module the player compile fails with CS0246 before Gradle runs.
            StringAssert.Contains(
                "\"com.unity.modules.androidjni\"",
                ReadProjectFile("Packages/manifest.json"),
                "Packages/manifest.json must declare com.unity.modules.androidjni.");
        }

        [Test]
        public void AndroidPlayer_IsConfiguredForDeviceBuilds()
        {
            Assert.AreEqual(
                ScriptingImplementation.IL2CPP,
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android),
                "Android builds must use IL2CPP (plan §5.5).");

            Assert.AreNotEqual(
                0,
                (int)(PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64),
                "Android builds must include ARM64 (plan §5.5).");

            Assert.GreaterOrEqual(
                (int)PlayerSettings.Android.minSdkVersion,
                (int)AndroidSdkVersions.AndroidApiLevel24,
                "Android builds target API 24 or newer (plan §5.5).");

            Assert.IsFalse(
                PlayerSettings.Android.useCustomKeystore,
                "Device test builds use Unity's debug keystore; release signing is configured later.");
        }

        [Test]
        public void ActiveInputHandling_UsesLegacyInputManager()
        {
            // The UI uses StandaloneInputModule, so the project must stay on the legacy
            // Input Manager. "Both" is rejected by Android, and new-only breaks the UI.
            Match match = Regex.Match(
                ReadProjectFile("ProjectSettings/ProjectSettings.asset"),
                @"^\s*activeInputHandler:\s*(\d+)",
                RegexOptions.Multiline);

            Assert.IsTrue(match.Success, "Could not read activeInputHandler from ProjectSettings.asset.");
            Assert.AreEqual(
                "0",
                match.Groups[1].Value,
                "Set Active Input Handling to 'Input Manager (Old)' (0) and restart Unity; the UI uses " +
                "StandaloneInputModule, and Android rejects 'Both'.");
        }
    }
}
