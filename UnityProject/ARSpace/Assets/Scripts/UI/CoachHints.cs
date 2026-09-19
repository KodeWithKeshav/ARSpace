using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using TMPro;
using ARSpace.Core;
using ARSpace.Furniture;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// A single top "hint pill" that always tells the user what to do next
    /// (scan the floor → pick an item → aim → place → adjust), and surfaces AR tracking problems.
    /// Tapping the pill five times quickly toggles the diagnostic overlay.
    /// </summary>
    public class CoachHints : MonoBehaviour
    {
        [SerializeField] CanvasGroup m_Group;
        [SerializeField] TextMeshProUGUI m_Text;
        [SerializeField] Button m_SecretToggle;
        [SerializeField] GameObject m_DebugHudRoot;

        string m_TrackingGuidance = string.Empty;
        string m_Current;
        float m_NextEvaluate;
        int m_TapCount;
        float m_FirstTapTime;

        ARPlaneManager m_PlaneManager;

        void Awake()
        {
            if (m_SecretToggle != null)
                m_SecretToggle.onClick.AddListener(OnSecretTap);

            if (m_DebugHudRoot != null)
                m_DebugHudRoot.SetActive(false);

            if (m_Group != null)
                m_Group.alpha = 0f;
        }

        void OnEnable() => GameEvents.TrackingGuidanceChanged += OnGuidanceChanged;
        void OnDisable() => GameEvents.TrackingGuidanceChanged -= OnGuidanceChanged;

        void OnGuidanceChanged(string guidance)
        {
            m_TrackingGuidance = guidance ?? string.Empty;
            m_NextEvaluate = 0f;
        }

        void Update()
        {
            if (Time.unscaledTime >= m_NextEvaluate)
            {
                m_NextEvaluate = Time.unscaledTime + 0.2f;
                Evaluate();
            }

            if (m_Group != null)
            {
                float target = string.IsNullOrEmpty(m_Current) ? 0f : 1f;
                m_Group.alpha = Mathf.MoveTowards(m_Group.alpha, target, Time.unscaledDeltaTime * 5f);
            }
        }

        void Evaluate()
        {
            string hint = ComputeHint();
            if (hint == m_Current)
                return;

            m_Current = hint;
            if (!string.IsNullOrEmpty(hint) && m_Text != null)
                m_Text.text = hint;
        }

        string ComputeHint()
        {
            if (!string.IsNullOrEmpty(m_TrackingGuidance))
                return m_TrackingGuidance;

            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();

            var registry = ServiceLocator.Get<PlacedObjectRegistry>();
            int placed = registry != null ? registry.Count : 0;
            int planes = m_PlaneManager != null ? m_PlaneManager.trackables.count : 0;

            var app = ServiceLocator.Get<ARSpaceApp>();
            AppState state = app != null ? app.CurrentState : AppState.Initializing;

            if (state == AppState.ObjectSelected)
                return "Drag to move  ·  twist to rotate  ·  pinch to resize";

            var catalog = ServiceLocator.Get<CatalogService>();
            FurnitureItem selected = catalog != null ? catalog.SelectedItem : null;

            if (selected != null && state == AppState.PlacementPending)
            {
                var reticle = ServiceLocator.Get<PlacementReticle>();
                if (reticle != null && reticle.IsPinned)
                    return $"Tap Place to put the {selected.DisplayName} on the marker  ·  tap elsewhere to move it";
                return reticle != null && reticle.HasHit
                    ? $"Tap the floor to choose a spot, or press Place at the circle"
                    : $"Tap the floor grid to choose where the {selected.DisplayName} goes";
            }

            if (planes == 0 && placed == 0)
                return "Move your phone slowly to scan the floor";

            if (placed == 0)
                return "Floor found — choose an item below";

            return string.Empty;
        }

        void OnSecretTap()
        {
            if (Time.unscaledTime - m_FirstTapTime > 3f)
            {
                m_FirstTapTime = Time.unscaledTime;
                m_TapCount = 0;
            }

            if (++m_TapCount >= 5 && m_DebugHudRoot != null)
            {
                m_TapCount = 0;
                m_DebugHudRoot.SetActive(!m_DebugHudRoot.activeSelf);
            }
        }
    }
}
