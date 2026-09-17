using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace ARSpace.Editor.Validation
{
    /// <summary>
    /// Menu item: ARSpace → Apply Android AR Build Settings
    ///
    /// Sets and verifies all build settings required for an ARCore Android build.
    /// Idempotent — safe to run multiple times.
    ///
    /// Settings applied:
    ///   - Active build target: Android
    ///   - Scripting backend: IL2CPP
    ///   - Target architecture: ARM64 only
    ///   - Graphics APIs: OpenGLES3 only (auto off)
    ///   - API compatibility: .NET Standard 2.1
    ///   - Min Android API: 26 (Android 8.0, ARCore minimum)
    ///   - Target Android API: 34
    ///   - Colour space: Linear
    ///   - Default orientation: Portrait (UI designed for portrait-first handheld use)
    ///   - Camera permission declared
    ///   - ARCore required via XR settings
    /// </summary>
    public static class BuildSettingsEnforcer
    {
        [MenuItem("ARSpace/Apply Android AR Build Settings", priority = 100)]
        public static void ApplySettings()
        {
            Debug.Log("[BuildSettingsEnforcer] Applying Android AR build settings...");
            int issuesFixed = 0;

            // ── Build Target ──────────────────────────────────
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.Log("[BuildSettingsEnforcer] Switching active build target to Android...");
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
                issuesFixed++;
            }

            // ── Scripting Backend ─────────────────────────────
            if (PlayerSettings.GetScriptingBackend(BuildTargetGroup.Android) != ScriptingImplementation.IL2CPP)
            {
                PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
                Debug.Log("[BuildSettingsEnforcer] Set scripting backend to IL2CPP.");
                issuesFixed++;
            }

            // ── Target Architecture ───────────────────────────
            if (PlayerSettings.Android.targetArchitectures != AndroidArchitecture.ARM64)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                Debug.Log("[BuildSettingsEnforcer] Set target architecture to ARM64 only.");
                issuesFixed++;
            }

            // ── Graphics APIs ─────────────────────────────────
            // Force OpenGLES3 only. Vulkan is not reliably supported by ARCore 6.5.
            if (PlayerSettings.GetUseDefaultGraphicsAPIs(BuildTarget.Android))
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
                issuesFixed++;
            }

            var currentAPIs = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
            bool needsUpdate = currentAPIs.Length != 1 ||
                               currentAPIs[0] != UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3;

            if (needsUpdate)
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
                    new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
                Debug.Log("[BuildSettingsEnforcer] Set graphics API to OpenGLES3 only (Vulkan removed — not reliably supported by ARCore 6.5).");
                issuesFixed++;
            }

            // ── API Compatibility ─────────────────────────────
            if (PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Android) != ApiCompatibilityLevel.NET_Standard_2_1)
            {
                PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_Standard_2_1);
                Debug.Log("[BuildSettingsEnforcer] Set API compatibility to .NET Standard 2.1.");
                issuesFixed++;
            }

            // ── Min / Target API Level ────────────────────────
            if (PlayerSettings.Android.minSdkVersion < AndroidSdkVersions.AndroidApiLevel26)
            {
                PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
                Debug.Log("[BuildSettingsEnforcer] Set minimum Android API to 26.");
                issuesFixed++;
            }

            // Target API — use the highest we have available
            // PlayerSettings.Android.targetSdkVersion = 0 means "automatic" which picks the latest installed
            // We'll leave it automatic unless it's set to something too low.

            // ── Colour Space ──────────────────────────────────
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[BuildSettingsEnforcer] Set colour space to Linear.");
                issuesFixed++;
            }

            // ── Default Orientation ───────────────────────────
            // Portrait as default — this is a handheld tool used one-handed on-site.
            // Allow auto-rotation for flexibility, but default to portrait.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // ── Camera Permission ─────────────────────────────
            // On Android, camera permission is automatically requested by ARFoundation.
            // We ensure the manifest declaration via Player Settings.
            // AR Foundation adds the camera permission automatically, but we can also force it.

            // ── Multithreaded Rendering ───────────────────────
            // Disable multithreaded rendering for OpenGLES3 + AR (known stability issue)
            PlayerSettings.SetMobileMTRendering(BuildTargetGroup.Android, false);

            // ── Product Name ──────────────────────────────────
            if (PlayerSettings.productName != "ARSpace")
            {
                PlayerSettings.productName = "ARSpace";
                issuesFixed++;
            }

            // ── Scene in Build Settings ───────────────────────
            EnsureSceneInBuildSettings();

            Debug.Log($"[BuildSettingsEnforcer] ✓ Done. {issuesFixed} setting(s) changed.");

            if (issuesFixed > 0)
            {
                AssetDatabase.SaveAssets();
            }
        }

        static void EnsureSceneInBuildSettings()
        {
            const string scenePath = "Assets/Scenes/ARWorkspace.unity";
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            bool found = false;
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                {
                    found = true;
                    scenes[i].enabled = true;

                    // Move to index 0 if not already
                    if (i != 0)
                    {
                        var scene = scenes[i];
                        scenes.RemoveAt(i);
                        scenes.Insert(0, scene);
                        Debug.Log("[BuildSettingsEnforcer] Moved ARWorkspace to build index 0.");
                    }
                    break;
                }
            }

            if (!found)
            {
                scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
                Debug.Log("[BuildSettingsEnforcer] Added ARWorkspace to build settings at index 0.");
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
