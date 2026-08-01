using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FurnitureDatabase", menuName = "ARSpace/Furniture Database")]
public class FurnitureDatabase : ScriptableObject
{
    [Header("Available Furniture")]
    public List<FurnitureItem> furnitureItems = new List<FurnitureItem>();

    public FurnitureItem GetItem(int index)
    {
        if (index < 0 || index >= furnitureItems.Count)
            return null;

        return furnitureItems[index];
    }
}