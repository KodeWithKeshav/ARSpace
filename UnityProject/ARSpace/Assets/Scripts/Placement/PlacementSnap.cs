using UnityEngine;

namespace ARSpace.Placement
{
    /// <summary>Optional snap-to-grid for the placement marker (a design tool, switched on from the Tools tray).</summary>
    public static class PlacementSnap
    {
        public const float GridMetres = 0.10f;

        public static bool Enabled { get; private set; }

        public static void Toggle() => Enabled = !Enabled;

        public static Vector3 Apply(Vector3 point)
        {
            if (!Enabled)
                return point;

            return new Vector3(
                Mathf.Round(point.x / GridMetres) * GridMetres,
                point.y,
                Mathf.Round(point.z / GridMetres) * GridMetres);
        }
    }
}
