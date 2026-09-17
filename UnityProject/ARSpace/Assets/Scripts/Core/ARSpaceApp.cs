using UnityEngine;

namespace ARSpace.Core
{
    /// <summary>
    /// Application root. Owns the <see cref="AppState"/> state machine,
    /// registers services into <see cref="ServiceLocator"/>, and orchestrates
    /// top-level lifecycle (pause/resume, state transitions).
    ///
    /// Attach to a root GameObject named "ARSpaceApp" in the scene.
    /// The scene builder creates and wires this automatically.
    /// </summary>
    public class ARSpaceApp : MonoBehaviour
    {
        [Header("State (read-only at runtime)")]
        [SerializeField] AppState m_CurrentState = AppState.Initializing;

        /// <summary>Current application state. Read-only externally; transition via <see cref="RequestStateChange"/>.</summary>
        public AppState CurrentState => m_CurrentState;

        /// <summary>Previous state before the last transition.</summary>
        public AppState PreviousState { get; private set; } = AppState.Initializing;

        /// <summary>Whether the app was in tracking-lost when it was paused (to restore on resume).</summary>
        bool m_WasTrackingLostBeforePause;

        void Awake()
        {
            // Register self so other services can query state
            ServiceLocator.Register(this);
        }

        void OnEnable()
        {
            GameEvents.SessionReady += OnSessionReady;
            GameEvents.SessionUnavailable += OnSessionUnavailable;
            GameEvents.TrackingLost += OnTrackingLost;
            GameEvents.TrackingRecovered += OnTrackingRecovered;
            GameEvents.FurnitureSelected += OnFurnitureSelected;
            GameEvents.PlacementCancelled += OnPlacementCancelled;
            GameEvents.ObjectPlaced += OnObjectPlaced;
            GameEvents.ObjectSelectionChanged += OnObjectSelectionChanged;
            GameEvents.PresentationModeToggled += OnPresentationModeToggled;
        }

        void OnDisable()
        {
            GameEvents.SessionReady -= OnSessionReady;
            GameEvents.SessionUnavailable -= OnSessionUnavailable;
            GameEvents.TrackingLost -= OnTrackingLost;
            GameEvents.TrackingRecovered -= OnTrackingRecovered;
            GameEvents.FurnitureSelected -= OnFurnitureSelected;
            GameEvents.PlacementCancelled -= OnPlacementCancelled;
            GameEvents.ObjectPlaced -= OnObjectPlaced;
            GameEvents.ObjectSelectionChanged -= OnObjectSelectionChanged;
            GameEvents.PresentationModeToggled -= OnPresentationModeToggled;
        }

        void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // Going to background — record state for clean resume
                m_WasTrackingLostBeforePause = m_CurrentState == AppState.TrackingLost;
            }
            else
            {
                // Returning from background
                // AR session will re-initialise via ARSessionController.
                // If tracking was OK before pause, it will recover; if not, TrackingLost stays.
                // Do NOT force a state change here — let the tracking monitor drive it.
                Debug.Log("[ARSpaceApp] Resumed from pause.");
            }
        }

        void OnApplicationQuit()
        {
            ServiceLocator.Clear();
        }

        // ── Public API ─────────────────────────────────────────

        /// <summary>
        /// Request a state transition. All transition logic lives here.
        /// Returns true if the transition was accepted.
        /// </summary>
        public bool RequestStateChange(AppState newState)
        {
            if (newState == m_CurrentState)
                return false;

            // Validate transition
            if (!IsTransitionValid(m_CurrentState, newState))
            {
                Debug.LogWarning($"[ARSpaceApp] Rejected state transition {m_CurrentState} → {newState}");
                return false;
            }

            PreviousState = m_CurrentState;
            m_CurrentState = newState;

            Debug.Log($"[ARSpaceApp] State: {PreviousState} → {m_CurrentState}");
            GameEvents.RaiseStateChanged(PreviousState, m_CurrentState);
            return true;
        }

        /// <summary>
        /// Returns to the previous meaningful state (e.g. closing a panel returns to Scanning/Browsing).
        /// </summary>
        public void ReturnToPreviousState()
        {
            // When closing overlays (Analysis, LayoutMenu), return to Scanning
            AppState target = PreviousState;

            // If previous state was also an overlay, fall back to Scanning
            if (target == AppState.AnalysisOpen || target == AppState.LayoutMenuOpen)
                target = AppState.Scanning;

            RequestStateChange(target);
        }

        // ── Transition Validation ──────────────────────────────

        static bool IsTransitionValid(AppState from, AppState to)
        {
            // TrackingLost can be entered from any state
            if (to == AppState.TrackingLost)
                return true;

            // TrackingRecovered restores to previous state — handled via event, not direct transition
            // Initializing can only go to Scanning or TrackingLost
            if (from == AppState.Initializing)
                return to == AppState.Scanning;

            // TrackingLost can recover to any non-Initializing state
            if (from == AppState.TrackingLost)
                return to != AppState.Initializing;

            // General: most transitions are valid outside of Initializing
            // We allow flexible navigation between states for usability
            return true;
        }

        // ── Event Handlers ─────────────────────────────────────

        void OnSessionReady()
        {
            if (m_CurrentState == AppState.Initializing)
            {
                RequestStateChange(AppState.Scanning);
            }
        }

        void OnSessionUnavailable(string reason)
        {
            // Stay in Initializing — UI will show the blocking message
            Debug.LogError($"[ARSpaceApp] AR session unavailable: {reason}");
        }

        void OnTrackingLost()
        {
            if (m_CurrentState != AppState.TrackingLost)
            {
                RequestStateChange(AppState.TrackingLost);
            }
        }

        void OnTrackingRecovered()
        {
            if (m_CurrentState == AppState.TrackingLost)
            {
                // Restore to the state we were in before tracking was lost
                AppState restoreTo = PreviousState;

                // Don't restore to Initializing or another TrackingLost
                if (restoreTo == AppState.Initializing || restoreTo == AppState.TrackingLost)
                    restoreTo = AppState.Scanning;

                RequestStateChange(restoreTo);
            }
        }

        void OnFurnitureSelected(string itemId)
        {
            if (m_CurrentState == AppState.Scanning ||
                m_CurrentState == AppState.Browsing ||
                m_CurrentState == AppState.PlacementPending)
            {
                RequestStateChange(AppState.PlacementPending);
            }
        }

        void OnPlacementCancelled()
        {
            if (m_CurrentState == AppState.PlacementPending)
            {
                RequestStateChange(AppState.Scanning);
            }
        }

        void OnObjectPlaced(GameObject placed)
        {
            // Stay in PlacementPending for rapid multi-placement.
            // User can deselect via PlacementCancelled to return to scanning.
        }

        void OnObjectSelectionChanged(GameObject selected)
        {
            if (selected != null)
            {
                RequestStateChange(AppState.ObjectSelected);
            }
            else
            {
                // Deselected — return to scanning or placement if an item is still chosen
                if (m_CurrentState == AppState.ObjectSelected)
                {
                    RequestStateChange(AppState.Scanning);
                }
            }
        }

        void OnPresentationModeToggled(bool presenting)
        {
            if (presenting)
            {
                RequestStateChange(AppState.Presenting);
            }
            else
            {
                RequestStateChange(AppState.Scanning);
            }
        }
    }
}
