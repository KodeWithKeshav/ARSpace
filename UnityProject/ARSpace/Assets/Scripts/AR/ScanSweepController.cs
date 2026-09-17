using UnityEngine;
using ARSpace.Core;

namespace ARSpace.AR
{
    /// <summary>
    /// Controls the expanding scan sweep ring visual during the <see cref="AppState.Scanning"/> state.
    /// Drives global shader variables read by <c>ARSpace/PlaneGrid</c> across all active planes.
    ///
    /// Stops permanently once the first furniture item is placed in the scene.
    /// </summary>
    public class ScanSweepController : MonoBehaviour
    {
        [Header("Sweep Animation")]
        [Tooltip("Speed in metres per second at which the sweep expands.")]
        [SerializeField]
        float m_SweepSpeed = 2.4f;

        [Tooltip("Maximum radius in metres before the sweep resets to 0.")]
        [SerializeField]
        float m_MaxRadius = 6.5f;

        [Tooltip("Width of the expanding pulse ring in metres.")]
        [SerializeField]
        float m_RingWidth = 0.35f;

        [Tooltip("Optional transform to use as sweep center (defaults to Main Camera).")]
        [SerializeField]
        Transform m_CenterOverride;

        // Shader property IDs (global)
        static readonly int s_SweepCenterId = Shader.PropertyToID("_ScanSweepCenter");
        static readonly int s_SweepRadiusId = Shader.PropertyToID("_ScanSweepRadius");
        static readonly int s_SweepWidthId = Shader.PropertyToID("_ScanSweepWidth");
        static readonly int s_SweepActiveId = Shader.PropertyToID("_ScanSweepActive");

        float m_CurrentRadius = 0f;
        float m_ActiveAlpha = 0f;
        bool m_IsScanning = false;
        bool m_HasPlacedFirstObject = false;

        Camera m_CachedMainCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureInstance()
        {
            if (FindFirstObjectByType<ScanSweepController>() == null)
            {
                var go = new GameObject("ScanSweepController");
                go.AddComponent<ScanSweepController>();
                DontDestroyOnLoad(go);
            }
        }

        void Awake()
        {
            ServiceLocator.Register(this);
            m_CachedMainCamera = Camera.main;
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<ScanSweepController>();
            // Reset global shader values when destroyed
            Shader.SetGlobalFloat(s_SweepActiveId, 0f);
        }

        void OnEnable()
        {
            GameEvents.StateChanged += OnStateChanged;
            GameEvents.ObjectPlaced += OnObjectPlaced;

            // Check if App is already in Scanning state
            var app = ServiceLocator.Get<ARSpaceApp>();
            if (app != null && app.CurrentState == AppState.Scanning && !m_HasPlacedFirstObject)
            {
                m_IsScanning = true;
            }
        }

        void OnDisable()
        {
            GameEvents.StateChanged -= OnStateChanged;
            GameEvents.ObjectPlaced -= OnObjectPlaced;
            Shader.SetGlobalFloat(s_SweepActiveId, 0f);
        }

        void Update()
        {
            bool shouldBeActive = m_IsScanning && !m_HasPlacedFirstObject;

            // Smoothly fade the sweep effect in or out
            m_ActiveAlpha = Mathf.MoveTowards(
                m_ActiveAlpha,
                shouldBeActive ? 1f : 0f,
                Time.deltaTime * (shouldBeActive ? 2.5f : 4f)
            );

            if (m_ActiveAlpha > 0.001f)
            {
                // Advance expanding ring
                m_CurrentRadius += m_SweepSpeed * Time.deltaTime;
                if (m_CurrentRadius > m_MaxRadius)
                {
                    m_CurrentRadius = 0f;
                }

                // Locate center position (user's device/camera position)
                Vector3 centerPos = Vector3.zero;
                if (m_CenterOverride != null)
                {
                    centerPos = m_CenterOverride.position;
                }
                else
                {
                    if (m_CachedMainCamera == null)
                        m_CachedMainCamera = Camera.main;

                    if (m_CachedMainCamera != null)
                        centerPos = m_CachedMainCamera.transform.position;
                }

                Shader.SetGlobalVector(s_SweepCenterId, new Vector4(centerPos.x, centerPos.y, centerPos.z, 0));
                Shader.SetGlobalFloat(s_SweepRadiusId, m_CurrentRadius);
                Shader.SetGlobalFloat(s_SweepWidthId, m_RingWidth);
                Shader.SetGlobalFloat(s_SweepActiveId, m_ActiveAlpha);
            }
            else
            {
                Shader.SetGlobalFloat(s_SweepActiveId, 0f);
            }
        }

        void OnStateChanged(AppState from, AppState to)
        {
            m_IsScanning = (to == AppState.Scanning);
            if (m_IsScanning && !m_HasPlacedFirstObject)
            {
                // Reset radius to start fresh outward sweep on entering Scanning
                m_CurrentRadius = 0f;
            }
        }

        void OnObjectPlaced(GameObject placedObject)
        {
            // First placement locks out the scan sweep for the remainder of the session
            if (!m_HasPlacedFirstObject)
            {
                m_HasPlacedFirstObject = true;
                Debug.Log("[ScanSweepController] First object placed. Scan sweep permanently stopped.");
            }
        }

        /// <summary>
        /// Resets the first-placement lockout (e.g. when clearing a layout).
        /// </summary>
        public void ResetPlacementLockout()
        {
            m_HasPlacedFirstObject = false;
        }
    }
}
