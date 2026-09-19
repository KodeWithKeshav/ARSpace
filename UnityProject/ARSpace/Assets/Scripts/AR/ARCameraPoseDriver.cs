using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace ARSpace.AR
{
    /// <summary>
    /// Drives the AR camera from exactly one pose source: the phone's own AR device pose.
    ///
    /// The template's TrackedPoseDriver binds position/rotation to BOTH an XR headset and the handheld AR
    /// device. Where both exist the Input System can switch between them from frame to frame, which makes the
    /// camera (and so every virtual object) vibrate and drift against the real world. This driver reads only
    /// the handheld AR device (falling back to the XR centre-eye device) and turns the template driver off once
    /// a source is confirmed. If neither source is found the template driver is left untouched.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class ARCameraPoseDriver : MonoBehaviour
    {
        /// <summary>Which pose source is currently driving the camera (shown in the debug readout).</summary>
        public static string Source { get; private set; } = "TrackedPoseDriver (template)";

        TrackedPoseDriver m_TemplateDriver;
        InputAction m_Position;
        InputAction m_Rotation;
        bool m_CustomActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Camera cam = Camera.main;
            if (cam != null && cam.GetComponent<ARCameraPoseDriver>() == null)
                cam.gameObject.AddComponent<ARCameraPoseDriver>();
        }

        void Awake()
        {
            m_TemplateDriver = GetComponent<TrackedPoseDriver>();

            m_Position = new InputAction("ARPosition", InputActionType.Value,
                "<HandheldARInputDevice>/devicePosition", expectedControlType: "Vector3");
            m_Rotation = new InputAction("ARRotation", InputActionType.Value,
                "<HandheldARInputDevice>/deviceRotation", expectedControlType: "Quaternion");
        }

        void OnEnable()
        {
            m_Position.Enable();
            m_Rotation.Enable();
            Application.onBeforeRender += ApplyPose;
        }

        void OnDisable()
        {
            Application.onBeforeRender -= ApplyPose;
            m_Position.Disable();
            m_Rotation.Disable();

            if (m_CustomActive && m_TemplateDriver != null)
                m_TemplateDriver.enabled = true;
            m_CustomActive = false;
        }

        void OnDestroy()
        {
            m_Position?.Dispose();
            m_Rotation?.Dispose();
        }

        void Update() => ApplyPose();

        void ApplyPose()
        {
            if (TryReadHandheldDevice(out Vector3 position, out Quaternion rotation))
            {
                UseCustom("HandheldARInputDevice");
                transform.SetLocalPositionAndRotation(position, rotation);
                return;
            }

            if (TryReadXRCenterEye(out position, out rotation))
            {
                UseCustom("XR centre-eye");
                transform.SetLocalPositionAndRotation(position, rotation);
            }
        }

        bool TryReadHandheldDevice(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;

            if (m_Position.controls.Count == 0 || m_Rotation.controls.Count == 0)
                return false;

            rotation = m_Rotation.ReadValue<Quaternion>();
            if (rotation.x == 0f && rotation.y == 0f && rotation.z == 0f && rotation.w == 0f)
                return false;

            position = m_Position.ReadValue<Vector3>();
            return true;
        }

        static bool TryReadXRCenterEye(out Vector3 position, out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;

            UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!device.isValid)
                return false;

            return device.TryGetFeatureValue(CommonUsages.centerEyePosition, out position)
                   && device.TryGetFeatureValue(CommonUsages.centerEyeRotation, out rotation);
        }

        void UseCustom(string source)
        {
            if (m_CustomActive && Source == source)
                return;

            m_CustomActive = true;
            Source = source;
            if (m_TemplateDriver != null)
                m_TemplateDriver.enabled = false;
        }
    }
}
