using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using ARSpace.Furniture;
using ARSpace.Placement;

namespace ARSpace.Editor.AssetBuilders
{
    /// <summary>
    /// Editor asset builder for the ARSpace catalogue.
    /// Menu item: ARSpace → Rebuild Furniture Catalogue
    ///
    /// Scans Assets/Art/Models for .glb/.fbx models and idempotently builds:
    /// 1. Runtime-ready prefabs under Assets/Prefabs/Furniture/ with PlacedObject,
    ///    BoxCollider sized to bounds, shadow-casting meshes, and normalized floor-level pivot.
    /// 2. Rendered thumbnail sprites under Assets/Art/Thumbnails/.
    /// 3. FurnitureItem ScriptableObjects with measured bounds, seating capacity, and CRE metadata.
    /// 4. FurnitureDatabase asset holding all catalogued items with zero validation errors.
    /// </summary>
    public static class FurnitureAssetBuilder
    {
        const string ModelsRoot = "Assets/Art/Models";
        const string PrefabsRoot = "Assets/Prefabs/Furniture";
        const string ThumbnailsRoot = "Assets/Art/Thumbnails";
        const string ItemsRoot = "Assets/ScriptableObjects/Furniture";
        const string DatabasePath = "Assets/ScriptableObjects/FurnitureDatabase.asset";

        struct ModelCREMetadata
        {
            public FurnitureCategory Category;
            public int SeatCount;
            public float ClearanceMargin;
            public bool WallAligned;
            public string DisplayName;
            public string Description;

            public ModelCREMetadata(FurnitureCategory cat, int seats, float clearance, bool wallAligned, string name, string desc)
            {
                Category = cat;
                SeatCount = seats;
                ClearanceMargin = clearance;
                WallAligned = wallAligned;
                DisplayName = name;
                Description = desc;
            }
        }

        [MenuItem("ARSpace/Rebuild Furniture Catalogue", priority = 10)]
        public static void RebuildCatalogue()
        {
            Debug.Log("[FurnitureAssetBuilder] ═════════════════════════════════════════════");
            Debug.Log("[FurnitureAssetBuilder] Starting catalogue rebuild from 3D model library...");

            EnsureDirectory(PrefabsRoot);
            EnsureDirectory(ThumbnailsRoot);
            EnsureDirectory(ItemsRoot);

            string[] modelGuids = AssetDatabase.FindAssets("t:Model", new[] { ModelsRoot });
            var modelPaths = new List<string>();

            foreach (var guid in modelGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".glb" || ext == ".fbx")
                {
                    modelPaths.Add(path);
                }
            }

            Debug.Log($"[FurnitureAssetBuilder] Discovered {modelPaths.Count} 3D models in {ModelsRoot}.");

            var generatedItems = new List<FurnitureItem>();

            for (int i = 0; i < modelPaths.Count; i++)
            {
                string modelPath = modelPaths[i];
                string modelName = Path.GetFileNameWithoutExtension(modelPath);

                EditorUtility.DisplayProgressBar(
                    "Rebuilding Catalogue",
                    $"Processing {modelName} ({i + 1}/{modelPaths.Count})...",
                    (float)i / modelPaths.Count
                );

                try
                {
                    ModelCREMetadata meta = ResolveMetadata(modelPath, modelName);

                    // 1. Build Prefab
                    string prefabPath = $"{PrefabsRoot}/{modelName}.prefab";
                    GameObject prefab = BuildPrefab(modelPath, prefabPath, modelName);

                    // 2. Generate Thumbnail
                    string thumbPath = $"{ThumbnailsRoot}/{modelName}.png";
                    Sprite thumbnail = GenerateThumbnail(prefab, thumbPath);

                    // 3. Build FurnitureItem ScriptableObject
                    string itemPath = $"{ItemsRoot}/{modelName}.asset";
                    FurnitureItem item = BuildFurnitureItem(itemPath, modelName, meta, prefab, thumbnail);

                    if (item != null)
                    {
                        generatedItems.Add(item);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[FurnitureAssetBuilder] Failed processing {modelName}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            EditorUtility.ClearProgressBar();

            // 4. Update FurnitureDatabase
            UpdateDatabase(generatedItems);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FurnitureAssetBuilder] ✓ Successfully rebuilt {generatedItems.Count} catalogue items.");
            Debug.Log("[FurnitureAssetBuilder] ═════════════════════════════════════════════");
        }

        static GameObject BuildPrefab(string modelPath, string targetPrefabPath, string modelName)
        {
            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[FurnitureAssetBuilder] Could not load model at {modelPath}");
                return null;
            }

            // Create container hierarchy
            GameObject root = new GameObject(modelName);
            var placedObj = root.AddComponent<PlacedObject>();

            GameObject modelInstance = Object.Instantiate(modelAsset, root.transform);
            modelInstance.name = "Model";

            // Normalise rotation so +Z faces front
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            // Configure shadow casting and calculate bounds across all renderers
            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsInitialized = false;

            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;

                if (!boundsInitialized)
                {
                    bounds = r.bounds;
                    boundsInitialized = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            // Offset child so root origin (0, 0, 0) rests exactly on the floor plane at Y = 0
            if (boundsInitialized)
            {
                modelInstance.transform.localPosition = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
            }

            // Recalculate bounds in root space for the combined BoxCollider
            Bounds finalBounds = new Bounds(
                new Vector3(0, bounds.size.y * 0.5f, 0),
                bounds.size
            );

            // Ensure collider has non-zero volume
            Vector3 colSize = finalBounds.size;
            colSize.x = Mathf.Max(colSize.x, 0.2f);
            colSize.y = Mathf.Max(colSize.y, 0.2f);
            colSize.z = Mathf.Max(colSize.z, 0.2f);

            var boxCol = root.AddComponent<BoxCollider>();
            boxCol.center = finalBounds.center;
            boxCol.size = colSize;

            placedObj.SetCollider(boxCol);
            placedObj.SetRenderers(renderers);

            // Save as prefab asset idempotently
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, targetPrefabPath);
            Object.DestroyImmediate(root);

            return savedPrefab;
        }

