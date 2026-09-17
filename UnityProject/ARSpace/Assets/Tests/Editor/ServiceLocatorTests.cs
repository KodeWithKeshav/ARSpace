using NUnit.Framework;
using UnityEngine;
using ARSpace.Core;

namespace ARSpace.Tests.Editor
{
    [TestFixture]
    public class ServiceLocatorTests
    {
        class TestServiceA : MonoBehaviour { }
        class TestServiceB : MonoBehaviour { }

        GameObject m_Container;

        [SetUp]
        public void SetUp()
        {
            ServiceLocator.Clear();
            m_Container = new GameObject("TestServiceContainer");
        }

        [TearDown]
        public void TearDown()
        {
            ServiceLocator.Clear();
            if (m_Container != null)
            {
                Object.DestroyImmediate(m_Container);
            }
        }

        [Test]
        public void RegisterAndGet_ReturnsRegisteredInstance()
        {
            var service = m_Container.AddComponent<TestServiceA>();
            ServiceLocator.Register(service);

            var retrieved = ServiceLocator.Get<TestServiceA>();
            Assert.AreSame(service, retrieved);
        }

        [Test]
        public void Get_UnregisteredType_ReturnsNull()
        {
            var retrieved = ServiceLocator.Get<TestServiceB>();
            Assert.IsNull(retrieved);
        }

        [Test]
        public void TryGet_ReturnsTrueIfPresent_FalseIfMissing()
        {
            var service = m_Container.AddComponent<TestServiceA>();
            ServiceLocator.Register(service);

            Assert.IsTrue(ServiceLocator.TryGet<TestServiceA>(out var found));
            Assert.AreSame(service, found);

            Assert.IsFalse(ServiceLocator.TryGet<TestServiceB>(out var missing));
            Assert.IsNull(missing);
        }

        [Test]
        public void Unregister_RemovesService()
        {
            var service = m_Container.AddComponent<TestServiceA>();
            ServiceLocator.Register(service);

            ServiceLocator.Unregister<TestServiceA>();
            Assert.IsNull(ServiceLocator.Get<TestServiceA>());
        }

        [Test]
        public void Clear_RemovesAllServices()
        {
            var sA = m_Container.AddComponent<TestServiceA>();
            var sB = m_Container.AddComponent<TestServiceB>();
            ServiceLocator.Register(sA);
            ServiceLocator.Register(sB);

            ServiceLocator.Clear();
            Assert.IsNull(ServiceLocator.Get<TestServiceA>());
            Assert.IsNull(ServiceLocator.Get<TestServiceB>());
        }
    }
}
