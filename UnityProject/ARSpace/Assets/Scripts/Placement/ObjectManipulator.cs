using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Implements manipulation operations for the currently selected placed furniture object:
    /// - 1-Finger Drag: Translates along detected floor planes and re-anchors on release.
    /// - 2-Finger Twist: Yaw rotation with 5° magnetic snapping to snap increments.
    /// - 2-Finger Pinch: Scales within item min/max bounds (uniform scaling).
    /// - Dynamic collision feedback: Tints object orange/red when footprint conflicts.
    /// Objects never leave the floor plane and never tilt on X/Z axes.
    /// </summary>
    public class ObjectManipulator : MonoBehaviour
    {
        [Header("Snapping & Constraints")]
        [Tooltip("Magnetic angle window in degrees for rotational snapping.")]
        [SerializeField]
        float m_MagneticSnapThreshold = 5.0f;

        [SerializeField]
        LayerMask m_ObstacleLayerMask = ~0;

        ARRaycastManager m_RaycastManager;
        AnchorService m_AnchorService;
        ObjectSelectionService m_SelectionService;

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        // Gesture state cache
        PlacedObject m_ActiveObject;
        ARPlane m_LastHitPlane;
        float m_InitialYaw;
        float m_InitialScale;
        bool m_IsDragging;

        void Awake()
        {
            ServiceLocator.Register(this);
            m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<ObjectManipulator>();
        }

        void Start()
        {
            m_AnchorService = ServiceLocator.Get<AnchorService>();
            m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();
        }

        public void StartDrag(PlacedObject target)
        {
            if (target == null || target.IsLocked) return;

            m_ActiveObject = target;
            m_IsDragging = true;
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!m_IsDragging || m_ActiveObject == null || m_ActiveObject.IsLocked)
                return;

            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();

            if (m_RaycastManager == null)
                return;

            if (m_RaycastManager.Raycast(screenPosition, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                for (int i = 0; i < s_Hits.Count; i++)
                {
                    var hit = s_Hits[i];
                    if (hit.trackable is ARPlane plane && plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        m_LastHitPlane = plane;

                        // Maintain current yaw while updating X and Z positions
                        // Keeps Y exactly at floor plane level
                        Vector3 newPos = hit.pose.position;
                        m_ActiveObject.transform.position = newPos;

                        // Check collision conflict while dragging
                        CheckCollisionFeedback(m_ActiveObject);
                        break;
                    }
                }
            }
        }

        public async void EndDrag()
        {
            if (!m_IsDragging || m_ActiveObject == null)
                return;

            m_IsDragging = false;
            m_ActiveObject.SetConflictTint(false);

            // Re-anchor object at its new position so ARCore tracking maintains stability
            if (m_AnchorService != null && m_LastHitPlane != null)
            {
                Pose newPose = new Pose(m_ActiveObject.transform.position, m_ActiveObject.transform.rotation);
                await m_AnchorService.AttachToAnchorAsync(m_ActiveObject, newPose, m_LastHitPlane);
                Debug.Log($"[ObjectManipulator] Re-anchored '{m_ActiveObject.name}' at {newPose.position}");
            }

            m_ActiveObject = null;
        }

        public void StartTwistAndPinch(PlacedObject target)
        {
            if (target == null) return;
            m_ActiveObject = target;
            m_InitialYaw = target.transform.eulerAngles.y;
            m_InitialScale = target.transform.localScale.x;
        }

        public void OnTwistAndPinch(float angleDeltaDeg, float pinchRatio)
        {
            if (m_ActiveObject == null) return;

            FurnitureItem item = m_ActiveObject.Item;

            // 1. Twist (Yaw Rotation) with magnetic snapping
            float targetYaw = m_InitialYaw - angleDeltaDeg;
            float snapStep = item != null ? item.SnapRotationDegrees : 45f;

            if (snapStep > 0f)
            {
                float closestSnap = Mathf.Round(targetYaw / snapStep) * snapStep;
                float distToSnap = Mathf.Abs(Mathf.DeltaAngle(targetYaw, closestSnap));

                if (distToSnap <= m_MagneticSnapThreshold)
                {
                    targetYaw = closestSnap; // Magnetically snaps within 5 degrees
                }
            }

            m_ActiveObject.transform.rotation = Quaternion.Euler(0, targetYaw, 0);

            // 2. Pinch (Scaling)
            if (pinchRatio > 0.01f)
            {
                float minScale = item != null ? item.MinScale : 0.5f;
                float maxScale = item != null ? item.MaxScale : 2.0f;

                float newScale = Mathf.Clamp(m_InitialScale * pinchRatio, minScale, maxScale);
                m_ActiveObject.transform.localScale = Vector3.one * newScale;
            }
        }

        public void EndTwistAndPinch()
        {
            m_ActiveObject = null;
        }

        void CheckCollisionFeedback(PlacedObject obj)
        {
            if (obj == null) return;

            FurnitureItem item = obj.Item;
            Vector2 fp = item != null ? item.Footprint : new Vector2(1f, 1f);
            Vector3 scale = obj.transform.localScale;

            float hx = fp.x * 0.5f * scale.x;
            float hz = fp.y * 0.5f * scale.z;

            Vector3 center = obj.transform.position + Vector3.up * 0.4f;
            Vector3 halfExtents = new Vector3(hx * 0.9f, 0.35f, hz * 0.9f);

            Collider[] overlaps = Physics.OverlapBox(center, halfExtents, obj.transform.rotation, m_ObstacleLayerMask);
            bool isColliding = false;

            foreach (var col in overlaps)
            {
                var other = col.GetComponentInParent<PlacedObject>();
                if (other != null && other != obj)
                {
                    isColliding = true;
                    break;
                }
            }

            obj.SetConflictTint(isColliding);
        }
    }
}
