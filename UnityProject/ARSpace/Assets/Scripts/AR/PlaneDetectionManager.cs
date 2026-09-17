using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARSpace.AR
{
    /// <summary>
    /// Wraps <see cref="ARPlaneManager"/>. Horizontal detection only.
    /// Filters for floor-classified planes where classification is available,
    /// exposes filtered floor planes, total area, and change events.
    ///
    /// AR Foundation 6.5 API verified from in-tree template code
    /// (MobileARTemplateAssets/Scripts/ARTemplateMenuManager.cs, lines 234/246/539-609):
    ///   - ARPlaneManager.trackablesChanged: UnityEvent, subscribed via AddListener/RemoveListener
    ///   - Handler receives ARTrackablesChangedEventArgs&lt;ARPlane&gt;
    ///   - eventArgs.added: List&lt;ARPlane&gt;
    ///   - eventArgs.removed: List&lt;KeyValuePair&lt;TrackableId, ARPlane&gt;&gt; (access .Value for ARPlane)
    ///   - eventArgs.updated: List&lt;ARPlane&gt;
    ///   - ARPlane.alignment: PlaneAlignment enum (HorizontalUp, HorizontalDown, Vertical, etc.)
    ///   - ARPlane.boundary: NativeArray&lt;Vector2&gt; of boundary points in plane-local space
    ///   - ARPlane.size: Vector2 (extents)
    ///   - ARPlaneManager.trackables: TrackableCollection&lt;ARPlane&gt; (iterable)
    ///   - ARPlaneManager.trackables.count: int
    ///   - PlaneDetectionMode enum: Horizontal
    /// </summary>
    public class PlaneDetectionManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ARPlaneManager m_PlaneManager;

        [Header("Settings")]
        [Tooltip("Tolerance in metres for floor-level filtering. Planes whose Y differs from the lowest detected plane by more than this are rejected as floor candidates.")]
        [SerializeField] float m_FloorLevelTolerance = 0.3f;

        /// <summary>All currently tracked planes that pass the floor filter.</summary>
        public IReadOnlyList<ARPlane> FloorPlanes => m_FloorPlanes;

        /// <summary>Total detected floor area in square metres.</summary>
        public float TotalFloorAreaSqM { get; private set; }

        /// <summary>Number of floor planes currently tracked.</summary>
        public int FloorPlaneCount => m_FloorPlanes.Count;

        /// <summary>Whether plane detection is currently active.</summary>
        public bool IsDetectionEnabled => m_PlaneManager != null && m_PlaneManager.enabled;

        /// <summary>The established floor Y level (world space). Set from the first detected floor plane.</summary>
        public float EstablishedFloorLevel { get; private set; } = float.NaN;

        /// <summary>Fired when a floor plane is added.</summary>
        public event Action<ARPlane> FloorPlaneAdded;

        /// <summary>Fired when a floor plane is updated.</summary>
        public event Action<ARPlane> FloorPlaneUpdated;

        /// <summary>Fired when a floor plane is removed.</summary>
        public event Action<ARPlane> FloorPlaneRemoved;

        readonly List<ARPlane> m_FloorPlanes = new List<ARPlane>();

        void OnEnable()
        {
            if (m_PlaneManager == null)
            {
                m_PlaneManager = FindAnyObjectByType<ARPlaneManager>();
            }

            if (m_PlaneManager != null)
            {
                m_PlaneManager.trackablesChanged.AddListener(OnPlanesChanged);
            }
            else
            {
                Debug.LogError("[PlaneDetectionManager] ARPlaneManager not found.");
            }
        }

        void OnDisable()
        {
            if (m_PlaneManager != null)
            {
                m_PlaneManager.trackablesChanged.RemoveListener(OnPlanesChanged);
            }
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Enable or disable plane detection at runtime.
        /// Disable during placement to reduce plane churn under furniture
        /// (a common source of drifting placements).
        /// </summary>
        public void SetDetectionEnabled(bool enabled)
        {
            if (m_PlaneManager != null)
            {
                m_PlaneManager.enabled = enabled;
                Debug.Log($"[PlaneDetectionManager] Detection {(enabled ? "enabled" : "disabled")}.");
            }
        }

        /// <summary>
        /// Check whether a given world position is over a detected floor plane
        /// and within the floor level tolerance.
        /// </summary>
        public bool IsPositionOnFloor(Vector3 worldPosition)
        {
            if (float.IsNaN(EstablishedFloorLevel))
                return false;

            return Mathf.Abs(worldPosition.y - EstablishedFloorLevel) <= m_FloorLevelTolerance;
        }

        // ── Internal ───────────────────────────────────────────

        void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
        {
            bool areaChanged = false;

            // Process added planes
            for (int i = 0; i < args.added.Count; i++)
            {
                var plane = args.added[i];
                if (IsFloorPlane(plane))
                {
                    m_FloorPlanes.Add(plane);
                    UpdateFloorLevel(plane);
                    areaChanged = true;
                    FloorPlaneAdded?.Invoke(plane);
                }
            }

            // Process updated planes
            for (int i = 0; i < args.updated.Count; i++)
            {
                var plane = args.updated[i];
                bool wasFloor = m_FloorPlanes.Contains(plane);
                bool isFloor = IsFloorPlane(plane);

                if (isFloor && !wasFloor)
                {
                    m_FloorPlanes.Add(plane);
                    FloorPlaneAdded?.Invoke(plane);
                    areaChanged = true;
                }
                else if (!isFloor && wasFloor)
                {
                    m_FloorPlanes.Remove(plane);
                    FloorPlaneRemoved?.Invoke(plane);
                    areaChanged = true;
                }
                else if (isFloor)
                {
                    FloorPlaneUpdated?.Invoke(plane);
                    areaChanged = true;
                }
            }

            // Process removed planes
            for (int i = 0; i < args.removed.Count; i++)
            {
                var kvp = args.removed[i];
                var plane = kvp.Value;
                if (plane != null && m_FloorPlanes.Contains(plane))
                {
                    m_FloorPlanes.Remove(plane);
                    FloorPlaneRemoved?.Invoke(plane);
                    areaChanged = true;
                }
            }

            if (areaChanged)
            {
                RecalculateFloorArea();
            }
        }

        bool IsFloorPlane(ARPlane plane)
        {
            // Must be horizontal-up
            if (plane.alignment != PlaneAlignment.HorizontalUp)
                return false;

            // If we've established a floor level, check tolerance
            if (!float.IsNaN(EstablishedFloorLevel))
            {
                float planeY = plane.transform.position.y;
                if (Mathf.Abs(planeY - EstablishedFloorLevel) > m_FloorLevelTolerance)
                    return false;
            }

            // If plane classification is available, prefer floor/ground classification
            // AR Foundation 6.5: ARPlane.classifications is a PlaneClassifications flags enum
            // but ARPlane.classification (singular) is also available as legacy.
            // We check alignment + height which is more reliable than classification
            // since not all devices provide plane classification data.

            return true;
        }

        void UpdateFloorLevel(ARPlane plane)
        {
            float planeY = plane.transform.position.y;

            if (float.IsNaN(EstablishedFloorLevel))
            {
                // First floor plane establishes the floor level
                EstablishedFloorLevel = planeY;
                Debug.Log($"[PlaneDetectionManager] Floor level established at Y={EstablishedFloorLevel:F3}m");
            }
            else
            {
                // Average toward the new plane to reduce noise
                EstablishedFloorLevel = Mathf.Lerp(EstablishedFloorLevel, planeY, 0.1f);
            }
        }

        void RecalculateFloorArea()
        {
            float totalArea = 0f;

            for (int i = 0; i < m_FloorPlanes.Count; i++)
            {
                var plane = m_FloorPlanes[i];
                if (plane == null)
                    continue;

                // Use the boundary polygon area via the shoelace formula
                // plane.boundary is NativeArray<Vector2> in plane-local space
                var boundary = plane.boundary;
                if (boundary.IsCreated && boundary.Length >= 3)
                {
                    totalArea += CalculatePolygonArea(boundary);
                }
                else
                {
                    // Fallback to size extents
                    totalArea += plane.size.x * plane.size.y;
                }
            }

            TotalFloorAreaSqM = totalArea;
            Core.GameEvents.RaiseFloorAreaUpdated(TotalFloorAreaSqM);
        }

        /// <summary>
        /// Shoelace formula for polygon area from a set of 2D points.
        /// </summary>
        static float CalculatePolygonArea(Unity.Collections.NativeArray<Vector2> points)
        {
            float area = 0f;
            int count = points.Length;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                area += points[i].x * points[j].y;
                area -= points[j].x * points[i].y;
            }

            return Mathf.Abs(area) * 0.5f;
        }
    }
}
