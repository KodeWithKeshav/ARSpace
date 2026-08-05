using UnityEngine;

public class FurnitureManager : MonoBehaviour
{
    [Header("Furniture Database")]
    [SerializeField] private FurnitureDatabase furnitureDatabase;

    private FurnitureItem selectedFurniture;

    private void Start()
    {
        if (furnitureDatabase != null && furnitureDatabase.furnitureItems.Count > 0)
        {
            selectedFurniture = furnitureDatabase.GetItem(0);
        }
    }

    public void SelectFurniture(int index)
    {
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

        return selectedFurniture.prefab;
    }
}