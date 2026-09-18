using System.Collections;
using UnityEngine;
using TMPro;
using ARSpace.Core;

namespace ARSpace.UI
{
    /// <summary>
    /// Presents short-lived toast messages raised via <see cref="GameEvents.ToastRequested"/>
    /// (e.g. "Aim at a detected floor surface to place") and the persistent tracking
    /// guidance banner driven by <see cref="GameEvents.TrackingGuidanceChanged"/>.
    /// </summary>
    public class ToastPresenter : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_ToastGroup;
        [SerializeField] TextMeshProUGUI m_ToastText;
        [SerializeField] TextMeshProUGUI m_GuidanceText;
        [SerializeField] float m_DisplaySeconds = 2.2f;

        Coroutine m_ActiveRoutine;

        void Awake()
        {
            if (m_ToastGroup != null)
                m_ToastGroup.alpha = 0f;

            if (m_GuidanceText != null)
                m_GuidanceText.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            GameEvents.ToastRequested += OnToastRequested;
            GameEvents.TrackingGuidanceChanged += OnGuidanceChanged;
        }

        void OnDisable()
        {
            GameEvents.ToastRequested -= OnToastRequested;
            GameEvents.TrackingGuidanceChanged -= OnGuidanceChanged;
        }

        void OnToastRequested(string message)
        {
            if (m_ToastGroup == null || m_ToastText == null)
                return;

            if (m_ActiveRoutine != null)
                StopCoroutine(m_ActiveRoutine);

            m_ActiveRoutine = StartCoroutine(ShowToastRoutine(message));
        }

        IEnumerator ShowToastRoutine(string message)
        {
            m_ToastText.text = message;
            m_ToastGroup.alpha = 1f;
            yield return new WaitForSeconds(m_DisplaySeconds);
            m_ToastGroup.alpha = 0f;
            m_ActiveRoutine = null;
        }

        void OnGuidanceChanged(string guidance)
        {
            if (m_GuidanceText == null)
                return;

            bool show = !string.IsNullOrEmpty(guidance);
            m_GuidanceText.gameObject.SetActive(show);
            if (show)
                m_GuidanceText.text = guidance;
        }
    }
}
