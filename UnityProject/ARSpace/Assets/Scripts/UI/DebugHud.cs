using UnityEngine;
using TMPro;
using UnityEngine.XR.ARFoundation;
using ARSpace.AR;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// Diagnostic readout (tap the top hint bar five times to show/hide): AR session state and tracking reason,
    /// the virtual camera's world position (it should change as you walk — if it does not, camera tracking is
    /// broken), detected plane count, floor height and anchor count.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI m_Text;

        ARPlaneManager m_PlaneManager;

        void Update()
        {
            if (m_Text == null)
                return;

            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

            Camera cam = Camera.main;
            string camPos = cam != null
                ? $"({cam.transform.position.x:0.00}, {cam.transform.position.y:0.00}, {cam.transform.position.z:0.00})"
                : "n/a";

            string floor = ServiceLocator.TryGet(out ManualFloor mf) && mf.HasFloor ? $"{mf.FloorY:0.00} ({mf.Source})" : "-";
            var anchors = ServiceLocator.TryGet(out AnchorService a) ? a.ActiveAnchorCount : 0;
            int planes = m_PlaneManager != null ? m_PlaneManager.trackables.count : -1;

            m_Text.text = $"{ARSession.state} · {ARSession.notTrackingReason} · usable {TrackingStatus.IsUsable}\n" +
                          $"cam {camPos} · src {ARCameraPoseDriver.Source}\nplanes {planes} · floor {floor} · anchors {anchors}";
        }
    }
}
