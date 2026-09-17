using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARSpace.Persistence
{
    /// <summary>
    /// Serializable data model representing an office layout in ARSpace.
    ///
    /// Critical Architecture Note:
    /// ARCore native anchors do not persist across app restarts without cloud anchors (out of scope).
    /// Therefore, all object positions and rotations are stored relative to a single Layout Origin.
    /// On reload, the user establishes the layout origin on the floor surface, and the entire arrangement
    /// is reconstructed relative to that origin.
    /// </summary>
    [Serializable]
    public class LayoutModel
    {
        public string schemaVersion = "1.0";
        public string layoutId;
        public string layoutName;
        public string createdAt;
        public string modifiedAt;
        public float recordedFloorArea;
        public int totalSeatingCapacity;
        public List<LayoutEntry> entries = new List<LayoutEntry>();

        public LayoutModel()
        {
            layoutId = Guid.NewGuid().ToString("N");
            string now = DateTime.UtcNow.ToString("o");
            createdAt = now;
            modifiedAt = now;
        }

        public LayoutModel(string name) : this()
        {
            layoutName = name;
        }
    }

    /// <summary>
    /// Individual placed furniture entry within a saved layout.
    /// Poses are relative to the Layout Origin.
    /// </summary>
    [Serializable]
    public class LayoutEntry
    {
        public string instanceId;
        public string furnitureItemId;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public Vector3 localScale = Vector3.one;
        public string anchorGroupId;
        public bool isLocked;

        public LayoutEntry() { }

        public LayoutEntry(string instanceId, string itemId, Vector3 localPos, Quaternion localRot, Vector3 scale, bool locked = false, string group = null)
        {
            this.instanceId = instanceId;
            this.furnitureItemId = itemId;
            this.localPosition = localPos;
            this.localRotation = localRot;
            this.localScale = scale;
            this.isLocked = locked;
            this.anchorGroupId = group ?? string.Empty;
        }
    }

    /// <summary>
    /// Metadata header for layout listings without loading all entity details.
    /// </summary>
    [Serializable]
    public class LayoutHeader
    {
        public string layoutId;
        public string layoutName;
        public string modifiedAt;
        public int objectCount;
        public int seatingCapacity;
        public float floorArea;
        public string filePath;
    }
}
