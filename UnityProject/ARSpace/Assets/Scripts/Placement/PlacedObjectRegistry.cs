using System;
using System.Collections.Generic;
using UnityEngine;
using ARSpace.Core;

namespace ARSpace.Placement
{
    /// <summary>
    /// Central registry tracking all active placed furniture objects in the scene.
    /// Handles tracking quality loss (dimming objects without teleportation)
    /// and provides spatial analytics (seating capacity, utilized floor area).
    /// </summary>
    public class PlacedObjectRegistry : MonoBehaviour
    {
        readonly List<PlacedObject> m_PlacedObjects = new List<PlacedObject>();
        readonly Dictionary<string, PlacedObject> m_Lookup = new Dictionary<string, PlacedObject>(StringComparer.OrdinalIgnoreCase);

        // MaterialPropertyBlock for zero-allocation dimming during tracking loss
        MaterialPropertyBlock m_DimPropertyBlock;
        static readonly int s_DimColorId = Shader.PropertyToID("_BaseColor");

        public IReadOnlyList<PlacedObject> AllObjects => m_PlacedObjects;
        public int Count => m_PlacedObjects.Count;

        void Awake()
        {
            ServiceLocator.Register(this);
            m_DimPropertyBlock = new MaterialPropertyBlock();
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<PlacedObjectRegistry>();
        }

        void OnEnable()
        {
            GameEvents.ObjectPlaced += OnObjectPlaced;
            GameEvents.ObjectRemoved += OnObjectRemoved;
            GameEvents.TrackingLost += OnTrackingLost;
            GameEvents.TrackingRecovered += OnTrackingRecovered;
        }

        void OnDisable()
        {
            GameEvents.ObjectPlaced -= OnObjectPlaced;
            GameEvents.ObjectRemoved -= OnObjectRemoved;
            GameEvents.TrackingLost -= OnTrackingLost;
            GameEvents.TrackingRecovered -= OnTrackingRecovered;
        }

        float m_UnusableSince = -1f;
        float m_UsableSince = -1f;
        bool m_HiddenForTracking;

        void Update()
        {
            // While ARCore has lost tracking the virtual camera stops following the phone, so objects would appear
            // glued to the screen and then jump when tracking returns. Hide them until the pose is trustworthy again.
            if (ARSpace.AR.TrackingStatus.IsUsable)
            {
                m_UnusableSince = -1f;
                if (m_UsableSince < 0f)
                    m_UsableSince = Time.unscaledTime;

                // Only re-show after tracking has been steady for a moment, so objects can't blink on and off.
                if (m_HiddenForTracking && Time.unscaledTime - m_UsableSince > 0.6f)
                    SetObjectsVisible(true);
            }
            else if (m_UnusableSince < 0f)
            {
                m_UnusableSince = Time.unscaledTime;
                m_UsableSince = -1f;
            }
            else if (!m_HiddenForTracking && Time.unscaledTime - m_UnusableSince > 1.0f && m_PlacedObjects.Count > 0)
            {
                SetObjectsVisible(false);
            }
        }

        void SetObjectsVisible(bool visible)
        {
            m_HiddenForTracking = !visible;
            for (int i = 0; i < m_PlacedObjects.Count; i++)
            {
                var obj = m_PlacedObjects[i];
                if (obj == null) continue;
                foreach (var r in obj.GetComponentsInChildren<Renderer>(true))
                    r.enabled = visible;
            }
        }

        public PlacedObject GetById(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;
            m_Lookup.TryGetValue(instanceId, out var obj);
            return obj;
        }

        void OnObjectPlaced(GameObject placedGo)
        {
            if (placedGo == null) return;
            var placedObj = placedGo.GetComponent<PlacedObject>();
            if (placedObj != null && !m_PlacedObjects.Contains(placedObj))
            {
                m_PlacedObjects.Add(placedObj);
                if (!string.IsNullOrEmpty(placedObj.InstanceId))
                {
                    m_Lookup[placedObj.InstanceId] = placedObj;
                }
                Debug.Log($"[PlacedObjectRegistry] Registered '{placedObj.name}' (Total: {m_PlacedObjects.Count})");
            }
        }

        void OnObjectRemoved(GameObject removedGo)
        {
            if (removedGo == null) return;
            var placedObj = removedGo.GetComponent<PlacedObject>();
            if (placedObj != null)
            {
                m_PlacedObjects.Remove(placedObj);
                if (!string.IsNullOrEmpty(placedObj.InstanceId))
                {
                    m_Lookup.Remove(placedObj.InstanceId);
                }
                Debug.Log($"[PlacedObjectRegistry] Unregistered '{placedObj.name}' (Remaining: {m_PlacedObjects.Count})");
            }
        }

        void OnTrackingLost()
        {
            Debug.Log("[PlacedObjectRegistry] Tracking lost. Dimming placed objects to indicate limited pose reliability.");
            SetAllObjectsDimmed(true);
        }

        void OnTrackingRecovered()
        {
            Debug.Log("[PlacedObjectRegistry] Tracking recovered. Restoring object visuals without teleportation.");
            SetAllObjectsDimmed(false);
        }

        void SetAllObjectsDimmed(bool dimmed)
        {
            for (int i = 0; i < m_PlacedObjects.Count; i++)
            {
                var obj = m_PlacedObjects[i];
                if (obj == null) continue;

                var renderers = obj.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                {
                    r.GetPropertyBlock(m_DimPropertyBlock);
                    // Tint to half opacity or dimmed tone during tracking loss
                    Color tint = dimmed ? new Color(0.5f, 0.5f, 0.5f, 0.6f) : Color.white;
                    m_DimPropertyBlock.SetColor(s_DimColorId, tint);
                    r.SetPropertyBlock(m_DimPropertyBlock);
                }
            }
        }

        /// <summary>
        /// Calculates total seating capacity across all registered furniture items.
        /// Powers the CRE capacity analysis requirement.
        /// </summary>
        public int CalculateTotalSeatingCapacity()
        {
            int totalSeats = 0;
            for (int i = 0; i < m_PlacedObjects.Count; i++)
            {
                var obj = m_PlacedObjects[i];
                if (obj != null && obj.Item != null)
                {
                    totalSeats += obj.Item.SeatCount;
                }
            }
            return totalSeats;
        }

        /// <summary>
        /// Calculates total utilized floor footprint area in square metres.
        /// </summary>
        public float CalculateTotalFootprintArea()
        {
            float totalArea = 0f;
            for (int i = 0; i < m_PlacedObjects.Count; i++)
            {
                var obj = m_PlacedObjects[i];
                if (obj != null && obj.Item != null)
                {
                    totalArea += obj.Item.Footprint.x * obj.Item.Footprint.y;
                }
            }
            return totalArea;
        }

        /// <summary>
        /// Clears all placed objects and their parent anchors.
        /// </summary>
        public void ClearAll()
        {
            for (int i = m_PlacedObjects.Count - 1; i >= 0; i--)
            {
                var obj = m_PlacedObjects[i];
                if (obj != null)
                {
                    GameEvents.RaiseObjectRemoved(obj.gameObject);
                    Destroy(obj.gameObject);
                }
            }

            m_PlacedObjects.Clear();
            m_Lookup.Clear();

            var anchorService = ServiceLocator.Get<AnchorService>();
            if (anchorService != null)
            {
                anchorService.DestroyAllAnchors();
            }
        }
    }
}
