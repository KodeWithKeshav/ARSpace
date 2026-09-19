using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.AR;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.UI
{
    /// <summary>
    /// The floating "Cancel / Place here" bar. Only visible while a catalogue item is selected and no placed
    /// object is being edited, so the screen stays clean the rest of the time.
    /// All placement logic lives in <see cref="ARPlacementManager"/>; this is a thin UI adapter.
    /// </summary>
    public class PlacementControls : MonoBehaviour
    {
        [SerializeField] GameObject m_BarRoot;
        [SerializeField] Button m_PlaceButton;
        [SerializeField] Button m_CancelButton;
        [SerializeField] TextMeshProUGUI m_PlaceLabel;

        bool m_ObjectSelected;

        void Awake()
        {
            if (m_PlaceButton != null)
                m_PlaceButton.onClick.AddListener(OnPlaceClicked);

            if (m_CancelButton != null)
                m_CancelButton.onClick.AddListener(OnCancelClicked);

            SetBarVisible(false);
        }

        void OnEnable()
        {
            GameEvents.FurnitureSelected += OnFurnitureSelected;
            GameEvents.PlacementCancelled += OnPlacementCancelled;
            GameEvents.ObjectSelectionChanged += OnObjectSelectionChanged;
        }

        void OnDisable()
        {
            GameEvents.FurnitureSelected -= OnFurnitureSelected;
            GameEvents.PlacementCancelled -= OnPlacementCancelled;
            GameEvents.ObjectSelectionChanged -= OnObjectSelectionChanged;
        }

        void OnFurnitureSelected(string itemId)
        {
            var catalog = ServiceLocator.Get<CatalogService>();
            var item = catalog != null && catalog.Database != null ? catalog.Database.GetById(itemId) : null;
            if (m_PlaceLabel != null)
                m_PlaceLabel.text = item != null ? $"Place {item.DisplayName}" : "Place here";

            m_ObjectSelected = false;
            SetBarVisible(true);
        }

        void OnPlacementCancelled()
        {
            SetBarVisible(false);
        }

        void OnObjectSelectionChanged(GameObject selected)
        {
            m_ObjectSelected = selected != null;
            var catalog = ServiceLocator.Get<CatalogService>();
            SetBarVisible(!m_ObjectSelected && catalog != null && catalog.HasSelection);
        }

        void SetBarVisible(bool visible)
        {
            if (m_BarRoot != null)
                m_BarRoot.SetActive(visible);
        }

        void OnPlaceClicked()
        {
            var manager = ServiceLocator.Get<ARPlacementManager>();
            if (manager != null)
                manager.PlaceCurrentItem();
        }

        void OnCancelClicked()
        {
            GameEvents.RaisePlacementCancelled();
        }
    }
}
