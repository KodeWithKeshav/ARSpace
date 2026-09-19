using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using ARSpace.Core;

namespace ARSpace.Placement
{
    /// <summary>
    /// Service that manages the lifecycle of native AR anchors.
    ///
    /// Implements nearby object clustering (objects within 1.5 m share an anchor)
    /// to avoid exceeding ARCore's internal anchor performance threshold.
    /// Automatically destroys anchors when their last child object is removed.
    /// </summary>
    public class AnchorService : MonoBehaviour
    {
        [Header("Clustering & Limits")]
        [Tooltip("Maximum distance in metres between objects to share a common anchor.")]
        [SerializeField]
        float m_ClusterRadius = 1.5f;

        [Tooltip("Hard cap on native AR anchors to prevent ARCore tracking degradation.")]
        [SerializeField]
        int m_MaxAnchors = 20;

        [Header("AR Foundation Reference")]
        [SerializeField]
        ARAnchorManager m_AnchorManager;

        public class AnchorCluster
        {
            public ARAnchor Anchor;
            public GameObject FallbackAnchorGo;
            public Vector3 OriginPosition;
            public readonly List<PlacedObject> Children = new List<PlacedObject>();

            public Transform AnchorTransform => Anchor != null ? Anchor.transform : FallbackAnchorGo.transform;
        }

        readonly List<AnchorCluster> m_Clusters = new List<AnchorCluster>();

        public int ActiveAnchorCount => m_Clusters.Count;
        public float ClusterRadius => m_ClusterRadius;
        public int MaxAnchors => m_MaxAnchors;

