using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.Core;
using ARSpace.Persistence;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// UI controller for managing workplace layout persistence:
    /// - New, Save, Save As, Clear All
    /// - Listing and loading saved user layouts
    /// - Listing and loading built-in layout presets
    /// - Prominent origin explanation and origin re-anchoring
    /// </summary>
    public class LayoutMenuPanel : MonoBehaviour
    {
        [Header("Main Panel Root")]
        [SerializeField]
        GameObject m_PanelRoot;

        [Header("Close & Navigation")]
        [SerializeField]
        Button m_CloseButton;

        [Header("Origin Information")]
        [SerializeField]
        TextMeshProUGUI m_OriginNoticeText;

        [SerializeField]
        Button m_SetOriginButton;

        [Header("Save Controls")]
        [SerializeField]
        TMP_InputField m_LayoutNameInput;

        [SerializeField]
        Button m_SaveButton;

        [SerializeField]
        Button m_NewLayoutButton;

        [SerializeField]
        Button m_ClearAllButton;

        [Header("List Containers")]
        [SerializeField]
        Transform m_SavedListContainer;

        [SerializeField]
        Transform m_PresetsListContainer;

        [SerializeField]
        GameObject m_LayoutCardPrefab;

        LayoutOriginManager m_OriginManager;
        PlacedObjectRegistry m_Registry;

        const string DefaultNotice = "Saved layouts restore relative to the origin location where you confirm on the floor.";

        void Awake()
        {
            if (m_PanelRoot == null)
            {
                m_PanelRoot = gameObject;
            }

            WireButtons();

            if (m_OriginNoticeText != null)
            {
                m_OriginNoticeText.text = DefaultNotice;
            }
        }

        void Start()
        {
            m_OriginManager = ServiceLocator.Get<LayoutOriginManager>();
            m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();

            // Default hidden
            SetVisible(false);
        }

        void WireButtons()
        {
            if (m_CloseButton != null)
                m_CloseButton.onClick.AddListener(Hide);

            if (m_SaveButton != null)
                m_SaveButton.onClick.AddListener(OnSaveClicked);

            if (m_NewLayoutButton != null)
                m_NewLayoutButton.onClick.AddListener(OnNewLayoutClicked);

            if (m_ClearAllButton != null)
                m_ClearAllButton.onClick.AddListener(OnClearAllClicked);

            if (m_SetOriginButton != null)
                m_SetOriginButton.onClick.AddListener(OnSetOriginClicked);
        }

        public void Toggle()
        {
            if (m_PanelRoot != null)
            {
                SetVisible(!m_PanelRoot.activeSelf);
            }
        }

        public void Show()
        {
            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        void SetVisible(bool visible)
        {
            if (m_PanelRoot != null)
            {
                m_PanelRoot.SetActive(visible);
            }

            var app = ServiceLocator.Get<ARSpaceApp>();
            if (app != null)
            {
                if (visible)
                {
                    app.RequestStateChange(AppState.LayoutMenuOpen);
                }
                else if (app.CurrentState == AppState.LayoutMenuOpen)
                {
                    app.ReturnToPreviousState();
                }
            }

            if (visible)
            {
                RefreshLayoutLists();
            }
        }

        public void RefreshLayoutLists()
        {
            PopulateSavedLayouts();
            PopulatePresets();
        }

        void PopulateSavedLayouts()
        {
            if (m_SavedListContainer == null) return;

            // Clear old children
            for (int i = m_SavedListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(m_SavedListContainer.GetChild(i).gameObject);
            }

            List<LayoutHeader> saved = LayoutStorage.ListSavedLayouts();
            if (saved.Count == 0)
            {
                CreateEmptyNotice(m_SavedListContainer, "No saved layouts yet. Design a layout and tap Save.");
                return;
            }

            for (int i = 0; i < saved.Count; i++)
            {
                var header = saved[i];
                CreateLayoutCard(m_SavedListContainer, header, isPreset: false);
            }
        }

        void PopulatePresets()
        {
            if (m_PresetsListContainer == null) return;

            // Clear old children
            for (int i = m_PresetsListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(m_PresetsListContainer.GetChild(i).gameObject);
            }

            List<LayoutHeader> presets = LayoutStorage.ListPresets();
            for (int i = 0; i < presets.Count; i++)
            {
                var header = presets[i];
                CreateLayoutCard(m_PresetsListContainer, header, isPreset: true);
            }
        }

        void CreateLayoutCard(Transform container, LayoutHeader header, bool isPreset)
        {
            GameObject cardGo;
            if (m_LayoutCardPrefab != null)
            {
                cardGo = Instantiate(m_LayoutCardPrefab, container);
            }
            else
            {
                cardGo = CreateFallbackCardUI(container, header.layoutName);
            }

            // Bind UI Texts
            var texts = cardGo.GetComponentsInChildren<TextMeshProUGUI>();
            if (texts.Length > 0) texts[0].text = header.layoutName;
            if (texts.Length > 1)
            {
                texts[1].text = $"{header.objectCount} items • {header.seatingCapacity} seats • {header.floorArea:F1} m²";
            }

            // Bind Buttons
            var buttons = cardGo.GetComponentsInChildren<Button>();
            for (int b = 0; b < buttons.Length; b++)
            {
                var btn = buttons[b];
                string btnName = btn.name.ToLowerInvariant();

                if (btnName.Contains("load") || b == 0)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnLoadRequested(header, isPreset));
                }
                else if (btnName.Contains("delete") && !isPreset)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => OnDeleteRequested(header.layoutId));
                }
                else if (isPreset && btnName.Contains("delete"))
                {
                    btn.gameObject.SetActive(false);
                }
            }
        }

        GameObject CreateFallbackCardUI(Transform container, string title)
        {
            GameObject card = new GameObject($"Card_{title}", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            card.transform.SetParent(container, false);

            var img = card.GetComponent<Image>();
            img.color = new Color(0.15f, 0.18f, 0.22f, 0.95f);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(card.transform, false);
            var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
            titleTmp.text = title;
            titleTmp.fontSize = 18f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;

            var subtitleGo = new GameObject("Subtitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            subtitleGo.transform.SetParent(card.transform, false);
            var subTmp = subtitleGo.GetComponent<TextMeshProUGUI>();
            subTmp.fontSize = 14f;
            subTmp.color = new Color(0.7f, 0.7f, 0.7f, 1f);

            var loadBtnGo = new GameObject("LoadButton", typeof(RectTransform), typeof(Image), typeof(Button));
            loadBtnGo.transform.SetParent(card.transform, false);
            var loadBtnImg = loadBtnGo.GetComponent<Image>();
            loadBtnImg.color = new Color(0.12f, 0.53f, 0.9f, 1f);

            var btnTextGo = new GameObject("BtnText", typeof(RectTransform), typeof(TextMeshProUGUI));
            btnTextGo.transform.SetParent(loadBtnGo.transform, false);
            var btnTmp = btnTextGo.GetComponent<TextMeshProUGUI>();
            btnTmp.text = "Load Layout";
            btnTmp.alignment = TextAlignmentOptions.Center;
            btnTmp.fontSize = 14f;
            btnTmp.color = Color.white;

            return card;
        }

        void CreateEmptyNotice(Transform container, string message)
        {
            GameObject noticeGo = new GameObject("EmptyNotice", typeof(RectTransform), typeof(TextMeshProUGUI));
            noticeGo.transform.SetParent(container, false);
            var tmp = noticeGo.GetComponent<TextMeshProUGUI>();
            tmp.text = message;
            tmp.fontSize = 14f;
            tmp.color = new Color(0.6f, 0.6f, 0.6f, 1f);
            tmp.alignment = TextAlignmentOptions.Center;
        }

        void OnSaveClicked()
        {
            if (m_OriginManager == null)
                m_OriginManager = ServiceLocator.Get<LayoutOriginManager>();

            string layoutName = m_LayoutNameInput != null ? m_LayoutNameInput.text : null;
            if (string.IsNullOrWhiteSpace(layoutName))
            {
                layoutName = $"Layout_{DateTime.Now:yyyy-MM-dd_HHmm}";
            }

            if (m_OriginManager != null)
            {
                if (m_OriginManager.SaveCurrentLayout(layoutName, out var model))
                {
                    GameEvents.RaiseLayoutSaved(layoutName);
                    RefreshLayoutLists();
                }
            }
        }

        void OnNewLayoutClicked()
        {
            if (m_Registry == null)
                m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();
            if (m_OriginManager == null)
                m_OriginManager = ServiceLocator.Get<LayoutOriginManager>();

            if (m_Registry != null)
            {
                m_Registry.ClearAll();
            }

            if (m_OriginManager != null)
            {
                m_OriginManager.ResetOrigin();
            }

            GameEvents.RaiseToastRequested("Started new layout. Place furniture on the floor.");
            Hide();
        }

        void OnClearAllClicked()
        {
            if (m_Registry == null)
                m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();

            if (m_Registry != null)
            {
                int count = m_Registry.Count;
                m_Registry.ClearAll();
                GameEvents.RaiseToastRequested($"Cleared all {count} furniture items.");
            }
            Hide();
        }

        void OnSetOriginClicked()
        {
            if (m_OriginManager == null)
                m_OriginManager = ServiceLocator.Get<LayoutOriginManager>();

            var reticle = ServiceLocator.Get<PlacementReticle>();
            if (reticle != null && reticle.HasHit)
            {
                if (m_OriginManager != null)
                {
                    m_OriginManager.SetOriginPose(reticle.CurrentPose);
                    GameEvents.RaiseToastRequested("Layout origin anchored to floor reticle.");
                }
            }
            else
            {
                GameEvents.RaiseToastRequested("Aim reticle at a floor surface to set origin.");
            }
        }

        async void OnLoadRequested(LayoutHeader header, bool isPreset)
        {
            if (m_OriginManager == null)
                m_OriginManager = ServiceLocator.Get<LayoutOriginManager>();

            if (m_OriginManager == null)
            {
                Debug.LogError("[LayoutMenuPanel] LayoutOriginManager service unavailable.");
                return;
            }

            LayoutModel layout = isPreset
                ? LayoutStorage.LoadPreset(header.layoutId)
                : LayoutStorage.LoadLayout(header.layoutId);

            if (layout != null)
            {
                Hide();
                await m_OriginManager.LoadLayoutAsync(layout);
                GameEvents.RaiseLayoutLoaded(layout.layoutName);
            }
            else
            {
                GameEvents.RaiseToastRequested($"Failed to load layout '{header.layoutName}'.");
            }
        }

        void OnDeleteRequested(string layoutId)
        {
            if (LayoutStorage.DeleteLayout(layoutId))
            {
                GameEvents.RaiseToastRequested("Deleted layout.");
                PopulateSavedLayouts();
            }
        }
    }
}
