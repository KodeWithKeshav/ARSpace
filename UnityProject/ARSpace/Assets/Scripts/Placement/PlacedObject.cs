using System;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Component attached to the root of every instantiated workplace furniture item.
    /// Manages identity, catalogue metadata link, bounds, selection, lock status, and visual tinting.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class PlacedObject : MonoBehaviour
    {
        [Header("Identity & Catalogue")]
        [SerializeField]
        string m_InstanceId;

        [SerializeField]
        FurnitureItem m_FurnitureItem;

        [Header("Visual Feedback")]
        [SerializeField]
        Renderer[] m_Renderers;

        [SerializeField]
        BoxCollider m_BoxCollider;

        bool m_IsSelected = false;
        bool m_IsLocked = false;
        bool m_HasConflict = false;

        MaterialPropertyBlock m_PropertyBlock;
        static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");

        // ── Drift rejection ─────────────────────────────────────
        // ARCore anchor corrections can jitter ±1–3 cm frame-to-frame on noisy floors.
        // We store the "intended" world pose and smooth small corrections so the object
        // appears rock-solid, while still allowing large relocalisations through.

        [Header("Stability")]
        [Tooltip("Maximum per-frame correction (metres) that counts as jitter and gets smoothed.")]
        [SerializeField]
        float m_DriftThreshold = 0.025f;

        [Tooltip("Smoothing factor for jitter rejection (lower = smoother but laggier).")]
        [SerializeField, Range(0.02f, 1f)]
        float m_SmoothFactor = 0.08f;

        /// <summary>The world pose we intend the object to be at. Set by placement / drag-end / nudge.</summary>
        Vector3 m_IntendedPosition;
        Quaternion m_IntendedRotation;
        bool m_IntendedPoseSet;

        /// <summary>True while the user is actively manipulating (dragging / rotating / height adjust).
        /// Drift rejection is paused so the user's input is applied immediately.</summary>
        bool m_IsBeingManipulated;
        public bool IsBeingManipulated => m_IsBeingManipulated;

        public string InstanceId => m_InstanceId;
        public FurnitureItem Item => m_FurnitureItem;
        public BoxCollider Collider => m_BoxCollider;
        public bool IsSelected => m_IsSelected;
        public bool IsLocked => m_IsLocked;
        public bool HasConflict => m_HasConflict;

        /// <summary>
        /// World bounds of this object computed from its box collider or renderers.
        /// </summary>
        public Bounds WorldBounds
        {
            get
            {
                if (m_BoxCollider != null)
                    return m_BoxCollider.bounds;

                Bounds combined = new Bounds(transform.position, Vector3.zero);
                if (m_Renderers != null && m_Renderers.Length > 0)
                {
                    combined = m_Renderers[0].bounds;
                    for (int i = 1; i < m_Renderers.Length; i++)
                    {
                        if (m_Renderers[i] != null)
                            combined.Encapsulate(m_Renderers[i].bounds);
                    }
                }
                return combined;
            }
        }

        void Awake()
        {
            if (string.IsNullOrEmpty(m_InstanceId))
            {
                m_InstanceId = Guid.NewGuid().ToString("N");
            }

            if (m_BoxCollider == null)
            {
                m_BoxCollider = GetComponent<BoxCollider>();
            }

            if (m_Renderers == null || m_Renderers.Length == 0)
            {
                m_Renderers = GetComponentsInChildren<Renderer>(true);
            }

            m_PropertyBlock = new MaterialPropertyBlock();

            if (GetComponent<ContactShadow>() == null)
                gameObject.AddComponent<ContactShadow>();
        }

        // ── Drift rejection in LateUpdate ────────────────────

        void LateUpdate()
        {
            if (!m_IntendedPoseSet || m_IsBeingManipulated)
                return;

            // After the anchor system has moved us, compare to where we should be.
            Vector3 currentPos = transform.position;
            float drift = Vector3.Distance(currentPos, m_IntendedPosition);

            if (drift > 0.0001f && drift < m_DriftThreshold)
            {
                // Small correction = jitter. Smooth it out.
                transform.position = Vector3.Lerp(currentPos, m_IntendedPosition, 1f - m_SmoothFactor);
            }
            else if (drift >= m_DriftThreshold)
            {
                // Large correction = genuine relocalisation. Accept and update intended.
                m_IntendedPosition = currentPos;
            }

            // Lock Y to floor height to prevent vertical floating.
            if (!float.IsNaN(FloorWorldY))
            {
                Vector3 pos = transform.position;
                float desiredY = FloorWorldY + (m_IntendedPosition.y - FloorWorldY);
                if (Mathf.Abs(pos.y - desiredY) > 0.0005f && Mathf.Abs(pos.y - desiredY) < 0.1f)
                {
                    pos.y = Mathf.Lerp(pos.y, desiredY, 0.2f);
                    transform.position = pos;
                }
            }
        }

        /// <summary>Call after placing, re-anchoring, or finishing a manipulation to record the intended pose.</summary>
        public void SetIntendedPose()
        {
            m_IntendedPosition = transform.position;
            m_IntendedRotation = transform.rotation;
            m_IntendedPoseSet = true;
        }

        /// <summary>Call when starting a drag/rotate/height-adjust gesture so drift rejection is paused.</summary>
        public void BeginManipulation()
        {
            m_IsBeingManipulated = true;
        }

        /// <summary>Call when ending a manipulation gesture. Records the new intended pose and re-enables drift rejection.</summary>
        public void EndManipulation()
        {
            m_IsBeingManipulated = false;
            SetIntendedPose();
        }

        /// <summary>World height of the floor this object was placed on (NaN if unknown). Used to reset height and draw its floor shadow.</summary>
        public float FloorWorldY { get; private set; } = float.NaN;

        public void SetFloorWorldY(float y) => FloorWorldY = y;

        /// <summary>World-space Y of the lowest visible point of the model (ignores overlays like the contact shadow).</summary>
        public float BaseWorldY
        {
            get
            {
                bool found = false;
                float minY = transform.position.y;
                if (m_Renderers != null)
                {
                    for (int i = 0; i < m_Renderers.Length; i++)
                    {
                        var r = m_Renderers[i];
                        if (r == null) continue;
                        float y = r.bounds.min.y;
                        if (!found || y < minY) { minY = y; found = true; }
                    }
                }
                return minY;
            }
        }

        /// <summary>Raises the object if its lowest point is below <paramref name="worldY"/>.</summary>
        public void SnapBaseToHeightIfBelow(float worldY)
        {
            float delta = worldY - BaseWorldY;
            if (delta > 0.0005f && delta < 0.5f)
                transform.position += Vector3.up * delta;
        }

        /// <summary>
        /// Moves the object vertically so the lowest point of its model rests exactly on the given world height.
        /// Guarantees furniture sits on the detected floor regardless of how the prefab pivot was authored.
        /// </summary>
        public void SnapBaseToHeight(float worldY)
        {
            float delta = worldY - BaseWorldY;

            // A wildly large correction means the renderer bounds are unreliable; leave the object where it is.
            if (Mathf.Abs(delta) > 0.5f)
                return;

            if (Mathf.Abs(delta) > 0.0005f)
                transform.position += Vector3.up * delta;
        }

        /// <summary>
        /// Initializes the placed object with its corresponding catalogue item and optional instance ID.
        /// </summary>
        public void Initialize(FurnitureItem item, string instanceId = null)
        {
            m_FurnitureItem = item;
            m_InstanceId = !string.IsNullOrEmpty(instanceId) ? instanceId : Guid.NewGuid().ToString("N");
        }

        /// <summary>
        /// Toggles selection state.
        /// </summary>
        public void SetSelected(bool selected)
        {
            m_IsSelected = selected;
        }

        /// <summary>
        /// Toggles lock state (preventing accidental translation gestures).
        /// </summary>
        public void SetLocked(bool locked)
        {
            m_IsLocked = locked;
        }

        /// <summary>
        /// Tints the object during drag manipulation if its footprint conflicts with another object.
        /// </summary>
        public void SetConflictTint(bool conflict)
        {
            if (m_HasConflict == conflict) return;
            m_HasConflict = conflict;

            if (m_Renderers == null) return;

            Color tint = conflict ? new Color(1.0f, 0.42f, 0.05f, 0.75f) : Color.white;

            for (int i = 0; i < m_Renderers.Length; i++)
            {
                var r = m_Renderers[i];
                if (r != null)
                {
                    r.GetPropertyBlock(m_PropertyBlock);
                    m_PropertyBlock.SetColor(s_BaseColorId, tint);
                    r.SetPropertyBlock(m_PropertyBlock);
                }
            }
        }

        /// <summary>
        /// Sets the box collider reference (used by Editor builders).
        /// </summary>
        public void SetCollider(BoxCollider boxCollider)
        {
            m_BoxCollider = boxCollider;
        }

        /// <summary>
        /// Sets the renderers list (used by Editor builders).
        /// </summary>
        public void SetRenderers(Renderer[] renderers)
        {
            m_Renderers = renderers;
        }
    }
}
