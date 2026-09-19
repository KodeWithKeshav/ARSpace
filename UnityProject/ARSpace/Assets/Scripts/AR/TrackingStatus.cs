using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARSpace.AR
{
    /// <summary>
    /// Whether ARCore's camera pose can currently be trusted. While it can't (tracking lost or relocalising)
    /// the virtual camera stops following the phone, so anything drawn in the world appears glued to the screen
    /// and then jumps once tracking recovers. The app hides placed objects and blocks placing during those moments.
    /// </summary>
    public static class TrackingStatus
    {
        public static bool IsUsable
        {
            get
            {
#if UNITY_EDITOR
                return true;
#else
                if (ARSession.state != ARSessionState.SessionTracking)
                    return false;

                switch (ARSession.notTrackingReason)
                {
                    case NotTrackingReason.Initializing:
                    case NotTrackingReason.Relocalizing:
                    case NotTrackingReason.CameraUnavailable:
                    case NotTrackingReason.Unsupported:
                        return false;
                    default:
                        return true;
                }
#endif
            }
        }
    }
}
