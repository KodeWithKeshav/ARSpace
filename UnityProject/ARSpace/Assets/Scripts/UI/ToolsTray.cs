using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.AR;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// The "Tools" drawer: separate Measure, Area, Compass and Snap tools, kept out of the way until you open it.
    /// Measure and Area are mutually exclusive (both consume floor taps).
    /// </summary>
    public class ToolsTray : MonoBehaviour
    {
        [SerializeField] GameObject m_Tray;
        [SerializeField] Button m_ToggleButton;
        [SerializeField] Button m_MeasureButton;
        [SerializeField] Button m_AreaButton;
        [SerializeField] Button m_CompassButton;
        [SerializeField] Button m_SnapButton;
        [SerializeField] TextMeshProUGUI m_SnapLabel;

        float m_NextRefresh;

        void Awake()
        {
            Hook(m_ToggleButton, () => { if (m_Tray != null) m_Tray.SetActive(!m_Tray.activeSelf); });
            Hook(m_MeasureButton, OnMeasure);
            Hook(m_AreaButton, OnArea);
            Hook(m_CompassButton, () => { if (ServiceLocator.TryGet(out CompassTool c)) c.Toggle(); });
            Hook(m_SnapButton, () =>
            {
                PlacementSnap.Toggle();
                GameEvents.RaiseToastRequested(PlacementSnap.Enabled ? "Snap on: placement moves in 10 cm steps" : "Snap off");
            });

            if (m_Tray != null)
                m_Tray.SetActive(false);
        }

        static void Hook(Button b, UnityEngine.Events.UnityAction a)
        {
            if (b != null)
                b.onClick.AddListener(a);
        }

        void OnMeasure()
        {
            if (ServiceLocator.TryGet(out AreaTool area))
                area.SetActive(false);
            if (ServiceLocator.TryGet(out MeasureTool tool))
                tool.Toggle();
        }

        void OnArea()
        {
            if (ServiceLocator.TryGet(out MeasureTool tool))
                tool.SetActive(false);
            if (ServiceLocator.TryGet(out AreaTool area))
                area.Toggle();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextRefresh)
                return;
            m_NextRefresh = Time.unscaledTime + 0.2f;

            Tint(m_ToggleButton, m_Tray != null && m_Tray.activeSelf);
            Tint(m_MeasureButton, MeasureTool.IsActive);
            Tint(m_AreaButton, AreaTool.IsActive);
            Tint(m_CompassButton, CompassTool.IsActive);
            Tint(m_SnapButton, PlacementSnap.Enabled);

            if (m_SnapLabel != null)
                m_SnapLabel.text = PlacementSnap.Enabled ? "Snap: On" : "Snap: Off";
        }

        static void Tint(Button b, bool on)
        {
            if (b != null && b.image != null)
                b.image.color = on ? UiStyle.Primary : UiStyle.Secondary;
        }
    }
}
