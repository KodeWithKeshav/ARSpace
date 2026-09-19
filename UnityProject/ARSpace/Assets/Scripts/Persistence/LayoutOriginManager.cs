using System;
using System.Collections.Generic;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;
using ARSpace.Placement;

namespace ARSpace.Persistence
{
    /// <summary>
    /// Manages the layout coordinate origin in the physical AR space.
    ///
    /// Since native ARCore anchors do not persist across app lifecycles without cloud anchors,
    /// all saved layouts are stored relative to a single anchor origin pose.
    /// On restore, the origin is confirmed on the detected floor, and all furniture
    /// entities are reconstructed at their respective relative offsets.
    /// </summary>
    public class LayoutOriginManager : MonoBehaviour
    {
        [Header("Origin Visualizer")]
        [SerializeField]
        GameObject m_OriginMarkerPrefab;

        GameObject m_OriginMarkerInstance;
        Pose m_OriginPose = Pose.identity;
        bool m_HasOrigin = false;

        public Pose OriginPose => m_OriginPose;
        public bool HasOrigin => m_HasOrigin;

        void Awake()
        {
            ServiceLocator.Register(this);
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<LayoutOriginManager>();
            if (m_OriginMarkerInstance != null)
            {
                Destroy(m_OriginMarkerInstance);
            }
        }

        void OnEnable()
        {
            GameEvents.ObjectPlaced += OnObjectPlacedAutoOrigin;
        }

        void OnDisable()
        {
            GameEvents.ObjectPlaced -= OnObjectPlacedAutoOrigin;
        }

        /// <summary>
        /// Automatically establishes the layout origin on the first placed object if not explicitly set.
        /// </summary>
        void OnObjectPlacedAutoOrigin(GameObject placedGo)
        {
            if (!m_HasOrigin && placedGo != null)
            {
                Vector3 pos = placedGo.transform.position;
                Quaternion rot = Quaternion.Euler(0f, placedGo.transform.eulerAngles.y, 0f);
                SetOriginPose(new Pose(pos, rot));
                Debug.Log($"[LayoutOriginManager] Auto-established Layout Origin at first object: {pos}");
            }
        }

        /// <summary>
        /// Explicitly establishes the layout origin pose on a floor plane.
        /// </summary>
        public void SetOriginPose(Pose pose)
        {
            m_OriginPose = pose;
            m_HasOrigin = true;

            UpdateOriginVisualizer();
        }

        /// <summary>
        /// Clears the established origin pose.
        /// </summary>
        public void ResetOrigin()
        {
            m_HasOrigin = false;
            m_OriginPose = Pose.identity;

            if (m_OriginMarkerInstance != null)
            {
                m_OriginMarkerInstance.SetActive(false);
            }
        }

        void UpdateOriginVisualizer()
        {
            if (m_OriginMarkerPrefab != null)
            {
                if (m_OriginMarkerInstance == null)
                {
                    m_OriginMarkerInstance = Instantiate(m_OriginMarkerPrefab);
                }

                m_OriginMarkerInstance.transform.SetPositionAndRotation(m_OriginPose.position, m_OriginPose.rotation);
                m_OriginMarkerInstance.SetActive(true);
            }
        }

        /// <summary>
        /// Serializes all active scene objects into a LayoutModel relative to the origin.
        /// </summary>
        public LayoutModel SerializeCurrentLayout(string layoutName)
        {
            var registry = ServiceLocator.Get<PlacedObjectRegistry>();
            if (registry == null || registry.Count == 0)
            {
                GameEvents.RaiseToastRequested("No furniture placed in the scene to save.");
                Debug.LogWarning("[LayoutOriginManager] Cannot save: scene contains no placed objects.");
                return null;
            }

            IReadOnlyList<PlacedObject> objects = registry.AllObjects;

            // If no origin has been established yet, use the first object's pose
            if (!m_HasOrigin && objects.Count > 0 && objects[0] != null)
            {
                var firstTrans = objects[0].transform;
                SetOriginPose(new Pose(firstTrans.position, Quaternion.Euler(0f, firstTrans.eulerAngles.y, 0f)));
            }

            string name = !string.IsNullOrEmpty(layoutName) ? layoutName.Trim() : "Office Layout";
            LayoutModel model = new LayoutModel(name);
            model.totalSeatingCapacity = registry.CalculateTotalSeatingCapacity();
            model.recordedFloorArea = registry.CalculateTotalFootprintArea();

            Quaternion invOriginRot = Quaternion.Inverse(m_OriginPose.rotation);

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null || obj.Item == null) continue;

                Transform t = obj.transform;
                Vector3 localPos = invOriginRot * (t.position - m_OriginPose.position);
                Quaternion localRot = invOriginRot * t.rotation;

                var entry = new LayoutEntry(
                    instanceId: obj.InstanceId,
                    itemId: obj.Item.Id,
                    localPos: localPos,
                    localRot: localRot,
                    scale: t.localScale,
                    locked: obj.IsLocked
                );

                model.entries.Add(entry);
            }

