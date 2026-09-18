using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
using ARSpace.Core;

namespace ARSpace.UI
{
    /// <summary>
    /// Controls Presentation Mode for executive walkthroughs:
    /// - Hides all chrome, UI buttons, drawers, and floor grid visuals for full visual immersion.
    /// - Provides a subtle floating exit button and double-tap gesture to restore normal mode.
    /// - Coordinates state transitions with <see cref="ARSpaceApp"/>.
    /// </summary>
    public class PresentationModeController : MonoBehaviour
    {
        [Header("UI Roots to Hide During Presentation")]
        [SerializeField]
        GameObject[] m_UIElementsToHide;

        [Header("Presentation Mode Controls")]
        [SerializeField]
        Button m_EnterPresentationButton;

        [SerializeField]
        Button m_ExitPresentationButton;

        [SerializeField]
        GameObject m_ExitButtonRoot;

        bool m_IsPresenting = false;
        float m_LastTapTime = 0f;
        const float DoubleTapThreshold = 0.35f;

        public bool IsPresenting => m_IsPresenting;

        void Awake()
        {
            ServiceLocator.Register(this);

            if (m_EnterPresentationButton != null)
                m_EnterPresentationButton.onClick.AddListener(EnterPresentationMode);

            if (m_ExitPresentationButton != null)
                m_ExitPresentationButton.onClick.AddListener(ExitPresentationMode);

            if (m_ExitButtonRoot != null)
                m_ExitButtonRoot.SetActive(false);
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<PresentationModeController>();
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        void Update()
        {
            if (m_IsPresenting)
            {
                DetectDoubleTapExit();
            }
        }

        void DetectDoubleTapExit()
        {
            if (Touch.activeTouches.Count == 1)
            {
                Touch t = Touch.activeTouches[0];
                if (t.phase == TouchPhase.Began)
                {
                    float currentTime = Time.time;
                    if (currentTime - m_LastTapTime <= DoubleTapThreshold)
                    {
                        ExitPresentationMode();
                        m_LastTapTime = 0f;
                        return;
                    }
                    m_LastTapTime = currentTime;
                }
            }
        }

        /// <summary>
        /// Enters presentation mode, hiding UI elements and plane grids.
        /// </summary>
        public void EnterPresentationMode()
        {
            if (m_IsPresenting) return;
            m_IsPresenting = true;

            // Hide standard UI chrome
            if (m_UIElementsToHide != null)
            {
                for (int i = 0; i < m_UIElementsToHide.Length; i++)
                {
                    if (m_UIElementsToHide[i] != null)
                    {
                        m_UIElementsToHide[i].SetActive(false);
                    }
                }
            }

            // Show subtle exit button
            if (m_ExitButtonRoot != null)
            {
                m_ExitButtonRoot.SetActive(true);
            }

            // Hide floor grids
            GameEvents.RaisePlaneVisualsToggled(false);

            // Notify app state & subscribers
            var app = ServiceLocator.Get<ARSpaceApp>();
            if (app != null)
            {
                app.RequestStateChange(AppState.Presenting);
            }

            GameEvents.RaisePresentationModeToggled(true);
            GameEvents.RaiseToastRequested("Presentation Mode: Full immersion active.");
            Debug.Log("[PresentationModeController] Entered Presentation Mode.");
        }

        /// <summary>
        /// Exits presentation mode, restoring UI chrome and floor grids.
        /// </summary>
        public void ExitPresentationMode()
        {
            if (!m_IsPresenting) return;
            m_IsPresenting = false;

            // Restore standard UI chrome
            if (m_UIElementsToHide != null)
            {
                for (int i = 0; i < m_UIElementsToHide.Length; i++)
                {
                    if (m_UIElementsToHide[i] != null)
                    {
                        m_UIElementsToHide[i].SetActive(true);
                    }
                }
            }

            // Hide exit button
            if (m_ExitButtonRoot != null)
            {
                m_ExitButtonRoot.SetActive(false);
            }

            // Restore floor grids
            GameEvents.RaisePlaneVisualsToggled(true);

            // Restore app state
            var app = ServiceLocator.Get<ARSpaceApp>();
            if (app != null)
            {
                app.ReturnToPreviousState();
            }

            GameEvents.RaisePresentationModeToggled(false);
            GameEvents.RaiseToastRequested("Exited Presentation Mode.");
            Debug.Log("[PresentationModeController] Exited Presentation Mode.");
        }

        public void Toggle()
        {
            if (m_IsPresenting) ExitPresentationMode();
            else EnterPresentationMode();
        }
    }
}
