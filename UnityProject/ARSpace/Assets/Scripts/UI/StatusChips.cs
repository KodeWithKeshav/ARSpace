using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.AR;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// The row under the hint bar: a live "items · seats · floor area" summary of the layout, a Measure toggle and
    /// an Undo button.
    /// </summary>
    public class StatusChips : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_StatsGroup;
        [SerializeField] TextMeshProUGUI m_StatsText;
        [SerializeField] Button m_MeasureButton;
        [SerializeField] TextMeshProUGUI m_MeasureLabel;
        [SerializeField] Button m_UndoButton;

        float m_NextRefresh;

        void Awake()
        {
            if (m_MeasureButton != null)
                m_MeasureButton.onClick.AddListener(OnMeasureClicked);
            if (m_UndoButton != null)
                m_UndoButton.onClick.AddListener(OnUndoClicked);
            if (m_StatsGroup != null)
                m_StatsGroup.alpha = 0f;
        }

        void OnMeasureClicked()
        {
            if (ServiceLocator.TryGet(out MeasureTool tool))
                tool.Toggle();
        }

        void OnUndoClicked()
        {
            if (ServiceLocator.TryGet(out UndoService undo))
                undo.Undo();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextRefresh)
                return;
            m_NextRefresh = Time.unscaledTime + 0.25f;

            RefreshStats();

            bool measuring = MeasureTool.IsActive;
            if (m_MeasureButton != null && m_MeasureButton.image != null)
                m_MeasureButton.image.color = measuring ? UiStyle.Primary : UiStyle.Secondary;
            if (m_MeasureLabel != null)
                m_MeasureLabel.text = measuring ? "Done" : "Measure";

            if (m_UndoButton != null)
                m_UndoButton.interactable = ServiceLocator.TryGet(out UndoService undo) && undo.CanUndo;
        }

        void RefreshStats()
        {
            if (m_StatsGroup == null || m_StatsText == null)
                return;

            if (!ServiceLocator.TryGet(out PlacedObjectRegistry registry) || registry.Count == 0)
            {
                m_StatsGroup.alpha = 0f;
                return;
            }

            int items = registry.Count;
            int seats = registry.CalculateTotalSeatingCapacity();
            float area = registry.CalculateTotalFootprintArea();

            m_StatsText.text = $"{items} item{(items == 1 ? "" : "s")}  ·  {seats} seat{(seats == 1 ? "" : "s")}  ·  {area:0.0} m²";
            m_StatsGroup.alpha = 1f;
        }
    }
}
