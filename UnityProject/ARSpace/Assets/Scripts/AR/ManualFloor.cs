using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.AR
{
    /// <summary>
    /// A flat virtual floor at a known height, so furniture can be positioned by tapping or dragging on the
    /// screen. Its height matters: a point picked on the floor is only stable when the floor height is right.
    ///
    /// Height sources, best first:
    ///  1. ARCore's detected floor plane (used automatically whenever one is available),
    ///  2. a solid ARCore surface point under a tap,
    ///  3. an estimate (phone height minus a typical hold height),
    /// with <see cref="Adjust"/> for small manual corrections. Manual corrections are limited to ±0.5 m of the
    /// automatic value so the floor can never be pushed to a nonsensical height, and automatic updates stop once
    /// furniture has been placed so placed objects never shift.
    /// </summary>
    public class ManualFloor : MonoBehaviour
    {
        public enum FloorSource { Assumed, ARCore, Manual }

        [Tooltip("Typical height of the phone above the floor when scanning, in metres.")]
        [SerializeField] float m_AssumedCameraHeight = 1.35f;

        [Tooltip("Farthest floor distance that can be targeted, in metres.")]
        [SerializeField] float m_MaxRayDistance = 4f;

        [Tooltip("Minimum area (m²) for a detected ARCore plane to be trusted as the floor.")]
        [SerializeField] float m_MinPlaneArea = 0.5f;

        const float MinFloorBelowPhone = 0.7f;
        const float MaxFloorBelowPhone = 2.4f;
        const float MaxManualOffset = 0.5f;

        /// <summary>True once the floor height has been initialised.</summary>
        public bool HasFloor { get; private set; }

        /// <summary>World-space height of the floor.</summary>
        public float FloorY { get; private set; }

        public FloorSource Source { get; private set; } = FloorSource.Assumed;

        /// <summary>True when the height comes from ARCore or a manual correction rather than a guess.</summary>
        public bool IsReliable => Source != FloorSource.Assumed;

        /// <summary>Distance from the phone down to the floor, or 0 when unknown.</summary>
        public float CameraHeightAboveFloor => HasFloor && m_Camera != null ? m_Camera.transform.position.y - FloorY : 0f;

        Camera m_Camera;
        ARPlaneManager m_PlaneManager;
        ARRaycastManager m_RaycastManager;
        float m_NextPlaneCheck;
        bool m_RebasedOnTracking;
        float m_BaselineY;

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        void Awake() => ServiceLocator.Register(this);
        void OnDestroy() => ServiceLocator.Unregister<ManualFloor>();

        /// <summary>Automatic height updates stop once furniture is placed, so nothing already placed can shift.</summary>
        static bool FurniturePlaced => ServiceLocator.TryGet(out PlacedObjectRegistry registry) && registry.Count > 0;

        void Update()
        {
            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            if (!HasFloor)
            {
                SetEstimate();
                HasFloor = true;
            }

            // When tracking actually starts the camera pose can jump; re-base the untouched estimate once.
            if (!m_RebasedOnTracking && ARSession.state == ARSessionState.SessionTracking)
            {
                m_RebasedOnTracking = true;
                if (Source == FloorSource.Assumed && !FurniturePlaced)
                    SetEstimate();
            }

            if (Source != FloorSource.Manual && !FurniturePlaced && Time.unscaledTime >= m_NextPlaneCheck)
            {
                m_NextPlaneCheck = Time.unscaledTime + 0.5f;
                UseDetectedFloorPlane();
            }
        }

        void SetEstimate()
        {
            FloorY = m_Camera.transform.position.y - m_AssumedCameraHeight;
            m_BaselineY = FloorY;
            Source = FloorSource.Assumed;
        }

        bool PlausibleFloor(float y)
        {
            if (m_Camera == null)
                return false;

            float below = m_Camera.transform.position.y - y;
            return below >= MinFloorBelowPhone && below <= MaxFloorBelowPhone;
        }

        /// <summary>Uses the lowest sizeable horizontal plane ARCore has found — the floor is the lowest surface.</summary>
        void UseDetectedFloorPlane()
        {
            if (m_PlaneManager == null)
                m_PlaneManager = FindFirstObjectByType<ARPlaneManager>();
            if (m_PlaneManager == null)
                return;

            bool found = false;
            float lowest = float.MaxValue;
            foreach (var plane in m_PlaneManager.trackables)
            {
                if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;
                if (plane.size.x * plane.size.y < m_MinPlaneArea)
                    continue;

                float y = plane.transform.position.y;
                if (!PlausibleFloor(y))
                    continue;

                if (y < lowest)
                {
                    lowest = y;
                    found = true;
                }
            }

            if (found)
                ApplyDetected(lowest);
        }

        void ApplyDetected(float y)
        {
            // First detection snaps; later refinements glide (5 cm at a time) so nothing visibly jumps.
            FloorY = Source == FloorSource.Assumed ? y : Mathf.MoveTowards(FloorY, y, 0.05f);
            m_BaselineY = FloorY;
            Source = FloorSource.ARCore;
        }

        /// <summary>Called with a floor height measured by ARCore at a tapped point.</summary>
        public void NoteFloorHeight(float y)
        {
            if (!HasFloor || Source == FloorSource.Manual || FurniturePlaced || !PlausibleFloor(y))
                return;

            if (Mathf.Abs(y - FloorY) > 0.6f)
                return;

            ApplyDetected(y);
        }

        /// <summary>Nudges the floor by hand. Limited to ±0.5 m of the automatic height so it cannot run away.</summary>
        public void Adjust(float deltaMetres)
        {
            if (!HasFloor || m_Camera == null)
                return;

            float y = FloorY + deltaMetres;
            y = Mathf.Clamp(y, m_BaselineY - MaxManualOffset, m_BaselineY + MaxManualOffset);
            y = Mathf.Clamp(y, m_Camera.transform.position.y - MaxFloorBelowPhone, m_Camera.transform.position.y - MinFloorBelowPhone);

            FloorY = y;
            Source = FloorSource.Manual;
        }

        /// <summary>Forgets manual adjustments and goes back to the automatic estimate.</summary>
        public void ResetToAutomatic()
        {
            if (m_Camera == null)
                return;

            SetEstimate();
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
            if (!HasFloor || Source == FloorSource.Manual || FurniturePlaced)
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
                if (Mathf.Abs(y - FloorY) > 0.6f || !PlausibleFloor(y))
                    continue;

                if (hit.trackable is ARPlane plane && plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    ApplyDetected(y);
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
