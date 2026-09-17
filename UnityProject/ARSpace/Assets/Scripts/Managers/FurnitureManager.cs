using UnityEngine;
using ARSpace.Furniture;

namespace ARSpace.Managers
{
    /// <summary>
    /// Legacy manager for catalogue selection.
    /// In Phase 4, catalogue selection will be managed via GameEvents.FurnitureSelected.
    /// </summary>
    public class FurnitureManager : MonoBehaviour
    {
        [Header("Furniture Database")]
        [SerializeField] private FurnitureDatabase furnitureDatabase;

        private FurnitureItem selectedFurniture;

        private void Start()
        {
            if (furnitureDatabase != null && furnitureDatabase.Count > 0)
            {
                selectedFurniture = furnitureDatabase.GetItem(0);
            }
        }

        public void SelectFurniture(int index)
        {
            if (furnitureDatabase == null)
                return;

            FurnitureItem item = furnitureDatabase.GetItem(index);
            if (item != null)
            {
                selectedFurniture = item;
            }
        }

        public GameObject GetSelectedPrefab()
        {
            if (selectedFurniture == null)
                return null;

            return selectedFurniture.Prefab;
        }
    }
}