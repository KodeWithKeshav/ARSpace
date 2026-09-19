using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARSpace.Core;

namespace ARSpace.AR
{
    /// <summary>
    /// A virtual, perfectly flat floor at a known height, so furniture can be positioned by simply tapping or
    /// dragging on the screen — no ARCore plane detection required (that detection is slow or fails on shiny,
    /// plain or patterned floors). ARCore still provides the phone's position, so anything placed on this floor
    /// stays fixed in the room as you walk around.
    ///
    /// The floor height starts as "phone height minus a typical hold height" and is refined automatically
    /// whenever ARCore does find a floor plane (or a solid floor point under a tap), and can be fine-tuned by
    /// hand with <see cref="Adjust"/>.
    /// </summary>
    public class ManualFloor : MonoBehaviour
    {
        public enum FloorSource { Assumed, ARCore, Manual }

        [Tooltip("Typical height of the phone above the floor when scanning, in metres.")]
        [SerializeField] float m_AssumedCameraHeight = 1.35f;

        [Tooltip("Farthest floor distance that can be targeted, in metres.")]
        [SerializeField] float m_MaxRayDistance = 5f;

        [Tooltip("Off by default: the floor height is set only by the phone-height estimate and the Raise/Lower floor buttons. Turn on to let ARCore's own floor detection adjust it when available.")]
        [SerializeField] bool m_UseARCoreFloor = false;

        [Tooltip("Minimum area (m²) for a detected ARCore plane to be trusted as the floor.")]
        [SerializeField] float m_MinPlaneArea = 0.4f;

        /// <summary>True once the floor height has been initialised (after AR tracking starts).</summary>
        public bool HasFloor { get; private set; }

        /// <summary>World-space height of the floor.</summary>
        public float FloorY { get; private set; }

        public FloorSource Source { get; private set; } = FloorSource.Assumed;

        /// <summary>Distance from the phone down to the floor, or 0 when unknown.</summary>
        public float CameraHeightAboveFloor => HasFloor && m_Camera != null ? m_Camera.transform.position.y - FloorY : 0f;

        Camera m_Camera;
        ARPlaneManager m_PlaneManager;
        ARRaycastManager m_RaycastManager;
        float m_NextPlaneCheck;
        bool m_RebasedOnTracking;

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        void Awake() => ServiceLocator.Register(this);
        void OnDestroy() => ServiceLocator.Unregister<ManualFloor>();

        void Update()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            if (!HasFloor)
            {
                FloorY = m_Camera.transform.position.y - m_AssumedCameraHeight;
                Source = FloorSource.Assumed;
                HasFloor = true;
            }

            // When tracking actually starts the camera pose can jump; re-base the untouched estimate once.
            if (!m_RebasedOnTracking && ARSession.state == ARSessionState.SessionTracking)
            {
                m_RebasedOnTracking = true;
                if (Source == FloorSource.Assumed)
                    FloorY = m_Camera.transform.position.y - m_AssumedCameraHeight;
            }

            if (m_UseARCoreFloor && Source != FloorSource.Manual && Time.unscaledTime >= m_NextPlaneCheck)
            {
                m_NextPlaneCheck = Time.unscaledTime + 0.5f;
                UseLargestDetectedFloorPlane();
            }
        }

        void UseLargestDetectedFloorPlane()
        {
            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();
            if (m_PlaneManager == null)
                return;

            ARPlane best = null;
            float bestArea = m_MinPlaneArea;
            foreach (var plane in m_PlaneManager.trackables)
            {
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;

                float area = plane.size.x * plane.size.y;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = plane;
                }
            }

            if (best != null)
            {
                FloorY = best.transform.position.y;
                Source = FloorSource.ARCore;
            }
        }

        /// <summary>Raises or lowers the floor by hand. Manual adjustments are never overridden by auto-detection.</summary>
        public void Adjust(float deltaMetres)
        {
            if (!HasFloor)
                return;

            FloorY += deltaMetres;
            Source = FloorSource.Manual;
        }

        /// <summary>Forgets manual adjustments and goes back to the automatic estimate.</summary>
        public void ResetToAutomatic()
        {
            if (m_Camera == null)
                return;

            FloorY = m_Camera.transform.position.y - m_AssumedCameraHeight;
            Source = FloorSource.Assumed;
            HasFloor = true;
            m_NextPlaneCheck = 0f;
        }

        /// <summary>Where a screen point lands on the virtual floor. False when aiming above the horizon or too far.</summary>
        public bool TryRaycast(Vector2 screenPoint, out Vector3 point)
        {
            point = default;
            if (!HasFloor || m_Camera == null)
                return false;

            Ray ray = m_Camera.ScreenPointToRay(screenPoint);
            if (ray.direction.y > -0.05f)
                return false;

            float t = (FloorY - ray.origin.y) / ray.direction.y;
            if (t < 0.3f || t > m_MaxRayDistance)
                return false;

            point = ray.GetPoint(t);
            return true;
        }

        /// <summary>
        /// A floor point for ANY screen tap — it never fails. Taps that land on the floor use the exact spot; taps
        /// on walls, the sky or too far away are placed on the floor in that direction, 2 m in front of the phone.
        /// </summary>
        public bool GetPoint(Vector2 screenPoint, out Vector3 point)
        {
            point = default;
            if (!HasFloor || m_Camera == null)
                return false;

            if (TryRaycast(screenPoint, out point))
                return true;

            Ray ray = m_Camera.ScreenPointToRay(screenPoint);
            Vector3 flat = ray.direction;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f)
            {
                flat = m_Camera.transform.forward;
                flat.y = 0f;
            }
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.forward;

            Vector3 origin = m_Camera.transform.position;
            point = new Vector3(origin.x, FloorY, origin.z) + flat.normalized * 2f;
            return true;
        }

        /// <summary>
        /// Uses ARCore, if it can see the floor under a tap, to correct the floor height. Real plane hits are
        /// trusted; single feature points only nudge the estimate. Hits far from the current estimate are ignored
        /// so tapping a bed or table can't pull the floor upwards.
        /// </summary>
        public void RefineFromScreenPoint(Vector2 screenPoint)
        {
            if (!m_UseARCoreFloor || !HasFloor || Source == FloorSource.Manual)
                return;

            if (m_RaycastManager == null)
                m_RaycastManager = FindFirstObjectByType<ARRaycastManager>();
            if (m_RaycastManager == null)
                return;

            const TrackableType types = TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.FeaturePoint;
            if (!m_RaycastManager.Raycast(screenPoint, s_Hits, types))
                return;

            for (int i = 0; i < s_Hits.Count; i++)
            {
                var hit = s_Hits[i];
                float y = hit.pose.position.y;
                if (Mathf.Abs(y - FloorY) > 0.6f)
                    continue;

                if (hit.trackable is ARPlane plane && plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    FloorY = y;
                    Source = FloorSource.ARCore;
                    return;
                }

                if ((hit.hitType & TrackableType.FeaturePoint) != 0 && Vector3.Dot(hit.pose.up, Vector3.up) > 0.85f)
                {
                    FloorY = Mathf.Lerp(FloorY, y, 0.5f);
                    return;
                }
            }
        }
    }
}
