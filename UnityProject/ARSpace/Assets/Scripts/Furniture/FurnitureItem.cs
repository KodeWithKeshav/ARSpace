using UnityEngine;

namespace ARSpace.Furniture
{
    /// <summary>
    /// ScriptableObject representing a catalogued furniture or architectural component
    /// in the ARSpace CRE workplace layout platform.
    /// </summary>
    [CreateAssetMenu(fileName = "NewFurnitureItem", menuName = "ARSpace/Furniture Item")]
    public class FurnitureItem : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable string identifier / GUID. Never renamed — this is the save-file key.")]
        [SerializeField]
        string m_Id;

        [Tooltip("User-facing display name.")]
        [SerializeField]
        string m_DisplayName;

        [TextArea(2, 4)]
        [Tooltip("Detailed description of the workplace component.")]
        [SerializeField]
        string m_Description;

        [Header("Classification")]
        [Tooltip("Workplace functional category.")]
        [SerializeField]
        FurnitureCategory m_Category = FurnitureCategory.Workstations;

        [Header("Assets")]
        [Tooltip("Runtime-ready prefab with PlacedObject and BoxCollider.")]
        [SerializeField]
        GameObject m_Prefab;

        [Tooltip("UI thumbnail sprite.")]
        [SerializeField]
        Sprite m_Thumbnail;

        [Header("Dimensions & Spatial Maths")]
        [Tooltip("Measured bounding size in metres (Width X, Height Y, Depth Z).")]
        [SerializeField]
        Vector3 m_RealWorldSize = Vector3.one;

        [Tooltip("Floor footprint in metres (Width X, Depth Y/Z). Used for area and density calculations.")]
        [SerializeField]
        Vector2 m_Footprint = Vector2.one;

        [Tooltip("Circulation space required around the item in metres.")]
        [SerializeField]
        float m_ClearanceMargin = 0.5f;

        [Header("CRE Capacity & Planning")]
        [Tooltip("Number of people this item seats. Drives seating capacity analytics.")]
        [SerializeField]
        int m_SeatCount = 0;

        [Tooltip("Partitions and reception desks prefer alignment to the nearest wall or plane boundary.")]
        [SerializeField]
        bool m_WallAligned = false;

        [Header("Placement & Manipulation Constraints")]
        [Tooltip("If true, manipulation gestures only scale uniformly across all axes.")]
        [SerializeField]
        bool m_AllowUniformScaleOnly = true;

        [SerializeField]
        float m_MinScale = 0.5f;

        [SerializeField]
        float m_MaxScale = 2.0f;

        [Tooltip("Angle step in degrees for rotational snapping (e.g. 15 or 45 degrees).")]
        [SerializeField]
        float m_SnapRotationDegrees = 45f;

        // ── Public Accessors ───────────────────────────────────

        public string Id => m_Id;
        public string DisplayName => m_DisplayName;
        public string Description => m_Description;
        public FurnitureCategory Category => m_Category;
        public GameObject Prefab => m_Prefab;
        public Sprite Thumbnail => m_Thumbnail;
        public Vector3 RealWorldSize => m_RealWorldSize;
        public Vector2 Footprint => m_Footprint;
        public float ClearanceMargin => m_ClearanceMargin;
        public int SeatCount => m_SeatCount;
        public bool WallAligned => m_WallAligned;
        public bool AllowUniformScaleOnly => m_AllowUniformScaleOnly;
        public float MinScale => m_MinScale;
        public float MaxScale => m_MaxScale;
        public float SnapRotationDegrees => m_SnapRotationDegrees;

        /// <summary>Calculates total footprint area including circulation clearance margin.</summary>
        public float TotalRequiredAreaSqM
        {
            get
            {
                float w = m_Footprint.x + m_ClearanceMargin * 2f;
                float d = m_Footprint.y + m_ClearanceMargin * 2f;
                return w * d;
            }
        }

        // ── Mutators (Used by Editor Asset Builders) ────────────

        public void SetIdentity(string id, string displayName, string description)
        {
            m_Id = id;
            m_DisplayName = displayName;
            m_Description = description;
        }

        public void SetClassification(FurnitureCategory category, int seatCount, bool wallAligned)
        {
            m_Category = category;
            m_SeatCount = seatCount;
            m_WallAligned = wallAligned;
        }

        public void SetDimensions(Vector3 realWorldSize, Vector2 footprint, float clearanceMargin)
        {
            m_RealWorldSize = realWorldSize;
            m_Footprint = footprint;
            m_ClearanceMargin = clearanceMargin;
        }

        public void SetAssets(GameObject prefab, Sprite thumbnail)
        {
            m_Prefab = prefab;
            m_Thumbnail = thumbnail;
        }

        public void SetManipulationConstraints(bool uniformScaleOnly, float minScale, float maxScale, float snapRotation)
        {
            m_AllowUniformScaleOnly = uniformScaleOnly;
            m_MinScale = minScale;
            m_MaxScale = maxScale;
            m_SnapRotationDegrees = snapRotation;
        }

        // ── Backwards Compatibility ────────────────────────────
        public string itemName => m_DisplayName;
        public GameObject prefab => m_Prefab;
        public Sprite thumbnail => m_Thumbnail;
        public Vector3 defaultScale => Vector3.one;
    }
}