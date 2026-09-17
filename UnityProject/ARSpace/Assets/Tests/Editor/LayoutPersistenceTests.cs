using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using ARSpace.Persistence;

namespace ARSpace.Tests.Editor
{
    [TestFixture]
    public class LayoutPersistenceTests
    {
        string m_TestDir;

        [SetUp]
        public void SetUp()
        {
            m_TestDir = Path.Combine(Application.temporaryCachePath, "LayoutTests_" + Guid.NewGuid().ToString("N"));
            if (!Directory.Exists(m_TestDir))
            {
                Directory.CreateDirectory(m_TestDir);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(m_TestDir))
            {
                Directory.Delete(m_TestDir, true);
            }
        }

        [Test]
        public void LayoutModel_Constructor_InitializesGuidsAndTimestamps()
        {
            var layout = new LayoutModel("Executive Office Suite");

            Assert.IsNotNull(layout.layoutId);
            Assert.IsNotEmpty(layout.layoutId);
            Assert.AreEqual("Executive Office Suite", layout.layoutName);
            Assert.AreEqual("1.0", layout.schemaVersion);
            Assert.IsNotNull(layout.createdAt);
            Assert.IsNotNull(layout.modifiedAt);
            Assert.IsNotNull(layout.entries);
        }

        [Test]
        public void LayoutModel_JsonSerialization_RoundtripsAccurately()
        {
            var layout = new LayoutModel("Test Layout");
            layout.recordedFloorArea = 150.5f;
            layout.totalSeatingCapacity = 24;

            layout.entries.Add(new LayoutEntry(
                instanceId: "inst_01",
                itemId: "cre_workstations_cubicle_3",
                localPos: new Vector3(2.5f, 0f, -1.8f),
                localRot: Quaternion.Euler(0f, 45f, 0f),
                scale: Vector3.one,
                locked: true
            ));

            string json = JsonUtility.ToJson(layout, true);
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("cre_workstations_cubicle_3"));

            var deserialized = JsonUtility.FromJson<LayoutModel>(json);
            Assert.IsNotNull(deserialized);
            Assert.AreEqual(layout.layoutId, deserialized.layoutId);
            Assert.AreEqual("Test Layout", deserialized.layoutName);
            Assert.AreEqual(150.5f, deserialized.recordedFloorArea);
            Assert.AreEqual(24, deserialized.totalSeatingCapacity);
            Assert.AreEqual(1, deserialized.entries.Count);

            var entry = deserialized.entries[0];
            Assert.AreEqual("inst_01", entry.instanceId);
            Assert.AreEqual("cre_workstations_cubicle_3", entry.furnitureItemId);
            Assert.AreEqual(2.5f, entry.localPosition.x);
            Assert.IsTrue(entry.isLocked);
        }

        [Test]
        public void BuiltInPresets_CanBeLoadedFromResources()
        {
            var presets = LayoutStorage.ListPresets();
            Assert.GreaterOrEqual(presets.Count, 3, "Expected at least 3 built-in presets in Resources/LayoutPresets/");

            var openPlan = LayoutStorage.LoadPreset("open_plan_24seat");
            Assert.IsNotNull(openPlan, "open_plan_24seat preset failed to load.");
            Assert.AreEqual(24, openPlan.totalSeatingCapacity);
            Assert.Greater(openPlan.entries.Count, 0);

            var hybridZone = LayoutStorage.LoadPreset("hybrid_team_zone");
            Assert.IsNotNull(hybridZone, "hybrid_team_zone preset failed to load.");
            Assert.AreEqual(16, hybridZone.totalSeatingCapacity);
            Assert.Greater(hybridZone.entries.Count, 0);

            var executive = LayoutStorage.LoadPreset("executive_floor");
            Assert.IsNotNull(executive, "executive_floor preset failed to load.");
            Assert.AreEqual(22, executive.totalSeatingCapacity);
            Assert.Greater(executive.entries.Count, 0);
        }

        [Test]
        public void TolerantDeserialization_HandlesUnknownOrExtraKeysSafely()
        {
            string foreignJson = @"{
                ""schemaVersion"": ""2.0"",
                ""layoutId"": ""foreign_001"",
                ""layoutName"": ""Forward Compatible Layout"",
                ""unknownField"": 12345,
                ""entries"": [
                    {
                        ""instanceId"": ""item_a"",
                        ""furnitureItemId"": ""cre_workstations_cubicle_3"",
                        ""extraProperty"": ""ignored""
                    }
                ]
            }";

            var layout = JsonUtility.FromJson<LayoutModel>(foreignJson);
            Assert.IsNotNull(layout);
            Assert.AreEqual("foreign_001", layout.layoutId);
            Assert.AreEqual(1, layout.entries.Count);
            Assert.AreEqual("cre_workstations_cubicle_3", layout.entries[0].furnitureItemId);
        }
    }
}
