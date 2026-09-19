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
    /// Furniture catalogue bottom sheet: category chips + a horizontally scrolling row of item cards,
    /// built at runtime from <see cref="CatalogService"/>'s <see cref="FurnitureDatabase"/>.
    /// Tapping a card raises <see cref="GameEvents.FurnitureSelected"/>.
    /// </summary>
    public class CatalogPanel : MonoBehaviour
    {
        [Header("Containers")]
        [SerializeField] RectTransform m_Content;
        [SerializeField] RectTransform m_ChipContent;
        [SerializeField] ScrollRect m_CardScroll;
        [SerializeField] TextMeshProUGUI m_CountText;

        [Header("Style (assigned by the scene builder)")]
        [SerializeField] Sprite m_RoundedSprite;

        const float CardWidth = 178f;
        const float CardHeight = 232f;

        static readonly FurnitureCategory[] s_CategoryOrder =
        {
            FurnitureCategory.Workstations, FurnitureCategory.Seating, FurnitureCategory.ConferenceTables,
            FurnitureCategory.ExecutiveCabins, FurnitureCategory.Reception, FurnitureCategory.Cafeteria,
            FurnitureCategory.Partitions, FurnitureCategory.Equipment, FurnitureCategory.Decor
        };

        class CardView
        {
            public FurnitureItem Item;
            public GameObject Root;
            public Image Background;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Subtitle;
        }

        class ChipView
        {
            public FurnitureCategory? Category;
            public Image Background;
            public TextMeshProUGUI Label;
        }

        readonly List<CardView> m_Cards = new List<CardView>();
        readonly List<ChipView> m_Chips = new List<ChipView>();
        FurnitureCategory? m_Filter;
        string m_SelectedId;

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
                Debug.LogWarning("[CatalogPanel] No CatalogService/FurnitureDatabase available — catalogue will be empty.");
                yield break;
            }

            Build(catalogService.Database);
        }

        void Build(FurnitureDatabase database)
        {
            if (m_Content == null)
            {
                Debug.LogError("[CatalogPanel] Card container not assigned.");
                return;
            }

            var present = new HashSet<FurnitureCategory>();
            foreach (var item in database.Items)
            {
                if (item == null || string.IsNullOrEmpty(item.Id))
                    continue;

                present.Add(item.Category);
                CreateCard(item);
            }

            if (m_ChipContent != null)
            {
                CreateChip(null, "All");
                foreach (var cat in s_CategoryOrder)
                {
                    if (present.Remove(cat))
                        CreateChip(cat, CategoryLabel(cat));
                }
                foreach (var leftover in present)
                    CreateChip(leftover, CategoryLabel(leftover));
            }

            ApplyFilter(null);
        }

        static string CategoryLabel(FurnitureCategory cat)
        {
            switch (cat)
            {
                case FurnitureCategory.Workstations: return "Workstations";
                case FurnitureCategory.Seating: return "Seating";
                case FurnitureCategory.ConferenceTables: return "Meeting";
                case FurnitureCategory.ExecutiveCabins: return "Executive";
                case FurnitureCategory.Reception: return "Reception";
                case FurnitureCategory.Cafeteria: return "Cafeteria";
                case FurnitureCategory.Partitions: return "Booths";
                case FurnitureCategory.Equipment: return "Equipment";
                case FurnitureCategory.Decor: return "Decor";
                default: return cat.ToString();
            }
        }

        // ── Chips ──────────────────────────────────────────────

        void CreateChip(FurnitureCategory? category, string label)
        {
            var go = new GameObject($"Chip_{label}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(m_ChipContent, false);

            var bg = go.GetComponent<Image>();
            UiStyle.Round(bg, m_RoundedSprite, 30f);

            var text = CreateLabel(rect, "Label", label, 24, FontStyles.Bold, TextAlignmentOptions.Center);
            var textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(26, 0);
            textRect.offsetMax = new Vector2(-26, 0);
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;

            // Width follows the label so short and long category names both look balanced.
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredHeight = 60;
            layout.preferredWidth = Mathf.Max(96f, text.GetPreferredValues(label).x + 52f);

            var view = new ChipView { Category = category, Background = bg, Label = text };
            m_Chips.Add(view);

            go.GetComponent<Button>().onClick.AddListener(() => ApplyFilter(category));
        }

        void ApplyFilter(FurnitureCategory? category)
        {
            m_Filter = category;

            int visible = 0;
            foreach (var card in m_Cards)
            {
                bool show = !category.HasValue || card.Item.Category == category.Value;
                card.Root.SetActive(show);
                if (show) visible++;
            }

            foreach (var chip in m_Chips)
            {
                bool active = chip.Category == category;
                chip.Background.color = active ? UiStyle.Primary : UiStyle.Chip;
                chip.Label.color = active ? Color.white : UiStyle.TextMuted;
            }

            if (m_CountText != null)
                m_CountText.text = visible == 1 ? "1 item" : $"{visible} items";

            if (m_CardScroll != null)
                m_CardScroll.horizontalNormalizedPosition = 0f;
        }

        // ── Cards ──────────────────────────────────────────────

        void CreateCard(FurnitureItem item)
        {
            var go = new GameObject(item.DisplayName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rect = (RectTransform)go.transform;
            rect.SetParent(m_Content, false);

            // HorizontalLayoutGroup ignores plain sizeDelta for non-expanding children.
            var layout = go.GetComponent<LayoutElement>();
            layout.preferredWidth = CardWidth;
            layout.preferredHeight = CardHeight;

            var bg = go.GetComponent<Image>();
            UiStyle.Round(bg, m_RoundedSprite, 30f);
            bg.color = UiStyle.Card;

            // Light thumbnail tile so the studio-lit renders read clearly on the dark card.
            var tileGo = new GameObject("Tile", typeof(RectTransform), typeof(Image));
            var tileRect = (RectTransform)tileGo.transform;
            tileRect.SetParent(rect, false);
            tileRect.anchorMin = new Vector2(0f, 0f);
            tileRect.anchorMax = new Vector2(1f, 1f);
            tileRect.offsetMin = new Vector2(10, 84);
            tileRect.offsetMax = new Vector2(-10, -10);
            var tile = tileGo.GetComponent<Image>();
            UiStyle.Round(tile, m_RoundedSprite, 22f);
            tile.color = UiStyle.Tile;
            tile.raycastTarget = false;

            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            var iconRect = (RectTransform)iconGo.transform;
            iconRect.SetParent(tileRect, false);
            iconRect.anchorMin = Vector2.zero;
            iconRect.anchorMax = Vector2.one;
            iconRect.offsetMin = new Vector2(6, 6);
            iconRect.offsetMax = new Vector2(-6, -6);
            var icon = iconGo.GetComponent<Image>();
            icon.sprite = item.Thumbnail;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            icon.enabled = item.Thumbnail != null;

            var name = CreateLabel(rect, "Name", item.DisplayName, 22, FontStyles.Bold, TextAlignmentOptions.Left);
            var nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 0f);
            nameRect.anchorMax = new Vector2(1f, 0f);
            nameRect.pivot = new Vector2(0.5f, 0f);
            nameRect.sizeDelta = new Vector2(-28, 34);
            nameRect.anchoredPosition = new Vector2(0, 40);
            name.enableWordWrapping = false;
            name.overflowMode = TextOverflowModes.Ellipsis;

            var sub = CreateLabel(rect, "Subtitle", Describe(item), 18, FontStyles.Normal, TextAlignmentOptions.Left);
            var subRect = sub.rectTransform;
            subRect.anchorMin = new Vector2(0f, 0f);
            subRect.anchorMax = new Vector2(1f, 0f);
            subRect.pivot = new Vector2(0.5f, 0f);
            subRect.sizeDelta = new Vector2(-28, 28);
            subRect.anchoredPosition = new Vector2(0, 12);
            sub.enableWordWrapping = false;
            sub.overflowMode = TextOverflowModes.Ellipsis;

            string id = item.Id;
            go.GetComponent<Button>().onClick.AddListener(() => GameEvents.RaiseFurnitureSelected(id));

            m_Cards.Add(new CardView { Item = item, Root = go, Background = bg, Name = name, Subtitle = sub });
            ApplyCardState(m_Cards[m_Cards.Count - 1], false);
        }

        static string Describe(FurnitureItem item)
        {
            Vector2 fp = item.Footprint;
            string size = $"{fp.x:0.0} × {fp.y:0.0} m";
            return item.SeatCount > 0 ? $"{item.SeatCount} seat{(item.SeatCount > 1 ? "s" : "")} · {size}" : size;
        }

        static TextMeshProUGUI CreateLabel(Transform parent, string name, string text, float size, FontStyles style, TextAlignmentOptions align)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = size;
            tmp.fontStyle = style;
            tmp.alignment = align;
            tmp.color = UiStyle.TextPrimary;
            tmp.raycastTarget = false;
            return tmp;
        }

        void ApplyCardState(CardView card, bool selected)
        {
            card.Background.color = selected ? UiStyle.Primary : UiStyle.Card;
            card.Name.color = UiStyle.TextPrimary;
            card.Subtitle.color = selected ? new Color(1f, 1f, 1f, 0.9f) : UiStyle.TextMuted;
        }

        void OnFurnitureSelected(string itemId)
        {
            m_SelectedId = itemId;
            foreach (var card in m_Cards)
                ApplyCardState(card, card.Item.Id == itemId);
        }

        void OnPlacementCancelled()
        {
            m_SelectedId = null;
            foreach (var card in m_Cards)
                ApplyCardState(card, false);
        }
    }
}