        static Sprite GenerateThumbnail(GameObject prefab, string targetPath)
        {
            // If thumbnail already exists, reload it to avoid redundant render work
            if (File.Exists(targetPath))
            {
                var existingSprite = AssetDatabase.LoadAssetAtPath<Sprite>(targetPath);
                if (existingSprite != null)
                    return existingSprite;
            }

            Texture2D texture = RenderOffscreenPreview(prefab, 256, 256);
            if (texture == null)
            {
                // Fallback to solid brand icon if offscreen rendering is unavailable
                texture = CreatePlaceholderTexture(256, 256);
            }

            byte[] pngData = texture.EncodeToPNG();
            File.WriteAllBytes(targetPath, pngData);
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);

            // Configure texture importer as Sprite
            var importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(targetPath);
        }

        static Texture2D RenderOffscreenPreview(GameObject prefab, int width, int height)
        {
            if (prefab == null) return null;

            GameObject tempInstance = Object.Instantiate(prefab);
            tempInstance.hideFlags = HideFlags.HideAndDontSave;

            GameObject camGo = new GameObject("ThumbnailCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = camGo.AddComponent<Camera>();

            GameObject lightGo = new GameObject("ThumbnailLight");
            lightGo.hideFlags = HideFlags.HideAndDontSave;
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.color = Color.white;
            lightGo.transform.rotation = Quaternion.Euler(50, -30, 0);

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);

            try
            {
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 0.0f); // Transparent studio background
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 50f;

                // Frame the object bounds nicely
                var boxCol = tempInstance.GetComponent<BoxCollider>();
                Bounds bounds = boxCol != null ? boxCol.bounds : new Bounds(tempInstance.transform.position, Vector3.one);

                float radius = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.9f;
                Vector3 viewDir = new Vector3(1f, 0.75f, -1.2f).normalized;
                camGo.transform.position = bounds.center + viewDir * (radius * 2.2f);
                camGo.transform.LookAt(bounds.center);

                RenderTexture.active = rt;
                cam.Render();

                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);

                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(tempInstance);
            }

            return result;
        }

        static Texture2D CreatePlaceholderTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            Color brand = new Color(1.0f, 0.42f, 0.0f, 0.9f);
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = brand;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        static FurnitureItem BuildFurnitureItem(string assetPath, string modelName, ModelCREMetadata meta, GameObject prefab, Sprite thumbnail)
        {
            FurnitureItem item = AssetDatabase.LoadAssetAtPath<FurnitureItem>(assetPath);
            bool isNew = (item == null);

            if (isNew)
            {
                item = ScriptableObject.CreateInstance<FurnitureItem>();
            }

            // Derive stable GUID id (preserve existing if already assigned)
            string stableId = (isNew || string.IsNullOrEmpty(item.Id))
                ? $"cre_{meta.Category.ToString().ToLowerInvariant()}_{modelName.ToLowerInvariant()}"
                : item.Id;

            item.SetIdentity(stableId, meta.DisplayName, meta.Description);
            item.SetClassification(meta.Category, meta.SeatCount, meta.WallAligned);

            // Compute real world size and footprint from prefab collider
            Vector3 size = Vector3.one;
            if (prefab != null)
            {
                var box = prefab.GetComponent<BoxCollider>();
                if (box != null)
                    size = box.size;
            }

            Vector2 footprint = new Vector2(size.x, size.z);
            item.SetDimensions(size, footprint, meta.ClearanceMargin);
            item.SetAssets(prefab, thumbnail);
            item.SetManipulationConstraints(true, 0.5f, 1.8f, 45f);

            if (isNew)
            {
                AssetDatabase.CreateAsset(item, assetPath);
            }

            EditorUtility.SetDirty(item);
            return item;
        }

        static void UpdateDatabase(List<FurnitureItem> items)
        {
            FurnitureDatabase db = AssetDatabase.LoadAssetAtPath<FurnitureDatabase>(DatabasePath);
            bool isNew = (db == null);

            if (isNew)
            {
                db = ScriptableObject.CreateInstance<FurnitureDatabase>();
                AssetDatabase.CreateAsset(db, DatabasePath);
                Debug.Log($"[FurnitureAssetBuilder] Created new FurnitureDatabase asset at {DatabasePath}");
            }

            db.SetItems(items);
            EditorUtility.SetDirty(db);
        }

        static ModelCREMetadata ResolveMetadata(string modelPath, string modelName)
        {
            string lowerName = modelName.ToLowerInvariant();
            string lowerPath = modelPath.ToLowerInvariant();

            // 1. Workstations & Cubicles
            if (lowerPath.Contains("workstation") || lowerName.Contains("cubicle"))
            {
                if (lowerName.Contains("cubicle_3"))
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 6, 1.0f, false, "6-Person Team Pod Cubicle", "Modular high-density 6-person workstations with privacy acoustic panels.");
                if (lowerName.Contains("cubicle_2"))
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 4, 0.9f, false, "4-Person Collaborative Cubicle", "Four-station linear bench with integrated power and data raceways.");
                return new ModelCREMetadata(FurnitureCategory.Workstations, 2, 0.8f, false, "2-Person Focus Cubicle", "Dual face-to-face workstation unit with wire management.");
            }

            // 2. Desks
            if (lowerPath.Contains("desk"))
            {
                if (lowerName.StartsWith("conf_desk"))
                {
                    if (lowerName.Contains("4"))
                        return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 12, 1.2f, false, "Executive Boardroom Table (12P)", "Premium veneer conference table seating 12 with integrated AV connectivity.");
                    if (lowerName.Contains("2"))
                        return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 10, 1.0f, false, "Conference Table (10P)", "Standard commercial conference table with central cable grommets.");
                    if (lowerName.Contains("3"))
                        return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 6, 0.8f, false, "Small Conference Table (6P)", "Huddle-room conference table seating 6.");
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 8, 1.0f, false, "Mid-Size Conference Table (8P)", "8-person rectangular conference table for meeting rooms.");
                }

                if (lowerName.StartsWith("recep"))
                {
                    if (lowerName.Contains("2"))
                        return new ModelCREMetadata(FurnitureCategory.Reception, 2, 1.2f, true, "Dual Reception Counter", "Two-station welcoming counter with elevated transaction shelf.");
                    return new ModelCREMetadata(FurnitureCategory.Reception, 1, 1.0f, true, "Executive Reception Desk", "Curved architectural reception counter with illuminated facade.");
                }

                if (lowerName.Contains("office_desk_2"))
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 2, 0.6f, false, "Dual Office Desk", "Shared twin desk configuration for team clusters.");

                return new ModelCREMetadata(FurnitureCategory.Workstations, 1, 0.6f, false, "Ergonomic Office Desk", "Individual height-adjustable workstation surface.");
            }

            // 3. Chairs
            if (lowerPath.Contains("chair") || lowerName.Contains("chair"))
            {
                if (lowerName.Contains("chair_4"))
                    return new ModelCREMetadata(FurnitureCategory.ExecutiveCabins, 1, 0.5f, false, "High-Back Executive Chair", "Ergonomic leather high-back executive chair with lumbar support.");
                if (lowerName.Contains("chair_3"))
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 1, 0.4f, false, "Conference Swivel Chair", "Breathable mesh conference chair with castors.");
                if (lowerName.Contains("chair_5"))
                    return new ModelCREMetadata(FurnitureCategory.Reception, 1, 0.4f, false, "Guest Reception Chair", "Contemporary guest lounge chair with chrome sled base.");

                return new ModelCREMetadata(FurnitureCategory.Workstations, 1, 0.4f, false, "Task Office Chair", "Standard ergonomic office task chair with 3D armrests.");
            }

            // 4. Rooms & Cabins
            if (lowerPath.Contains("room"))
            {
                if (lowerName.Contains("ceo"))
                    return new ModelCREMetadata(FurnitureCategory.ExecutiveCabins, 4, 1.5f, false, "CEO Suite Layout", "Full executive cabin suite with managerial desk, credentials, and meeting zone.");
                if (lowerName.Contains("meeting"))
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 8, 1.5f, false, "Modular Meeting Room Unit", "Enclosed acoustic meeting zone with conference setting.");
                if (lowerName.Contains("booth"))
                    return new ModelCREMetadata(FurnitureCategory.Partitions, 2, 0.6f, false, "Acoustic Phone Booth", "Sound-isolated focus booth for private calls and 1-on-1 meetings.");

                return new ModelCREMetadata(FurnitureCategory.ExecutiveCabins, 3, 1.2f, false, "Managerial Cabin", "Private office cabin with desk and guest seating.");
            }

            // 5. Sofas & Lounges
            if (lowerPath.Contains("sofa"))
            {
                int seats = 3;
                if (lowerName.Contains("2") || lowerName.Contains("4")) seats = 2;
                if (lowerName.Contains("5")) seats = 4;

                var cat = lowerName.Contains("couch_4") || lowerName.Contains("couch_5")
                    ? FurnitureCategory.Cafeteria
                    : FurnitureCategory.Reception;

                return new ModelCREMetadata(cat, seats, 0.6f, false, $"Modular Lounge Sofa ({seats}P)", $"Upholstered {seats}-seater modular sofa for breakout spaces and reception.");
            }

            // 6. Food & Beverage
            if (lowerPath.Contains("food") || lowerPath.Contains("beverage"))
            {
                if (lowerName.Contains("cafe"))
                    return new ModelCREMetadata(FurnitureCategory.Cafeteria, 0, 1.2f, true, "Cafeteria Service Counter", "Solid-surface cafeteria serving counter with storage.");
                return new ModelCREMetadata(FurnitureCategory.Cafeteria, 0, 0.8f, true, "Vending Machine Unit", "Standard automated refreshments and snack vending unit.");
            }

            // 7. Equipments
            if (lowerPath.Contains("equipment"))
            {
                if (lowerName.Contains("white_board"))
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.6f, false, "Mobile Whiteboard", "Double-sided magnetic dry-erase whiteboard on locking castors.");
                if (lowerName.Contains("printer"))
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.8f, false, "Multifunction Floor Printer", "Commercial networked MFP printer/scanner station.");
                if (lowerName.Contains("water"))
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.5f, true, "Water Dispenser Unit", "Floor-standing commercial water filtration and cooler unit.");
                if (lowerName.Contains("cabinet"))
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.6f, true, "Lockable Storage Cabinet", "Dual-door steel archival storage cabinet.");
                if (lowerName.Contains("monitor"))
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.3f, false, "Presentation Display Screen", "High-definition commercial display monitor on stand.");
                if (lowerName.Contains("message"))
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.4f, true, "Notice Bulletin Board", "Wall-mounted commercial information board.");

                return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.3f, false, "Desk Equipment", "Desktop office accessory unit.");
            }

            // 8. Decor & Games
            if (lowerPath.Contains("decor") || lowerPath.Contains("game"))
            {
                if (lowerName.Contains("plant"))
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 0.3f, false, "Biophilic Office Plant", "Indoor biophilic floor planter for enhanced workplace well-being.");
                if (lowerName.Contains("pool"))
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 1.5f, false, "Breakout Pool Table", "Full-size slate pool table for employee recreational zones.");
                if (lowerName.Contains("foosball"))
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 1.2f, false, "Foosball Game Table", "Commercial four-player table football unit.");

                return new ModelCREMetadata(FurnitureCategory.Decor, 0, 0.4f, true, "Plant Display Rack", "Multi-tiered vertical green display rack.");
            }

            // Default fallback
            return new ModelCREMetadata(FurnitureCategory.Workstations, 0, 0.5f, false, modelName, "Workplace component.");
        }

        static void EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
            }
        }
    }
}
