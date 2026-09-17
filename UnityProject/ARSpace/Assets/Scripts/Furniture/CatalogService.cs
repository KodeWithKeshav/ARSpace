using UnityEngine;
using ARSpace.Core;

namespace ARSpace.Furniture
{
    /// <summary>
    /// Service that manages catalogue access and the currently selected furniture item for placement.
    /// Decoupled from UI via <see cref="GameEvents.FurnitureSelected"/> and <see cref="GameEvents.PlacementCancelled"/>.
    /// </summary>
    public class CatalogService : MonoBehaviour
    {
        [Header("Database Reference")]
        [SerializeField]
        FurnitureDatabase m_Database;

        FurnitureItem m_SelectedItem;

        public FurnitureDatabase Database => m_Database;
        public FurnitureItem SelectedItem => m_SelectedItem;
        public bool HasSelection => m_SelectedItem != null;

        void Awake()
        {
            ServiceLocator.Register(this);

            if (m_Database == null)
            {
                m_Database = Resources.Load<FurnitureDatabase>("FurnitureDatabase");
            }
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<CatalogService>();
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
        }

        public void SetDatabase(FurnitureDatabase database)
        {
            m_Database = database;
        }

        public void SelectItem(string itemId)
        {
            if (m_Database == null)
            {
                Debug.LogWarning("[CatalogService] Cannot select item: FurnitureDatabase is null.");
                return;
            }

            m_SelectedItem = m_Database.GetById(itemId);
            if (m_SelectedItem != null)
            {
                var app = ServiceLocator.Get<ARSpaceApp>();
                if (app != null)
                {
                    app.RequestStateChange(AppState.Placement);
                }
            }
            else
            {
                Debug.LogWarning($"[CatalogService] Item '{itemId}' not found in database.");
            }
        }

        public void ClearSelection()
        {
            m_SelectedItem = null;
            var app = ServiceLocator.Get<ARSpaceApp>();
            if (app != null && app.CurrentState == AppState.Placement)
            {
                app.RequestStateChange(AppState.Browsing);
            }
        }

        void OnFurnitureSelected(string itemId)
        {
            SelectItem(itemId);
        }

        void OnPlacementCancelled()
        {
            ClearSelection();
        }

        void OnObjectPlaced(GameObject placedObject)
        {
            // After placement, remain in Placement state or switch to Browsing based on app flow
            // Placed object keeps selection until user explicitly cancels or selects another
        }
    }
}
