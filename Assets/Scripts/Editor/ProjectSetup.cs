#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using LustHouse.Core;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LustHouse.EditorTools
{
    /// <summary>
    /// One-click project setup for plan §0.4 P0–P2. Everything that needs the editor —
    /// the URP asset, scene files, build-settings order, Addressables groups, and the
    /// first content assets — is generated here rather than hand-authored as YAML.
    ///
    /// Run from the menu, or headless:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod LustHouse.EditorTools.ProjectSetup.RunAll
    ///
    /// Safe to re-run: every step checks for what already exists.
    /// </summary>
    public static class ProjectSetup
    {
        private const string SettingsFolder = "Assets/Settings";
        private const string UrpAssetPath = SettingsFolder + "/LustHouseURP.asset";
        private const string RendererPath = SettingsFolder + "/LustHouseRenderer2D.asset";
        private const string AddressableSettingsPath = "Assets/AddressableAssetsData";

        private static readonly string[] AddressableGroups = { "Boot", "Slots", "Deferred" };

        private static readonly (string name, string[] scenes)[] SceneSpecs =
        {
            ("BootLoader", new[] { "BootLoader", "Lobby", "Slot" }),
            ("Lobby", new[] { "Lobby", "Slot" }),
            ("Slot", new[] { "Slot", "Lobby" })
        };

        [MenuItem("LustHouse/Setup/Run all setup", priority = 0)]
        public static void RunAll()
        {
            EnsureFolders();
            ConfigureRenderPipeline();
            ConfigureProjectSettings();
            CreateScenes();
            ConfigureBuildSettings();
            CreateAddressableGroups();
            ContentBootstrap.CreateStarterContent();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[LustHouse] Setup complete. Open Assets/Scenes/BootLoader.unity and press Play.");
        }

        [MenuItem("LustHouse/Setup/1. Render pipeline (URP 2D)", priority = 20)]
        public static void ConfigureRenderPipeline()
        {
            EnsureFolders();

            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<Renderer2DData>();
                AssetDatabase.CreateAsset(rendererData, RendererPath);
                Debug.Log($"[LustHouse] Created 2D renderer data at {RendererPath}");
            }

            var pipelineAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (pipelineAsset == null)
            {
                pipelineAsset = UniversalRenderPipelineAsset.Create(rendererData);
                AssetDatabase.CreateAsset(pipelineAsset, UrpAssetPath);
                Debug.Log($"[LustHouse] Created URP asset at {UrpAssetPath}");
            }
            else
            {
                // Re-point rather than recreate so serialized quality overrides survive.
                // rendererDataList is read-only, so go through SerializedObject.
                var so = new SerializedObject(pipelineAsset);
                var list = so.FindProperty("m_RendererDataList");
                if (list != null && list.arraySize > 0)
                {
                    list.GetArrayElementAtIndex(0).objectReferenceValue = rendererData;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            // This is the line that was {fileID: 0} for the whole life of the project.
            GraphicsSettings.defaultRenderPipeline = pipelineAsset;
            EnsureAlwaysIncludedShaders();

            AssignQualityLevels(pipelineAsset);

            AssetDatabase.SaveAssets();
            Debug.Log("[LustHouse] URP assigned to GraphicsSettings and all quality levels.");
        }

        /// <summary>
        /// Adds the shaders a 2D game needs to Always Included Shaders. Without this,
        /// UI and sprite shaders get stripped from WebGL builds and render magenta.
        /// </summary>
        private static void EnsureAlwaysIncludedShaders()
        {
            var graphicsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
            if (graphicsAsset == null || graphicsAsset.Length == 0) return;

            var so = new SerializedObject(graphicsAsset[0]);
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray) return;

            var wanted = new[]
            {
                Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default"),
                Shader.Find("Universal Render Pipeline/UI/UI/Default")
            };

            bool changed = false;
            foreach (var shader in wanted)
            {
                if (shader == null) continue;

                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                {
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { present = true; break; }
                }

                if (present) continue;

                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
            }

            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[LustHouse] Added 2D/UI shaders to Always Included Shaders.");
        }

        /// <summary>
        /// QualitySettings has no public per-level render-pipeline setter, so the levels
        /// are written through a SerializedObject on the settings asset itself. All six
        /// levels were {fileID: 0} before this.
        /// </summary>
        private static void AssignQualityLevels(RenderPipelineAsset pipelineAsset)
        {
            var qualityAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (qualityAsset == null || qualityAsset.Length == 0)
            {
                Debug.LogWarning("[LustHouse] Could not open QualitySettings.asset; " +
                                 "the URP asset is assigned globally instead.");
                return;
            }

            var so = new SerializedObject(qualityAsset[0]);
            var levels = so.FindProperty("customRenderPipeline");
            if (levels == null || !levels.isArray)
            {
                Debug.LogWarning("[LustHouse] QualitySettings.customRenderPipeline not found; " +
                                 "the URP asset is assigned globally instead.");
                return;
            }

            for (int i = 0; i < levels.arraySize; i++)
                levels.GetArrayElementAtIndex(i).objectReferenceValue = pipelineAsset;

            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[LustHouse] Assigned URP to {levels.arraySize} quality level(s).");
        }




        [MenuItem("LustHouse/Setup/2. Project settings (colour, identity)", priority = 21)]
        public static void ConfigureProjectSettings()
        {
            // Gamma -> Linear. Free right now because Assets/Art is still empty, and
            // required by the neon/glow art direction in plan §2.1.
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[LustHouse] Color space set to Linear.");
            }

            PlayerSettings.companyName = "Lust House";
            PlayerSettings.productName = "Lust House";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.lusthouse.casino");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.lusthouse.casino");
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, "com.lusthouse.casino");

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            PlayerSettings.runInBackground = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;

            // Age rating and store category are declared in the store consoles, not here.
            // The only thing this project controls is the 18+ gate itself (§ guardrails 3).

