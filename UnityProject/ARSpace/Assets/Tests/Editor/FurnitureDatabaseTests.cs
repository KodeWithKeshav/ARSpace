using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ARSpace.Furniture;

namespace ARSpace.Tests.Editor
{
    [TestFixture]
    public class FurnitureDatabaseTests
    {
        FurnitureDatabase m_Database;
        FurnitureItem m_Item1;
        FurnitureItem m_Item2;

        [SetUp]
        public void SetUp()
        {
            m_Database = ScriptableObject.CreateInstance<FurnitureDatabase>();

            m_Item1 = ScriptableObject.CreateInstance<FurnitureItem>();
            m_Item1.SetIdentity("cre_workstations_cubicle_3", "6-Person Cubicle", "High-density pod");
            m_Item1.SetClassification(FurnitureCategory.Workstations, 6, false);
            m_Item1.SetDimensions(new Vector3(3.0f, 1.2f, 2.0f), new Vector2(3.0f, 2.0f), 0.8f);

            m_Item2 = ScriptableObject.CreateInstance<FurnitureItem>();
            m_Item2.SetIdentity("cre_conferencetables_conf_desk_4", "Boardroom Table", "12-person table");
            m_Item2.SetClassification(FurnitureCategory.ConferenceTables, 12, false);
            m_Item2.SetDimensions(new Vector3(4.0f, 0.75f, 1.5f), new Vector2(4.0f, 1.5f), 1.2f);

            m_Database.SetItems(new List<FurnitureItem> { m_Item1, m_Item2 });
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Item1 != null) Object.DestroyImmediate(m_Item1);
            if (m_Item2 != null) Object.DestroyImmediate(m_Item2);
            if (m_Database != null) Object.DestroyImmediate(m_Database);
        }

        [Test]
        public void GetById_WithExistingId_ReturnsCorrectItem()
        {
            var result = m_Database.GetById("cre_workstations_cubicle_3");
            Assert.IsNotNull(result);
            Assert.AreEqual("6-Person Cubicle", result.DisplayName);
            Assert.AreEqual(6, result.SeatCount);
        }

        [Test]
        public void GetById_CaseInsensitive_ReturnsCorrectItem()
        {
            var result = m_Database.GetById("CRE_WORKSTATIONS_CUBICLE_3");
            Assert.IsNotNull(result);
            Assert.AreEqual("cre_workstations_cubicle_3", result.Id);
        }

        [Test]
        public void GetById_WithNonExistentId_ReturnsNull()
        {
            var result = m_Database.GetById("non_existent_guid");
            Assert.IsNull(result);
        }

        [Test]
        public void GetById_WithNullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(m_Database.GetById(null));
            Assert.IsNull(m_Database.GetById(string.Empty));
        }

        [Test]
        public void GetByCategory_ReturnsMatchingItems()
        {
            var workstations = m_Database.GetByCategory(FurnitureCategory.Workstations);
            Assert.AreEqual(1, workstations.Count);
            Assert.AreEqual(m_Item1, workstations[0]);

            var confTables = m_Database.GetByCategory(FurnitureCategory.ConferenceTables);
            Assert.AreEqual(1, confTables.Count);
            Assert.AreEqual(m_Item2, confTables[0]);

            var cafeteria = m_Database.GetByCategory(FurnitureCategory.Cafeteria);
            Assert.AreEqual(0, cafeteria.Count);
        }

        [Test]
        public void GetPopulatedCategories_ReturnsDistinctActiveCategories()
        {
            var categories = m_Database.GetPopulatedCategories();
            Assert.AreEqual(2, categories.Count);
            Assert.Contains(FurnitureCategory.Workstations, categories);
            Assert.Contains(FurnitureCategory.ConferenceTables, categories);
        }
    }
}
