using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARSpace.AR
{
    /// <summary>
    /// Manages the AR session lifecycle: checks AR availability and support at startup,
    /// requests camera permission, handles session state changes, and exposes
    /// clean SessionReady / SessionUnavailable signals through <see cref="Core.GameEvents"/>.
    ///
    /// If the device does not support ARCore, shows a blocking message rather than
    /// crashing or showing a black screen.
    ///
    /// API verified against AR Foundation 6.5:
    ///   - ARSession.state (static, ARSessionState enum)
    ///   - ARSession.stateChanged (static event Action&lt;ARSessionStateChangedEventArgs&gt;)
    ///   - ARSession.CheckAvailability() returns IEnumerator (coroutine)
    ///   - ARSessionState enum: None, Unsupported, CheckingAvailability, NeedsInstall,
    ///     Installing, Ready, SessionInitializing, SessionTracking
    /// </summary>
    public class ARSessionController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] ARSession m_ARSession;

        /// <summary>Whether the session is fully ready and tracking.</summary>
        public bool IsSessionReady { get; private set; }

        /// <summary>Current AR session state (mirrors ARSession.state for easy access).</summary>
        public ARSessionState SessionState => ARSession.state;

        bool m_HasRaisedReady;
        bool m_HasRaisedUnavailable;

        void OnEnable()
        {
            ARSession.stateChanged += OnSessionStateChanged;
        }

        void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;
        }

        IEnumerator Start()
        {
            // Ensure the session component is assigned
            if (m_ARSession == null)
            {
                m_ARSession = FindAnyObjectByType<ARSession>();
                if (m_ARSession == null)
                {
                    RaiseUnavailable("ARSession component not found in scene.");
                    yield break;
                }
            }

            // Check AR availability
            Debug.Log("[ARSessionController] Checking AR availability...");
            yield return ARSession.CheckAvailability();

            if (ARSession.state == ARSessionState.Unsupported)
            {
                RaiseUnavailable("AR is not supported on this device. ARCore is required.");
                yield break;
            }

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                Debug.Log("[ARSessionController] AR software needs install, attempting...");
                yield return ARSession.Install();

                if (ARSession.state == ARSessionState.NeedsInstall)
                {
                    RaiseUnavailable("AR software installation failed. Please install Google Play Services for AR.");
                    yield break;
                }

                if (ARSession.state == ARSessionState.Unsupported)
                {
                    RaiseUnavailable("AR is not supported on this device after installation attempt.");
                    yield break;
                }
            }

            // At this point AR is available — the session will begin initializing.
            // Camera permission is requested by Unity's ARCameraManager automatically
            // when the session starts. If denied, we'll catch it via state changes.
            Debug.Log($"[ARSessionController] AR available. Current state: {ARSession.state}");
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            Debug.Log($"[ARSessionController] Session state changed: {args.state}");

            switch (args.state)
            {
                case ARSessionState.SessionTracking:
                    if (!m_HasRaisedReady)
                    {
                        IsSessionReady = true;
                        m_HasRaisedReady = true;
                        Core.GameEvents.RaiseSessionReady();
                        Debug.Log("[ARSessionController] ✓ Session is tracking. SessionReady raised.");
                    }
                    else if (!IsSessionReady)
                    {
                        // Recovered from a non-tracking state
                        IsSessionReady = true;
                    }
                    break;

                case ARSessionState.SessionInitializing:
                    // Normal startup phase — wait for tracking
                    break;

                case ARSessionState.Ready:
                    // AR subsystem is ready but session hasn't started tracking yet
                    break;

                case ARSessionState.Unsupported:
                    RaiseUnavailable("AR is not supported on this device.");
                    break;

                case ARSessionState.NeedsInstall:
                    RaiseUnavailable("AR software needs to be installed. Please install Google Play Services for AR.");
                    break;

                case ARSessionState.None:
                    // Session reset or not yet initialised
                    IsSessionReady = false;
                    break;

                case ARSessionState.CheckingAvailability:
                case ARSessionState.Installing:
                    // Transient states — do nothing
                    break;
            }
        }

        void RaiseUnavailable(string reason)
        {
            if (m_HasRaisedUnavailable)
                return;

            m_HasRaisedUnavailable = true;
            IsSessionReady = false;
            Debug.LogError($"[ARSessionController] ✗ {reason}");
            Core.GameEvents.RaiseSessionUnavailable(reason);
        }

        /// <summary>
        /// Reset the session (e.g. after loading a layout and needing fresh tracking).
        /// </summary>
        public void ResetSession()
        {
            if (m_ARSession != null)
            {
                m_ARSession.Reset();
                m_HasRaisedReady = false;
                IsSessionReady = false;
                Debug.Log("[ARSessionController] Session reset requested.");
            }
        }
    }
}
