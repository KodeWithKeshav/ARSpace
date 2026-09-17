using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ARSpace.Core;

namespace ARSpace.AR
{
    /// <summary>
    /// Visualises detected AR planes with a world-space architectural grid,
    /// a crisp boundary outline, and boundary feathering.
    ///
    /// Requirements:
    /// - Uses ONE shared material across all plane instances.
    /// - Uses <see cref="MaterialPropertyBlock"/> for any per-plane shader properties.
    ///   Never instantiates materials via <c>renderer.material</c>.
    /// - LineRenderer renders the boundary polygon in real-time.
    /// - Vertex colors provide smooth boundary feathering to eliminate hard mesh edges.
    /// - Toggleable via <see cref="GameEvents.PlaneVisualsToggled"/>.
    /// - Auto-hides when entering presentation mode via <see cref="GameEvents.PresentationModeToggled"/>.
    /// </summary>
    [RequireComponent(typeof(ARPlane))]
    [RequireComponent(typeof(ARPlaneMeshVisualizer))]
    [RequireComponent(typeof(MeshRenderer))]
    public class PlaneGridVisualizer : MonoBehaviour
    {
        [Header("Feathering & Boundary")]
        [Tooltip("Distance (in metres) over which the grid fades to 0 opacity near the boundary.")]
        [SerializeField]
        float m_FeatheringWidth = 0.25f;

        [Tooltip("Thickness of the boundary outline in metres.")]
        [SerializeField]
        float m_OutlineWidth = 0.006f;

        [Tooltip("Duration of alpha fade-in / fade-out when planes appear or toggle visibility.")]
        [SerializeField]
        float m_FadeDuration = 0.35f;

        [Header("Materials (Shared Assets)")]
        [SerializeField]
        Material m_SharedGridMaterial;

        [SerializeField]
        Material m_OutlineMaterial;

        // Components
        ARPlane m_Plane;
        ARPlaneMeshVisualizer m_MeshVisualizer;
        MeshRenderer m_MeshRenderer;
        LineRenderer m_LineRenderer;

        // Material property block for zero-allocation per-plane properties
        MaterialPropertyBlock m_PropertyBlock;
        static readonly int s_PlaneAlphaId = Shader.PropertyToID("_PlaneAlpha");

        // Visibility & Fading state
        float m_CurrentAlpha = 0f;
        float m_TargetAlpha = 1f;
        bool m_VisualsEnabled = true;
        bool m_InPresentationMode = false;

        // Preallocated memory to avoid per-frame GC allocations
        Vector3[] m_LinePointsBuffer = new Vector3[64];
        Color32[] m_VertexColorsBuffer = new Color32[64];
        readonly List<Vector3> m_VerticesList = new List<Vector3>(64);

        void Awake()
        {
            m_Plane = GetComponent<ARPlane>();
            m_MeshVisualizer = GetComponent<ARPlaneMeshVisualizer>();
            m_MeshRenderer = GetComponent<MeshRenderer>();
            m_LineRenderer = GetComponent<LineRenderer>();

            m_PropertyBlock = new MaterialPropertyBlock();

            // Set up LineRenderer for crisp boundary outline
            SetupLineRenderer();

            // Ensure shared material is applied without instantiation
            if (m_SharedGridMaterial != null && m_MeshRenderer.sharedMaterial == null)
            {
                m_MeshRenderer.sharedMaterial = m_SharedGridMaterial;
            }

            // Start transparent and fade in
            m_CurrentAlpha = 0f;
            m_TargetAlpha = 1f;
            ApplyPropertyBlockAlpha(0f);
        }

        void OnEnable()
        {
            m_Plane.boundaryChanged += OnBoundaryChanged;
            GameEvents.PlaneVisualsToggled += OnPlaneVisualsToggled;
            GameEvents.PresentationModeToggled += OnPresentationModeToggled;

            UpdateVisibilityState();

            // Initial update if boundary is already available
            if (m_Plane.boundary.IsCreated && m_Plane.boundary.Length >= 3)
            {
                UpdateBoundaryOutline();
                UpdateBoundaryFeathering();
            }
        }

        void OnDisable()
        {
            m_Plane.boundaryChanged -= OnBoundaryChanged;
            GameEvents.PlaneVisualsToggled -= OnPlaneVisualsToggled;
            GameEvents.PresentationModeToggled -= OnPresentationModeToggled;
        }

        void Update()
        {
            // Smoothly animate alpha toward target
            if (!Mathf.Approximately(m_CurrentAlpha, m_TargetAlpha))
            {
                m_CurrentAlpha = Mathf.MoveTowards(
                    m_CurrentAlpha,
                    m_TargetAlpha,
                    Time.deltaTime / Mathf.Max(m_FadeDuration, 0.01f)
                );

                ApplyPropertyBlockAlpha(m_CurrentAlpha);

                // When fully faded out, disable renderers to conserve mobile GPU fillrate
                bool isVisible = m_CurrentAlpha > 0.001f;
                if (m_MeshRenderer.enabled != isVisible)
                {
                    m_MeshRenderer.enabled = isVisible;
                }
                if (m_LineRenderer != null && m_LineRenderer.enabled != isVisible)
                {
                    m_LineRenderer.enabled = isVisible;
                }
            }
        }

