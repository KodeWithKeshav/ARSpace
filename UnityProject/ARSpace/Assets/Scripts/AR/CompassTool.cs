using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using ARSpace.Core;

namespace ARSpace.AR
{
    /// <summary>
    /// On-screen compass. Shows which compass direction the camera faces (useful for orienting a layout — window
    /// side, entrance side, sun). Uses the phone's magnetometer combined with the AR camera's own orientation, so
    /// the reading is steady and unaffected by how the phone is held. Without a magnetometer the dial still works
    /// but is relative to the direction the app started facing, and says so.
    /// </summary>
    public class CompassTool : MonoBehaviour
    {
        [SerializeField] GameObject m_Panel;
        [SerializeField] RectTransform m_Dial;
        [SerializeField] TextMeshProUGUI m_HeadingText;

        public static bool IsActive { get; private set; }

        static readonly string[] s_Names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        MagneticFieldSensor m_Sensor;
        Vector3 m_FieldWorld;
        bool m_HasField;
        float m_NorthYaw;
        float m_ShownHeading;
        bool m_HasHeading;

        void Awake()
        {
            ServiceLocator.Register(this);
            SetPanel(false);
        }

        void OnDestroy()
        {
            IsActive = false;
            ServiceLocator.Unregister<CompassTool>();
        }

        public void Toggle()
        {
            IsActive = !IsActive;
            SetPanel(IsActive);

            if (IsActive)
            {
                m_Sensor = MagneticFieldSensor.current;
                if (m_Sensor != null && !m_Sensor.enabled)
                    InputSystem.EnableDevice(m_Sensor);

                m_HasField = false;
                m_HasHeading = false;
            }
        }

        void SetPanel(bool visible)
        {
            if (m_Panel != null)
                m_Panel.SetActive(visible);
        }

        void Update()
        {
            if (!IsActive)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            // Direction of magnetic north in AR-world space (heavily smoothed — the raw sensor is noisy).
            if (m_Sensor != null && m_Sensor.enabled)
            {
                Vector3 device = m_Sensor.magneticField.ReadValue();
                if (device.sqrMagnitude > 1e-4f)
                {
                    // Sensor axes: x right, y up the screen, z out of the screen; the camera looks along -z of that.
                    Vector3 world = cam.transform.rotation * new Vector3(device.x, device.y, -device.z);
                    world.y = 0f;
                    if (world.sqrMagnitude > 1e-4f)
                    {
                        world.Normalize();
                        m_FieldWorld = m_HasField ? Vector3.Slerp(m_FieldWorld, world, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2f)) : world;
                        m_HasField = true;
                        m_NorthYaw = Mathf.Atan2(m_FieldWorld.x, m_FieldWorld.z) * Mathf.Rad2Deg;
                    }
                }
            }

            Vector3 fwd = cam.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                return; // looking straight up/down — keep the last heading

            float camYaw = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
            float target = Mathf.Repeat(camYaw - (m_HasField ? m_NorthYaw : 0f), 360f);

            m_ShownHeading = m_HasHeading
                ? Mathf.MoveTowardsAngle(m_ShownHeading, target, 240f * Time.unscaledDeltaTime)
                : target;
            m_HasHeading = true;

            if (m_Dial != null)
                m_Dial.localRotation = Quaternion.Euler(0f, 0f, m_ShownHeading);

            if (m_HeadingText != null)
            {
                float h = Mathf.Repeat(m_ShownHeading, 360f);
                string name = s_Names[Mathf.RoundToInt(h / 45f) % 8];
                m_HeadingText.text = m_HasField ? $"{name}  {h:0}°" : $"{name}  {h:0}°  (relative)";
            }
        }
    }
}
