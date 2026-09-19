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
            public bool SizeByHeight;
            public float TargetMetres;

            public ModelCREMetadata(FurnitureCategory cat, int seats, float clearance, bool wallAligned, string name, string desc,
                bool sizeByHeight = false, float targetMetres = 0f)
            {
                Category = cat;
                SeatCount = seats;
                ClearanceMargin = clearance;
                WallAligned = wallAligned;
                DisplayName = name;
                Description = desc;
                SizeByHeight = sizeByHeight;
                TargetMetres = targetMetres;
            }
        }

        // Small desktop accessories make no sense as floor-standing catalogue items.
        static readonly HashSet<string> s_SkippedModels = new HashSet<string>
        {
            "phone_1", "monitor_1", "monitor_2", "message_board_1"
        };

        static readonly FurnitureCategory[] s_CategoryOrder =
        {
            FurnitureCategory.Workstations, FurnitureCategory.Seating, FurnitureCategory.ConferenceTables,
            FurnitureCategory.ExecutiveCabins, FurnitureCategory.Reception, FurnitureCategory.Cafeteria,
            FurnitureCategory.Partitions, FurnitureCategory.Equipment, FurnitureCategory.Decor
        };

        [MenuItem("ARSpace/Rebuild Furniture Catalogue", priority = 10)]
        public static void RebuildCatalogue()
        {
            Debug.Log("[FurnitureAssetBuilder] ═════════════════════════════════════════════");
            Debug.Log("[FurnitureAssetBuilder] Starting catalogue rebuild from 3D model library...");

            EnsureDirectory(PrefabsRoot);
            EnsureDirectory(ThumbnailsRoot);
            EnsureDirectory(ItemsRoot);

            // Enumerate by file extension rather than AssetDatabase's "t:Model" filter: .glb files are
            // handled by the glTFast importer and are not classified as "Model" assets, so the type
            // filter silently found only the single .fbx.
            var modelPaths = new List<string>();
            foreach (string file in Directory.GetFiles(ModelsRoot, "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".glb" || ext == ".gltf" || ext == ".fbx")
                    modelPaths.Add(file.Replace('\\', '/'));
            }
            modelPaths.Sort(StringComparer.OrdinalIgnoreCase);

            Debug.Log($"[FurnitureAssetBuilder] Discovered {modelPaths.Count} 3D models in {ModelsRoot}.");

            var generatedItems = new List<FurnitureItem>();
            int skipped = 0;

            for (int i = 0; i < modelPaths.Count; i++)
            {
                string modelPath = modelPaths[i];
                string modelName = Path.GetFileNameWithoutExtension(modelPath);

                if (s_SkippedModels.Contains(modelName.ToLowerInvariant()))
                    continue;

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
                    GameObject prefab = BuildPrefab(modelPath, prefabPath, modelName, meta);
                    if (prefab == null)
                    {
                        skipped++;
                        continue;
                    }

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
                    skipped++;
                    Debug.LogError($"[FurnitureAssetBuilder] Failed processing {modelName}: {ex.Message}\n{ex.StackTrace}");
                }
            }

            EditorUtility.ClearProgressBar();

            generatedItems.Sort((a, b) =>
            {
                int ca = Array.IndexOf(s_CategoryOrder, a.Category);
                int cb = Array.IndexOf(s_CategoryOrder, b.Category);
                return ca != cb ? ca.CompareTo(cb) : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
            });

            // 4. Update FurnitureDatabase
            UpdateDatabase(generatedItems);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[FurnitureAssetBuilder] ✓ Rebuilt {generatedItems.Count} catalogue items ({skipped} could not be imported).");
            if (skipped > 0)
                Debug.LogWarning("[FurnitureAssetBuilder] Some models could not be imported — see the errors above. " +
                                 "If they are .glb files, confirm the glTFast package (com.atteneder.gltfast) resolved without errors in the Package Manager.");
            Debug.Log("[FurnitureAssetBuilder] ═════════════════════════════════════════════");
        }

        /// <summary>
        /// Loads a model as a GameObject. .glb/.gltf files that Unity imported with the default (empty)
        /// importer — which is what the checked-in .meta files say — are switched to the glTFast importer.
        /// </summary>
        static GameObject LoadModel(string modelPath)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            if (model != null)
                return model;

            string ext = Path.GetExtension(modelPath).ToLowerInvariant();
            if (ext == ".glb" || ext == ".gltf")
            {
                Type importerType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    importerType = asm.GetType("GLTFast.Editor.GltfImporter");
                    if (importerType != null) break;
                }

                if (importerType == null)
                {
                    Debug.LogError("[FurnitureAssetBuilder] glTFast importer type not found. Install/resolve the glTFast package " +
                                   "(com.atteneder.gltfast) so .glb models can be imported.");
                    return null;
                }

                var method = typeof(AssetDatabase).GetMethod("SetImporterOverride");
                if (method != null)
                {
                    method.MakeGenericMethod(importerType).Invoke(null, new object[] { modelPath });
                    AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceUpdate);
                    model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                }
            }

            return model;
        }

        static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(false);
            bool init = false;
            Bounds b = new Bounds(root.transform.position, Vector3.zero);
            foreach (var r in renderers)
            {
                if (r is LineRenderer || r is ParticleSystemRenderer) continue;
                if (!init) { b = r.bounds; init = true; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static GameObject BuildPrefab(string modelPath, string targetPrefabPath, string modelName, ModelCREMetadata meta)
        {
            GameObject modelAsset = LoadModel(modelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[FurnitureAssetBuilder] Could not load model at {modelPath}");
                return null;
            }

            GameObject root = new GameObject(modelName);
            var placedObj = root.AddComponent<PlacedObject>();

            GameObject modelInstance = (GameObject)UnityEngine.Object.Instantiate(modelAsset, root.transform);
            modelInstance.name = "Model";
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            Bounds raw = CalculateBounds(modelInstance);
            if (raw.size.sqrMagnitude < 1e-8f)
            {
                Debug.LogError($"[FurnitureAssetBuilder] {modelName} has no visible renderers — skipped.");
                UnityEngine.Object.DestroyImmediate(root);
                return null;
            }

            // The source models come from many different tools and use wildly different units
            // (some are ~0.5 units wide, others thousands). Normalise every model to a realistic size.
            float measured = meta.SizeByHeight ? raw.size.y : Mathf.Max(raw.size.x, raw.size.z);
            float scale = 1f;
            if (meta.TargetMetres > 0f && measured > 1e-5f)
            {
                scale = meta.TargetMetres / measured;
            }
            else
            {
                float largest = Mathf.Max(raw.size.x, raw.size.y, raw.size.z);
                if (largest > 4f || largest < 0.2f)
                    scale = 1f / Mathf.Max(largest, 1e-5f);
            }
            modelInstance.transform.localScale = Vector3.one * scale;

            // Centre the model over the root and rest its lowest point exactly on the root's origin,
            // so instantiating at a floor hit puts the furniture on the floor.
            Bounds scaled = CalculateBounds(modelInstance);
            modelInstance.transform.localPosition = new Vector3(-scaled.center.x, -scaled.min.y, -scaled.center.z);

            Renderer[] renderers = modelInstance.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
            }

            Vector3 colSize = scaled.size;
            colSize.x = Mathf.Max(colSize.x, 0.1f);
            colSize.y = Mathf.Max(colSize.y, 0.1f);
            colSize.z = Mathf.Max(colSize.z, 0.1f);

            var boxCol = root.AddComponent<BoxCollider>();
            boxCol.center = new Vector3(0f, scaled.size.y * 0.5f, 0f);
            boxCol.size = colSize;

            placedObj.SetCollider(boxCol);
            placedObj.SetRenderers(renderers);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, targetPrefabPath);
            UnityEngine.Object.DestroyImmediate(root);

            return savedPrefab;
        }

        static Sprite GenerateThumbnail(GameObject prefab, string targetPath)
        {
            // Always re-render: thumbnails are cheap, and stale ones (from the old, dim renderer) look wrong in the catalogue.
            Texture2D texture = RenderOffscreenPreview(prefab, 384, 384);
            if (texture == null)
            {
                // Fallback to solid brand icon if offscreen rendering is unavailable
                texture = CreatePlaceholderTexture(384, 384);
            }

            byte[] pngData = texture.EncodeToPNG();
            File.WriteAllBytes(targetPath, pngData);
            UnityEngine.Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(targetPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(targetPath);
        }

        static Texture2D RenderOffscreenPreview(GameObject prefab, int width, int height)
        {
            if (prefab == null) return null;

            // Render far away from anything in the open scene so no stray objects end up in the frame.
            var studioOrigin = new Vector3(0f, -5000f, 0f);

            GameObject tempInstance = UnityEngine.Object.Instantiate(prefab, studioOrigin, Quaternion.identity);
            tempInstance.hideFlags = HideFlags.HideAndDontSave;

            GameObject camGo = new GameObject("ThumbnailCam");
            camGo.hideFlags = HideFlags.HideAndDontSave;
            Camera cam = camGo.AddComponent<Camera>();

            GameObject keyGo = new GameObject("ThumbnailKeyLight");
            keyGo.hideFlags = HideFlags.HideAndDontSave;
            Light key = keyGo.AddComponent<Light>();
            key.type = LightType.Directional;
            key.intensity = 1.4f;
            key.color = Color.white;
            keyGo.transform.rotation = Quaternion.Euler(45, -35, 0);

            GameObject fillGo = new GameObject("ThumbnailFillLight");
            fillGo.hideFlags = HideFlags.HideAndDontSave;
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.7f;
            fill.color = new Color(0.85f, 0.9f, 1f);
            fillGo.transform.rotation = Quaternion.Euler(25, 140, 0);

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);

            try
            {
                cam.targetTexture = rt;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.93f, 0.94f, 0.96f, 1f);
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 200f;
                cam.fieldOfView = 26f;
                cam.allowHDR = false;
                cam.allowMSAA = false;

                Bounds bounds = CalculateBounds(tempInstance);
                float radius = Mathf.Max(bounds.extents.magnitude, 0.1f);
                float distance = radius / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.02f;
                Vector3 viewDir = new Vector3(0.8f, 0.55f, -1f).normalized;
                camGo.transform.position = bounds.center + viewDir * distance;
                camGo.transform.LookAt(bounds.center);

                cam.Render();

                RenderTexture.active = rt;
                result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                result.Apply();
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);

                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(keyGo);
                UnityEngine.Object.DestroyImmediate(fillGo);
                UnityEngine.Object.DestroyImmediate(tempInstance);
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
            string key = modelName.ToLowerInvariant();

            // Explicit, hand-authored metadata per model. Category choices for models referenced by the
            // built-in layout presets are unchanged, because catalogue ids are derived from the category.
            // TargetMetres is the real-world size the model is normalised to (height or longest floor side).
            switch (key)
            {
                // ── Seating ──
                case "chair":
                    return Seat("Task Chair", "Ergonomic office task chair.", 0.95f);
                case "office_chair_1":
                    return Seat("Mesh Task Chair", "Breathable mesh task chair with adjustable arms.", 0.95f);
                case "office_chair_2":
                    return Seat("Swivel Chair", "Height-adjustable swivel office chair.", 0.95f);
                case "office_chair_3":
                    return Seat("Conference Chair", "Padded conference chair on castors.", 0.92f);
                case "office_chair_4":
                    return Seat("Executive Chair", "High-back executive chair with lumbar support.", 1.15f);
                case "office_chair_5":
                    return Seat("Guest Chair", "Guest and lounge chair.", 0.9f);

                // ── Workstations ──
                case "office_desk_1":
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 1, 0.6f, false, "Office Desk", "Single-person work desk.", false, 1.6f);
                case "office_desk_2":
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 2, 0.6f, false, "Dual Desk", "Shared twin work desk.", false, 2.4f);
                case "cubicle_1":
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 2, 0.8f, false, "2-Person Cubicle", "Dual face-to-face workstation unit.", false, 2.2f);
                case "cubicle_2":
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 4, 0.9f, false, "4-Person Cubicle", "Four-station collaborative cubicle.", false, 3.2f);
                case "cubicle_3":
                    return new ModelCREMetadata(FurnitureCategory.Workstations, 6, 1.0f, false, "6-Person Pod", "High-density six-seat team pod.", false, 3.8f);

                // ── Conference ──
                case "conf_desk_1":
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 8, 1.0f, false, "Meeting Table (8)", "Rectangular meeting table seating eight.", false, 2.8f);
                case "conf_desk_2":
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 10, 1.0f, false, "Meeting Table (10)", "Conference table seating ten.", false, 3.2f);
                case "conf_desk_3":
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 6, 0.8f, false, "Huddle Table (6)", "Compact huddle-room table seating six.", false, 1.8f);
                case "conf_desk_4":
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 12, 1.2f, false, "Boardroom Table (12)", "Boardroom table seating twelve.", false, 4.0f);
                case "meeting_room_1":
                    return new ModelCREMetadata(FurnitureCategory.ConferenceTables, 8, 1.5f, false, "Meeting Room", "Enclosed meeting room with conference setting.", false, 4.5f);

                // ── Executive ──
                case "ceo_office":
                    return new ModelCREMetadata(FurnitureCategory.ExecutiveCabins, 4, 1.5f, false, "CEO Suite", "Executive suite with desk and meeting zone.", false, 4.5f);
                case "cabin_1":
                    return new ModelCREMetadata(FurnitureCategory.ExecutiveCabins, 3, 1.2f, false, "Manager Cabin", "Private cabin with desk and guest seating.", false, 3.8f);

                // ── Reception ──
                case "recep_1":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 1, 1.0f, true, "Reception Desk", "Welcome desk for the front of house.", false, 2.4f);
                case "recep_2":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 2, 1.2f, true, "Reception Desk (Dual)", "Two-station reception counter.", false, 3.0f);
                case "recep_3":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 1, 1.0f, true, "Reception Counter", "Curved reception counter.", false, 2.6f);
                case "couch_1":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 3, 0.6f, false, "Lounge Couch (3)", "Three-seat lounge couch.", false, 2.1f);
                case "couch_2":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 2, 0.6f, false, "Lounge Couch (2)", "Two-seat lounge couch.", false, 1.7f);
                case "couch_3":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 3, 0.6f, false, "Lounge Sofa (3)", "Three-seat upholstered sofa.", false, 2.0f);
                case "sofa_2":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 2, 0.6f, false, "Sofa (2)", "Two-seat sofa.", false, 1.8f);
                case "sofa_3":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 3, 0.6f, false, "Sofa (3)", "Three-seat sofa.", false, 2.2f);
                case "sofa_4":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 2, 0.6f, false, "Modern Sofa (2)", "Contemporary two-seat sofa.", false, 2.0f);
                case "sofa_5":
                    return new ModelCREMetadata(FurnitureCategory.Reception, 4, 0.6f, false, "Sectional Sofa", "Four-seat sectional sofa.", false, 3.0f);

                // ── Cafeteria ──
                case "couch_4":
                    return new ModelCREMetadata(FurnitureCategory.Cafeteria, 2, 0.6f, false, "Cafe Bench Sofa", "Two-seat cafe bench sofa.", false, 1.8f);
                case "couch_5":
                    return new ModelCREMetadata(FurnitureCategory.Cafeteria, 2, 0.6f, false, "Cafe Loveseat", "Compact cafe loveseat.", false, 1.5f);
                case "cafe_counter":
                    return new ModelCREMetadata(FurnitureCategory.Cafeteria, 0, 1.2f, true, "Cafe Counter", "Serving counter with storage.", false, 2.0f);
                case "vending_machine_1":
                    return new ModelCREMetadata(FurnitureCategory.Cafeteria, 0, 0.8f, true, "Vending Machine", "Snack and drink vending machine.", true, 1.85f);
                case "vending_machine_2":
                    return new ModelCREMetadata(FurnitureCategory.Cafeteria, 0, 0.8f, true, "Vending Machine XL", "Wide refreshments vending machine.", true, 1.85f);

                // ── Partitions ──
                case "booth_1":
                    return new ModelCREMetadata(FurnitureCategory.Partitions, 2, 0.6f, false, "Phone Booth", "Sound-isolated booth for calls and 1-on-1s.", true, 2.2f);

                // ── Equipment ──
                case "cabinet_1":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.6f, true, "Storage Cabinet", "Lockable storage cabinet.", true, 1.6f);
                case "printer_1":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.8f, false, "Compact Printer", "Compact office printer.", false, 0.55f);
                case "printer_2":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.8f, false, "Office Printer", "Networked multifunction printer.", true, 1.1f);
                case "printer_3":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.8f, false, "Floor Printer", "Floor-standing print station.", true, 1.1f);
                case "water_unit_1":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.5f, true, "Water Dispenser", "Floor-standing water dispenser.", true, 1.1f);
                case "white_board_1":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.6f, false, "Whiteboard", "Freestanding whiteboard.", false, 1.2f);
                case "white_board_2":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.6f, false, "Mobile Whiteboard", "Whiteboard on a rolling stand.", false, 1.2f);
                case "white_board_3":
                    return new ModelCREMetadata(FurnitureCategory.Equipment, 0, 0.6f, false, "Wide Whiteboard", "Wide presentation whiteboard.", false, 1.6f);

                // ── Decor ──
                case "plant_1":
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 0.3f, false, "Floor Plant (Tall)", "Tall indoor floor plant.", true, 1.4f);
                case "plant_2":
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 0.3f, false, "Floor Plant", "Indoor floor planter.", true, 1.0f);
                case "plant_3":
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 0.3f, false, "Small Plant", "Small potted plant.", true, 0.6f);
                case "plant_rack_1":
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 0.4f, true, "Plant Shelf", "Tiered plant display rack.", true, 1.8f);
                case "pool_table_1":
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 1.5f, false, "Pool Table", "Full-size pool table for recreation zones.", false, 2.3f);
                case "foosball_1":
                    return new ModelCREMetadata(FurnitureCategory.Decor, 0, 1.2f, false, "Foosball Table", "Four-player foosball table.", false, 1.4f);
            }

            return new ModelCREMetadata(FurnitureCategory.Workstations, 0, 0.5f, false, modelName, "Workplace component.", true, 1.0f);
        }

        static ModelCREMetadata Seat(string name, string description, float heightMetres)
        {
            return new ModelCREMetadata(FurnitureCategory.Seating, 1, 0.4f, false, name, description, true, heightMetres);
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
