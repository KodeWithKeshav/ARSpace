using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.Core;
using ARSpace.Furniture;

namespace ARSpace.UI
{
    /// <summary>
    /// Builds the furniture catalogue drawer at runtime from <see cref="CatalogService"/>'s
    /// <see cref="FurnitureDatabase"/> and raises <see cref="GameEvents.FurnitureSelected"/>
    /// when an item button is tapped. Highlights the currently selected item.
    /// </summary>
    public class CatalogPanel : MonoBehaviour
    {
        [SerializeField] RectTransform m_Content;

        [Header("Card Style (assigned by the scene builder)")]
        [Tooltip("Rounded-corner sprite used for every card background. Runtime code cannot load Editor built-in resources itself.")]
        [SerializeField] Sprite m_CardSprite;

        [SerializeField] Color m_NormalColor = new Color(0.14f, 0.15f, 0.18f, 0.92f);
        [SerializeField] Color m_SelectedColor = new Color(1.0f, 0.42f, 0.0f, 1f);
        [SerializeField] Color m_NormalTextColor = new Color(0.92f, 0.93f, 0.95f, 1f);
        [SerializeField] Color m_SelectedTextColor = Color.white;

        readonly List<(string id, Image bg, TextMeshProUGUI label)> m_Buttons = new List<(string, Image, TextMeshProUGUI)>();

        void OnEnable()
        {
            GameEvents.FurnitureSelected += OnFurnitureSelected;
            GameEvents.PlacementCancelled += OnPlacementCancelled;
        }

        void OnDisable()
        {
            GameEvents.FurnitureSelected -= OnFurnitureSelected;
            GameEvents.PlacementCancelled -= OnPlacementCancelled;
        }

        IEnumerator Start()
        {
            // Wait a frame so every service has registered itself in Awake() first.
            yield return null;

            CatalogService catalogService = ServiceLocator.Get<CatalogService>();
            if (catalogService == null || catalogService.Database == null)
            {
                Debug.LogWarning("[CatalogPanel] No CatalogService/FurnitureDatabase available — catalogue drawer will be empty.");
                yield break;
            }

            BuildButtons(catalogService.Database);
        }

        void BuildButtons(FurnitureDatabase database)
        {
            if (m_Content == null)
            {
                Debug.LogError("[CatalogPanel] Content container not assigned.");
                return;
            }

            foreach (var item in database.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id))
                    continue;

                CreateButton(item);
            }
        }

        void CreateButton(FurnitureItem item)
        {
            var go = new GameObject(item.DisplayName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(m_Content, false);
            rect.sizeDelta = new Vector2(150, 150);

            // HorizontalLayoutGroup on the content container ignores plain sizeDelta for
            // non-expanding children, so a LayoutElement is required to hold the button size.
            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 150;
            layoutElement.preferredHeight = 150;

            var bg = go.GetComponent<Image>();
            bg.color = m_NormalColor;
            if (m_CardSprite != null)
            {
                bg.sprite = m_CardSprite;
                bg.type = Image.Type.Sliced;
            }

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = new Vector2(0.14f, 0.34f);
            iconRect.anchorMax = new Vector2(0.86f, 0.90f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = item.Thumbnail;
            icon.preserveAspect = true;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = new Vector2(0.04f, 0.06f);
            labelRect.anchorMax = new Vector2(0.96f, 0.30f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = item.DisplayName;
            label.fontSize = 19;
            label.alignment = TextAlignmentOptions.Center;
            label.color = m_NormalTextColor;
            label.enableWordWrapping = true;
            label.overflowMode = TextOverflowModes.Ellipsis;

            var button = go.GetComponent<Button>();
            string id = item.Id;
            button.onClick.AddListener(() => GameEvents.RaiseFurnitureSelected(id));

            m_Buttons.Add((id, bg, label));
        }

        void OnFurnitureSelected(string itemId)
        {
            foreach (var (id, bg, label) in m_Buttons)
            {
                bool selected = id == itemId;
                if (bg != null) bg.color = selected ? m_SelectedColor : m_NormalColor;
                if (label != null) label.color = selected ? m_SelectedTextColor : m_NormalTextColor;
            }
        }

        void OnPlacementCancelled()
        {
            foreach (var (_, bg, label) in m_Buttons)
            {
                if (bg != null) bg.color = m_NormalColor;
                if (label != null) label.color = m_NormalTextColor;
            }
        }
    }
}
