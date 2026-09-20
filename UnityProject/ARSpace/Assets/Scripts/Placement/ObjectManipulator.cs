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

        [Header("Drag Smoothing")]
        [Tooltip("Exponential smoothing factor for drag position (lower = smoother).")]
        [SerializeField, Range(0.05f, 1f)]
        float m_DragSmoothFactor = 0.35f;

        [Tooltip("Maximum drag velocity in metres per second. Prevents objects shooting off.")]
        [SerializeField]
        float m_MaxDragSpeed = 2.0f;

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
        public bool IsDragging => m_IsDragging;
        Vector3 m_SmoothedDragTarget; // low-pass filtered drag target

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

        float m_DragFloorY;
        bool m_HasGrabOffset;
        Vector3 m_GrabOffset;

        public void StartDrag(PlacedObject target)
        {
            if (target == null || target.IsLocked) return;

            m_ActiveObject = target;
            m_IsDragging = true;
            m_HasGrabOffset = false;
            m_DragFloorY = target.BaseWorldY;
            m_SmoothedDragTarget = target.transform.position;
            target.BeginManipulation();
        }

        public void OnDrag(Vector2 screenPosition)
        {
            if (!m_IsDragging || m_ActiveObject == null || m_ActiveObject.IsLocked)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            // The object already stands on the floor, so slide it along that same horizontal plane.
            // (Raycasting AR planes here let it hop onto any other horizontal surface — a bed, a table —
            // which is what made furniture jump into mid-air.)
            var floor = new Plane(Vector3.up, new Vector3(0f, m_DragFloorY, 0f));
            Ray ray = cam.ScreenPointToRay(screenPosition);
            if (!floor.Raycast(ray, out float distance))
                return;

            if (distance > 6f)
                return;

            Vector3 hitPoint = ray.GetPoint(distance);
            Transform t = m_ActiveObject.transform;

            if (!m_HasGrabOffset)
            {
                m_GrabOffset = t.position - hitPoint;
                m_GrabOffset.y = 0f;
                m_HasGrabOffset = true;
            }

            Vector3 rawTarget = hitPoint + m_GrabOffset;

            // Low-pass filter: smooth the target position to reject hand micro-tremors
            m_SmoothedDragTarget = Vector3.Lerp(m_SmoothedDragTarget, rawTarget, m_DragSmoothFactor);

            // Velocity clamp: prevent the object from shooting off if the finger jumps
            Vector3 delta = new Vector3(m_SmoothedDragTarget.x - t.position.x, 0f, m_SmoothedDragTarget.z - t.position.z);
            float maxDelta = m_MaxDragSpeed * Time.deltaTime;
            if (delta.sqrMagnitude > maxDelta * maxDelta)
                delta = delta.normalized * maxDelta;

            t.position = new Vector3(t.position.x + delta.x, t.position.y, t.position.z + delta.z);
            m_ActiveObject.SnapBaseToHeight(m_DragFloorY);

            CheckCollisionFeedback(m_ActiveObject);
        }

        public async void EndDrag()
        {
            if (!m_IsDragging || m_ActiveObject == null)
                return;

            m_IsDragging = false;
            var obj = m_ActiveObject;
            m_ActiveObject = null;
            obj.SetConflictTint(false);

            // Snap Y back to floor to undo any vertical drift during drag
            obj.SnapBaseToHeight(m_DragFloorY);

            if (m_AnchorService != null)
            {
                Pose newPose = new Pose(obj.transform.position, obj.transform.rotation);
                await m_AnchorService.AttachToAnchorAsync(obj, newPose, null);
                if (obj != null)
                    obj.SnapBaseToHeight(m_DragFloorY);
            }

            // End manipulation — re-enables drift rejection and records the new intended pose
            if (obj != null)
                obj.EndManipulation();
        }

        public void StartTwistAndPinch(PlacedObject target)
        {
            if (target == null || target.IsLocked) return;
            m_ActiveObject = target;
            m_InitialYaw = target.transform.eulerAngles.y;
            m_InitialScale = target.transform.localScale.x;
            target.BeginManipulation();
        }

        public void OnTwistAndPinch(float angleDeltaDeg, float pinchRatio)
        {
            if (m_ActiveObject == null || m_ActiveObject.IsLocked) return;

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
            if (m_ActiveObject != null)
                m_ActiveObject.EndManipulation();
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
