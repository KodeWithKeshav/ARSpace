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
    /// hugs the surface, shows the selected item's real-world footprint as a
    /// rounded-square outline with an inner crosshair circle, animated corner brackets,
    /// a pulse animation, and a directional arrow showing which way the object will face.
    /// Warns in red when the footprint overlaps an existing object or extends beyond
    /// the detected plane boundary.
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

        [Header("Colours")]
        [SerializeField]
        Color m_ValidColor = new Color(1.0f, 0.42f, 0.0f, 1f); // ARSpace orange

        [SerializeField]
        Color m_WarningColor = new Color(1f, 0.12f, 0.10f, 1f); // red = blocked

        [Header("Animation")]
        [Tooltip("Pulse speed for the inner crosshair circle (cycles per second).")]
        [SerializeField]
        float m_PulseSpeed = 1.5f;

        [Tooltip("Pulse width range: min and max multiplier for the inner circle line width.")]
        [SerializeField]
        Vector2 m_PulseWidthRange = new Vector2(0.006f, 0.014f);

        [Header("Smoothing & Stability")]
        [Tooltip("Position smoothing speed (higher = faster tracking, lower = more jitter reduction).")]
        [SerializeField]
        float m_PositionSmoothSpeed = 16f;

        [Tooltip("Rotation smoothing speed.")]
        [SerializeField]
        float m_RotationSmoothSpeed = 12f;

        Vector3 m_SmoothedPosition;
        Quaternion m_SmoothedRotation;
        bool m_HasValidPose;

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

        // ── Visual elements ─────────────────────────────────────
        // Outer square outline (matches footprint)
        LineRenderer m_SquareRenderer;
        readonly Vector3[] m_SquarePoints = new Vector3[5];

        // Inner crosshair circle
        LineRenderer m_CircleRenderer;
        const int CircleSegments = 48;
        readonly Vector3[] m_CirclePoints = new Vector3[CircleSegments];

        // 4 corner bracket marks
        LineRenderer[] m_CornerRenderers = new LineRenderer[4];
        readonly Vector3[][] m_CornerPoints = new Vector3[4][];

        // Directional arrow (forward-facing indicator)
        LineRenderer m_ArrowRenderer;
        readonly Vector3[] m_ArrowPoints = new Vector3[3];

        // Centre dot
        LineRenderer m_DotRenderer;
        const int DotSegments = 16;
        readonly Vector3[] m_DotPoints = new Vector3[DotSegments];

        const float GroundLift = 0.003f; // 3mm above floor to prevent z-fighting

        void Awake()
        {
            ServiceLocator.Register(this);
            m_MainCamera = Camera.main;
            m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            // Self-heal: make sure the virtual floor exists even if the scene predates it.
            if (!ServiceLocator.TryGet(out ManualFloor _) && FindFirstObjectByType<ManualFloor>() == null)
                gameObject.AddComponent<ManualFloor>();

            for (int i = 0; i < 4; i++)
                m_CornerPoints[i] = new Vector3[3]; // L-shape: 3 points per corner

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

        LineRenderer CreateLineChild(string name, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.loop = loop;
            lr.useWorldSpace = false;
            lr.alignment = LineAlignment.TransformZ;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 4;
            lr.numCornerVertices = 4;
            return lr;
        }

        void SetupRenderers()
        {
            // ── Outer square (footprint outline) ────────────────
            m_SquareRenderer = CreateLineChild("ReticleSquare", 0.010f, true);

            // ── Inner crosshair circle ──────────────────────────
            m_CircleRenderer = CreateLineChild("ReticleCircle", 0.008f, true);

            // ── Corner brackets (4 × L-shaped marks) ────────────
            for (int i = 0; i < 4; i++)
            {
                m_CornerRenderers[i] = CreateLineChild($"ReticleCorner{i}", 0.014f, false);
                m_CornerRenderers[i].positionCount = 3;
            }

            // ── Directional arrow ───────────────────────────────
            m_ArrowRenderer = CreateLineChild("ReticleArrow", 0.010f, false);
            m_ArrowRenderer.positionCount = 3;

            // ── Centre dot ──────────────────────────────────────
            m_DotRenderer = CreateLineChild("ReticleDot", 0.006f, true);
            BuildDot(0.012f);
        }

        void BuildDot(float radius)
        {
            for (int i = 0; i < DotSegments; i++)
            {
                float angle = (i / (float)DotSegments) * Mathf.PI * 2f;
                m_DotPoints[i] = new Vector3(Mathf.Cos(angle) * radius, GroundLift + 0.001f, Mathf.Sin(angle) * radius);
            }
            m_DotRenderer.positionCount = DotSegments;
            m_DotRenderer.SetPositions(m_DotPoints);
        }

        void Update()
        {
            if (m_MainCamera == null)
                m_MainCamera = Camera.main;

            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            bool isPlacementState = IsPlacementActive;

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

            if (!found)
            {
                HasHit = false;
                CurrentPlane = null;
                SetVisible(false);
                return;
            }

            HasHit = true;
            CurrentPlane = floorPlane;

            // Position hugs floor surface with slight 3mm offset to eliminate z-fighting
            Vector3 targetReticlePos = floorPose.position + Vector3.up * GroundLift;
            Quaternion targetReticleRot = floorPose.rotation;

            // Apply exponential smoothing to prevent reticle jumping or jittering
            if (!m_HasValidPose || m_IsPinned)
            {
                m_SmoothedPosition = targetReticlePos;
                m_SmoothedRotation = targetReticleRot;
                m_HasValidPose = true;
            }
            else
            {
                float posT = 1f - Mathf.Exp(-m_PositionSmoothSpeed * Time.deltaTime);
                float rotT = 1f - Mathf.Exp(-m_RotationSmoothSpeed * Time.deltaTime);
                m_SmoothedPosition = Vector3.Lerp(m_SmoothedPosition, targetReticlePos, posT);
                m_SmoothedRotation = Quaternion.Slerp(m_SmoothedRotation, targetReticleRot, rotT);
            }

            transform.SetPositionAndRotation(m_SmoothedPosition, m_SmoothedRotation);
            CurrentPose = new Pose(m_SmoothedPosition, m_SmoothedRotation);

            FurnitureItem selectedItem = m_CatalogService != null ? m_CatalogService.SelectedItem : null;
            UpdateReticleGeometry(selectedItem);
            AnimatePulse();
            SetVisible(true);
        }

        // ── Geometry updates ──────────────────────────────────

        void UpdateReticleGeometry(FurnitureItem item)
        {
            Vector2 footprint = item != null ? item.Footprint : new Vector2(0.6f, 0.6f);
            float hx = footprint.x * 0.5f;
            float hz = footprint.y * 0.5f;

            // ── Outer square ────────────────────────────────────
            m_SquarePoints[0] = new Vector3(-hx, GroundLift, -hz);
            m_SquarePoints[1] = new Vector3( hx, GroundLift, -hz);
            m_SquarePoints[2] = new Vector3( hx, GroundLift,  hz);
            m_SquarePoints[3] = new Vector3(-hx, GroundLift,  hz);
            m_SquarePoints[4] = m_SquarePoints[0];
            m_SquareRenderer.positionCount = 5;
            m_SquareRenderer.SetPositions(m_SquarePoints);

            // ── Inner circle (inscribed, radius = smaller half-extent × 0.6) ──
            float circleRadius = Mathf.Min(hx, hz) * 0.55f;
            circleRadius = Mathf.Max(circleRadius, 0.04f); // minimum 4cm radius
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = (i / (float)CircleSegments) * Mathf.PI * 2f;
                m_CirclePoints[i] = new Vector3(
                    Mathf.Cos(angle) * circleRadius,
                    GroundLift + 0.0005f,
                    Mathf.Sin(angle) * circleRadius);
            }
            m_CircleRenderer.positionCount = CircleSegments;
            m_CircleRenderer.SetPositions(m_CirclePoints);

            // ── Corner brackets (L-shapes at each corner) ───────
            float bracketLen = Mathf.Min(hx, hz) * 0.35f;
            bracketLen = Mathf.Max(bracketLen, 0.03f);

            // Corner 0: top-left (-hx, -hz)
            m_CornerPoints[0][0] = new Vector3(-hx, GroundLift + 0.001f, -hz + bracketLen);
            m_CornerPoints[0][1] = new Vector3(-hx, GroundLift + 0.001f, -hz);
            m_CornerPoints[0][2] = new Vector3(-hx + bracketLen, GroundLift + 0.001f, -hz);

            // Corner 1: top-right (hx, -hz)
            m_CornerPoints[1][0] = new Vector3(hx - bracketLen, GroundLift + 0.001f, -hz);
            m_CornerPoints[1][1] = new Vector3(hx, GroundLift + 0.001f, -hz);
            m_CornerPoints[1][2] = new Vector3(hx, GroundLift + 0.001f, -hz + bracketLen);

            // Corner 2: bottom-right (hx, hz)
            m_CornerPoints[2][0] = new Vector3(hx, GroundLift + 0.001f, hz - bracketLen);
            m_CornerPoints[2][1] = new Vector3(hx, GroundLift + 0.001f, hz);
            m_CornerPoints[2][2] = new Vector3(hx - bracketLen, GroundLift + 0.001f, hz);

            // Corner 3: bottom-left (-hx, hz)
            m_CornerPoints[3][0] = new Vector3(-hx + bracketLen, GroundLift + 0.001f, hz);
            m_CornerPoints[3][1] = new Vector3(-hx, GroundLift + 0.001f, hz);
            m_CornerPoints[3][2] = new Vector3(-hx, GroundLift + 0.001f, hz - bracketLen);

            for (int i = 0; i < 4; i++)
                m_CornerRenderers[i].SetPositions(m_CornerPoints[i]);

            // ── Directional arrow (points forward/+Z, the direction the object faces) ──
            float arrowBase = hz + 0.03f;
            float arrowTip = hz + 0.08f;
            float arrowWing = 0.025f;
            m_ArrowPoints[0] = new Vector3(-arrowWing, GroundLift + 0.001f, arrowBase);
            m_ArrowPoints[1] = new Vector3(0f, GroundLift + 0.001f, arrowTip);
            m_ArrowPoints[2] = new Vector3(arrowWing, GroundLift + 0.001f, arrowBase);
            m_ArrowRenderer.SetPositions(m_ArrowPoints);

            // ── Validate collision and boundary containment ─────
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

        void AnimatePulse()
        {
            // Subtle breathing animation on the inner circle's line width
            float t = (Mathf.Sin(Time.time * m_PulseSpeed * Mathf.PI * 2f) + 1f) * 0.5f; // 0..1
            float width = Mathf.Lerp(m_PulseWidthRange.x, m_PulseWidthRange.y, t);
            if (m_CircleRenderer != null)
            {
                m_CircleRenderer.startWidth = width;
                m_CircleRenderer.endWidth = width;
            }

            // Pulse the centre dot opacity via alpha
            if (m_DotRenderer != null)
            {
                Color dotColor = m_DotRenderer.startColor;
                dotColor.a = Mathf.Lerp(0.4f, 1.0f, t);
                m_DotRenderer.startColor = dotColor;
                m_DotRenderer.endColor = dotColor;
            }
        }

        // ── Tap-to-place ───────────────────────────────────────

        /// <summary>True while a catalogue item is being positioned (marker active).</summary>
        public bool IsPlacementActive
        {
            get
            {
                // Positioning is active whenever a catalogue item is chosen and no placed object is being edited.
                // (Deliberately independent of the app state machine and of ARCore tracking state.)
                var catalog = ServiceLocator.Get<CatalogService>();
                var selection = ServiceLocator.Get<ObjectSelectionService>();
                bool editingObject = selection != null && selection.HasSelection;
                return catalog != null && catalog.HasSelection && !editingObject;
            }
        }

        /// <summary>True while the marker is pinned to a spot the user tapped.</summary>
        public bool IsPinned => m_IsPinned;

        /// <summary>Furniture placed further away than this barely shows parallax and reads as floating. Capped at 3.5m to stop the reticle flying off.</summary>
        const float MaxPlacementDistance = 3.5f;

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
            if (m_SquareRenderer != null)
            {
                m_SquareRenderer.startColor = col;
                m_SquareRenderer.endColor = col;
            }
            if (m_CircleRenderer != null)
            {
                m_CircleRenderer.startColor = col;
                m_CircleRenderer.endColor = col;
            }
            for (int i = 0; i < 4; i++)
            {
                if (m_CornerRenderers[i] != null)
                {
                    m_CornerRenderers[i].startColor = col;
                    m_CornerRenderers[i].endColor = col;
                }
            }
            if (m_ArrowRenderer != null)
            {
                Color arrowCol = col;
                arrowCol.a *= 0.7f;
                m_ArrowRenderer.startColor = arrowCol;
                m_ArrowRenderer.endColor = arrowCol;
            }
            if (m_DotRenderer != null)
            {
                m_DotRenderer.startColor = col;
                m_DotRenderer.endColor = col;
            }
        }

        void SetVisible(bool visible)
        {
            SetRendererEnabled(m_SquareRenderer, visible);
            SetRendererEnabled(m_CircleRenderer, visible);
            SetRendererEnabled(m_ArrowRenderer, visible);
            SetRendererEnabled(m_DotRenderer, visible);
            for (int i = 0; i < 4; i++)
                SetRendererEnabled(m_CornerRenderers[i], visible);
            if (!visible)
                m_HasValidPose = false;
        }

        static void SetRendererEnabled(Renderer r, bool enabled)
        {
            if (r != null && r.enabled != enabled)
                r.enabled = enabled;
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
