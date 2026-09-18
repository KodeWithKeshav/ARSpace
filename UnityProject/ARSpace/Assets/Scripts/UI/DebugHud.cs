using UnityEngine;
using TMPro;
using UnityEngine.XR.ARFoundation;
using ARSpace.AR;
using ARSpace.Core;

namespace ARSpace.UI
{
    /// <summary>
    /// Small always-on diagnostic readout (top-right corner) showing AR session state,
    /// tracking guidance, and the raw detected plane count straight from ARPlaneManager.
    /// This is a troubleshooting aid, not part of the polished UI — it exists so plane
    /// detection failures can be diagnosed from a screenshot instead of guesswork.
    /// </summary>
    public class DebugHud : MonoBehaviour
    {
        [SerializeField] TextMeshProUGUI m_Text;

        ARPlaneManager m_PlaneManager;

        void Start()
        {
            m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();
        }

        void Update()
        {
            if (m_Text == null)
                return;

            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

            int planeCount = m_PlaneManager != null ? m_PlaneManager.trackables.count : -1;
            string reason = ARSession.notTrackingReason.ToString();

            m_Text.text = $"AR: {ARSession.state}   Planes: {planeCount}" +
                          (reason != "None" ? $"   ({reason})" : string.Empty);
        }
    }
}
