using System;
using UnityEngine;

namespace ARSpace.Core
{
    /// <summary>
    /// Central event bus. UI raises intents here; AR systems and services subscribe.
    /// This keeps UI fully decoupled from AR — no direct references between layers.
    ///
    /// Convention: events are static Actions. Subscribers add/remove in OnEnable/OnDisable.
    /// Invoke only via the Raise* methods which include null-safety.
    /// </summary>
    public static class GameEvents
    {
        // ── State ──────────────────────────────────────────────

        /// <summary>Fired when <see cref="ARSpaceApp"/> transitions to a new state.</summary>
        public static event Action<AppState, AppState> StateChanged;
        public static void RaiseStateChanged(AppState from, AppState to)
        {
            StateChanged?.Invoke(from, to);
        }

        // ── AR Session ─────────────────────────────────────────

        /// <summary>AR session is fully ready (device supported, permission granted, tracking).</summary>
        public static event Action SessionReady;
        public static void RaiseSessionReady()
        {
            SessionReady?.Invoke();
        }

        /// <summary>AR session is unavailable or fatally broken. Carries a user-facing reason string.</summary>
        public static event Action<string> SessionUnavailable;
        public static void RaiseSessionUnavailable(string reason)
        {
            SessionUnavailable?.Invoke(reason);
        }

        // ── Tracking ───────────────────────────────────────────

        /// <summary>Tracking quality changed. Carries a user-facing guidance string (empty = tracking OK).</summary>
        public static event Action<string> TrackingGuidanceChanged;
        public static void RaiseTrackingGuidanceChanged(string guidance)
        {
            TrackingGuidanceChanged?.Invoke(guidance);
        }

        /// <summary>Tracking was lost completely.</summary>
        public static event Action TrackingLost;
        public static void RaiseTrackingLost()
        {
            TrackingLost?.Invoke();
        }

        /// <summary>Tracking recovered after a loss.</summary>
        public static event Action TrackingRecovered;
        public static void RaiseTrackingRecovered()
        {
            TrackingRecovered?.Invoke();
        }

        // ── Plane Detection ────────────────────────────────────

        /// <summary>A new floor plane was detected. Carries the total floor area in m².</summary>
        public static event Action<float> FloorAreaUpdated;
        public static void RaiseFloorAreaUpdated(float totalAreaSqM)
        {
            FloorAreaUpdated?.Invoke(totalAreaSqM);
        }

        // ── Catalogue / Placement ──────────────────────────────

        /// <summary>User selected a furniture item from the catalogue. Carries the item id.</summary>
        public static event Action<string> FurnitureSelected;
        public static void RaiseFurnitureSelected(string itemId)
        {
            FurnitureSelected?.Invoke(itemId);
        }

        /// <summary>User cancelled the current placement (deselected item before placing).</summary>
        public static event Action PlacementCancelled;
        public static void RaisePlacementCancelled()
        {
            PlacementCancelled?.Invoke();
        }

        /// <summary>An object was placed in the scene. Carries the placed object's GameObject.</summary>
        public static event Action<GameObject> ObjectPlaced;
        public static void RaiseObjectPlaced(GameObject placedObject)
        {
            ObjectPlaced?.Invoke(placedObject);
        }

        /// <summary>An object was removed from the scene. Carries the removed object's GameObject.</summary>
        public static event Action<GameObject> ObjectRemoved;
        public static void RaiseObjectRemoved(GameObject removedObject)
        {
            ObjectRemoved?.Invoke(removedObject);
        }

        // ── Selection ──────────────────────────────────────────

        /// <summary>An object was selected. Carries the selected GameObject (null = deselected).</summary>
        public static event Action<GameObject> ObjectSelectionChanged;
        public static void RaiseObjectSelectionChanged(GameObject selected)
        {
            ObjectSelectionChanged?.Invoke(selected);
        }

        // ── Plane Visuals ──────────────────────────────────────

        /// <summary>Plane grid visuals toggled. True = visible.</summary>
        public static event Action<bool> PlaneVisualsToggled;
        public static void RaisePlaneVisualsToggled(bool visible)
        {
            PlaneVisualsToggled?.Invoke(visible);
        }

        // ── Layout ─────────────────────────────────────────────

        /// <summary>A layout was loaded. Carries the layout name.</summary>
        public static event Action<string> LayoutLoaded;
        public static void RaiseLayoutLoaded(string layoutName)
        {
            LayoutLoaded?.Invoke(layoutName);
        }

        /// <summary>A layout was saved. Carries the layout name.</summary>
        public static event Action<string> LayoutSaved;
        public static void RaiseLayoutSaved(string layoutName)
        {
            LayoutSaved?.Invoke(layoutName);
        }

        // ── Presentation Mode ──────────────────────────────────

        /// <summary>Presentation mode toggled. True = presenting (hide all chrome).</summary>
        public static event Action<bool> PresentationModeToggled;
        public static void RaisePresentationModeToggled(bool presenting)
        {
            PresentationModeToggled?.Invoke(presenting);
        }

        // ── Toast ──────────────────────────────────────────────

        /// <summary>Show a transient toast message to the user.</summary>
        public static event Action<string> ToastRequested;
        public static void RaiseToastRequested(string message)
        {
            ToastRequested?.Invoke(message);
        }
    }
}
