using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARSpace.Core;

namespace ARSpace.Placement
{
    /// <summary>
    /// Ties every placed object to its own native AR anchor.
    ///
    /// ARCore keeps refining its map of the room; when it corrects itself the phone's pose jumps. Anchors are
    /// moved by ARCore together with that correction, so an anchored object stays on the same real-world spot,
    /// whereas an object at fixed world coordinates appears to slide or float away. The object is parented to the
    /// anchor at local identity, so it always sits exactly at the anchor.
    /// If an anchor cannot be created the object simply stays fixed at its world pose.
    /// </summary>
    public class AnchorService : MonoBehaviour
    {
        [Tooltip("Attach objects to native AR anchors. Turn off only to debug.")]
        [SerializeField]
        bool m_UseNativeAnchors = true;

        [Tooltip("Hard cap on native AR anchors to prevent ARCore tracking degradation.")]
        [SerializeField]
        int m_MaxAnchors = 40;

        [Header("AR Foundation Reference")]
        [SerializeField]
        ARAnchorManager m_AnchorManager;

        readonly Dictionary<PlacedObject, ARAnchor> m_Anchors = new Dictionary<PlacedObject, ARAnchor>();
        readonly Dictionary<PlacedObject, int> m_Versions = new Dictionary<PlacedObject, int>();

        public int ActiveAnchorCount => m_Anchors.Count;
        public int MaxAnchors => m_MaxAnchors;
        public bool UsingNativeAnchors => m_UseNativeAnchors && m_AnchorManager != null;

        void Awake()
        {
            ServiceLocator.Register(this);

            if (m_AnchorManager == null)
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<AnchorService>();
        }

        void OnEnable()
        {
            GameEvents.ObjectRemoved += OnObjectRemoved;
        }

        void OnDisable()
        {
            GameEvents.ObjectRemoved -= OnObjectRemoved;
        }

        /// <summary>
        /// Fixes an object at <paramref name="pose"/> in the real world. Any previous anchor for the object is
        /// replaced. Returns the anchor transform, or null when the object is left at fixed world coordinates.
        /// </summary>
        public async Awaitable<Transform> AttachToAnchorAsync(PlacedObject placedObj, Pose pose, ARPlane plane)
        {
            if (placedObj == null)
                return null;

            int version = m_Versions.TryGetValue(placedObj, out int v) ? v + 1 : 1;
            m_Versions[placedObj] = version;

            // Show it at the exact requested pose immediately, detached from any previous anchor.
            placedObj.transform.SetParent(null, true);
            placedObj.transform.SetPositionAndRotation(pose.position, pose.rotation);
            ReleaseAnchor(placedObj);

            if (!m_UseNativeAnchors || m_AnchorManager == null || m_Anchors.Count >= m_MaxAnchors)
                return null;

            ARAnchor anchor = null;
            try
            {
                var result = await m_AnchorManager.TryAddAnchorAsync(pose);
                if (result.status.IsSuccess())
                    anchor = result.value;
                else
                    Debug.LogWarning($"[AnchorService] Anchor creation returned {result.status}; object stays at fixed world pose.");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AnchorService] Anchor creation failed: {ex.Message}; object stays at fixed world pose.");
            }

            // Object destroyed, or a newer attach superseded this one, while awaiting.
            if (placedObj == null || !m_Versions.TryGetValue(placedObj, out int current) || current != version)
            {
                if (anchor != null)
                    Destroy(anchor.gameObject);
                return null;
            }

            if (anchor == null)
                return null;

            // A brand-new anchor can report a stale transform for a frame; pin it to the requested pose so
            // the object never jumps, then parent at local identity.
            anchor.transform.SetPositionAndRotation(pose.position, pose.rotation);
            placedObj.transform.SetParent(anchor.transform, false);
            placedObj.transform.localPosition = Vector3.zero;
            placedObj.transform.localRotation = Quaternion.identity;

            m_Anchors[placedObj] = anchor;
            return anchor.transform;
        }

        void ReleaseAnchor(PlacedObject placedObj)
        {
            if (placedObj != null && m_Anchors.TryGetValue(placedObj, out ARAnchor anchor))
            {
                m_Anchors.Remove(placedObj);
                if (anchor != null)
                    Destroy(anchor.gameObject);
            }
        }

        void OnObjectRemoved(GameObject removedGo)
        {
            if (removedGo == null)
                return;

            var placedObj = removedGo.GetComponent<PlacedObject>();
            if (placedObj == null)
                return;

            m_Versions.Remove(placedObj);
            if (m_Anchors.TryGetValue(placedObj, out ARAnchor anchor))
            {
                m_Anchors.Remove(placedObj);
                placedObj.transform.SetParent(null, true);
                if (anchor != null)
                    Destroy(anchor.gameObject);
            }
        }

        /// <summary>Clears and destroys all active anchors.</summary>
        public void DestroyAllAnchors()
        {
            foreach (var pair in m_Anchors)
            {
                if (pair.Value != null)
                    Destroy(pair.Value.gameObject);
            }
            m_Anchors.Clear();
            m_Versions.Clear();
        }
    }
}
