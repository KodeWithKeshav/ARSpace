using System.Collections.Generic;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.Placement
{
    /// <summary>
    /// Undo for placing and deleting furniture. Undoing a placement removes the object again; undoing a delete
    /// (including "clear all") puts it back where it was, at the same size and height.
    /// </summary>
    public class UndoService : MonoBehaviour
    {
        const int MaxEntries = 40;

        enum Kind { Placed, Deleted }

        class Entry
        {
            public Kind Kind;
            public PlacedObject Placed;
            public FurnitureItem Item;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public float FloorY;
            public bool Locked;
        }

        readonly List<Entry> m_History = new List<Entry>();
        bool m_Replaying;

        public bool CanUndo => m_History.Count > 0;

        void Awake() => ServiceLocator.Register(this);
        void OnDestroy() => ServiceLocator.Unregister<UndoService>();

        void OnEnable()
        {
            GameEvents.ObjectPlaced += OnPlaced;
            GameEvents.ObjectRemoved += OnRemoved;
        }

        void OnDisable()
        {
            GameEvents.ObjectPlaced -= OnPlaced;
            GameEvents.ObjectRemoved -= OnRemoved;
        }

        void OnPlaced(GameObject go)
        {
            if (m_Replaying || go == null)
                return;

            var placed = go.GetComponent<PlacedObject>();
            if (placed != null)
                Push(new Entry { Kind = Kind.Placed, Placed = placed });
        }

        void OnRemoved(GameObject go)
        {
            if (m_Replaying || go == null)
                return;

            var placed = go.GetComponent<PlacedObject>();
            if (placed == null || placed.Item == null)
                return;

            Transform t = placed.transform;
            Push(new Entry
            {
                Kind = Kind.Deleted,
                Item = placed.Item,
                Position = t.position,
                Rotation = t.rotation,
                Scale = t.localScale,
                FloorY = placed.FloorWorldY,
                Locked = placed.IsLocked
            });
        }

        void Push(Entry entry)
        {
            m_History.Add(entry);
            if (m_History.Count > MaxEntries)
                m_History.RemoveAt(0);
        }

        /// <summary>Reverses the most recent place / delete that can still be reversed.</summary>
        public async void Undo()
        {
            while (m_History.Count > 0)
            {
                Entry entry = m_History[m_History.Count - 1];
                m_History.RemoveAt(m_History.Count - 1);

                if (entry.Kind == Kind.Placed)
                {
                    if (entry.Placed == null)
                        continue; // already gone (deleted later) — try the next entry

                    string name = entry.Placed.Item != null ? entry.Placed.Item.DisplayName : "object";
                    GameObject go = entry.Placed.gameObject;

                    m_Replaying = true;
                    GameEvents.RaiseObjectRemoved(go);
                    m_Replaying = false;

                    Destroy(go);
                    GameEvents.RaiseToastRequested($"Undo: removed {name}");
                    return;
                }

                if (entry.Item == null || entry.Item.Prefab == null)
                    continue;

                await RestoreAsync(entry);
                return;
            }

            GameEvents.RaiseToastRequested("Nothing to undo");
        }

        async Awaitable RestoreAsync(Entry entry)
        {
            GameObject go = Instantiate(entry.Item.Prefab, entry.Position, entry.Rotation);
            go.transform.localScale = entry.Scale;

            var placed = go.GetComponent<PlacedObject>();
            if (placed == null)
                placed = go.AddComponent<PlacedObject>();
            placed.Initialize(entry.Item);

            var anchors = ServiceLocator.Get<AnchorService>();
            if (anchors != null)
                await anchors.AttachToAnchorAsync(placed, new Pose(entry.Position, entry.Rotation), null);

            if (placed == null)
                return;

            if (!float.IsNaN(entry.FloorY))
                placed.SetFloorWorldY(entry.FloorY);
            placed.SetLocked(entry.Locked);

            m_Replaying = true;
            GameEvents.RaiseObjectPlaced(go);
            m_Replaying = false;

            GameEvents.RaiseToastRequested($"Undo: restored {entry.Item.DisplayName}");
        }
    }
}
