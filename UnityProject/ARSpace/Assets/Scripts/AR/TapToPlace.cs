using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager))]
public class TapToPlace : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject prefabToPlace;

    private ARRaycastManager raycastManager;

    private static readonly List<ARRaycastHit> hits = new();

    private GameObject spawnedObject;

    void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
    }

    void Update()
    {
        if (Input.touchCount == 0)
            return;

        Touch touch = Input.GetTouch(0);

        if (touch.phase != TouchPhase.Began)
            return;

        if (raycastManager.Raycast(
            touch.position,
            hits,
            TrackableType.PlaneWithinPolygon))
        {
            Pose pose = hits[0].pose;

            if (spawnedObject == null)
            {
                spawnedObject = Instantiate(
                    prefabToPlace,
                    pose.position,
                    pose.rotation);
            }
            else
            {
                spawnedObject.transform.SetPositionAndRotation(
                    pose.position,
                    pose.rotation);
            }
        }
    }
}