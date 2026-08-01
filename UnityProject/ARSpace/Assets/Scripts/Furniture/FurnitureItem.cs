using UnityEngine;

[CreateAssetMenu(fileName = "FurnitureItem", menuName = "ARSpace/Furniture Item")]
public class FurnitureItem : ScriptableObject
{
    [Header("Basic Information")]
    public string itemName;

    [TextArea]
    public string description;

    [Header("Prefab")]
    public GameObject prefab;

    [Header("Thumbnail")]
    public Sprite thumbnail;

    [Header("Placement Settings")]
    public Vector3 defaultScale = Vector3.one;
}