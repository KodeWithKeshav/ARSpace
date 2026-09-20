using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Shows a semi-transparent "ghost" preview of the furniture at the reticle position
    /// before the user commits to placement. This lets them see exactly what the furniture
    /// will look like (size, orientation, fit) before tapping Place.
    ///
    /// The ghost is destroyed on placement confirmation, cancellation, or item change.
    /// A subtle breathing opacity animation indicates "preview mode".
    /// </summary>
    public class PlacementPreview : MonoBehaviour
    {
        [Header("Appearance")]
        [Tooltip("Base opacity of the ghost preview (0 = invisible, 1 = fully opaque).")]
        [SerializeField, Range(0.1f, 0.8f)]
        float m_BaseOpacity = 0.35f;

        [Tooltip("Pulse amplitude added to the base opacity.")]
        [SerializeField, Range(0f, 0.2f)]
        float m_PulseAmplitude = 0.08f;

        [Tooltip("Pulse speed in cycles per second.")]
        [SerializeField]
        float m_PulseSpeed = 1.2f;

        PlacementReticle m_Reticle;
        CatalogService m_CatalogService;

        GameObject m_PreviewInstance;
        FurnitureItem m_PreviewItem;
        Renderer[] m_PreviewRenderers;
        MaterialPropertyBlock m_PropBlock;

        static readonly int s_BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int s_ColorId = Shader.PropertyToID("_Color");

        void Awake()
        {
            m_PropBlock = new MaterialPropertyBlock();
        }

        void Start()
        {
            m_Reticle = ServiceLocator.Get<PlacementReticle>();
            m_CatalogService = ServiceLocator.Get<CatalogService>();
        }

        void OnEnable()
        {
            GameEvents.FurnitureSelected += OnFurnitureSelected;
            GameEvents.PlacementCancelled += OnPlacementCancelled;
            GameEvents.ObjectPlaced += OnObjectPlaced;
        }

        void OnDisable()
        {
            GameEvents.FurnitureSelected -= OnFurnitureSelected;
            GameEvents.PlacementCancelled -= OnPlacementCancelled;
            GameEvents.ObjectPlaced -= OnObjectPlaced;
            DestroyPreview();
        }

        void Update()
        {
            if (m_Reticle == null)
                m_Reticle = ServiceLocator.Get<PlacementReticle>();
            if (m_CatalogService == null)
                m_CatalogService = ServiceLocator.Get<CatalogService>();

            FurnitureItem currentItem = m_CatalogService != null ? m_CatalogService.SelectedItem : null;

            // If the selected item changed, rebuild the preview
            if (currentItem != m_PreviewItem)
            {
                DestroyPreview();
                if (currentItem != null && currentItem.Prefab != null)
                    CreatePreview(currentItem);
            }

            // Update position to follow the reticle
            if (m_PreviewInstance != null && m_Reticle != null)
            {
                bool show = m_Reticle.HasHit && m_Reticle.IsPlacementActive;
                m_PreviewInstance.SetActive(show);

                if (show)
                {
                    Pose pose = m_Reticle.CurrentPose;
                    m_PreviewInstance.transform.SetPositionAndRotation(pose.position, pose.rotation);

                    // Breathing opacity animation
                    float pulse = m_BaseOpacity + Mathf.Sin(Time.time * m_PulseSpeed * Mathf.PI * 2f) * m_PulseAmplitude;
                    ApplyGhostOpacity(pulse);
                }
            }
        }

        void CreatePreview(FurnitureItem item)
        {
            m_PreviewItem = item;
            m_PreviewInstance = Instantiate(item.Prefab);
            m_PreviewInstance.name = $"Preview_{item.DisplayName}";

            // Remove any components that shouldn't be on the preview
            // (PlacedObject, Colliders, ContactShadow, etc.)
            foreach (var comp in m_PreviewInstance.GetComponentsInChildren<PlacedObject>(true))
                Destroy(comp);
            foreach (var comp in m_PreviewInstance.GetComponentsInChildren<ContactShadow>(true))
                Destroy(comp);
            foreach (var col in m_PreviewInstance.GetComponentsInChildren<Collider>(true))
                Destroy(col);
            foreach (var rb in m_PreviewInstance.GetComponentsInChildren<Rigidbody>(true))
                Destroy(rb);

            m_PreviewRenderers = m_PreviewInstance.GetComponentsInChildren<Renderer>(true);
            ApplyGhostOpacity(m_BaseOpacity);

            m_PreviewInstance.SetActive(false);
        }

        void ApplyGhostOpacity(float opacity)
        {
            if (m_PreviewRenderers == null) return;

            Color tint = new Color(0.8f, 0.9f, 1.0f, opacity); // slight blue tint for "ghost" feel

            for (int i = 0; i < m_PreviewRenderers.Length; i++)
            {
                var r = m_PreviewRenderers[i];
                if (r == null) continue;

                r.GetPropertyBlock(m_PropBlock);
                m_PropBlock.SetColor(s_BaseColorId, tint);
                m_PropBlock.SetColor(s_ColorId, tint);
                r.SetPropertyBlock(m_PropBlock);
            }
        }

        void DestroyPreview()
        {
            if (m_PreviewInstance != null)
            {
                Destroy(m_PreviewInstance);
                m_PreviewInstance = null;
            }
            m_PreviewItem = null;
            m_PreviewRenderers = null;
        }

        void OnFurnitureSelected(string itemId)
        {
            // Preview will be rebuilt in Update when the item changes
        }

        void OnPlacementCancelled()
        {
            DestroyPreview();
        }

        void OnObjectPlaced(GameObject placed)
        {
            // Keep the preview alive for rapid multi-placement.
            // It will be destroyed when the user cancels or selects a different item.
        }
    }
}
