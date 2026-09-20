using UnityEngine;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.AR
{
    /// <summary>
    /// Shows the floor-detection grid only while it is useful: during the initial scan and while a
    /// catalogue item is being positioned, and only until the first piece of furniture is placed.
    /// After that the grid is hidden — a persistent grid draws lines that run up to the furniture's
    /// silhouette and reads as stray artefacts around the placed objects.
    /// </summary>
    public class PlaneVisibilityController : MonoBehaviour
    {
        bool m_LastVisible = true;

        void OnEnable()
        {
            GameEvents.StateChanged += OnStateChanged;
            GameEvents.ObjectPlaced += OnObjectsChanged;
            GameEvents.ObjectRemoved += OnObjectsChanged;
            PlaneGridVisualizer.DefaultVisible = true;
        }

        void OnDisable()
        {
            GameEvents.StateChanged -= OnStateChanged;
            GameEvents.ObjectPlaced -= OnObjectsChanged;
            GameEvents.ObjectRemoved -= OnObjectsChanged;
        }

        // Deferred one frame so the registry has processed the same event before its count is read.
        void OnStateChanged(AppState from, AppState to) => ScheduleRefresh();
        void OnObjectsChanged(GameObject go) => ScheduleRefresh();

        void ScheduleRefresh()
        {
            CancelInvoke(nameof(Refresh));
            Invoke(nameof(Refresh), 0f);
        }

        void Refresh()
        {
            var app = ServiceLocator.Get<ARSpaceApp>();
            var registry = ServiceLocator.Get<PlacedObjectRegistry>();
            int placed = registry != null ? registry.Count : 0;

            AppState state = app != null ? app.CurrentState : AppState.Scanning;
            bool scanning = state == AppState.Initializing || state == AppState.Scanning || state == AppState.PlacementPending;

            // Show the grid while scanning or positioning furniture.
            // Hide it only after objects are placed AND the user is no longer actively placing.
            bool visible = scanning || placed == 0;

            if (visible == m_LastVisible)
                return;

            m_LastVisible = visible;
            PlaneGridVisualizer.DefaultVisible = visible;
            GameEvents.RaisePlaneVisualsToggled(visible);
        }
    }
}