        void SetupLineRenderer()
        {
            if (m_LineRenderer == null)
            {
                m_LineRenderer = gameObject.AddComponent<LineRenderer>();
            }

            m_LineRenderer.loop = true;
            m_LineRenderer.useWorldSpace = false;
            m_LineRenderer.alignment = LineAlignment.View;
            m_LineRenderer.startWidth = m_OutlineWidth;
            m_LineRenderer.endWidth = m_OutlineWidth;
            m_LineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_LineRenderer.receiveShadows = false;

            if (m_OutlineMaterial != null)
            {
                m_LineRenderer.sharedMaterial = m_OutlineMaterial;
            }
        }

        void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args)
        {
            UpdateBoundaryOutline();
            UpdateBoundaryFeathering();
        }

        /// <summary>
        /// Updates the <see cref="LineRenderer"/> positions to follow the plane's boundary polygon.
        /// </summary>
        void UpdateBoundaryOutline()
        {
            if (m_LineRenderer == null)
                return;

            var boundary = m_Plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3)
            {
                m_LineRenderer.positionCount = 0;
                return;
            }

            int count = boundary.Length;
            if (m_LinePointsBuffer == null || m_LinePointsBuffer.Length < count)
            {
                m_LinePointsBuffer = new Vector3[Mathf.NextPowerOfTwo(count)];
            }

            // In plane-local space: X is local X, Y is local Z.
            // Slight Y offset of 2mm lifts the outline cleanly above the floor mesh,
            // preventing any depth contention.
            const float localHeightOffset = 0.002f;
            for (int i = 0; i < count; i++)
            {
                Vector2 pt = boundary[i];
                m_LinePointsBuffer[i] = new Vector3(pt.x, localHeightOffset, pt.y);
            }

            m_LineRenderer.positionCount = count;
            m_LineRenderer.SetPositions(m_LinePointsBuffer);
        }

        /// <summary>
        /// Calculates vertex colors on the plane mesh based on distance to the boundary polygon,
        /// providing smooth edge feathering without hard rectangular edges.
        /// </summary>
        void UpdateBoundaryFeathering()
        {
            var mesh = m_MeshVisualizer != null ? m_MeshVisualizer.mesh : null;
            if (mesh == null)
                return;

            var boundary = m_Plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3)
                return;

            int vertexCount = mesh.vertexCount;
            if (vertexCount == 0)
                return;

            if (m_VertexColorsBuffer == null || m_VertexColorsBuffer.Length < vertexCount)
            {
                m_VertexColorsBuffer = new Color32[Mathf.NextPowerOfTwo(vertexCount)];
            }

            mesh.GetVertices(m_VerticesList);

            float invFeather = 1.0f / Mathf.Max(m_FeatheringWidth, 0.001f);
            int boundaryCount = boundary.Length;

            for (int i = 0; i < m_VerticesList.Count; i++)
            {
                Vector3 v = m_VerticesList[i];
                Vector2 pt = new Vector2(v.x, v.z);

                // Compute shortest distance squared from vertex to any boundary segment
                float minDistSq = float.MaxValue;
                for (int j = 0; j < boundaryCount; j++)
                {
                    int next = (j + 1) % boundaryCount;
                    Vector2 a = boundary[j];
                    Vector2 b = boundary[next];
                    float distSq = DistancePointToSegmentSq(pt, a, b);
                    if (distSq < minDistSq)
                        minDistSq = distSq;
                }

                float minDist = Mathf.Sqrt(minDistSq);
                float alpha = Mathf.Clamp01(minDist * invFeather);
                byte alphaByte = (byte)(alpha * 255);

                m_VertexColorsBuffer[i] = new Color32(255, 255, 255, alphaByte);
            }

            mesh.SetColors(m_VertexColorsBuffer, 0, m_VerticesList.Count);
        }

        static float DistancePointToSegmentSq(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float abLenSq = ab.sqrMagnitude;
            if (abLenSq < 1e-6f)
                return (p - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / abLenSq);
            Vector2 proj = a + t * ab;
            return (p - proj).sqrMagnitude;
        }

        void ApplyPropertyBlockAlpha(float alpha)
        {
            if (m_MeshRenderer == null)
                return;

            m_MeshRenderer.GetPropertyBlock(m_PropertyBlock);
            m_PropertyBlock.SetFloat(s_PlaneAlphaId, alpha);
            m_MeshRenderer.SetPropertyBlock(m_PropertyBlock);
        }

        void OnPlaneVisualsToggled(bool visible)
        {
            m_VisualsEnabled = visible;
            UpdateVisibilityState();
        }

        void OnPresentationModeToggled(bool presenting)
        {
            m_InPresentationMode = presenting;
            UpdateVisibilityState();
        }

        void UpdateVisibilityState()
        {
            bool shouldBeVisible = m_VisualsEnabled && !m_InPresentationMode;
            m_TargetAlpha = shouldBeVisible ? 1.0f : 0.0f;

            if (shouldBeVisible)
            {
                m_MeshRenderer.enabled = true;
                if (m_LineRenderer != null)
                    m_LineRenderer.enabled = true;
            }
        }
    }
}
