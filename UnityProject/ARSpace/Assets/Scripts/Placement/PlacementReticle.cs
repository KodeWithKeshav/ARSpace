using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
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
        Color m_ValidColor = new Color(0.2f, 0.9f, 0.5f, 0.85f);

        [SerializeField]
        Color m_WarningColor = new Color(1.0f, 0.42f, 0.0f, 0.95f); // ARSpace orange

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
            m_RingRenderer.startWidth = 0.005f;
            m_RingRenderer.endWidth = 0.005f;
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
            m_FootprintRenderer.startWidth = 0.008f;
            m_FootprintRenderer.endWidth = 0.008f;
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

            if (!isPlacementState || m_RaycastManager == null || m_MainCamera == null)
            {
                SetVisible(false);
                HasHit = false;
                return;
            }

            // Raycast from screen center (or touch drag position)
            Vector2 screenPoint = m_CustomScreenPosition ?? new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            // Accept the plane's full tracked bounds/estimated extent, not just its (often still tiny,
            // freshly-detected) boundary polygon — PlaneWithinPolygon alone routinely misses valid floor
            // hits for the first several seconds of scanning, which reads to the user as "no floor found".
            const TrackableType floorTrackables = TrackableType.PlaneWithinPolygon
                | TrackableType.PlaneWithinBounds
                | TrackableType.PlaneWithinInfinity
                | TrackableType.PlaneEstimated;

            if (m_RaycastManager.Raycast(screenPoint, s_Hits, floorTrackables))
            {
                ARRaycastHit bestHit = default;
                bool foundFloor = false;
                bool bestIsEstimated = true;

                // Prefer hits against a real detected plane over "estimated" ones (derived from feature
                // points, whose height can be off by several centimetres and read as floating furniture).
                for (int i = 0; i < s_Hits.Count; i++)
                {
                    var hit = s_Hits[i];
                    if (hit.distance > m_MaxDistance)
                        continue;

                    if (!(hit.trackable is ARPlane plane) || plane.alignment != PlaneAlignment.HorizontalUp)
                        continue;

                    bool isEstimated = hit.hitType == TrackableType.PlaneEstimated;
                    if (!foundFloor || (bestIsEstimated && !isEstimated))
                    {
                        bestHit = hit;
                        bestIsEstimated = isEstimated;
                        foundFloor = true;
                        CurrentPlane = plane;
                        if (!isEstimated)
                            break;
                    }
                }

                if (foundFloor)
                {
                    HasHit = true;

                    // Compute yaw facing the user, snapped to item's snap increment
                    FurnitureItem selectedItem = m_CatalogService != null ? m_CatalogService.SelectedItem : null;
                    float snapDegrees = selectedItem != null ? selectedItem.SnapRotationDegrees : 45f;

                    Vector3 camPos = m_MainCamera.transform.position;
                    Vector3 lookDir = camPos - bestHit.pose.position;
                    lookDir.y = 0f;

                    Quaternion targetRot = Quaternion.identity;
                    if (lookDir.sqrMagnitude > 0.001f)
                    {
                        float rawYaw = Quaternion.LookRotation(lookDir, Vector3.up).eulerAngles.y;
                        float snappedYaw = Mathf.Round(rawYaw / snapDegrees) * snapDegrees;
                        targetRot = Quaternion.Euler(0, snappedYaw, 0);
                    }

                    // Position hugs floor surface with slight 3mm offset to eliminate z-fighting
                    Vector3 reticlePos = bestHit.pose.position + Vector3.up * 0.003f;
                    transform.SetPositionAndRotation(reticlePos, targetRot);

                    CurrentPose = new Pose(reticlePos, targetRot);

                    // Update footprint rectangle & check validity
                    UpdateFootprint(selectedItem);
                    SetVisible(true);
                    return;
                }
            }

            HasHit = false;
            SetVisible(false);
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
