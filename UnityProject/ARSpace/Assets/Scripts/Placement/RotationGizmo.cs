using System;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Unity-style interactive rotation gizmo that appears around the selected object.
    /// Draws a Y-axis rotation ring at floor level with 4 draggable handle points (N/S/E/W).
    /// Dragging any handle rotates the object — the rotation follows the finger angle
    /// relative to the object centre. Snaps to 15° increments with visual feedback.
    /// </summary>
    public class RotationGizmo : MonoBehaviour
    {
        [Header("Ring Settings")]
        [Tooltip("Extra padding added to the ring radius beyond the object's footprint.")]
        [SerializeField]
        float m_RadiusPadding = 0.15f;

        [Tooltip("Number of segments for the rotation ring.")]
        [SerializeField]
        int m_RingSegments = 64;

        [Tooltip("Width of the ring line.")]
        [SerializeField]
        float m_RingWidth = 0.008f;

        [Header("Handles")]
        [Tooltip("Radius of the draggable handle spheres.")]
        [SerializeField]
        float m_HandleRadius = 0.035f;

        [Tooltip("Screen-space hit radius for touch detection on handles (pixels).")]
        [SerializeField]
        float m_HandleTouchRadius = 60f;

        [Header("Snapping")]
        [Tooltip("Rotation snaps to multiples of this angle in degrees.")]
        [SerializeField]
        float m_SnapAngle = 15f;

        [Tooltip("Angle window (degrees) within which the rotation magnetically snaps.")]
        [SerializeField]
        float m_SnapMagneticThreshold = 4f;

        [Header("Colours")]
        [SerializeField]
        Color m_IdleColor = new Color(1.0f, 0.42f, 0.0f, 0.6f); // semi-transparent orange

        [SerializeField]
        Color m_ActiveColor = new Color(1.0f, 0.55f, 0.0f, 1.0f); // solid orange when dragging

        [SerializeField]
        Color m_SnapColor = new Color(0.2f, 0.85f, 0.4f, 1.0f); // green at snap points

        [SerializeField]
        Color m_HandleColor = new Color(1.0f, 0.42f, 0.0f, 0.9f);

        // Components
        LineRenderer m_RingRenderer;
        GameObject[] m_HandleObjects = new GameObject[4];
        MeshRenderer[] m_HandleRenderers = new MeshRenderer[4];

        // State
        PlacedObject m_Target;
        float m_CurrentRadius;
        bool m_IsActive;
        bool m_IsDragging;
        int m_DragHandleIndex = -1;
        float m_DragStartAngle;
        float m_ObjectStartYaw;
        Camera m_Camera;

        // Precomputed ring points
        Vector3[] m_RingPoints;

        // Handle collider layer
        static Mesh s_SphereMesh;
        static Material s_HandleMaterial;

        void Awake()
        {
            ServiceLocator.Register(this);
            m_Camera = Camera.main;
            m_RingPoints = new Vector3[m_RingSegments];
            BuildVisuals();
            SetVisible(false);
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<RotationGizmo>();
        }

        void OnEnable()
        {
            GameEvents.ObjectSelectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            GameEvents.ObjectSelectionChanged -= OnSelectionChanged;
        }

        void BuildVisuals()
        {
            // ── Ring ────────────────────────────────────────────
            var ringGo = new GameObject("GizmoRing");
            ringGo.transform.SetParent(transform, false);
            m_RingRenderer = ringGo.AddComponent<LineRenderer>();
            m_RingRenderer.loop = true;
            m_RingRenderer.useWorldSpace = true;
            m_RingRenderer.alignment = LineAlignment.View;
            m_RingRenderer.startWidth = m_RingWidth;
            m_RingRenderer.endWidth = m_RingWidth;
            m_RingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_RingRenderer.receiveShadows = false;
            m_RingRenderer.numCapVertices = 4;
            m_RingRenderer.positionCount = m_RingSegments;

            // ── Handles (4 small spheres at N/E/S/W) ────────────
            if (s_SphereMesh == null)
                s_SphereMesh = CreateSphereMesh();

            if (s_HandleMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                s_HandleMaterial = new Material(shader) { name = "GizmoHandle" };
                s_HandleMaterial.color = m_HandleColor;
            }

            for (int i = 0; i < 4; i++)
            {
                var handleGo = new GameObject($"GizmoHandle{i}");
                handleGo.transform.SetParent(transform, false);
                handleGo.transform.localScale = Vector3.one * m_HandleRadius * 2f;

                handleGo.AddComponent<MeshFilter>().sharedMesh = s_SphereMesh;
                var mr = handleGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = s_HandleMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                m_HandleObjects[i] = handleGo;
                m_HandleRenderers[i] = mr;
            }
        }

        void OnSelectionChanged(GameObject selectedGo)
        {
            if (selectedGo != null)
            {
                var placed = selectedGo.GetComponent<PlacedObject>();
                if (placed != null)
                {
                    Show(placed);
                    return;
                }
            }

            Hide();
        }

        public void Show(PlacedObject target)
        {
            m_Target = target;
            m_IsActive = true;

            // Compute radius from footprint
            FurnitureItem item = target.Item;
            Vector2 fp = item != null ? item.Footprint : new Vector2(1f, 1f);
            float halfDiag = Mathf.Sqrt(fp.x * fp.x + fp.y * fp.y) * 0.5f;
            m_CurrentRadius = halfDiag * target.transform.localScale.x + m_RadiusPadding;
            m_CurrentRadius = Mathf.Max(m_CurrentRadius, 0.25f); // minimum radius

            UpdateRingGeometry();
            UpdateHandlePositions();
            SetVisible(true);
        }

        public void Hide()
        {
            m_Target = null;
            m_IsActive = false;
            m_IsDragging = false;
            m_DragHandleIndex = -1;
            SetVisible(false);
        }

        void Update()
        {
            if (!m_IsActive || m_Target == null)
                return;

            if (m_Camera == null)
                m_Camera = Camera.main;

            UpdateRingGeometry();
            UpdateHandlePositions();

            // Snap visual feedback: flash the ring green when near a snap angle
            if (m_IsDragging)
            {
                float currentYaw = m_Target.transform.eulerAngles.y;
                float nearestSnap = Mathf.Round(currentYaw / m_SnapAngle) * m_SnapAngle;
                float distToSnap = Mathf.Abs(Mathf.DeltaAngle(currentYaw, nearestSnap));

                Color ringColor = distToSnap < m_SnapMagneticThreshold ? m_SnapColor : m_ActiveColor;
                m_RingRenderer.startColor = ringColor;
                m_RingRenderer.endColor = ringColor;
                m_RingRenderer.startWidth = m_RingWidth * 1.5f;
                m_RingRenderer.endWidth = m_RingWidth * 1.5f;
            }
            else
            {
                m_RingRenderer.startColor = m_IdleColor;
                m_RingRenderer.endColor = m_IdleColor;
                m_RingRenderer.startWidth = m_RingWidth;
                m_RingRenderer.endWidth = m_RingWidth;
            }
        }

        void UpdateRingGeometry()
        {
            Vector3 center = m_Target.transform.position;
            float floorY = !float.IsNaN(m_Target.FloorWorldY) ? m_Target.FloorWorldY : center.y;
            center.y = floorY + 0.004f; // slightly above floor

            for (int i = 0; i < m_RingSegments; i++)
            {
                float angle = (i / (float)m_RingSegments) * Mathf.PI * 2f;
                m_RingPoints[i] = center + new Vector3(
                    Mathf.Cos(angle) * m_CurrentRadius,
                    0f,
                    Mathf.Sin(angle) * m_CurrentRadius);
            }

            m_RingRenderer.positionCount = m_RingSegments;
            m_RingRenderer.SetPositions(m_RingPoints);
        }

        void UpdateHandlePositions()
        {
            Vector3 center = m_Target.transform.position;
            float floorY = !float.IsNaN(m_Target.FloorWorldY) ? m_Target.FloorWorldY : center.y;

            // Place handles at 0°, 90°, 180°, 270° relative to the object's current facing
            float baseYaw = m_Target.transform.eulerAngles.y * Mathf.Deg2Rad;
            for (int i = 0; i < 4; i++)
            {
                float angle = baseYaw + i * Mathf.PI * 0.5f;
                Vector3 pos = new Vector3(
                    center.x + Mathf.Sin(angle) * m_CurrentRadius,
                    floorY + m_HandleRadius + 0.005f,
                    center.z + Mathf.Cos(angle) * m_CurrentRadius);
                m_HandleObjects[i].transform.position = pos;
            }
        }

        // ── Touch interaction ───────────────────────────────────

        /// <summary>
        /// Checks if a screen position hits any of the 4 gizmo handles.
        /// Returns the handle index (0-3) or -1 if no hit.
        /// </summary>
        public int HitTestHandles(Vector2 screenPos)
        {
            if (!m_IsActive || m_Target == null || m_Camera == null)
                return -1;

            float bestDist = m_HandleTouchRadius;
            int bestIndex = -1;

            for (int i = 0; i < 4; i++)
            {
                Vector3 screenPt = m_Camera.WorldToScreenPoint(m_HandleObjects[i].transform.position);
                if (screenPt.z < 0f) continue; // behind camera

                float dist = Vector2.Distance(new Vector2(screenPt.x, screenPt.y), screenPos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        /// <summary>Begins a rotation drag on the specified handle.</summary>
        public void BeginDrag(int handleIndex, Vector2 screenPos)
        {
            if (m_Target == null || m_Target.IsLocked)
                return;

            m_IsDragging = true;
            m_DragHandleIndex = handleIndex;
            m_ObjectStartYaw = m_Target.transform.eulerAngles.y;

            // Compute the angle of the initial touch relative to the object centre
            Vector3 center = m_Target.transform.position;
            Vector3 centerScreen = m_Camera.WorldToScreenPoint(center);
            Vector2 toTouch = screenPos - new Vector2(centerScreen.x, centerScreen.y);
            m_DragStartAngle = Mathf.Atan2(toTouch.y, toTouch.x) * Mathf.Rad2Deg;

            m_Target.BeginManipulation();
        }

        /// <summary>Updates the rotation based on the current finger position.</summary>
        public void OnDrag(Vector2 screenPos)
        {
            if (!m_IsDragging || m_Target == null || m_Camera == null)
                return;

            Vector3 center = m_Target.transform.position;
            Vector3 centerScreen = m_Camera.WorldToScreenPoint(center);
            Vector2 toTouch = screenPos - new Vector2(centerScreen.x, centerScreen.y);
            float currentAngle = Mathf.Atan2(toTouch.y, toTouch.x) * Mathf.Rad2Deg;

            // The delta in screen-space angle translates inversely to yaw (screen right = world clockwise)
            float angleDelta = Mathf.DeltaAngle(m_DragStartAngle, currentAngle);
            float targetYaw = m_ObjectStartYaw - angleDelta;

            // Apply magnetic snapping
            if (m_SnapAngle > 0f)
            {
                float nearestSnap = Mathf.Round(targetYaw / m_SnapAngle) * m_SnapAngle;
                float distToSnap = Mathf.Abs(Mathf.DeltaAngle(targetYaw, nearestSnap));

                if (distToSnap <= m_SnapMagneticThreshold)
                    targetYaw = nearestSnap;
            }

            m_Target.transform.rotation = Quaternion.Euler(0, targetYaw, 0);
        }

        /// <summary>Finishes the rotation drag.</summary>
        public async void EndDrag()
        {
            if (!m_IsDragging || m_Target == null)
                return;

            m_IsDragging = false;
            m_DragHandleIndex = -1;

            var obj = m_Target;
            obj.EndManipulation();

            // Re-anchor at the new rotation
            var anchorService = ServiceLocator.Get<AnchorService>();
            if (anchorService != null)
            {
                await anchorService.AttachToAnchorAsync(obj, new Pose(obj.transform.position, obj.transform.rotation), null);
            }

            float snappedYaw = obj.transform.eulerAngles.y;
            float nearestSnap = Mathf.Round(snappedYaw / m_SnapAngle) * m_SnapAngle;
            GameEvents.RaiseToastRequested($"Rotated to {nearestSnap:F0}°");
        }

        /// <summary>Whether the gizmo is currently in a rotation drag.</summary>
        public bool IsDragging => m_IsDragging;

        /// <summary>Whether the gizmo is visible and active.</summary>
        public bool IsActive => m_IsActive;

        void SetVisible(bool visible)
        {
            if (m_RingRenderer != null && m_RingRenderer.enabled != visible)
                m_RingRenderer.enabled = visible;

            for (int i = 0; i < 4; i++)
            {
                if (m_HandleObjects[i] != null && m_HandleObjects[i].activeSelf != visible)
                    m_HandleObjects[i].SetActive(visible);
            }
        }

        // ── Sphere mesh generation ──────────────────────────────

        static Mesh CreateSphereMesh()
        {
            // Simplified UV sphere (good enough for tiny handle dots)
            var mesh = new Mesh { name = "GizmoHandleSphere" };

            const int latSteps = 8;
            const int lonSteps = 12;
            int vertCount = (latSteps + 1) * (lonSteps + 1);
            var verts = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var tris = new int[latSteps * lonSteps * 6];

            int v = 0;
            for (int lat = 0; lat <= latSteps; lat++)
            {
                float theta = lat * Mathf.PI / latSteps;
                float sinTheta = Mathf.Sin(theta);
                float cosTheta = Mathf.Cos(theta);

                for (int lon = 0; lon <= lonSteps; lon++)
                {
                    float phi = lon * 2f * Mathf.PI / lonSteps;
                    float x = sinTheta * Mathf.Cos(phi);
                    float y = cosTheta;
                    float z = sinTheta * Mathf.Sin(phi);

                    verts[v] = new Vector3(x, y, z) * 0.5f;
                    normals[v] = new Vector3(x, y, z);
                    v++;
                }
            }

            int t = 0;
            for (int lat = 0; lat < latSteps; lat++)
            {
                for (int lon = 0; lon < lonSteps; lon++)
                {
                    int a = lat * (lonSteps + 1) + lon;
                    int b = a + lonSteps + 1;

                    tris[t++] = a;
                    tris[t++] = b;
                    tris[t++] = a + 1;

                    tris[t++] = a + 1;
                    tris[t++] = b;
                    tris[t++] = b + 1;
                }
            }

            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
