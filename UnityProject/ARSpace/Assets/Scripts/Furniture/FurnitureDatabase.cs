using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARSpace.Furniture
{
    /// <summary>
    /// ScriptableObject database containing all registered workplace furniture items.
    /// Provides category queries, fast ID lookup, and editor validation.
    /// </summary>
    [CreateAssetMenu(fileName = "FurnitureDatabase", menuName = "ARSpace/Furniture Database")]
    public class FurnitureDatabase : ScriptableObject
    {
        [Header("Registered Catalogue Items")]
        [SerializeField]
        List<FurnitureItem> m_FurnitureItems = new List<FurnitureItem>();

        // Cached lookup dictionary for fast O(1) runtime lookups
        [NonSerialized]
        Dictionary<string, FurnitureItem> m_IdLookup;

        public IReadOnlyList<FurnitureItem> Items => m_FurnitureItems;
        public int Count => m_FurnitureItems.Count;

        // Backwards compatibility
        public List<FurnitureItem> furnitureItems => m_FurnitureItems;

        void OnEnable()
        {
            BuildLookupTable();
        }

        void BuildLookupTable()
        {
            m_IdLookup = new Dictionary<string, FurnitureItem>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < m_FurnitureItems.Count; i++)
            {
                var item = m_FurnitureItems[i];
                if (item != null && !string.IsNullOrEmpty(item.Id))
                {
                    if (!m_IdLookup.ContainsKey(item.Id))
                    {
                        m_IdLookup.Add(item.Id, item);
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves a furniture item by its stable string GUID.
        /// </summary>
        public FurnitureItem GetById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            if (m_IdLookup == null)
                BuildLookupTable();

            if (m_IdLookup.TryGetValue(id, out var item))
                return item;

            return null;
        }

        /// <summary>
        /// Retrieves all items belonging to a specific workplace functional category.
        /// </summary>
        public List<FurnitureItem> GetByCategory(FurnitureCategory category)
        {
            var results = new List<FurnitureItem>();
            for (int i = 0; i < m_FurnitureItems.Count; i++)
            {
                var item = m_FurnitureItems[i];
                if (item != null && item.Category == category)
                {
                    results.Add(item);
                }
            }
            return results;
        }

        /// <summary>
        /// Returns all distinct categories that have at least one item registered.
        /// </summary>
        public List<FurnitureCategory> GetPopulatedCategories()
        {
            var categories = new HashSet<FurnitureCategory>();
            for (int i = 0; i < m_FurnitureItems.Count; i++)
            {
                var item = m_FurnitureItems[i];
                if (item != null)
                {
                    categories.Add(item.Category);
                }
            }
            return new List<FurnitureCategory>(categories);
        }

        /// <summary>
        /// Backwards compatibility method for index-based selection.
        /// </summary>
        public FurnitureItem GetItem(int index)
        {
            if (index < 0 || index >= m_FurnitureItems.Count)
                return null;

            return m_FurnitureItems[index];
        }

        /// <summary>
        /// Replaces the item list (used by <c>FurnitureAssetBuilder</c>).
        /// </summary>
        public void SetItems(List<FurnitureItem> items)
        {
            m_FurnitureItems = items ?? new List<FurnitureItem>();
            BuildLookupTable();
        }

        void OnValidate()
        {
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < m_FurnitureItems.Count; i++)
            {
                var item = m_FurnitureItems[i];
                if (item == null)
                {
                    Debug.LogWarning($"[FurnitureDatabase] Null item reference at index {i}.");
                    continue;
                }

                if (string.IsNullOrEmpty(item.Id))
                {
                    Debug.LogError($"[FurnitureDatabase] Item '{item.name}' at index {i} has an empty or null Id!");
                }
                else if (!seenIds.Add(item.Id))
                {
                    Debug.LogError($"[FurnitureDatabase] Duplicate Id detected: '{item.Id}' on item '{item.name}' (index {i}).");
                }

                if (item.Prefab == null)
                {
                    Debug.LogError($"[FurnitureDatabase] Item '{item.DisplayName ?? item.name}' has no Prefab assigned!");
                }

                if (item.Thumbnail == null)
                {
                    Debug.LogWarning($"[FurnitureDatabase] Item '{item.DisplayName ?? item.name}' has no Thumbnail assigned.");
                }
            }

            BuildLookupTable();
        }
    }
}