        void Awake()
        {
            ServiceLocator.Register(this);

            if (m_AnchorManager == null)
            {
                m_AnchorManager = FindFirstObjectByType<ARAnchorManager>();
            }
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
        /// Attaches a placed object to an anchor at the specified pose on the plane.
        /// Groups objects within <see cref="ClusterRadius"/> (1.5 m) under a shared anchor.
        /// </summary>
        public async Awaitable<Transform> AttachToAnchorAsync(PlacedObject placedObj, Pose pose, ARPlane plane)
        {
            if (placedObj == null)
                return null;

            // 1. Check for an existing anchor cluster within the cluster threshold
            AnchorCluster bestCluster = FindNearbyCluster(pose.position, m_ClusterRadius);

            if (bestCluster != null)
            {
                // Attach as child of existing shared anchor
                AttachObjectToCluster(placedObj, bestCluster, pose);
                Debug.Log($"[AnchorService] Clustered '{placedObj.name}' under existing anchor (Distance: {Vector3.Distance(pose.position, bestCluster.OriginPosition):F2}m). Active anchors: {m_Clusters.Count}");
                return bestCluster.AnchorTransform;
            }

            // 2. If max anchors reached, fallback to the closest existing cluster
            if (m_Clusters.Count >= m_MaxAnchors && m_Clusters.Count > 0)
            {
                AnchorCluster nearest = FindNearbyCluster(pose.position, float.MaxValue);
                if (nearest != null)
                {
                    AttachObjectToCluster(placedObj, nearest, pose);
                    Debug.LogWarning($"[AnchorService] Max anchor cap ({m_MaxAnchors}) reached. Attached to nearest anchor.");
                    return nearest.AnchorTransform;
                }
            }

            // 3. Create a new anchor
            var newCluster = await CreateNewClusterAsync(pose, plane);
            AttachObjectToCluster(placedObj, newCluster, pose);
            m_Clusters.Add(newCluster);

            Debug.Log($"[AnchorService] Created new anchor cluster at {pose.position}. Total active anchors: {m_Clusters.Count}/{m_MaxAnchors}");
            return newCluster.AnchorTransform;
        }

        AnchorCluster FindNearbyCluster(Vector3 position, float maxDistance)
        {
            AnchorCluster closest = null;
            float closestDist = maxDistance;

            for (int i = 0; i < m_Clusters.Count; i++)
            {
                var cluster = m_Clusters[i];
                if (cluster.AnchorTransform == null) continue;

                float dist = Vector3.Distance(position, cluster.AnchorTransform.position);
                if (dist < closestDist)
                {
                    closestDist = dist;
                    closest = cluster;
                }
            }

            return closest;
        }

        void AttachObjectToCluster(PlacedObject placedObj, AnchorCluster cluster, Pose targetPose)
        {
            Transform anchorTrans = cluster.AnchorTransform;

            // Keep target world position and rotation intact when parenting to anchor
            placedObj.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
            placedObj.transform.SetParent(anchorTrans, true);

            if (!cluster.Children.Contains(placedObj))
            {
                cluster.Children.Add(placedObj);
            }
        }

        async Awaitable<AnchorCluster> CreateNewClusterAsync(Pose pose, ARPlane plane)
        {
            var cluster = new AnchorCluster
            {
                OriginPosition = pose.position
            };

            // Attempt 1: Attach anchor to hit plane (superior tracking for plane-bound objects)
            if (m_AnchorManager != null && plane != null)
            {
                try
                {
                    cluster.Anchor = m_AnchorManager.AttachAnchor(plane, pose);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AnchorService] AttachAnchor to plane failed: {ex.Message}. Falling back to TryAddAnchorAsync.");
                }
            }

            // Attempt 2: Add general async anchor
            if (cluster.Anchor == null && m_AnchorManager != null)
            {
                try
                {
                    var result = await m_AnchorManager.TryAddAnchorAsync(pose);
                    if (result.status.IsSuccess())
                    {
                        cluster.Anchor = result.value;
                    }
                    else
                    {
                        Debug.LogWarning($"[AnchorService] TryAddAnchorAsync returned status: {result.status}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AnchorService] TryAddAnchorAsync exception: {ex.Message}");
                }
            }

            // A freshly created anchor can report a stale (identity) transform for a frame or two.
            // Children are parented with world-position-stays, so pin it to the requested pose now;
            // otherwise the child would inherit the wrong local offset and drift/float once tracking corrects it.
            if (cluster.Anchor != null)
                cluster.Anchor.transform.SetPositionAndRotation(pose.position, pose.rotation);

            // Attempt 3: Simulation or fallback anchor
            if (cluster.Anchor == null)
            {
                var fallbackGo = new GameObject("SimulatedAnchor");
                fallbackGo.transform.SetPositionAndRotation(pose.position, pose.rotation);
                cluster.FallbackAnchorGo = fallbackGo;
            }

            return cluster;
        }

        void OnObjectRemoved(GameObject removedGo)
        {
            if (removedGo == null) return;
            var placedObj = removedGo.GetComponent<PlacedObject>();
            if (placedObj == null) return;

            // Find cluster containing this object
            for (int i = m_Clusters.Count - 1; i >= 0; i--)
            {
                var cluster = m_Clusters[i];
                if (cluster.Children.Remove(placedObj))
                {
                    // If no children remain, destroy the anchor
                    if (cluster.Children.Count == 0)
                    {
                        DestroyCluster(cluster);
                        m_Clusters.RemoveAt(i);
                        Debug.Log($"[AnchorService] Anchor destroyed after last child removed. Remaining anchors: {m_Clusters.Count}");
                    }
                    break;
                }
            }
        }

        void DestroyCluster(AnchorCluster cluster)
        {
            if (cluster.Anchor != null)
            {
                Destroy(cluster.Anchor.gameObject);
                cluster.Anchor = null;
            }
            if (cluster.FallbackAnchorGo != null)
            {
                Destroy(cluster.FallbackAnchorGo);
                cluster.FallbackAnchorGo = null;
            }
        }

        /// <summary>
        /// Clears and destroys all active anchors.
        /// </summary>
        public void DestroyAllAnchors()
        {
            for (int i = 0; i < m_Clusters.Count; i++)
            {
                DestroyCluster(m_Clusters[i]);
            }
            m_Clusters.Clear();
        }
    }
}
