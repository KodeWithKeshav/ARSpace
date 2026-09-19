using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using ARSpace.AR;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// AR placement reticle that continuously raycasts against floor planes (PlaneWithinPolygon),
    /// hugs the surface, shows the selected item's real-world footprint rectangle,
    /// and warns in ARSpace orange when the footprint overlaps an existing object
    /// or extends beyond the detected plane boundary.
    /// </summary>
    public class PlacementReticle : MonoBehaviour
    {
        [Header("Raycast & Limits")]
        [Tooltip("Maximum raycast distance in metres beyond which tracking noise dominates.")]
        [SerializeField]
        float m_MaxDistance = 8.0f;

        [Tooltip("Layer mask for collision checking against already placed furniture.")]
        [SerializeField]
        LayerMask m_ObstacleLayerMask = ~0;

        [Header("Visual Elements")]
        [SerializeField]
        LineRenderer m_RingRenderer;

        [SerializeField]
        LineRenderer m_FootprintRenderer;

        [Header("Colours")]
        [SerializeField]
        Color m_ValidColor = new Color(1.0f, 0.42f, 0.0f, 1f); // ARSpace orange

        [SerializeField]
        Color m_WarningColor = new Color(1f, 0.12f, 0.10f, 1f); // red = blocked

        // AR Services
        ARRaycastManager m_RaycastManager;
        Camera m_MainCamera;
        CatalogService m_CatalogService;

        // Hits buffer
        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        // State
        public bool HasHit { get; private set; }
        public bool IsValidPlacement { get; private set; }
        public Pose CurrentPose { get; private set; }
        public ARPlane CurrentPlane { get; private set; }
        public string WarningMessage { get; private set; }

        Vector2? m_CustomScreenPosition = null;

        readonly Vector3[] m_RingPoints = new Vector3[32];
        readonly Vector3[] m_FootprintPoints = new Vector3[5];

        void Awake()
        {
            ServiceLocator.Register(this);
            m_MainCamera = Camera.main;
            m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            SetupRenderers();
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<PlacementReticle>();
        }

        void Start()
        {
            m_CatalogService = ServiceLocator.Get<CatalogService>();
        }

        void SetupRenderers()
        {
            // Set up circular ring
            if (m_RingRenderer == null)
            {
                var ringGo = new GameObject("ReticleRing");
                ringGo.transform.SetParent(transform, false);
                m_RingRenderer = ringGo.AddComponent<LineRenderer>();
            }

            m_RingRenderer.loop = true;
            m_RingRenderer.useWorldSpace = false;
            m_RingRenderer.alignment = LineAlignment.TransformZ;
            m_RingRenderer.startWidth = 0.012f;
            m_RingRenderer.endWidth = 0.012f;
            m_RingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_RingRenderer.receiveShadows = false;

            // Precompute circle
            const float radius = 0.20f;
            for (int i = 0; i < 32; i++)
            {
                float angle = (i / 32f) * Mathf.PI * 2f;
                m_RingPoints[i] = new Vector3(Mathf.Cos(angle) * radius, 0.002f, Mathf.Sin(angle) * radius);
            }
            m_RingRenderer.positionCount = 32;
            m_RingRenderer.SetPositions(m_RingPoints);

            // Set up footprint rectangle
            if (m_FootprintRenderer == null)
            {
                var fpGo = new GameObject("ReticleFootprint");
                fpGo.transform.SetParent(transform, false);
                m_FootprintRenderer = fpGo.AddComponent<LineRenderer>();
            }

            m_FootprintRenderer.loop = true;
            m_FootprintRenderer.useWorldSpace = false;
            m_FootprintRenderer.alignment = LineAlignment.TransformZ;
            m_FootprintRenderer.startWidth = 0.016f;
            m_FootprintRenderer.endWidth = 0.016f;
            m_FootprintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_FootprintRenderer.receiveShadows = false;
        }

        void Update()
        {
            if (m_MainCamera == null)
                m_MainCamera = Camera.main;

            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            var app = ServiceLocator.Get<ARSpaceApp>();
            bool isPlacementState = app != null && (app.CurrentState == AppState.PlacementPending || app.CurrentState == AppState.Browsing);

            bool hasManualFloor = ServiceLocator.TryGet(out ManualFloor updateFloor) && updateFloor.HasFloor;
            if (!isPlacementState || (m_RaycastManager == null && !hasManualFloor) || m_MainCamera == null)
            {
                SetVisible(false);
                HasHit = false;
                return;
            }

            Pose floorPose;
            ARPlane floorPlane;
            bool found;

            if (!m_IsPinned && hasManualFloor)
            {
                // Manual mode: nothing is shown until the user taps a spot.
                floorPose = default;
                floorPlane = null;
                found = false;
            }
            else if (m_IsPinned)
            {
                // The user tapped a spot: keep the marker exactly there, world-locked, regardless of where the phone points.
                floorPose = m_PinnedPose;
                floorPlane = m_PinnedPlane;
                found = true;

                // Keep the marker on the virtual floor when its height is adjusted with Raise / Lower floor.
                if (hasManualFloor)
                    floorPose = new Pose(new Vector3(m_PinnedPose.position.x, updateFloor.FloorY, m_PinnedPose.position.z), m_PinnedPose.rotation);
            }
            else
            {
                // Auto mode: follow whatever floor is under the screen centre.
                Vector2 screenPoint = m_CustomScreenPosition ?? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                found = TryRaycastFloor(screenPoint, out floorPose, out floorPlane);
                if (found)
                    floorPose = new Pose(floorPose.position, FacingYaw(floorPose.position));
            }

            if (!found || !TrackingStatus.IsUsable)
            {
                HasHit = false;
                CurrentPlane = null;
                SetVisible(false);
                return;
            }

            HasHit = true;
            CurrentPlane = floorPlane;

            // Position hugs floor surface with slight 3mm offset to eliminate z-fighting
            Vector3 reticlePos = floorPose.position + Vector3.up * 0.003f;
            transform.SetPositionAndRotation(reticlePos, floorPose.rotation);
            CurrentPose = new Pose(reticlePos, floorPose.rotation);

            FurnitureItem selectedItem = m_CatalogService != null ? m_CatalogService.SelectedItem : null;
            UpdateFootprint(selectedItem);
            SetVisible(true);
        }

        // ── Tap-to-place ───────────────────────────────────────

        /// <summary>True while a catalogue item is being positioned (marker active).</summary>
        public bool IsPlacementActive
        {
            get
            {
                var app = ServiceLocator.Get<ARSpaceApp>();
                bool placing = app != null && (app.CurrentState == AppState.PlacementPending || app.CurrentState == AppState.Browsing);
                var catalog = ServiceLocator.Get<CatalogService>();
                return placing && catalog != null && catalog.HasSelection;
            }
        }

        /// <summary>True while the marker is pinned to a spot the user tapped.</summary>
        public bool IsPinned => m_IsPinned;

        /// <summary>Furniture placed further away than this barely shows parallax and reads as floating.</summary>
        const float MaxPlacementDistance = 5f;

        bool m_IsPinned;
        Pose m_PinnedPose;
        ARPlane m_PinnedPlane;
        float m_LastFailToastTime = -10f;

        /// <summary>
        /// Moves the marker to the floor point under a screen tap. Returns false (and tells the user) if no
        /// detected floor is there. Ignored unless a catalogue item is being positioned.
        /// </summary>
        public bool TryPinAtScreenPoint(Vector2 screenPoint, bool refineFloor = true)
        {
            if (!IsPlacementActive || m_MainCamera == null)
                return false;

            if (!TrackingStatus.IsUsable)
            {
                if (Time.unscaledTime - m_LastFailToastTime > 2f)
                {
                    m_LastFailToastTime = Time.unscaledTime;
                    GameEvents.RaiseToastRequested("Tracking is lost — move the phone slowly and show more of the room.");
                }
                return false;
            }

            var manualFloor = ServiceLocator.TryGet(out ManualFloor mf) ? mf : null;
            if (manualFloor == null && m_RaycastManager == null)
                return false;

            // If ARCore can see the floor at the tapped point, use it to correct the virtual floor's height.
            if (manualFloor != null && refineFloor)
                manualFloor.RefineFromScreenPoint(screenPoint);

            if (!TryRaycastFloor(screenPoint, out Pose hitPose, out ARPlane plane))
            {
                if (Time.unscaledTime - m_LastFailToastTime > 2f)
                {
                    m_LastFailToastTime = Time.unscaledTime;
                    GameEvents.RaiseToastRequested(manualFloor != null
                        ? "Point the phone down at the floor, then tap where the furniture should go."
                        : "No floor detected there yet — tap a spot where the floor grid shows.");
                }
                return false;
            }

            m_PinnedPose = new Pose(hitPose.position, FacingYaw(hitPose.position));
            m_PinnedPlane = plane;
            m_IsPinned = true;
            return true;
        }

        /// <summary>Returns the marker to following the screen centre.</summary>
        public void ClearPin()
        {
            m_IsPinned = false;
            m_PinnedPlane = null;
        }

        void OnEnable()
        {
            GameEvents.FurnitureSelected += OnPlacementContextChanged;
            GameEvents.PlacementCancelled += ClearPin;
            GameEvents.ObjectPlaced += OnObjectPlaced;
        }

        void OnDisable()
        {
            GameEvents.FurnitureSelected -= OnPlacementContextChanged;
            GameEvents.PlacementCancelled -= ClearPin;
            GameEvents.ObjectPlaced -= OnObjectPlaced;
        }

        void OnPlacementContextChanged(string itemId) => ClearPin();
        void OnObjectPlaced(GameObject placed) => ClearPin();

        /// <summary>Yaw facing the user, snapped to the selected item's rotation increment.</summary>
        Quaternion FacingYaw(Vector3 floorPoint)
        {
            FurnitureItem item = m_CatalogService != null ? m_CatalogService.SelectedItem : null;
            float snapDegrees = item != null ? item.SnapRotationDegrees : 45f;

            Vector3 lookDir = m_MainCamera.transform.position - floorPoint;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude <= 0.001f)
                return Quaternion.identity;

            float rawYaw = Quaternion.LookRotation(lookDir, Vector3.up).eulerAngles.y;
            float snappedYaw = snapDegrees > 0f ? Mathf.Round(rawYaw / snapDegrees) * snapDegrees : rawYaw;
            return Quaternion.Euler(0, snappedYaw, 0);
        }

        /// <summary>
        /// Floor hit under a screen point. Accepts a plane's full tracked bounds/estimated extent, not just its
        /// (often still tiny) boundary polygon, and prefers real planes over estimated ones.
        /// </summary>
        bool TryRaycastFloor(Vector2 screenPoint, out Pose pose, out ARPlane plane)
        {
            pose = default;
            plane = null;

            // Preferred: the virtual floor — works with no ARCore plane detection at all.
            if (ServiceLocator.TryGet(out ManualFloor manualFloor) && manualFloor.HasFloor)
            {
                if (!manualFloor.GetPoint(screenPoint, out Vector3 floorPoint))
                    return false;

                pose = new Pose(floorPoint, Quaternion.identity);
                return true;
            }

            // Fallback (no ManualFloor in the scene): only accept the detected floor patch itself (its outline or bounding rectangle). Infinite/estimated
            // planes extend the floor beyond walls, so a ray aimed at a wall "hit" floor on the far side of it and
            // furniture ended up metres away, behind the wall, looking like it floated on the wall.
            const TrackableType floorTrackables = TrackableType.PlaneWithinPolygon
                | TrackableType.PlaneWithinBounds;

            if (!m_RaycastManager.Raycast(screenPoint, s_Hits, floorTrackables))
                return false;

            bool found = false;
            bool bestIsEstimated = true;

            for (int i = 0; i < s_Hits.Count; i++)
            {
                var hit = s_Hits[i];
                if (hit.distance > Mathf.Min(m_MaxDistance, MaxPlacementDistance))
                    continue;

                if (!(hit.trackable is ARPlane p) || p.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                bool isEstimated = hit.hitType == TrackableType.PlaneEstimated;
                if (!found || (bestIsEstimated && !isEstimated))
                {
                    pose = hit.pose;
                    plane = p;
                    bestIsEstimated = isEstimated;
                    found = true;
                    if (!isEstimated)
                        break;
                }
            }

            return found;
        }

        void UpdateFootprint(FurnitureItem item)
        {
            Vector2 footprint = item != null ? item.Footprint : new Vector2(0.6f, 0.6f);
            float hx = footprint.x * 0.5f;
            float hz = footprint.y * 0.5f;

            m_FootprintPoints[0] = new Vector3(-hx, 0.003f, -hz);
            m_FootprintPoints[1] = new Vector3( hx, 0.003f, -hz);
            m_FootprintPoints[2] = new Vector3( hx, 0.003f,  hz);
            m_FootprintPoints[3] = new Vector3(-hx, 0.003f,  hz);
            m_FootprintPoints[4] = m_FootprintPoints[0];

            m_FootprintRenderer.positionCount = 5;
            m_FootprintRenderer.SetPositions(m_FootprintPoints);

            // Validate collision and boundary containment
            bool isOverlap = CheckObstacleOverlap(hx, hz);
            bool isOutOfBounds = CheckPlaneBoundaryViolation(hx, hz);

            if (isOverlap)
            {
                IsValidPlacement = false;
                WarningMessage = "Placement overlaps existing furniture";
                ApplyColor(m_WarningColor);
            }
            else if (isOutOfBounds)
            {
                IsValidPlacement = false;
                WarningMessage = "Footprint extends beyond detected floor";
                ApplyColor(m_WarningColor);
            }
            else
            {
                IsValidPlacement = true;
                WarningMessage = string.Empty;
                ApplyColor(m_ValidColor);
            }
        }

        bool CheckObstacleOverlap(float halfX, float halfZ)
        {
            Vector3 center = transform.position + Vector3.up * 0.5f;
            Vector3 halfExtents = new Vector3(halfX * 0.95f, 0.45f, halfZ * 0.95f);

            Collider[] hits = Physics.OverlapBox(center, halfExtents, transform.rotation, m_ObstacleLayerMask);
            foreach (var col in hits)
            {
                // Overlap occurs if hitting a PlacedObject collider (excluding reticle itself)
                if (col.GetComponentInParent<PlacedObject>() != null)
                {
                    return true;
                }
            }
            return false;
        }

        bool CheckPlaneBoundaryViolation(float halfX, float halfZ)
        {
            if (CurrentPlane == null || !CurrentPlane.boundary.IsCreated || CurrentPlane.boundary.Length < 3)
                return false;

            // Transform 4 corners into plane-local 2D space and test point-in-polygon
            Vector3[] localCorners = new[]
            {
                transform.TransformPoint(new Vector3(-halfX, 0, -halfZ)),
                transform.TransformPoint(new Vector3( halfX, 0, -halfZ)),
                transform.TransformPoint(new Vector3( halfX, 0,  halfZ)),
                transform.TransformPoint(new Vector3(-halfX, 0,  halfZ))
            };

            var boundary = CurrentPlane.boundary;
            Transform planeTrans = CurrentPlane.transform;

            for (int i = 0; i < 4; i++)
            {
                Vector3 ptInPlaneLocal = planeTrans.InverseTransformPoint(localCorners[i]);
                Vector2 pt2D = new Vector2(ptInPlaneLocal.x, ptInPlaneLocal.z);

                if (!IsPointInPolygon(pt2D, boundary))
                {
                    return true; // Corner extends beyond polygon boundary
                }
            }

            return false;
        }

        static bool IsPointInPolygon(Vector2 point, NativeArray<Vector2> polygon)
        {
            bool inside = false;
            int count = polygon.Length;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        void ApplyColor(Color col)
        {
            if (m_RingRenderer != null)
            {
                m_RingRenderer.startColor = col;
                m_RingRenderer.endColor = col;
            }
            if (m_FootprintRenderer != null)
            {
                m_FootprintRenderer.startColor = col;
                m_FootprintRenderer.endColor = col;
            }
        }

        void SetVisible(bool visible)
        {
            if (m_RingRenderer != null && m_RingRenderer.enabled != visible)
                m_RingRenderer.enabled = visible;
            if (m_FootprintRenderer != null && m_FootprintRenderer.enabled != visible)
                m_FootprintRenderer.enabled = visible;
        }

        /// <summary>
        /// Sets a custom screen position (e.g. during a finger drag gesture).
        /// Pass null to reset to screen center.
        /// </summary>
        public void SetCustomScreenPosition(Vector2? screenPos)
        {
            m_CustomScreenPosition = screenPos;
        }
    }
}
