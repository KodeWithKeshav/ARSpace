namespace ARSpace.Core
{
    /// <summary>
    /// Single source of truth for application state.
    /// Every input handler checks the current state to decide whether its gesture is legal.
    /// Transitions are managed exclusively by <see cref="ARSpaceApp"/>.
    /// </summary>
    public enum AppState
    {
        /// <summary>AR session initialising, checking device support, requesting permissions.</summary>
        Initializing,

        /// <summary>AR is running. User is scanning the floor to build detected surfaces.</summary>
        Scanning,

        /// <summary>User is browsing the catalogue. Placement is not active.</summary>
        Browsing,

        /// <summary>An item is selected from the catalogue. The reticle is active, awaiting placement confirmation.</summary>
        PlacementPending,

        /// <summary>A placed object is selected for manipulation (move/rotate/scale).</summary>
        ObjectSelected,

        /// <summary>The analytics bottom sheet is open.</summary>
        AnalysisOpen,

        /// <summary>The layout save/load menu is open.</summary>
        LayoutMenuOpen,

        /// <summary>AR tracking is lost. All interactions disabled. Status banner shown.</summary>
        TrackingLost,

        /// <summary>Presentation mode: all UI chrome, grids, and reticles hidden for clean viewing.</summary>
        Presenting
    }
}
