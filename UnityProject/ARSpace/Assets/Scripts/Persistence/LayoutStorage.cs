using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ARSpace.Persistence
{
    /// <summary>
    /// Handles persistent storage of office layout files on local device storage.
    /// Provides atomic file writes, tolerant deserialization, and preset resource loading.
    /// </summary>
    public static class LayoutStorage
    {
        public const string LayoutsFolderName = "Layouts";
        public const string PresetsResourceFolder = "LayoutPresets";

        public static string LayoutsDirectory
        {
            get
            {
                string dir = Path.Combine(Application.persistentDataPath, LayoutsFolderName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                return dir;
            }
        }

        /// <summary>
        /// Saves a layout to persistent storage using an atomic write (.tmp -> rename).
        /// </summary>
        public static bool SaveLayout(LayoutModel layout, out string savedFilePath)
        {
            savedFilePath = null;
            if (layout == null)
            {
                Debug.LogError("[LayoutStorage] Cannot save null layout.");
                return false;
            }

            try
            {
                if (string.IsNullOrEmpty(layout.layoutId))
                {
                    layout.layoutId = Guid.NewGuid().ToString("N");
                }

                layout.modifiedAt = DateTime.UtcNow.ToString("o");
                if (string.IsNullOrEmpty(layout.createdAt))
                {
                    layout.createdAt = layout.modifiedAt;
                }

                string dir = LayoutsDirectory;
                string fileName = $"{layout.layoutId}.json";
                string targetPath = Path.Combine(dir, fileName);
                string tempPath = targetPath + ".tmp";

                string json = JsonUtility.ToJson(layout, true);

                // Atomic write
                File.WriteAllText(tempPath, json);
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempPath, targetPath);

                savedFilePath = targetPath;
                Debug.Log($"[LayoutStorage] ✓ Successfully saved layout '{layout.layoutName}' ({layout.layoutId}) to {targetPath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LayoutStorage] Failed to save layout '{layout?.layoutName}': {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Loads a saved layout by its unique ID.
        /// </summary>
        public static LayoutModel LoadLayout(string layoutId)
        {
            if (string.IsNullOrEmpty(layoutId))
            {
                Debug.LogError("[LayoutStorage] layoutId cannot be null or empty.");
                return null;
            }

            string filePath = Path.Combine(LayoutsDirectory, $"{layoutId}.json");
            return LoadLayoutFromFile(filePath);
        }

        /// <summary>
        /// Loads a layout from an absolute file path.
        /// </summary>
        public static LayoutModel LoadLayoutFromFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogWarning($"[LayoutStorage] Layout file does not exist: {filePath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(filePath);
                LayoutModel layout = JsonUtility.FromJson<LayoutModel>(json);
                if (layout == null)
                {
                    Debug.LogError($"[LayoutStorage] Deserialization returned null for {filePath}");
                    return null;
                }
                return layout;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LayoutStorage] Failed loading layout from {filePath}: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Deletes a saved layout file by ID.
        /// </summary>
        public static bool DeleteLayout(string layoutId)
        {
            if (string.IsNullOrEmpty(layoutId)) return false;

            try
            {
                string filePath = Path.Combine(LayoutsDirectory, $"{layoutId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    Debug.Log($"[LayoutStorage] Deleted layout: {layoutId}");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LayoutStorage] Error deleting layout {layoutId}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Returns metadata headers for all saved user layouts in persistent storage.
        /// </summary>
        public static List<LayoutHeader> ListSavedLayouts()
        {
            var headers = new List<LayoutHeader>();
            string dir = LayoutsDirectory;

            if (!Directory.Exists(dir)) return headers;

            string[] files = Directory.GetFiles(dir, "*.json");
            foreach (var file in files)
            {
                try
                {
                    string json = File.ReadAllText(file);
                    LayoutModel layout = JsonUtility.FromJson<LayoutModel>(json);
                    if (layout != null)
                    {
                        headers.Add(new LayoutHeader
                        {
                            layoutId = layout.layoutId,
                            layoutName = string.IsNullOrEmpty(layout.layoutName) ? Path.GetFileNameWithoutExtension(file) : layout.layoutName,
                            modifiedAt = layout.modifiedAt,
                            objectCount = layout.entries != null ? layout.entries.Count : 0,
                            seatingCapacity = layout.totalSeatingCapacity,
                            floorArea = layout.recordedFloorArea,
                            filePath = file
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[LayoutStorage] Error reading header from {file}: {ex.Message}");
                }
            }

            headers.Sort((a, b) => string.Compare(b.modifiedAt, a.modifiedAt, StringComparison.Ordinal));
            return headers;
        }

        /// <summary>
        /// Loads a built-in preset layout from Resources/LayoutPresets/.
        /// </summary>
        public static LayoutModel LoadPreset(string presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return null;

            try
            {
                TextAsset asset = Resources.Load<TextAsset>($"{PresetsResourceFolder}/{presetName}");
                if (asset == null)
                {
                    Debug.LogError($"[LayoutStorage] Preset not found in Resources: {PresetsResourceFolder}/{presetName}");
                    return null;
                }

                LayoutModel layout = JsonUtility.FromJson<LayoutModel>(asset.text);
                return layout;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LayoutStorage] Failed loading preset '{presetName}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Returns headers for all built-in layout presets in Resources.
        /// </summary>
        public static List<LayoutHeader> ListPresets()
        {
            var headers = new List<LayoutHeader>();
            try
            {
                TextAsset[] presets = Resources.LoadAll<TextAsset>(PresetsResourceFolder);
                foreach (var asset in presets)
                {
                    try
                    {
                        LayoutModel layout = JsonUtility.FromJson<LayoutModel>(asset.text);
                        if (layout != null)
                        {
                            headers.Add(new LayoutHeader
                            {
                                layoutId = asset.name,
                                layoutName = string.IsNullOrEmpty(layout.layoutName) ? asset.name : layout.layoutName,
                                modifiedAt = layout.modifiedAt,
                                objectCount = layout.entries != null ? layout.entries.Count : 0,
                                seatingCapacity = layout.totalSeatingCapacity,
                                floorArea = layout.recordedFloorArea,
                                filePath = $"Resources/{PresetsResourceFolder}/{asset.name}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[LayoutStorage] Failed reading preset '{asset.name}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LayoutStorage] Failed listing presets: {ex.Message}");
            }

            return headers;
        }
    }
}
