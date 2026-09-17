using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Renders visual selection feedback around the currently selected furniture object:
    /// - Inner footprint rectangle hugging the floor plane
    /// - Outer circulation clearance margin boundary
    /// - Dynamically updates as the object is translated, rotated, or scaled
    /// </summary>
    public class SelectionVisual : MonoBehaviour
    {
        [Header("Renderers")]
        [SerializeField]
        LineRenderer m_FootprintRenderer;

        [SerializeField]
        LineRenderer m_ClearanceRenderer;

        [Header("Colours")]
        [SerializeField]
        Color m_SelectedColor = new Color(1.0f, 0.42f, 0.0f, 0.95f); // ARSpace Orange

        [SerializeField]
        Color m_ClearanceColor = new Color(1.0f, 0.65f, 0.2f, 0.6f);

        [SerializeField]
        Color m_ConflictColor = new Color(0.95f, 0.2f, 0.1f, 0.95f); // Red/Amber Conflict

        PlacedObject m_CurrentTarget;

        readonly Vector3[] m_InnerPoints = new Vector3[5];
        readonly Vector3[] m_OuterPoints = new Vector3[5];

        void Awake()
        {
            SetupRenderers();
            SetVisible(false);
        }

        void OnEnable()
        {
            GameEvents.ObjectSelectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            GameEvents.ObjectSelectionChanged -= OnSelectionChanged;
        }

        void SetupRenderers()
        {
            if (m_FootprintRenderer == null)
            {
                var innerGo = new GameObject("InnerFootprint");
                innerGo.transform.SetParent(transform, false);
                m_FootprintRenderer = innerGo.AddComponent<LineRenderer>();
            }

            m_FootprintRenderer.loop = true;
            m_FootprintRenderer.useWorldSpace = true;
            m_FootprintRenderer.alignment = LineAlignment.View;
            m_FootprintRenderer.startWidth = 0.008f;
            m_FootprintRenderer.endWidth = 0.008f;
            m_FootprintRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_FootprintRenderer.receiveShadows = false;

            if (m_ClearanceRenderer == null)
            {
                var outerGo = new GameObject("OuterClearance");
                outerGo.transform.SetParent(transform, false);
                m_ClearanceRenderer = outerGo.AddComponent<LineRenderer>();
            }

            m_ClearanceRenderer.loop = true;
            m_ClearanceRenderer.useWorldSpace = true;
            m_ClearanceRenderer.alignment = LineAlignment.View;
            m_ClearanceRenderer.startWidth = 0.004f;
            m_ClearanceRenderer.endWidth = 0.004f;
            m_ClearanceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_ClearanceRenderer.receiveShadows = false;

            // Load line material if available
            var mat = Resources.Load<Material>("M_PlaneOutline");
            if (mat != null)
            {
                m_FootprintRenderer.sharedMaterial = mat;
                m_ClearanceRenderer.sharedMaterial = mat;
            }
        }

        void Update()
        {
            if (m_CurrentTarget == null)
            {
                SetVisible(false);
                return;
            }

            UpdateGeometry();
        }

        void UpdateGeometry()
        {
            Transform t = m_CurrentTarget.transform;
            Vector3 center = t.position;
            Quaternion rot = t.rotation;
            Vector3 scale = t.localScale;

            FurnitureItem item = m_CurrentTarget.Item;
            Vector2 fp = item != null ? item.Footprint : new Vector2(1f, 1f);
            float clearance = item != null ? item.ClearanceMargin : 0.5f;

            // Scaled half-extents
            float hx = fp.x * 0.5f * scale.x;
            float hz = fp.y * 0.5f * scale.z;

            float cx = hx + clearance * scale.x;
            float cz = hz + clearance * scale.z;

            const float groundOffset = 0.003f; // 3mm above floor plane

            // Compute 4 corners in local space relative to center, rotated by yaw
            m_InnerPoints[0] = center + rot * new Vector3(-hx, groundOffset, -hz);
            m_InnerPoints[1] = center + rot * new Vector3( hx, groundOffset, -hz);
            m_InnerPoints[2] = center + rot * new Vector3( hx, groundOffset,  hz);
            m_InnerPoints[3] = center + rot * new Vector3(-hx, groundOffset,  hz);
            m_InnerPoints[4] = m_InnerPoints[0];

            m_OuterPoints[0] = center + rot * new Vector3(-cx, groundOffset, -cz);
            m_OuterPoints[1] = center + rot * new Vector3( cx, groundOffset, -cz);
            m_OuterPoints[2] = center + rot * new Vector3( cx, groundOffset,  cz);
            m_OuterPoints[3] = center + rot * new Vector3(-cx, groundOffset,  cz);
            m_OuterPoints[4] = m_OuterPoints[0];

            m_FootprintRenderer.positionCount = 5;
            m_FootprintRenderer.SetPositions(m_InnerPoints);

            m_ClearanceRenderer.positionCount = 5;
            m_ClearanceRenderer.SetPositions(m_OuterPoints);

            // Update color based on conflict status
            Color primaryColor = m_CurrentTarget.HasConflict ? m_ConflictColor : m_SelectedColor;
            m_FootprintRenderer.startColor = primaryColor;
            m_FootprintRenderer.endColor = primaryColor;

            m_ClearanceRenderer.startColor = m_ClearanceColor;
            m_ClearanceRenderer.endColor = m_ClearanceColor;
        }

        void OnSelectionChanged(GameObject selectedGo)
        {
            if (selectedGo != null)
            {
                m_CurrentTarget = selectedGo.GetComponent<PlacedObject>();
                SetVisible(m_CurrentTarget != null);
                if (m_CurrentTarget != null)
                {
                    UpdateGeometry();
                }
            }
            else
            {
                m_CurrentTarget = null;
                SetVisible(false);
            }
        }

        void SetVisible(bool visible)
        {
            if (m_FootprintRenderer != null && m_FootprintRenderer.enabled != visible)
                m_FootprintRenderer.enabled = visible;
            if (m_ClearanceRenderer != null && m_ClearanceRenderer.enabled != visible)
                m_ClearanceRenderer.enabled = visible;
        }
    }
}
