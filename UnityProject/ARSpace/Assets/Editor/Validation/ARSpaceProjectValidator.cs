using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Build;

namespace ARSpace.Editor.Validation
{
    /// <summary>
    /// Editor window and menu item: ARSpace → Validate Project
    ///
    /// Reports a green/red checklist of whether all build and project settings are correct.
    /// Run before every build to catch misconfigurations early.
    /// </summary>
    public class ARSpaceProjectValidator : EditorWindow
    {
        struct CheckResult
        {
            public string Name;
            public bool Passed;
            public string Detail;

            public CheckResult(string name, bool passed, string detail = "")
            {
                Name = name;
                Passed = passed;
                Detail = detail;
            }
        }

        List<CheckResult> m_Results = new List<CheckResult>();
        Vector2 m_ScrollPos;
        int m_PassCount;
        int m_FailCount;

        [MenuItem("ARSpace/Validate Project", priority = 101)]
        public static void ShowWindow()
        {
            var window = GetWindow<ARSpaceProjectValidator>("ARSpace Validator");
            window.minSize = new Vector2(450, 400);
            window.RunAllChecks();
        }

        [MenuItem("ARSpace/Validate Project (Console Only)", priority = 102)]
        public static void ValidateToConsole()
        {
            var validator = new ARSpaceProjectValidator();
            validator.RunAllChecks();

            Debug.Log($"[ARSpaceProjectValidator] ═══════════════════════════════════");
            Debug.Log($"[ARSpaceProjectValidator]   Results: {validator.m_PassCount} PASS, {validator.m_FailCount} FAIL");
            Debug.Log($"[ARSpaceProjectValidator] ═══════════════════════════════════");

            foreach (var r in validator.m_Results)
            {
                if (r.Passed)
                    Debug.Log($"  ✓ {r.Name}");
                else
                    Debug.LogWarning($"  ✗ {r.Name}: {r.Detail}");
            }
        }

        void RunAllChecks()
        {
            m_Results.Clear();
            m_PassCount = 0;
            m_FailCount = 0;

            // Build settings
            Check("Build target: Android",
                EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android,
                $"Current: {EditorUserBuildSettings.activeBuildTarget}");

            Check("Scripting backend: IL2CPP",
                PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android) == ScriptingImplementation.IL2CPP,
                $"Current: {PlayerSettings.GetScriptingBackend(NamedBuildTarget.Android)}");

            Check("Target arch: ARM64 only",
                PlayerSettings.Android.targetArchitectures == AndroidArchitecture.ARM64,
                $"Current: {PlayerSettings.Android.targetArchitectures}");

            // Graphics APIs
            var apis = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            bool glesOnly = apis.Length == 1 && apis[0] == UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3;
            Check("Graphics API: OpenGLES3 only",
                !PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android) && glesOnly,
                glesOnly ? "Auto is on" : $"Current: {string.Join(", ", apis)}");

