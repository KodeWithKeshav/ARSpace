using UnityEngine;
using TMPro;
using ARSpace.AR;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.UI
{
    /// <summary>
    /// "Floor height" fine-tuning row shown while positioning furniture. If the marker looks like it hovers above
    /// or sinks below the real floor when you move around, press-and-hold Lower / Raise until it sits on the floor.
    /// </summary>
    public class FloorAdjustControls : MonoBehaviour
    {
        [SerializeField] GameObject m_Row;
        [SerializeField] HoldButton m_LowerButton;
        [SerializeField] HoldButton m_RaiseButton;
        [SerializeField] TextMeshProUGUI m_Status;

        const float TapStep = 0.01f;       // metres per tap
        const float HoldSpeed = 0.10f;     // metres per second while held

        bool m_ObjectSelected;
        ManualFloor m_Floor;

        void Awake()
        {
            SetVisible(false);
            if (m_LowerButton != null) m_LowerButton.Pressed += () => Nudge(-TapStep);
            if (m_RaiseButton != null) m_RaiseButton.Pressed += () => Nudge(TapStep);
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

        void OnFurnitureSelected(string id) { m_ObjectSelected = false; SetVisible(true); }
        void OnPlacementCancelled() => SetVisible(false);

        void OnObjectSelectionChanged(GameObject selected)
        {
            m_ObjectSelected = selected != null;
            var catalog = ServiceLocator.Get<CatalogService>();
            SetVisible(!m_ObjectSelected && catalog != null && catalog.HasSelection);
        }

        void SetVisible(bool visible)
        {
            if (m_Row != null)
                m_Row.SetActive(visible);
        }

        void Update()
        {
            if (m_Row == null || !m_Row.activeSelf)
                return;

            if (m_Floor == null)
                m_Floor = ServiceLocator.Get<ManualFloor>();
            if (m_Floor == null)
                return;

            if (m_LowerButton != null && m_LowerButton.IsHeld) m_Floor.Adjust(-HoldSpeed * Time.unscaledDeltaTime);
            if (m_RaiseButton != null && m_RaiseButton.IsHeld) m_Floor.Adjust(HoldSpeed * Time.unscaledDeltaTime);

            if (m_Status != null)
            {
                string source = m_Floor.Source == ManualFloor.FloorSource.ARCore ? "detected"
                              : m_Floor.Source == ManualFloor.FloorSource.Manual ? "adjusted" : "estimated";
                m_Status.text = $"Floor {source}  ·  {m_Floor.CameraHeightAboveFloor:0.00} m below phone";
            }
        }

        void Nudge(float delta)
        {
            if (m_Floor == null)
                m_Floor = ServiceLocator.Get<ManualFloor>();
            if (m_Floor != null)
                m_Floor.Adjust(delta);
        }
    }
}
