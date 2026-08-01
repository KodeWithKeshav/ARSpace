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
        selectedFurniture = furnitureDatabase.GetItem(index);
    }

    public GameObject GetSelectedPrefab()
    {
        return selectedFurniture != null ? selectedFurniture.prefab : null;
    }
}