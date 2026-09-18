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

        [SerializeField] Color m_NormalColor = new Color(1f, 1f, 1f, 0.9f);
        [SerializeField] Color m_SelectedColor = new Color(1.0f, 0.42f, 0.0f, 1f);

        readonly List<(string id, Image bg)> m_Buttons = new List<(string, Image)>();

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
            rect.sizeDelta = new Vector2(140, 140);

            // HorizontalLayoutGroup on the content container ignores plain sizeDelta for
            // non-expanding children, so a LayoutElement is required to hold the button size.
            var layoutElement = go.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 140;
            layoutElement.preferredHeight = 140;

            var bg = go.GetComponent<Image>();
            bg.color = m_NormalColor;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(rect, false);
            iconRect.anchorMin = new Vector2(0.08f, 0.30f);
            iconRect.anchorMax = new Vector2(0.92f, 0.95f);
            iconRect.offsetMin = Vector2.zero;
            iconRect.offsetMax = Vector2.zero;
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = item.Thumbnail;
            icon.preserveAspect = true;

            var labelGo = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)labelGo.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 0.30f);
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;
            var label = labelGo.AddComponent<TextMeshProUGUI>();
            label.text = item.DisplayName;
            label.fontSize = 20;
            label.alignment = TextAlignmentOptions.Center;
            label.color = Color.black;
            label.enableWordWrapping = true;

            var button = go.GetComponent<Button>();
            string id = item.Id;
            button.onClick.AddListener(() => GameEvents.RaiseFurnitureSelected(id));

            m_Buttons.Add((id, bg));
        }

        void OnFurnitureSelected(string itemId)
        {
            foreach (var (id, bg) in m_Buttons)
            {
                if (bg != null)
                    bg.color = id == itemId ? m_SelectedColor : m_NormalColor;
            }
        }

        void OnPlacementCancelled()
        {
            foreach (var (_, bg) in m_Buttons)
            {
                if (bg != null)
                    bg.color = m_NormalColor;
            }
        }
    }
}