            Check("API compatibility: .NET Standard 2.1",
                PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Android) == ApiCompatibilityLevel.NET_Standard,
                $"Current: {PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Android)}");

            Check("Min Android API ≥ 26",
                PlayerSettings.Android.minSdkVersion >= AndroidSdkVersions.AndroidApiLevel26,
                $"Current: {PlayerSettings.Android.minSdkVersion}");

            Check("Colour space: Linear",
                PlayerSettings.colorSpace == ColorSpace.Linear,
                $"Current: {PlayerSettings.colorSpace}");

            // XR settings
            CheckXRSettings();

            // URP asset
            CheckURPAsset();

            // Scene in build list
            CheckSceneInBuildSettings();

            // Phase 2: Plane Grid Visualizer
            CheckPlaneVisualizer();

            // Phase 3: Catalogue Data Layer
            CheckCatalogue();

            // Phase 4: Placement Pipeline
            CheckPlacementPipeline();

            // Phase 5: Selection and Manipulation
            CheckSelectionAndManipulation();

            // Phase 6: Layout Persistence
            CheckLayoutPersistence();

            // Phase 7: Space Utilization Analysis
            CheckSpaceAnalytics();

            // Phase 8: Polish, UX & Performance
            CheckPolishAndUX();

            // Phase 9: Automated Test Suite
            CheckTests();
        }

        void CheckTests()
        {
            bool testAsmExists = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(
                "Assets/Tests/Editor/ARSpace.Tests.Editor.asmdef") != null;
            Check("ARSpace.Tests.Editor.asmdef exists", testAsmExists,
                "Missing Assets/Tests/Editor/ARSpace.Tests.Editor.asmdef");

            bool dbTestsExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Tests/Editor/FurnitureDatabaseTests.cs") != null;
            Check("FurnitureDatabaseTests.cs exists", dbTestsExists,
                "Missing Assets/Tests/Editor/FurnitureDatabaseTests.cs");

            bool persistTestsExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Tests/Editor/LayoutPersistenceTests.cs") != null;
            Check("LayoutPersistenceTests.cs exists", persistTestsExists,
                "Missing Assets/Tests/Editor/LayoutPersistenceTests.cs");

            bool analyticsTestsExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Tests/Editor/SpaceAnalyticsTests.cs") != null;
            Check("SpaceAnalyticsTests.cs exists", analyticsTestsExists,
                "Missing Assets/Tests/Editor/SpaceAnalyticsTests.cs");

            bool serviceLocatorTestsExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Tests/Editor/ServiceLocatorTests.cs") != null;
            Check("ServiceLocatorTests.cs exists", serviceLocatorTestsExists,
                "Missing Assets/Tests/Editor/ServiceLocatorTests.cs");

            bool anchorTestsExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Tests/Editor/AnchorClusteringTests.cs") != null;
            Check("AnchorClusteringTests.cs exists", anchorTestsExists,
                "Missing Assets/Tests/Editor/AnchorClusteringTests.cs");
        }

        void CheckPolishAndUX()
        {
            bool coachExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/UI/OnboardingCoach.cs") != null;
            Check("OnboardingCoach.cs exists", coachExists,
                "Missing Assets/Scripts/UI/OnboardingCoach.cs");

            bool lightBinderExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/AR/LightEstimationBinder.cs") != null;
            Check("LightEstimationBinder.cs exists", lightBinderExists,
                "Missing Assets/Scripts/AR/LightEstimationBinder.cs");

            bool presControllerExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/UI/PresentationModeController.cs") != null;
            Check("PresentationModeController.cs exists", presControllerExists,
                "Missing Assets/Scripts/UI/PresentationModeController.cs");

            bool shadowShaderExists = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Art/Shaders/ShadowReceiver.shader") != null;
            Check("ShadowReceiver.shader exists", shadowShaderExists,
                "Missing Assets/Art/Shaders/ShadowReceiver.shader");
        }

        void CheckSpaceAnalytics()
        {
            bool floorCalcExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Analysis/FloorAreaCalculator.cs") != null;
            Check("FloorAreaCalculator.cs exists", floorCalcExists,
                "Missing Assets/Scripts/Analysis/FloorAreaCalculator.cs");

            bool spaceServiceExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Analysis/SpaceAnalyticsService.cs") != null;
            Check("SpaceAnalyticsService.cs exists", spaceServiceExists,
                "Missing Assets/Scripts/Analysis/SpaceAnalyticsService.cs");

            bool analyticsPanelExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/UI/AnalyticsPanel.cs") != null;
            Check("AnalyticsPanel.cs exists", analyticsPanelExists,
                "Missing Assets/Scripts/UI/AnalyticsPanel.cs");
        }

        void CheckLayoutPersistence()
        {
            bool modelExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Persistence/LayoutModel.cs") != null;
            Check("LayoutModel.cs exists", modelExists,
                "Missing Assets/Scripts/Persistence/LayoutModel.cs");

            bool storageExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Persistence/LayoutStorage.cs") != null;
            Check("LayoutStorage.cs exists", storageExists,
                "Missing Assets/Scripts/Persistence/LayoutStorage.cs");

            bool originManagerExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Persistence/LayoutOriginManager.cs") != null;
            Check("LayoutOriginManager.cs exists", originManagerExists,
                "Missing Assets/Scripts/Persistence/LayoutOriginManager.cs");

            bool menuPanelExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/UI/LayoutMenuPanel.cs") != null;
            Check("LayoutMenuPanel.cs exists", menuPanelExists,
                "Missing Assets/Scripts/UI/LayoutMenuPanel.cs");

            bool preset1Exists = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/LayoutPresets/open_plan_24seat.json") != null;
            Check("Preset: Open Plan 24-Seat exists", preset1Exists,
                "Missing Assets/Resources/LayoutPresets/open_plan_24seat.json");

            bool preset2Exists = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/LayoutPresets/hybrid_team_zone.json") != null;
            Check("Preset: Hybrid Team Zone exists", preset2Exists,
                "Missing Assets/Resources/LayoutPresets/hybrid_team_zone.json");

            bool preset3Exists = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/Resources/LayoutPresets/executive_floor.json") != null;
            Check("Preset: Executive Floor exists", preset3Exists,
                "Missing Assets/Resources/LayoutPresets/executive_floor.json");
        }

        void CheckSelectionAndManipulation()
        {
            bool selectionServiceExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/ObjectSelectionService.cs") != null;
            Check("ObjectSelectionService.cs exists", selectionServiceExists,
                "Missing Assets/Scripts/Placement/ObjectSelectionService.cs");

            bool selectionVisualExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/SelectionVisual.cs") != null;
            Check("SelectionVisual.cs exists", selectionVisualExists,
                "Missing Assets/Scripts/Placement/SelectionVisual.cs");

            bool gestureRouterExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/GestureRouter.cs") != null;
            Check("GestureRouter.cs exists", gestureRouterExists,
                "Missing Assets/Scripts/Placement/GestureRouter.cs");

            bool manipulatorExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/ObjectManipulator.cs") != null;
            Check("ObjectManipulator.cs exists", manipulatorExists,
                "Missing Assets/Scripts/Placement/ObjectManipulator.cs");

            bool rotationGizmoExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/RotationGizmo.cs") != null;
            Check("RotationGizmo.cs exists", rotationGizmoExists,
                "Missing Assets/Scripts/Placement/RotationGizmo.cs");

            bool placementPreviewExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/PlacementPreview.cs") != null;
            Check("PlacementPreview.cs exists", placementPreviewExists,
                "Missing Assets/Scripts/Placement/PlacementPreview.cs");

            bool snapToEdgeExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/SnapToObjectEdge.cs") != null;
            Check("SnapToObjectEdge.cs exists", snapToEdgeExists,
                "Missing Assets/Scripts/Placement/SnapToObjectEdge.cs");

            bool selectionToolbarExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/UI/SelectionToolbar.cs") != null;
            Check("SelectionToolbar.cs exists", selectionToolbarExists,
                "Missing Assets/Scripts/UI/SelectionToolbar.cs");
        }

        void CheckPlacementPipeline()
        {
            bool arPlacementManagerExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/AR/ARPlacementManager.cs") != null;
            Check("ARPlacementManager.cs exists", arPlacementManagerExists,
                "Missing Assets/Scripts/AR/ARPlacementManager.cs");

            bool reticleExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/PlacementReticle.cs") != null;
            Check("PlacementReticle.cs exists", reticleExists,
                "Missing Assets/Scripts/Placement/PlacementReticle.cs");

            bool anchorServiceExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/AnchorService.cs") != null;
            Check("AnchorService.cs exists", anchorServiceExists,
                "Missing Assets/Scripts/Placement/AnchorService.cs");

            bool registryExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Placement/PlacedObjectRegistry.cs") != null;
            Check("PlacedObjectRegistry.cs exists", registryExists,
                "Missing Assets/Scripts/Placement/PlacedObjectRegistry.cs");

            bool catalogServiceExists = AssetDatabase.LoadAssetAtPath<MonoScript>(
                "Assets/Scripts/Furniture/CatalogService.cs") != null;
            Check("CatalogService.cs exists", catalogServiceExists,
                "Missing Assets/Scripts/Furniture/CatalogService.cs");

            bool oldPlacementGone = !System.IO.File.Exists("Assets/Scripts/AR/Placement.cs");
            Check("Obsolete Placement.cs deleted", oldPlacementGone,
                "Assets/Scripts/AR/Placement.cs should be deleted");

            bool oldTapToPlaceGone = !System.IO.File.Exists("Assets/Scripts/AR/TapToPlace.cs");
            Check("Obsolete TapToPlace.cs deleted", oldTapToPlaceGone,
                "Assets/Scripts/AR/TapToPlace.cs should be deleted");
        }

        void CheckCatalogue()
        {
            var db = AssetDatabase.LoadAssetAtPath<Furniture.FurnitureDatabase>(
                "Assets/ScriptableObjects/FurnitureDatabase.asset");

            Check("FurnitureDatabase asset exists", db != null,
                "Run ARSpace → Rebuild Furniture Catalogue");

            if (db != null)
            {
                Check($"Catalogue has items ({db.Count} registered)", db.Count > 0,
                    "Catalogue is empty. Run ARSpace → Rebuild Furniture Catalogue.");

                int missingPrefabs = 0;
                int missingThumbnails = 0;
                int missingIds = 0;
                var seenIds = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                int duplicates = 0;

                foreach (var item in db.Items)
                {
                    if (item == null) continue;
                    if (string.IsNullOrEmpty(item.Id)) missingIds++;
                    else if (!seenIds.Add(item.Id)) duplicates++;

                    if (item.Prefab == null) missingPrefabs++;
                    if (item.Thumbnail == null) missingThumbnails++;
                }

                Check("All catalogue items have valid prefabs", missingPrefabs == 0,
                    $"{missingPrefabs} items missing prefabs");
                Check("All catalogue items have thumbnails", missingThumbnails == 0,
                    $"{missingThumbnails} items missing thumbnails");
                Check("All catalogue items have unique IDs", missingIds == 0 && duplicates == 0,
                    $"{missingIds} missing IDs, {duplicates} duplicate IDs");
            }
        }

        void CheckPlaneVisualizer()
        {
            bool gridShaderExists = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Art/Shaders/PlaneGrid.shader") != null;
            Check("PlaneGrid.shader exists", gridShaderExists,
                "Missing Assets/Art/Shaders/PlaneGrid.shader");

            bool lineShaderExists = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Art/Shaders/UnlitLine.shader") != null;
            Check("UnlitLine.shader exists", lineShaderExists,
                "Missing Assets/Art/Shaders/UnlitLine.shader");

            var gridMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_PlaneGrid.mat");
            bool gridMatValid = gridMat != null && gridMat.shader != null && gridMat.shader.name == "ARSpace/PlaneGrid";
            Check("M_PlaneGrid.mat configured with ARSpace/PlaneGrid", gridMatValid,
                "Run ARSpace → Setup Plane Grid Assets to generate material");

            var outlineMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/M_PlaneOutline.mat");
            bool outlineMatValid = outlineMat != null && outlineMat.shader != null;
            Check("M_PlaneOutline.mat configured", outlineMatValid,
                "Run ARSpace → Setup Plane Grid Assets to generate outline material");

            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Plane.prefab");
            bool hasVisualizer = planePrefab != null && planePrefab.GetComponent<AR.PlaneGridVisualizer>() != null;
            Check("Plane.prefab has PlaneGridVisualizer", hasVisualizer,
                "Run ARSpace → Setup Plane Grid Assets to attach visualizer to Plane.prefab");
        }

        void CheckXRSettings()
        {
            // Check XR General Settings exist
            bool xrSettingsExist = AssetDatabase.LoadAssetAtPath<Object>(
                "Assets/XR/XRGeneralSettings.asset") != null;
            Check("XR General Settings asset exists", xrSettingsExist,
                "Missing Assets/XR/XRGeneralSettings.asset");

            // Check ARCore Settings
            bool arcoreSettingsExist = AssetDatabase.LoadAssetAtPath<Object>(
                "Assets/XR/Settings/AR Core Settings.asset") != null;
            Check("ARCore Settings asset exists", arcoreSettingsExist,
                "Missing Assets/XR/Settings/AR Core Settings.asset");

            // Check ARCore loader is present
            bool loaderExists = AssetDatabase.LoadAssetAtPath<Object>(
                "Assets/XR/Loaders") != null;
            Check("XR Loaders directory exists", loaderExists,
                "Missing Assets/XR/Loaders/");

            // NOTE: Verifying m_AutomaticLoading=true and ARCore Requirement=Required
            // requires reading the ScriptableObject at runtime or parsing YAML.
            // We flag this as a manual check and handle it via BuildSettingsEnforcer.
            // In Phase 1 report we document that m_AutomaticLoading must be set to true.
        }

        void CheckURPAsset()
        {
            // Check that a URP asset is assigned in Graphics settings
            var urpAsset = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            Check("URP Pipeline Asset assigned in Graphics Settings",
                urpAsset != null,
                "No render pipeline asset assigned. Assign URP-Performant in Project Settings → Graphics.");
        }

        void CheckSceneInBuildSettings()
        {
            const string scenePath = "Assets/Scenes/ARWorkspace.unity";
            var scenes = EditorBuildSettings.scenes;
            bool found = false;
            bool isIndex0 = false;

            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path == scenePath && scenes[i].enabled)
                {
                    found = true;
                    isIndex0 = i == 0;
                    break;
                }
            }

            Check("ARWorkspace scene in Build Settings", found,
                "Add Assets/Scenes/ARWorkspace.unity to Build Settings.");
            Check("ARWorkspace scene at build index 0", isIndex0,
                "ARWorkspace should be the first scene in Build Settings.");
        }

        void Check(string name, bool passed, string failDetail)
        {
            m_Results.Add(new CheckResult(name, passed, failDetail));
            if (passed) m_PassCount++;
            else m_FailCount++;
        }

        // ── Editor Window GUI ──────────────────────────────────

        void OnGUI()
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField("ARSpace Project Validator", EditorStyles.boldLabel);
            GUILayout.Space(4);

            if (GUILayout.Button("Run All Checks", GUILayout.Height(30)))
            {
                RunAllChecks();
            }

            if (m_Results.Count == 0)
            {
                EditorGUILayout.HelpBox("Click 'Run All Checks' to validate the project.", MessageType.Info);
                return;
            }

            GUILayout.Space(8);

            // Summary
            Color originalColor = GUI.backgroundColor;
            GUI.backgroundColor = m_FailCount == 0 ? new Color(0.2f, 0.8f, 0.2f) : new Color(0.9f, 0.3f, 0.3f);
            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = originalColor;

            EditorGUILayout.LabelField(
                $"Results: {m_PassCount} passed, {m_FailCount} failed",
                EditorStyles.boldLabel);
            EditorGUILayout.EndVertical();

            GUILayout.Space(4);

            // Results list
            m_ScrollPos = EditorGUILayout.BeginScrollView(m_ScrollPos);

            foreach (var result in m_Results)
            {
                EditorGUILayout.BeginHorizontal();

                // Status icon
                GUIStyle style = new GUIStyle(EditorStyles.label);
                if (result.Passed)
                {
                    style.normal.textColor = new Color(0.2f, 0.8f, 0.2f);
                    EditorGUILayout.LabelField("✓", style, GUILayout.Width(20));
                    EditorGUILayout.LabelField(result.Name);
                }
                else
                {
                    style.normal.textColor = new Color(0.9f, 0.3f, 0.3f);
                    EditorGUILayout.LabelField("✗", style, GUILayout.Width(20));
                    EditorGUILayout.LabelField($"{result.Name} — {result.Detail}");
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(8);

            if (m_FailCount > 0)
            {
                if (GUILayout.Button("Fix: Apply Android AR Build Settings", GUILayout.Height(28)))
                {
                    BuildSettingsEnforcer.ApplySettings();
                    RunAllChecks();
                }

                if (GUILayout.Button("Fix: Setup Plane Grid Assets", GUILayout.Height(28)))
                {
                    AssetBuilders.PlaneVisualizerBuilder.SetupPlaneGridAssets();
                    RunAllChecks();
                }

                if (GUILayout.Button("Fix: Rebuild Furniture Catalogue", GUILayout.Height(28)))
                {
                    AssetBuilders.FurnitureAssetBuilder.RebuildCatalogue();
                    RunAllChecks();
                }
            }
        }
    }
}
