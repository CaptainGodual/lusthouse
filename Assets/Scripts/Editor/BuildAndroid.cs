#if UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// Reproducible Android device builds (plan §5.5).
    ///
    /// The Build Profiles window is easy to drift: an AAB instead of an APK, a stale
    /// scripting backend, or "Both" input handling will stop an Android build before
    /// IL2CPP even starts. This script pins the settings the project actually ships,
    /// runs the checks that have blocked builds before, and writes a single APK that
    /// can be installed with adb.
    ///
    /// Menu:  LustHouse > Build > Android APK
    ///        LustHouse > Build > Android APK + Run
    /// CLI:   Unity.exe -batchmode -quit -projectPath . ^
    ///          -executeMethod LustHouse.EditorTools.BuildAndroid.BuildApk ^
    ///          -logFile Logs/android-build.log
    /// </summary>
    public static class BuildAndroid
    {
        private static readonly string OutputFolder = Path.Combine("Builds", "Android");
        private const string ReleaseApk = "LustHouse.apk";
        private const string DevelopmentApk = "LustHouse-dev.apk";

        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [MenuItem("LustHouse/Build/Android APK", priority = 40)]
        public static void BuildApk()
        {
            Build(development: false, autoRun: false);
        }

        [MenuItem("LustHouse/Build/Android APK + Run", priority = 41)]
        public static void BuildAndRunApk()
        {
            Build(development: true, autoRun: true);
        }

        [MenuItem("LustHouse/Build/Apply Android Player settings", priority = 38)]
        public static void ConfigureAndroidPlayerSettings()
        {
            ApplyAndroidPlayerSettings();
            AssetDatabase.SaveAssets();
            UnityEngine.Debug.Log(
                "[LustHouse] Android Player settings applied: IL2CPP, ARM64, min API 24, target API automatic.");
        }

        private static void ApplyAndroidPlayerSettings()
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCompilerConfiguration(
                NamedBuildTarget.Android, Il2CppCompilerConfiguration.Release);

            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.bundleVersionCode = Math.Max(1, PlayerSettings.Android.bundleVersionCode);
            PlayerSettings.Android.useCustomKeystore = false;
        }

        [MenuItem("LustHouse/Build/Validate Android setup", priority = 39)]
        public static void ValidateAndroidSetup()
        {
            if (!Validate(requireDevice: false, out string error))
            {
                UnityEngine.Debug.LogError($"[LustHouse] Android setup is not ready: {error}");
                return;
            }

            UnityEngine.Debug.Log("[LustHouse] Android setup is ready: build support, JNI module and input handling checks passed.");
        }

        /// <summary>Batch-mode entry point for a release APK.</summary>
        public static void BuildApkBatch()
        {
            Build(development: false, autoRun: false);
        }

        /// <summary>Batch-mode entry point for a development APK installed on the connected device.</summary>
        public static void BuildAndRunApkBatch()
        {
            Build(development: true, autoRun: true);
        }

        private static void Build(bool development, bool autoRun)
        {
            if (!Validate(autoRun, out string error))
            {
                Fail(error);
                return;
            }

            if (!ApplyAndroidSettings(development))
            {
                Fail("Could not switch the active build target to Android.");
                return;
            }

            if (!BuildAddressablesContent())
            {
                // Not a hard stop. The runtime copes with a missing catalogue — every game
                // falls back to its bundled content — so the build is still worth making.
                // What is lost is the remote-pack path, and the log says so.
                UnityEngine.Debug.LogWarning(
                    "[LustHouse] The Addressables content build failed. This APK ships " +
                    "without a content catalogue: every game will use its bundled content, " +
                    "which works, but remote packs cannot be fetched.");
            }

            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                Fail("EditorBuildSettings has no enabled scenes.");
                return;
            }

            if (!scenes[0].EndsWith("BootLoader.unity", StringComparison.OrdinalIgnoreCase))
            {
                Fail("BootLoader must be the first enabled scene in Build Settings.");
                return;
            }

            string apkName = development ? DevelopmentApk : ReleaseApk;
            string outputPath = Path.Combine(ProjectRoot, OutputFolder, apkName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Path.Combine(ProjectRoot, OutputFolder));

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            if (development)
                options.options |= BuildOptions.Development | BuildOptions.AllowDebugging;

            if (autoRun)
                options.options |= BuildOptions.AutoRunPlayer;

            UnityEngine.Debug.Log(
                $"[LustHouse] Building Android {(development ? "development" : "release")} APK: {outputPath}");

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result == BuildResult.Succeeded)
            {
                long apkBytes = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
                UnityEngine.Debug.Log(
                    $"[LustHouse] Android build succeeded: {outputPath} " +
                    $"({apkBytes / (1024f * 1024f):F1} MB APK, {summary.totalTime.TotalSeconds:F0}s).");

                if (!autoRun)
                    UnityEngine.Debug.Log($"[LustHouse] Install with: adb install -r \"{outputPath}\"");

                return;
            }

            foreach (BuildStep step in report.steps)
            {
                foreach (BuildStepMessage message in step.messages)
                {
                    if (message.type == LogType.Error || message.type == LogType.Exception)
                        UnityEngine.Debug.LogError($"[LustHouse] {step.name}: {message.content}");
                }
            }

            Fail($"Android build {summary.result}: {summary.totalErrors} error(s). See the Console for details.");
        }

        /// <summary>
        /// Builds the Addressables content before the player.
        ///
        /// The content lands in <c>Assets/StreamingAssets</c>, which the player build copies
        /// into the app's streaming assets. Without it the device ships no catalogue at all:
        /// <c>Addressables.InitializeAsync</c> fails at runtime, every launch pays a failed
        /// initialisation, and the remote-pack path is dead on device.
        /// </summary>
        private static bool BuildAddressablesContent()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                UnityEngine.Debug.LogError(
                    "[LustHouse] No Addressables settings; the build would ship without a " +
                    "content catalogue.");
                return false;
            }

            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);

            if (!string.IsNullOrEmpty(result.Error))
            {
                UnityEngine.Debug.LogError(
                    $"[LustHouse] The Addressables content build failed: {result.Error}");
                return false;
            }

            UnityEngine.Debug.Log(
                $"[LustHouse] Addressables content built: {result.LocationCount} locations " +
                $"in {result.Duration:F0}s -> {result.OutputPath}");

            return true;
        }

        private static bool ApplyAndroidSettings(bool development)
        {
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                return false;

            // A device test needs an APK. An App Bundle cannot be installed with adb.
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.development = development;
            EditorUserBuildSettings.allowDebugging = development;

            ApplyAndroidPlayerSettings();
            return true;
        }

        private static bool Validate(bool requireDevice, out string error)
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                error = "Android Build Support is not installed for this Unity editor. " +
                        "Add it from Unity Hub > Installs > Add modules.";
                return false;
            }

            string manifestPath = Path.Combine(ProjectRoot, "Packages", "manifest.json");
            string lockPath = Path.Combine(ProjectRoot, "Packages", "packages-lock.json");

            if (!File.Exists(manifestPath) || !File.ReadAllText(manifestPath).Contains("com.unity.modules.androidjni"))
            {
                error = "Packages/manifest.json does not declare com.unity.modules.androidjni. " +
                        "Android player compilation needs it for AndroidJavaClass (Unity.Localization).";
                return false;
            }

            if (!File.Exists(lockPath) || !File.ReadAllText(lockPath).Contains("com.unity.modules.androidjni"))
            {
                error = "com.unity.modules.androidjni is declared but not resolved yet. " +
                        "Open Package Manager or restart Unity so packages-lock.json is regenerated.";
                return false;
            }

            string activeInputHandler = ReadActiveInputHandler();
            if (activeInputHandler != "0")
            {
                error = activeInputHandler == "2"
                    ? "Active Input Handling is set to Both, which Android rejects. " +
                      "Set Edit > Project Settings > Player > Active Input Handling to 'Input Manager (Old)' " +
                      "and restart Unity."
                    : "Active Input Handling must be 'Input Manager (Old)' because the UI uses " +
                      "StandaloneInputModule. Change it in Edit > Project Settings > Player and restart Unity.";
                return false;
            }

            if (requireDevice && !HasConnectedAndroidDevice(out string devices))
            {
                error = "No authorized Android device is attached. Enable USB debugging, connect the device, " +
                        $"and confirm it appears in `adb devices`.{Environment.NewLine}{devices}";
                return false;
            }

            error = null;
            return true;
        }

        private static string ReadActiveInputHandler()
        {
            string settingsPath = Path.Combine(ProjectRoot, "ProjectSettings", "ProjectSettings.asset");
            if (!File.Exists(settingsPath))
                return null;

            Match match = Regex.Match(
                File.ReadAllText(settingsPath),
                @"^\s*activeInputHandler:\s*(\d+)",
                RegexOptions.Multiline);

            return match.Success ? match.Groups[1].Value : null;
        }

        private static bool HasConnectedAndroidDevice(out string devices)
        {
            devices = string.Empty;
            string adbPath = Path.Combine(
                EditorApplication.applicationContentsPath,
                "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools",
                Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb");

            if (!File.Exists(adbPath))
            {
                devices = $"adb was not found at {adbPath}.";
                return false;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = adbPath,
                    Arguments = "devices",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                        return false;

                    string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                    process.WaitForExit(5000);
                    devices = output.Trim();

                    return output
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Any(line => line.TrimEnd().EndsWith("\tdevice", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (Exception exception)
            {
                devices = $"Could not run adb: {exception.Message}";
                return false;
            }
        }

        private static void Fail(string message)
        {
            UnityEngine.Debug.LogError($"[LustHouse] Android build blocked: {message}");

            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}
#endif
