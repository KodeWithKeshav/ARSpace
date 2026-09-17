using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

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
                PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) == ScriptingImplementation.IL2CPP,
                $"Current: {PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android)}");

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
                PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Android) == ApiCompatibilityLevel.NET_Standard_2_1,
                $"Current: {PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Android)}");

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
            }
        }
    }
}
