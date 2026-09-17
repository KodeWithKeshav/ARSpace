using System;
using UnityEngine;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Component attached to the root of every instantiated workplace furniture item.
    /// Manages the object's identity, item reference, bounds, selection state, and transform tracking.
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

        public string InstanceId => m_InstanceId;
        public FurnitureItem Item => m_FurnitureItem;
        public BoxCollider Collider => m_BoxCollider;
        public bool IsSelected => m_IsSelected;

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
        /// Toggles selection highlight state.
        /// </summary>
        public void SetSelected(bool selected)
        {
            m_IsSelected = selected;
            // Visual affordance or outline can be driven here or via ObjectSelectionService
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