            Debug.Log($"[LayoutOriginManager] Serialized {model.entries.Count} objects relative to origin {m_OriginPose.position}.");
            return model;
        }

        /// <summary>
        /// Saves the current layout to persistent storage.
        /// </summary>
        public bool SaveCurrentLayout(string layoutName, out LayoutModel savedModel)
        {
            savedModel = SerializeCurrentLayout(layoutName);
            if (savedModel == null) return false;

            bool success = LayoutStorage.SaveLayout(savedModel, out string path);
            if (success)
            {
                GameEvents.RaiseToastRequested($"Saved layout '{savedModel.layoutName}'");
            }
            else
            {
                GameEvents.RaiseToastRequested("Failed to save layout file.");
            }
            return success;
        }

        /// <summary>
        /// Reconstructs a layout relative to an established floor origin pose.
        /// Tolerantly handles missing item IDs without crashing or interrupting other items.
        /// </summary>
        public async Awaitable<int> LoadLayoutAsync(LayoutModel layout, Pose? targetOrigin = null)
        {
            if (layout == null || layout.entries == null)
            {
                Debug.LogError("[LayoutOriginManager] Cannot load null or invalid layout.");
                GameEvents.RaiseToastRequested("Invalid layout data.");
                return 0;
            }

            var registry = ServiceLocator.Get<PlacedObjectRegistry>();
            var catalog = ServiceLocator.Get<CatalogService>();
            var anchorService = ServiceLocator.Get<AnchorService>();

            FurnitureDatabase database = catalog != null ? catalog.Database : null;
            if (database == null)
            {
                Debug.LogError("[LayoutOriginManager] Cannot load layout: FurnitureDatabase not available.");
                GameEvents.RaiseToastRequested("Catalogue database unavailable.");
                return 0;
            }

            // 1. Resolve Origin Pose
            Pose origin;
            if (targetOrigin.HasValue)
            {
                origin = targetOrigin.Value;
                SetOriginPose(origin);
            }
            else if (m_HasOrigin)
            {
                origin = m_OriginPose;
            }
            else
            {
                // Fallback: Use reticle pose if available, else camera forward projection on floor
                var reticle = ServiceLocator.Get<PlacementReticle>();
                if (reticle != null && reticle.HasHit)
                {
                    origin = reticle.CurrentPose;
                }
                else
                {
                    Camera cam = Camera.main;
                    Vector3 camFloor = cam != null
                        ? cam.transform.position + (cam.transform.forward * 2.0f)
                        : Vector3.forward * 2f;
                    camFloor.y = 0f;
                    origin = new Pose(camFloor, Quaternion.identity);
                }
                SetOriginPose(origin);
            }

            // 2. Clear existing scene objects
            if (registry != null)
            {
                registry.ClearAll();
            }

            // 3. Reconstruct each entry
            int restoredCount = 0;
            int skippedCount = 0;

            for (int i = 0; i < layout.entries.Count; i++)
            {
                var entry = layout.entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.furnitureItemId))
                {
                    continue;
                }

                // Tolerant lookup: if item is missing, log warning and skip safely
                FurnitureItem item = database.GetById(entry.furnitureItemId);
                if (item == null)
                {
                    Debug.LogWarning($"[LayoutOriginManager] Furniture item '{entry.furnitureItemId}' not found in database. Skipping entry {i}.");
                    skippedCount++;
                    continue;
                }

                if (item.Prefab == null)
                {
                    Debug.LogWarning($"[LayoutOriginManager] Prefab for '{item.DisplayName}' is missing. Skipping entry {i}.");
                    skippedCount++;
                    continue;
                }

                Vector3 worldPos = origin.position + (origin.rotation * entry.localPosition);
                Quaternion worldRot = origin.rotation * entry.localRotation;

                GameObject instance = Instantiate(item.Prefab, worldPos, worldRot);
                instance.transform.localScale = entry.localScale != Vector3.zero ? entry.localScale : Vector3.one;
                instance.name = $"{item.DisplayName}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";

                var placedObj = instance.GetComponent<PlacedObject>();
                if (placedObj == null)
                {
                    placedObj = instance.AddComponent<PlacedObject>();
                }
                placedObj.Initialize(item, entry.instanceId);

                if (entry.isLocked)
                {
                    placedObj.SetLocked(true);
                }

                // Attach to clustered AR anchor
                if (anchorService != null)
                {
                    await anchorService.AttachToAnchorAsync(placedObj, new Pose(worldPos, worldRot), null);
                }

                placedObj.SnapBaseToHeight(worldPos.y);

                GameEvents.RaiseObjectPlaced(instance);
                restoredCount++;
            }

            string resultMsg = $"Restored layout '{layout.layoutName}' ({restoredCount} items).";
            if (skippedCount > 0)
            {
                resultMsg += $" ({skippedCount} unavailable items skipped)";
            }

            GameEvents.RaiseToastRequested(resultMsg);
            Debug.Log($"[LayoutOriginManager] ✓ {resultMsg}");

            return restoredCount;
        }
    }
}
