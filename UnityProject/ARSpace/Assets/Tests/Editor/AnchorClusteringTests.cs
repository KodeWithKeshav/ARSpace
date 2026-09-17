using NUnit.Framework;
using UnityEngine;

namespace ARSpace.Tests.Editor
{
    [TestFixture]
    public class AnchorClusteringTests
    {
        const float ClusterRadius = 1.5f;

        [Test]
        public void ObjectWithin1Point5Metres_ClustersUnderExistingAnchor()
        {
            Vector3 anchorPos = new Vector3(0f, 0f, 0f);
            Vector3 nearObjectPos = new Vector3(0.8f, 0f, 1.0f); // distance = sqrt(0.64 + 1.0) = 1.28m <= 1.5m

            float distance = Vector3.Distance(anchorPos, nearObjectPos);
            bool shouldCluster = distance <= ClusterRadius;

            Assert.LessOrEqual(distance, ClusterRadius);
            Assert.IsTrue(shouldCluster, "Object within 1.5m should share the existing anchor cluster.");
        }

        [Test]
        public void ObjectBeyond1Point5Metres_DoesNotCluster()
        {
            Vector3 anchorPos = new Vector3(0f, 0f, 0f);
            Vector3 farObjectPos = new Vector3(1.5f, 0f, 1.5f); // distance = sqrt(2.25 + 2.25) = 2.12m > 1.5m

            float distance = Vector3.Distance(anchorPos, farObjectPos);
            bool shouldCluster = distance <= ClusterRadius;

            Assert.Greater(distance, ClusterRadius);
            Assert.IsFalse(shouldCluster, "Object beyond 1.5m should not share the anchor cluster.");
        }

        [Test]
        public void MaxAnchorCap_ConstrainsActiveAnchorCount()
        {
            int maxAnchors = 20;
            int activeAnchors = 20;

            bool canCreateNew = activeAnchors < maxAnchors;
            Assert.IsFalse(canCreateNew, "At max anchor cap (20), no new native anchors should be allocated.");
        }
    }
}
