using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;
using ARSpace.Core;

namespace ARSpace.AR
{
    /// <summary>
    /// Binds AR Foundation real-time camera light estimation data to the scene's
    /// primary directional light and ambient environment settings.
    ///
    /// Smoothly filters incoming brightness, color temperature, and spherical harmonics
    /// to prevent visual popping or jitter as the user moves between lighting zones.
    /// </summary>
    [DisallowMultipleComponent]
    public class LightEstimationBinder : MonoBehaviour
    {
        [Header("AR References")]
        [SerializeField]
        ARCameraManager m_CameraManager;

        [Header("Lighting Targets")]
        [SerializeField]
        Light m_DirectionalLight;

        [Header("Filtering & Ranges")]
        [Tooltip("Lerp interpolation speed for light transitions.")]
        [SerializeField]
        float m_SmoothingSpeed = 3.0f;

        [Tooltip("Minimum allowable intensity for the directional light.")]
        [SerializeField]
        float m_MinIntensity = 0.25f;

        [Tooltip("Maximum allowable intensity for the directional light.")]
        [SerializeField]
        float m_MaxIntensity = 1.6f;

        float m_TargetIntensity = 1.0f;
        Color m_TargetColor = Color.white;

        void Awake()
        {
            ServiceLocator.Register(this);

            if (m_CameraManager == null)
            {
                m_CameraManager = FindFirstObjectByType<ARCameraManager>();
            }

            if (m_DirectionalLight == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional)
                    {
                        m_DirectionalLight = lights[i];
                        break;
                    }
                }
            }
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<LightEstimationBinder>();
        }

        void OnEnable()
        {
            if (m_CameraManager != null)
            {
                m_CameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        void OnDisable()
        {
            if (m_CameraManager != null)
            {
                m_CameraManager.frameReceived -= OnCameraFrameReceived;
            }
        }

        void Update()
        {
            if (m_DirectionalLight != null)
            {
                float dt = Time.deltaTime * m_SmoothingSpeed;
                m_DirectionalLight.intensity = Mathf.Lerp(m_DirectionalLight.intensity, m_TargetIntensity, dt);
                m_DirectionalLight.color = Color.Lerp(m_DirectionalLight.color, m_TargetColor, dt);
            }
        }

        void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            var lightData = eventArgs.lightEstimation;

            // 1. Average Brightness
            if (lightData.averageBrightness.HasValue)
            {
                float rawBrightness = lightData.averageBrightness.Value;
                m_TargetIntensity = Mathf.Clamp(rawBrightness, m_MinIntensity, m_MaxIntensity);
            }

            // 2. Average Color Temperature
            if (lightData.averageColorTemperature.HasValue)
            {
                float kelvin = lightData.averageColorTemperature.Value;
                m_TargetColor = Mathf.CorrelatedColorTemperatureToRGB(kelvin);
            }
            else if (lightData.colorCorrection.HasValue)
            {
                m_TargetColor = lightData.colorCorrection.Value;
            }

            // 3. Main Light Direction & Intensity (if supported by device)
            if (lightData.mainLightDirection.HasValue && m_DirectionalLight != null)
            {
                Vector3 dir = lightData.mainLightDirection.Value;
                if (dir != Vector3.zero)
                {
                    Quaternion targetRot = Quaternion.LookRotation(dir);
                    m_DirectionalLight.transform.rotation = Quaternion.Slerp(
                        m_DirectionalLight.transform.rotation,
                        targetRot,
                        Time.deltaTime * m_SmoothingSpeed
                    );
                }
            }

            // 4. Ambient Spherical Harmonics (if supported)
            if (lightData.ambientSphericalHarmonics.HasValue)
            {
                RenderSettings.ambientMode = AmbientMode.Custom;
                RenderSettings.ambientProbe = lightData.ambientSphericalHarmonics.Value;
            }
        }
    }
}
