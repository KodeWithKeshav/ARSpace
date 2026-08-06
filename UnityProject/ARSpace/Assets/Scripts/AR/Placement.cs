using UnityEngine;

// Attach this to any GameObject in your scene (e.g. an empty "PlacementManager").
// It does NOT need AR plane detection to work — it just puts the object
// a fixed distance in front of whatever the camera is currently looking at.
public class PlaceInFrontOfCamera : MonoBehaviour
{
    [Header("Prefab to place")]
    public GameObject prefabToPlace;

    [Header("Placement settings")]
    public float distanceFromCamera = 1.5f;

    private Camera cam;
    private GameObject spawnedObject;

    void Awake()
    {
        cam = Camera.main;

        if (cam == null)
        {
            Debug.LogError("PlaceInFrontOfCamera: No Camera tagged 'MainCamera' found in scene.");
        }
    }

    // Hook this up to a UI Button's OnClick() event
    public void PlaceObject()
    {
        if (prefabToPlace == null)
        {
            Debug.LogError("PlaceInFrontOfCamera: prefabToPlace is not assigned.");
            return;
        }

        if (cam == null)
        {
            Debug.LogError("PlaceInFrontOfCamera: cam is null, cannot place object.");
            return;
        }

        // Use only the horizontal (flat) forward direction so tilting the phone
        // up/down doesn't push the object underground or up into the sky.
        Vector3 flatForward = cam.transform.forward;
        flatForward.y = 0f;

        if (flatForward.sqrMagnitude < 0.0001f)
        {
            // Camera is looking almost straight up/down; fall back to its right vector's flat projection.
            flatForward = cam.transform.right;
            flatForward.y = 0f;
        }
        flatForward.Normalize();

        Vector3 spawnPos = cam.transform.position + flatForward * distanceFromCamera;
        spawnPos.y = cam.transform.position.y - 1.0f; // roughly floor level relative to camera height, adjust as needed

        Debug.Log("PlaceInFrontOfCamera: cam.position=" + cam.transform.position + " cam.forward=" + cam.transform.forward + " spawnPos=" + spawnPos);

        if (spawnedObject == null)
        {
            spawnedObject = Instantiate(prefabToPlace, spawnPos, Quaternion.identity);
            Debug.Log("PlaceInFrontOfCamera: Spawned object at " + spawnPos);
        }
        else
        {
            spawnedObject.transform.position = spawnPos;
            Debug.Log("PlaceInFrontOfCamera: Moved existing object to " + spawnPos);
        }
    }
}