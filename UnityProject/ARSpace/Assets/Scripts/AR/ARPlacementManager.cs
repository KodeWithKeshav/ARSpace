using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
using ARSpace.Core;
using ARSpace.Furniture;
using ARSpace.Placement;

namespace ARSpace.AR
{
    /// <summary>
    /// Coordinates the placement pipeline for workplace components:
    /// - Resolves selected item from <see cref="CatalogService"/>
    /// - Evaluates reticle hit and surface constraints
    /// - Creates plane-attached or clustered anchors via <see cref="AnchorService"/>
    /// - Instantiates prefab, normalizes yaw to face the user snapped to item increments
    /// - Registers in <see cref="PlacedObjectRegistry"/> and raises <see cref="GameEvents.ObjectPlaced"/>
    ///
    /// Provides <see cref="PlaceCurrentItem"/> as the primary entry point for the Place UI button.
    /// </summary>
    public class ARPlacementManager : MonoBehaviour
    {
        [Header("Placement Configuration")]
        [Tooltip("Allow tap directly on the reticle to confirm placement in addition to the Place button.")]
        [SerializeField]
        bool m_AllowTapOnReticle = true;

        [SerializeField]
        PlacementReticle m_Reticle;

        CatalogService m_CatalogService;
        AnchorService m_AnchorService;
        PlacedObjectRegistry m_Registry;

        bool m_IsPlacing = false;

        void Awake()
        {
            ServiceLocator.Register(this);
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<ARPlacementManager>();
        }

        void Start()
        {
            m_CatalogService = ServiceLocator.Get<CatalogService>();
            m_AnchorService = ServiceLocator.Get<AnchorService>();
            m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();

            if (m_Reticle == null)
            {
                m_Reticle = ServiceLocator.Get<PlacementReticle>();
                if (m_Reticle == null)
                    m_Reticle = FindFirstObjectByType<PlacementReticle>();
            }
        }

        void Update()
        {
            // Optional tap on reticle support (uses the new Input System's enhanced touch,
            // since this project has Active Input Handling set to "Input System Package (New)"
            // and the legacy UnityEngine.Input class throws at runtime in that mode).
            if (!m_AllowTapOnReticle || m_IsPlacing || Touch.activeTouches.Count == 0)
                return;

            Touch touch = Touch.activeTouches[0];
            if (touch.phase != TouchPhase.Began)
                return;

            // Ensure touch is not over UI
            if (UiPointer.IsOverUI(touch.screenPosition))
                return;

            if (Camera.main == null)
                return;

            // Check if tap ray hits reticle bounds
            Ray ray = Camera.main.ScreenPointToRay(touch.screenPosition);
            if (Physics.Raycast(ray, out var hit, 10f))
            {
                if (hit.collider.transform == (m_Reticle != null ? m_Reticle.transform : null) ||
                    hit.collider.GetComponentInParent<PlacementReticle>() != null)
                {
                    PlaceCurrentItem();
                }
            }
        }

        /// <summary>
        /// Primary placement method. Hooked to the UI "Place" button.
        /// </summary>
        public void PlaceCurrentItem()
        {
            if (m_IsPlacing)
                return;

            _ = ConfirmPlacementAsync();
        }

        /// <summary>
        /// Asynchronously executes the full placement sequence.
        /// </summary>
        public async Awaitable<PlacedObject> ConfirmPlacementAsync()
        {
            m_IsPlacing = true;
            try
            {
                if (m_CatalogService == null)
                    m_CatalogService = ServiceLocator.Get<CatalogService>();
                if (m_AnchorService == null)
                    m_AnchorService = ServiceLocator.Get<AnchorService>();
                if (m_Registry == null)
                    m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();

                // 1. Resolve selected FurnitureItem
                var item = m_CatalogService != null ? m_CatalogService.SelectedItem : null;
                if (item == null)
                {
                    GameEvents.RaiseToastRequested("Select an item from the catalogue first.");
                    Debug.LogWarning("[ARPlacementManager] Placement rejected: No furniture item selected.");
                    return null;
                }

                if (item.Prefab == null)
                {
                    GameEvents.RaiseToastRequested($"Selected item '{item.DisplayName}' has no prefab.");
                    Debug.LogError($"[ARPlacementManager] Item '{item.DisplayName}' is missing a prefab reference.");
                    return null;
                }

                // 2. Validate Reticle Hit
                if (m_Reticle == null || !m_Reticle.HasHit)
                {
                    GameEvents.RaiseToastRequested(ServiceLocator.TryGet(out ManualFloor _)
                        ? "Tap on the floor first to choose where it goes."
                        : "Aim at a detected floor surface to place.");
                    Debug.LogWarning("[ARPlacementManager] Placement rejected: Reticle has no valid floor hit.");
                    return null;
                }

                if (!m_Reticle.IsValidPlacement)
                {
                    // Show specific warning feedback
                    string warning = !string.IsNullOrEmpty(m_Reticle.WarningMessage)
                        ? m_Reticle.WarningMessage
                        : "Invalid placement location.";
                    GameEvents.RaiseToastRequested(warning);
                    Debug.LogWarning($"[ARPlacementManager] Placement warned: {warning}");
                }

                Pose targetPose = m_Reticle.CurrentPose;
                ARPlane targetPlane = m_Reticle.CurrentPlane;

                // 3. Instantiate Prefab
                GameObject instance = Instantiate(item.Prefab, targetPose.position, targetPose.rotation);
                instance.name = $"{item.DisplayName}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";

                var placedObj = instance.GetComponent<PlacedObject>();
                if (placedObj == null)
                {
                    placedObj = instance.AddComponent<PlacedObject>();
                }
                placedObj.Initialize(item);

                // 4. Anchor via AnchorService (plane-attached anchor or clustered anchor within 1.5m)
                if (m_AnchorService != null)
                {
                    await m_AnchorService.AttachToAnchorAsync(placedObj, targetPose, targetPlane);
                }

                // Seat the model's lowest point exactly on the detected floor height, independent of
                // where the prefab's pivot happens to sit.
                placedObj.SnapBaseToHeight(targetPose.position.y);

                placedObj.SetFloorWorldY(targetPose.position.y);

                // 5. Register in PlacedObjectRegistry and fire GameEvents.ObjectPlaced
                GameEvents.RaiseObjectPlaced(instance);
                GameEvents.RaiseToastRequested($"Placed {item.DisplayName}");

                Debug.Log($"[ARPlacementManager] ✓ Successfully placed '{item.DisplayName}' at {targetPose.position}.");
                return placedObj;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ARPlacementManager] Placement failed with exception: {ex.Message}\n{ex.StackTrace}");
                GameEvents.RaiseToastRequested("Placement failed. Please try again.");
                return null;
            }
            finally
            {
                m_IsPlacing = false;
            }
        }
    }
}
