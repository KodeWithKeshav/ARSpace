using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.Core;
using ARSpace.Furniture;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// Contextual selection toolbar providing manipulation actions:
    /// - Duplicate, Delete, Rotate 90°, Reset Scale, Lock/Unlock
    /// - Info row displaying item name, dimensions, and seating capacity
    /// </summary>
    public class SelectionToolbar : MonoBehaviour
    {
        [Header("UI Root")]
        [SerializeField]
        GameObject m_ToolbarPanel;

        [Header("Info Row")]
        [SerializeField]
        TextMeshProUGUI m_ItemNameText;

        [SerializeField]
        TextMeshProUGUI m_DimensionsText;

        [SerializeField]
        TextMeshProUGUI m_SeatCountText;

        [Header("Action Buttons")]
        [SerializeField]
        Button m_DuplicateButton;

        [SerializeField]
        Button m_DeleteButton;

        [SerializeField]
        Button m_RotateButton;

        [SerializeField]
        Button m_ResetScaleButton;

        [SerializeField]
        Button m_LockButton;

        [SerializeField]
        TextMeshProUGUI m_LockStatusText;

        PlacedObject m_SelectedObject;
        ObjectSelectionService m_SelectionService;
        AnchorService m_AnchorService;

        void Awake()
        {
            if (m_ToolbarPanel == null)
                m_ToolbarPanel = gameObject;

            WireButtons();
            SetVisible(false);
        }

        void OnEnable()
        {
            GameEvents.ObjectSelectionChanged += OnSelectionChanged;
        }

        void OnDisable()
        {
            GameEvents.ObjectSelectionChanged -= OnSelectionChanged;
        }

        void Start()
        {
            m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();
            m_AnchorService = ServiceLocator.Get<AnchorService>();
        }

        void WireButtons()
        {
            if (m_DuplicateButton != null)
                m_DuplicateButton.onClick.AddListener(OnDuplicateClicked);
            if (m_DeleteButton != null)
                m_DeleteButton.onClick.AddListener(OnDeleteClicked);
            if (m_RotateButton != null)
                m_RotateButton.onClick.AddListener(OnRotateClicked);
            if (m_ResetScaleButton != null)
                m_ResetScaleButton.onClick.AddListener(OnResetScaleClicked);
            if (m_LockButton != null)
                m_LockButton.onClick.AddListener(OnLockClicked);
        }

        void OnSelectionChanged(GameObject selectedGo)
        {
            if (selectedGo != null)
            {
                m_SelectedObject = selectedGo.GetComponent<PlacedObject>();
                if (m_SelectedObject != null)
                {
                    UpdateInfoRow();
                    SetVisible(true);
                    return;
                }
            }

            m_SelectedObject = null;
            SetVisible(false);
        }

        void UpdateInfoRow()
        {
            if (m_SelectedObject == null) return;

            FurnitureItem item = m_SelectedObject.Item;
            string displayName = item != null ? item.DisplayName : m_SelectedObject.name;
            Vector3 size = item != null ? item.RealWorldSize : m_SelectedObject.WorldBounds.size;
            int seats = item != null ? item.SeatCount : 0;

            if (m_ItemNameText != null)
                m_ItemNameText.text = displayName;

            if (m_DimensionsText != null)
                m_DimensionsText.text = $"{size.x:F1}m × {size.z:F1}m × {size.y:F1}m";

            if (m_SeatCountText != null)
                m_SeatCountText.text = seats > 0 ? $"{seats} Seats" : "0 Seats";

            UpdateLockButtonText();
        }

        void UpdateLockButtonText()
        {
            if (m_LockStatusText != null && m_SelectedObject != null)
            {
                m_LockStatusText.text = m_SelectedObject.IsLocked ? "Unlock" : "Lock";
            }
        }

        public async void OnDuplicateClicked()
        {
            if (m_SelectedObject == null || m_SelectedObject.Item == null) return;

            FurnitureItem item = m_SelectedObject.Item;
            float baseY = m_SelectedObject.BaseWorldY;
            Vector3 offset = m_SelectedObject.transform.right * (item.Footprint.x + 0.3f);
            Vector3 newPos = m_SelectedObject.transform.position + offset;
            Quaternion rot = m_SelectedObject.transform.rotation;

            GameObject duplicateGo = Instantiate(item.Prefab, newPos, rot);
            duplicateGo.name = $"{item.DisplayName}_{Guid.NewGuid().ToString("N").Substring(0, 4)}";

            var duplicateObj = duplicateGo.GetComponent<PlacedObject>();
            if (duplicateObj == null) duplicateObj = duplicateGo.AddComponent<PlacedObject>();
            duplicateObj.Initialize(item);

            if (m_AnchorService == null)
                m_AnchorService = ServiceLocator.Get<AnchorService>();

            if (m_AnchorService != null)
            {
                await m_AnchorService.AttachToAnchorAsync(duplicateObj, new Pose(newPos, rot), null);
            }

            duplicateObj.SnapBaseToHeight(baseY);

            GameEvents.RaiseObjectPlaced(duplicateGo);
            GameEvents.RaiseToastRequested($"Duplicated {item.DisplayName}");

            // Select the duplicate
            if (m_SelectionService == null)
                m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();

            if (m_SelectionService != null)
            {
                m_SelectionService.Select(duplicateObj);
            }
        }

        public void OnDeleteClicked()
        {
            if (m_SelectedObject == null) return;

            GameObject toDelete = m_SelectedObject.gameObject;
            string itemName = m_SelectedObject.Item != null ? m_SelectedObject.Item.DisplayName : "Object";

            if (m_SelectionService == null)
                m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();

            if (m_SelectionService != null)
            {
                m_SelectionService.Deselect();
            }

            GameEvents.RaiseObjectRemoved(toDelete);
            Destroy(toDelete);

            GameEvents.RaiseToastRequested($"Deleted {itemName}");
        }

        public void OnRotateClicked()
        {
            if (m_SelectedObject == null || m_SelectedObject.IsLocked) return;

            Transform t = m_SelectedObject.transform;
            float newYaw = (t.eulerAngles.y + 90f) % 360f;
            t.rotation = Quaternion.Euler(0, newYaw, 0);

            GameEvents.RaiseToastRequested("Rotated 90°");
        }

        public void OnResetScaleClicked()
        {
            if (m_SelectedObject == null || m_SelectedObject.IsLocked) return;

            m_SelectedObject.transform.localScale = Vector3.one;
            GameEvents.RaiseToastRequested("Scale reset to 1.0x");
        }

        public void OnLockClicked()
        {
            if (m_SelectedObject == null) return;

            bool nextState = !m_SelectedObject.IsLocked;
            m_SelectedObject.SetLocked(nextState);
            UpdateLockButtonText();

            GameEvents.RaiseToastRequested(nextState ? "Object locked in place" : "Object unlocked");
        }

        void SetVisible(bool visible)
        {
            if (m_ToolbarPanel != null)
                m_ToolbarPanel.SetActive(visible);
        }
    }
}
