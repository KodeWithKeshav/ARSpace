using UnityEngine;
using UnityEngine.UI;
using ARSpace.AR;
using ARSpace.Core;

namespace ARSpace.UI
{
    /// <summary>
    /// Wires the "Place" and "Cancel" buttons to the placement pipeline.
    /// Kept as a thin UI adapter — all placement logic lives in <see cref="ARPlacementManager"/>.
    /// </summary>
    public class PlacementControls : MonoBehaviour
    {
        [SerializeField] Button m_PlaceButton;
        [SerializeField] Button m_CancelButton;

        void Awake()
        {
            if (m_PlaceButton != null)
                m_PlaceButton.onClick.AddListener(OnPlaceClicked);

            if (m_CancelButton != null)
                m_CancelButton.onClick.AddListener(OnCancelClicked);
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
