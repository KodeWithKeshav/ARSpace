using System.Collections.Generic;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Edge-snapping system: when dragging an object near another object's edge,
    /// magnetically snaps to align edges. This makes it easy to arrange furniture
    /// in rows, against walls, or tightly packed.
    ///
    /// Listens to drag events from <see cref="ObjectManipulator"/> via Update polling
    /// and nudges the active object when an edge snap is detected.
    /// Shows a visual indicator (green line) when snap is active.
    /// </summary>
    public class SnapToObjectEdge : MonoBehaviour
    {
        [Header("Snap Settings")]
        [Tooltip("Distance in metres within which an edge snap activates.")]
        [SerializeField]
        float m_SnapDistance = 0.06f;

        [Tooltip("Maximum distance to search for nearby objects.")]
        [SerializeField]
        float m_SearchRadius = 3f;

        [Header("Visual Indicator")]
        [SerializeField]
        Color m_SnapLineColor = new Color(0.2f, 0.85f, 0.4f, 0.8f); // green

        LineRenderer m_SnapLineRenderer;
        PlacedObjectRegistry m_Registry;
        ObjectSelectionService m_SelectionService;
        ObjectManipulator m_Manipulator;

        readonly Vector3[] m_SnapLinePoints = new Vector3[2];
        bool m_SnapActive;

        void Awake()
        {
            SetupLineRenderer();
            SetSnapVisible(false);
        }

        void Start()
        {
            m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();
            m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();
            m_Manipulator = ServiceLocator.Get<ObjectManipulator>();
        }

        void SetupLineRenderer()
        {
            var go = new GameObject("SnapIndicator");
            go.transform.SetParent(transform, false);
            m_SnapLineRenderer = go.AddComponent<LineRenderer>();
            m_SnapLineRenderer.positionCount = 2;
            m_SnapLineRenderer.useWorldSpace = true;
            m_SnapLineRenderer.alignment = LineAlignment.View;
            m_SnapLineRenderer.startWidth = 0.005f;
            m_SnapLineRenderer.endWidth = 0.005f;
            m_SnapLineRenderer.startColor = m_SnapLineColor;
            m_SnapLineRenderer.endColor = m_SnapLineColor;
            m_SnapLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_SnapLineRenderer.receiveShadows = false;
            m_SnapLineRenderer.numCapVertices = 4;
        }

        void LateUpdate()
        {
            if (m_SelectionService == null)
                m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();
            if (m_Registry == null)
                m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();

            PlacedObject selected = m_SelectionService != null ? m_SelectionService.SelectedObject : null;
            if (selected == null || selected.IsLocked || selected.Item == null)
            {
                if (m_SnapActive)
                    SetSnapVisible(false);
                return;
            }

            // Only snap while the object is actively being manipulated/dragged
            if (!selected.IsBeingManipulated)
            {
                if (m_SnapActive)
                    SetSnapVisible(false);
                return;
            }

            TrySnapEdges(selected);
        }

        void TrySnapEdges(PlacedObject dragged)
        {
            Vector3 draggedPos = dragged.transform.position;
            Quaternion draggedRot = dragged.transform.rotation;
            Vector3 draggedScale = dragged.transform.localScale;

            FurnitureItem draggedItem = dragged.Item;
            float dhx = draggedItem.Footprint.x * 0.5f * draggedScale.x;
            float dhz = draggedItem.Footprint.y * 0.5f * draggedScale.z;

            // Compute edges of the dragged object (4 edges as line segments)
            Vector3[] draggedCorners = GetWorldCorners(draggedPos, draggedRot, dhx, dhz);

            float bestDist = m_SnapDistance;
            Vector3 bestSnapOffset = Vector3.zero;
            Vector3 bestSnapLineA = Vector3.zero;
            Vector3 bestSnapLineB = Vector3.zero;
            bool foundSnap = false;

            var allObjects = m_Registry.AllObjects;
            for (int i = 0; i < allObjects.Count; i++)
            {
                PlacedObject other = allObjects[i];
                if (other == null || other == dragged || other.Item == null)
                    continue;

                float dist = Vector3.Distance(draggedPos, other.transform.position);
                if (dist > m_SearchRadius)
                    continue;

                Vector3 otherPos = other.transform.position;
                Quaternion otherRot = other.transform.rotation;
                Vector3 otherScale = other.transform.localScale;

                float ohx = other.Item.Footprint.x * 0.5f * otherScale.x;
                float ohz = other.Item.Footprint.y * 0.5f * otherScale.z;

                Vector3[] otherCorners = GetWorldCorners(otherPos, otherRot, ohx, ohz);

                // Check each dragged edge against each other edge
                for (int de = 0; de < 4; de++)
                {
                    Vector3 dA = draggedCorners[de];
                    Vector3 dB = draggedCorners[(de + 1) % 4];
                    Vector3 dMid = (dA + dB) * 0.5f;
                    Vector3 dDir = (dB - dA).normalized;

                    for (int oe = 0; oe < 4; oe++)
                    {
                        Vector3 oA = otherCorners[oe];
                        Vector3 oB = otherCorners[(oe + 1) % 4];
                        Vector3 oDir = (oB - oA).normalized;

                        // Only snap parallel edges (dot product close to ±1)
                        float dot = Mathf.Abs(Vector3.Dot(dDir, oDir));
                        if (dot < 0.9f)
                            continue;

                        // Distance from dragged edge midpoint to the other edge line
                        Vector3 oMid = (oA + oB) * 0.5f;
                        Vector3 closestOnOther = ClosestPointOnSegment(dMid, oA, oB);
                        Vector3 closestOnDragged = ClosestPointOnSegment(oMid, dA, dB);

                        // Perpendicular distance between the two edges
                        Vector3 perp = closestOnOther - dMid;
                        perp.y = 0f; // only horizontal snapping
                        float perpDist = perp.magnitude;

                        if (perpDist < bestDist && perpDist > 0.001f)
                        {
                            bestDist = perpDist;
                            bestSnapOffset = perp;
                            bestSnapLineA = closestOnOther;
                            bestSnapLineB = closestOnDragged;
                            foundSnap = true;
                        }
                    }
                }
            }

            if (foundSnap)
            {
                // Apply the snap offset to the dragged object
                dragged.transform.position += bestSnapOffset;

                // Show visual indicator
                float floorY = !float.IsNaN(dragged.FloorWorldY) ? dragged.FloorWorldY : draggedPos.y;
                m_SnapLinePoints[0] = new Vector3(bestSnapLineA.x, floorY + 0.005f, bestSnapLineA.z);
                m_SnapLinePoints[1] = new Vector3(bestSnapLineB.x, floorY + 0.005f, bestSnapLineB.z);
                m_SnapLineRenderer.SetPositions(m_SnapLinePoints);
                SetSnapVisible(true);
            }
            else
            {
                if (m_SnapActive)
                    SetSnapVisible(false);
            }
        }

        static Vector3[] GetWorldCorners(Vector3 center, Quaternion rot, float hx, float hz)
        {
            return new[]
            {
                center + rot * new Vector3(-hx, 0, -hz),
                center + rot * new Vector3( hx, 0, -hz),
                center + rot * new Vector3( hx, 0,  hz),
                center + rot * new Vector3(-hx, 0,  hz)
            };
        }

        static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float sqLen = ab.sqrMagnitude;
            if (sqLen < 1e-6f) return a;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / sqLen);
            return a + t * ab;
        }

        void SetSnapVisible(bool visible)
        {
            m_SnapActive = visible;
            if (m_SnapLineRenderer != null && m_SnapLineRenderer.enabled != visible)
                m_SnapLineRenderer.enabled = visible;
        }
    }
}
