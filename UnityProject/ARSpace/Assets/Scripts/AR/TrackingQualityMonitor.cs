using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ARSpace.AR
{
    /// <summary>
    /// Monitors AR tracking state and raises user-facing guidance messages.
    /// Drives a persistent status chip in the UI via <see cref="Core.GameEvents.TrackingGuidanceChanged"/>.
    ///
    /// AR Foundation 6.5 API verified:
    ///   - ARSession.state (static property)
    ///   - ARSession.stateChanged (static event)
    ///   - ARSession.notTrackingReason (static property, NotTrackingReason enum):
    ///     None, Initializing, Relocalizing, InsufficientLight, InsufficientFeatures,
    ///     ExcessiveMotion, Unsupported, CameraUnavailable
    /// </summary>
    public class TrackingQualityMonitor : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Seconds of continuous non-tracking before raising TrackingLost.")]
        [SerializeField] float m_TrackingLostThreshold = 2.0f;

        /// <summary>Current user-facing guidance string. Empty when tracking is good.</summary>
        public string CurrentGuidance { get; private set; } = string.Empty;

        /// <summary>Whether tracking is currently in a good state.</summary>
        public bool IsTrackingGood { get; private set; }

        float m_NotTrackingTimer;
        bool m_HasRaisedTrackingLost;
        NotTrackingReason m_LastReason = NotTrackingReason.None;

        void OnEnable()
        {
            ARSession.stateChanged += OnSessionStateChanged;
        }

        void OnDisable()
        {
            ARSession.stateChanged -= OnSessionStateChanged;
        }

        void Update()
        {
            // Only monitor once the session has been initialised
            if (ARSession.state < ARSessionState.SessionInitializing)
                return;

            var reason = ARSession.notTrackingReason;

            if (ARSession.state == ARSessionState.SessionTracking)
            {
                // Tracking is good
                if (!IsTrackingGood)
                {
                    IsTrackingGood = true;
                    m_NotTrackingTimer = 0f;

                    if (m_HasRaisedTrackingLost)
                    {
                        m_HasRaisedTrackingLost = false;
                        Core.GameEvents.RaiseTrackingRecovered();
                    }
                }

                SetGuidance(string.Empty);
            }
            else
            {
                // Not tracking
                IsTrackingGood = false;

                // Generate guidance based on reason
                if (reason != m_LastReason)
                {
                    string guidance = GetGuidanceForReason(reason);
                    SetGuidance(guidance);
                    m_LastReason = reason;
                }

                // Accumulate non-tracking time
                m_NotTrackingTimer += Time.deltaTime;

                if (m_NotTrackingTimer >= m_TrackingLostThreshold && !m_HasRaisedTrackingLost)
                {
                    m_HasRaisedTrackingLost = true;
                    Core.GameEvents.RaiseTrackingLost();
                }
            }
        }

        void OnSessionStateChanged(ARSessionStateChangedEventArgs args)
        {
            if (args.state == ARSessionState.SessionTracking)
            {
                m_NotTrackingTimer = 0f;
                m_LastReason = NotTrackingReason.None;
            }
        }

        void SetGuidance(string guidance)
        {
            if (guidance == CurrentGuidance)
                return;

            CurrentGuidance = guidance;
            Core.GameEvents.RaiseTrackingGuidanceChanged(guidance);
        }

        static string GetGuidanceForReason(NotTrackingReason reason)
        {
            switch (reason)
            {
                case NotTrackingReason.None:
                    return string.Empty;
                case NotTrackingReason.Initializing:
                    return "Starting AR…";
                case NotTrackingReason.Relocalizing:
                    return "Relocalising — move your phone slowly";
                case NotTrackingReason.InsufficientLight:
                    return "Too dark — move to a brighter area";
                case NotTrackingReason.InsufficientFeatures:
                    return "Point at a textured surface";
                case NotTrackingReason.ExcessiveMotion:
                    return "Moving too fast — slow down";
                case NotTrackingReason.Unsupported:
                    return "AR tracking not supported";
                case NotTrackingReason.CameraUnavailable:
                    return "Camera unavailable — check permissions";
                default:
                    return "Tracking issue — move slowly and look at the floor";
            }
        }
    }
}