#if UNITY_EDITOR_WIN
            PlayerSettings.SetIl2CppCompilerConfiguration(NamedBuildTarget.Standalone, Il2CppCompilerConfiguration.Release);
#endif
            Debug.Log("[LustHouse] Player settings updated (identity, orientation, colour space).");
        }

        [MenuItem("LustHouse/Setup/3. Scenes", priority = 22)]
        public static void CreateScenes()
        {
            EnsureFolders();

            foreach (var spec in SceneSpecs)
                CreateScene(spec.name, spec.scenes);

            AssetDatabase.Refresh();
        }

        private static void CreateScene(string sceneName, string[] additiveScenes)
        {
            string path = $"Assets/Scenes/{sceneName}.unity";
            if (File.Exists(path))
            {
                Debug.Log($"[LustHouse] Scene {sceneName} already exists; leaving it alone.");
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(0x14, 0x0A, 0x14, 0xFF); // velvet.base (§2.1)

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            var eventSystemGo = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

            switch (sceneName)
            {
                case "BootLoader":
                    // Must exist before anything asks for WalletService.Instance.
                    var servicesGo = new GameObject("Services", typeof(ServicesBootstrap));
                    servicesGo.GetComponent<ServicesBootstrap>().lobbySceneName = "Lobby";

                    var bootGo = new GameObject("BootSequence", typeof(LustHouse.Boot.BootSequence));
                    bootGo.GetComponent<LustHouse.Boot.BootSequence>().lobbySceneName = "Lobby";
                    break;

                case "Lobby":
                    var lobbyGo = new GameObject("Lobby", typeof(LustHouse.Lobby.LobbyController));
                    var lobby = lobbyGo.GetComponent<LustHouse.Lobby.LobbyController>();
                    lobby.catalog = ContentBootstrap.Catalog;
                    lobby.slotSceneName = "Slot";
                    break;

                case "Slot":
                    var reelGo = new GameObject("ReelEngine", typeof(LustHouse.Slots.ReelEngine));
                    var reel = reelGo.GetComponent<LustHouse.Slots.ReelEngine>();
                    reel.sessionSeed = 0;

                    var runnerGo = new GameObject("SlotGameRunner", typeof(LustHouse.Slots.SlotGameRunner));
                    var runner = runnerGo.GetComponent<LustHouse.Slots.SlotGameRunner>();
                    runner.reelEngine = reel;

                    var ctrlGo = new GameObject("SlotSceneController", typeof(LustHouse.Slots.SlotSceneController));
                    var ctrl = ctrlGo.GetComponent<LustHouse.Slots.SlotSceneController>();
                    ctrl.runner = runner;
                    ctrl.reelEngine = reel;
                    break;
            }

            // A label so the greybox is readable in the Scene view.
            var labelGo = new GameObject("SceneLabel");
            var label = labelGo.AddComponent<UnityEngine.UI.Text>();
            label.text = sceneName;
            label.font = null;
            label.alignment = TextAnchor.MiddleCenter;
            var rect = labelGo.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            labelGo.transform.SetParent(canvasGo.transform, false);

            EditorSceneManager.SaveScene(scene, path);
            Debug.Log($"[LustHouse] Created scene {path}");
        }

        [MenuItem("LustHouse/Setup/4. Build settings", priority = 23)]
        public static void ConfigureBuildSettings()
        {
            var order = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene("Assets/Scenes/BootLoader.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Lobby.unity", true),
                new EditorBuildSettingsScene("Assets/Scenes/Slot.unity", true)
            };

            EditorBuildSettings.scenes = order.ToArray();
            Debug.Log("[LustHouse] Build settings order: BootLoader, Lobby, Slot.");
        }

        [MenuItem("LustHouse/Setup/5. Addressables groups", priority = 24)]
        public static void CreateAddressableGroups()
        {
            var settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                Debug.LogWarning("[LustHouse] Could not create Addressables settings; skipping groups.");
                return;
            }

            foreach (var groupName in AddressableGroups)
            {
                if (settings.FindGroup(groupName) != null) continue;

                var schemas = new System.Collections.Generic.List<AddressableAssetGroupSchema>
                {
                    new BundledAssetGroupSchema(),
                    new ContentUpdateGroupSchema()
                };

                var group = settings.CreateGroup(groupName, false, false, true, schemas);
                if (group == null) continue;

                var bundled = group.GetSchema<BundledAssetGroupSchema>();
                if (bundled != null)
                {
                    bundled.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                    bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
                    bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
                }

                Debug.Log($"[LustHouse] Created addressable group '{groupName}'");
            }

            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("LustHouse/Setup/6. Starter content", priority = 25)]
        public static void CreateStarterContent() => ContentBootstrap.CreateStarterContent();

        // ---- helpers -----------------------------------------------------------

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Scenes");
            EnsureFolder("Assets", "Settings");
            EnsureFolder("Assets/Data", "Slots");
            EnsureFolder("Assets/Data", "Catalog");
            EnsureFolder("Assets/Data", "Economy");
            EnsureFolder("Assets/Data", "Cosmetics");
        }

        private static void EnsureFolder(string parent, string leaf)
        {
            if (AssetDatabase.IsValidFolder($"{parent}/{leaf}")) return;
            AssetDatabase.CreateFolder(parent, leaf);
        }

        /// <summary>Batchmode entry point.</summary>
        public static void RunAllBatch() => RunAll();
    }
}
#endif