using UnityEditor;
using UnityEngine;
using System.IO;

public class BuildScript
{
    [MenuItem("ARSpace/Build Android APK")]
    public static void BuildAndroidAPK()
    {
        string buildPath = Path.GetFullPath(Path.Combine(Application.dataPath, "../../builds"));
        if (!Directory.Exists(buildPath))
        {
            Directory.CreateDirectory(buildPath);
        }

        string apkPath = Path.Combine(buildPath, "ARSpace.apk");

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions();
        buildPlayerOptions.scenes = new[] { "Assets/Scenes/ARWorkspace.unity" };
        buildPlayerOptions.locationPathName = apkPath;
        buildPlayerOptions.target = BuildTarget.Android;
        buildPlayerOptions.options = BuildOptions.None;

        Debug.Log("Starting Android APK Build...");
        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        
        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"APK built successfully! Saved to: {apkPath}");
            EditorUtility.DisplayDialog("Build Success", $"APK saved to:\n{apkPath}", "OK");
        }
        else
        {
            Debug.LogError($"Build failed with {report.summary.totalErrors} errors.");
            EditorUtility.DisplayDialog("Build Failed", "Check Console log for details.", "OK");
        }
    }
}
