using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using ARSpace.Analysis;
using ARSpace.Core;

namespace ARSpace.UI
{
    /// <summary>
    /// Executive CRE dashboard panel presenting live workplace metrics:
    /// - Usable floor area and circulation egress ratio
    /// - Seating capacity and density per 100 m²
    /// - Regulatory compliance badges and clearance conflict alerts
    /// - Functional workplace category breakdown
    /// - Metric (m²) and Imperial (sq ft) unit toggling
    /// </summary>
    public class AnalyticsPanel : MonoBehaviour
    {
        [Header("Root Panel")]
        [SerializeField]
        GameObject m_PanelRoot;

        [Header("Buttons & Navigation")]
        [SerializeField]
        Button m_CloseButton;

        [SerializeField]
        Button m_UnitToggleButton;

        [SerializeField]
        TextMeshProUGUI m_UnitToggleText;

        [Header("Metrics Text Fields")]
        [SerializeField]
        TextMeshProUGUI m_FloorAreaText;

        [SerializeField]
        TextMeshProUGUI m_FootprintAreaText;

        [SerializeField]
        TextMeshProUGUI m_CirculationAreaText;

        [SerializeField]
        TextMeshProUGUI m_SeatingText;

        [SerializeField]
        TextMeshProUGUI m_DensityText;

        [Header("Compliance Badges")]
        [SerializeField]
        Image m_CirculationBadgeBg;

        [SerializeField]
        TextMeshProUGUI m_CirculationBadgeText;

        [SerializeField]
        Image m_DensityBadgeBg;

        [SerializeField]
        TextMeshProUGUI m_DensityBadgeText;

        [Header("Conflict Notice")]
        [SerializeField]
        GameObject m_ConflictNoticeRoot;

        [SerializeField]
        TextMeshProUGUI m_ConflictNoticeText;

        [Header("Category Breakdown Container")]
        [SerializeField]
        Transform m_CategoryListContainer;

        [SerializeField]
        GameObject m_CategoryRowPrefab;

        SpaceAnalyticsService m_AnalyticsService;
        bool m_UseImperialUnits = false;

        static readonly Color s_ColorGreen = new Color(0.13f, 0.77f, 0.36f, 1.0f);
        static readonly Color s_ColorAmber = new Color(0.96f, 0.65f, 0.14f, 1.0f);
        static readonly Color s_ColorRed = new Color(0.92f, 0.26f, 0.21f, 1.0f);
        static readonly Color s_ColorBlue = new Color(0.12f, 0.53f, 0.90f, 1.0f);

        void Awake()
        {
            if (m_PanelRoot == null)
            {
                m_PanelRoot = gameObject;
            }

            if (m_CloseButton != null)
                m_CloseButton.onClick.AddListener(Hide);

            if (m_UnitToggleButton != null)
                m_UnitToggleButton.onClick.AddListener(ToggleUnits);

            UpdateUnitToggleLabel();
            SetVisible(false);
        }

        void OnEnable()
        {
            GameEvents.PresentationModeToggled += OnPresentationModeToggled;
        }

        void OnDisable()
        {
            GameEvents.PresentationModeToggled -= OnPresentationModeToggled;

            if (m_AnalyticsService != null)
            {
                m_AnalyticsService.OnMetricsUpdated -= OnMetricsUpdated;
            }
        }

        void Start()
        {
            m_AnalyticsService = ServiceLocator.Get<SpaceAnalyticsService>();
            if (m_AnalyticsService != null)
            {
                m_AnalyticsService.OnMetricsUpdated += OnMetricsUpdated;
                UpdateUI(m_AnalyticsService.CurrentMetrics);
            }
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
                    app.RequestStateChange(AppState.AnalysisOpen);
                }
                else if (app.CurrentState == AppState.AnalysisOpen)
                {
                    app.ReturnToPreviousState();
                }
            }

            if (visible && m_AnalyticsService != null)
            {
                UpdateUI(m_AnalyticsService.Recalculate());
            }
        }

        void ToggleUnits()
        {
            m_UseImperialUnits = !m_UseImperialUnits;
            UpdateUnitToggleLabel();

            if (m_AnalyticsService != null)
            {
                UpdateUI(m_AnalyticsService.CurrentMetrics);
            }
        }

        void UpdateUnitToggleLabel()
        {
            if (m_UnitToggleText != null)
            {
                m_UnitToggleText.text = m_UseImperialUnits ? "Unit: Imperial (sq ft)" : "Unit: Metric (m²)";
            }
        }

        void OnPresentationModeToggled(bool presenting)
        {
            if (presenting)
            {
                Hide();
            }
        }

        void OnMetricsUpdated(SpaceAnalyticsData data)
        {
            if (m_PanelRoot != null && m_PanelRoot.activeSelf)
            {
                UpdateUI(data);
            }
        }

        void UpdateUI(SpaceAnalyticsData data)
        {
            if (data == null) return;

            string areaUnit = m_UseImperialUnits ? "sq ft" : "m²";
            float floorArea = m_UseImperialUnits ? data.TotalFloorAreaSqFt : data.TotalFloorAreaSqM;
            float footprintArea = m_UseImperialUnits ? data.TotalFootprintSqFt : data.TotalFootprintSqM;
            float circulationArea = m_UseImperialUnits ? data.CirculationSqFt : data.CirculationSqM;

            // 1. Floor Area
            if (m_FloorAreaText != null)
            {
                m_FloorAreaText.text = $"{floorArea:F1} {areaUnit}";
            }

            // 2. Footprint Area
            if (m_FootprintAreaText != null)
            {
                m_FootprintAreaText.text = $"{footprintArea:F1} {areaUnit} ({data.FootprintRatio * 100f:F1}%)";
            }

            // 3. Circulation Area
            if (m_CirculationAreaText != null)
            {
                m_CirculationAreaText.text = $"{circulationArea:F1} {areaUnit} ({data.CirculationRatio * 100f:F1}%)";
            }

            // 4. Seating & Density
            if (m_SeatingText != null)
            {
                m_SeatingText.text = $"{data.TotalSeats} Seats ({data.TotalFurnitureCount} Items)";
            }

            if (m_DensityText != null)
            {
                m_DensityText.text = $"{data.SeatingDensityPer100SqM:F1} seats / 100 m²";
            }

            // 5. Circulation Compliance Badge
            UpdateCirculationBadge(data.CirculationCompliance);

            // 6. Density Health Badge
            UpdateDensityBadge(data.DensityHealth);

            // 7. Clearance Conflict Banner
            if (m_ConflictNoticeRoot != null)
            {
                bool hasConflicts = data.ConflictCount > 0;
                m_ConflictNoticeRoot.SetActive(hasConflicts);
                if (hasConflicts && m_ConflictNoticeText != null)
                {
                    m_ConflictNoticeText.text = $"⚠️ {data.ConflictCount} items have overlapping clearance margins!";
                }
            }

            // 8. Category Breakdown
            UpdateCategoryBreakdown(data.CategoryBreakdown, areaUnit);
        }

        void UpdateCirculationBadge(CirculationCompliance compliance)
        {
            if (m_CirculationBadgeText == null) return;

            switch (compliance)
            {
                case CirculationCompliance.Compliant:
                    m_CirculationBadgeText.text = "COMPLIANT (≥45%)";
                    if (m_CirculationBadgeBg != null) m_CirculationBadgeBg.color = s_ColorGreen;
                    break;
                case CirculationCompliance.Constrained:
                    m_CirculationBadgeText.text = "CONSTRAINED (30-45%)";
                    if (m_CirculationBadgeBg != null) m_CirculationBadgeBg.color = s_ColorAmber;
                    break;
                case CirculationCompliance.NonCompliant:
                    m_CirculationBadgeText.text = "NON-COMPLIANT (<30%)";
                    if (m_CirculationBadgeBg != null) m_CirculationBadgeBg.color = s_ColorRed;
                    break;
            }
        }

        void UpdateDensityBadge(DensityHealth density)
        {
            if (m_DensityBadgeText == null) return;

            switch (density)
            {
                case DensityHealth.Sparse:
                    m_DensityBadgeText.text = "SPARSE (<8/100m²)";
                    if (m_DensityBadgeBg != null) m_DensityBadgeBg.color = s_ColorBlue;
                    break;
                case DensityHealth.Optimal:
                    m_DensityBadgeText.text = "OPTIMAL (8-14/100m²)";
                    if (m_DensityBadgeBg != null) m_DensityBadgeBg.color = s_ColorGreen;
                    break;
                case DensityHealth.HighDensity:
                    m_DensityBadgeText.text = "HIGH DENSITY (14-20)";
                    if (m_DensityBadgeBg != null) m_DensityBadgeBg.color = s_ColorAmber;
                    break;
                case DensityHealth.Overcrowded:
                    m_DensityBadgeText.text = "OVERCROWDED (>20)";
                    if (m_DensityBadgeBg != null) m_DensityBadgeBg.color = s_ColorRed;
                    break;
            }
        }

        void UpdateCategoryBreakdown(List<CategoryMetrics> breakdown, string areaUnit)
        {
            if (m_CategoryListContainer == null) return;

            // Clear old children
            for (int i = m_CategoryListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(m_CategoryListContainer.GetChild(i).gameObject);
            }

            if (breakdown == null || breakdown.Count == 0)
            {
                CreateFallbackCategoryRow("No furniture placed", "", 0);
                return;
            }

            for (int i = 0; i < breakdown.Count; i++)
            {
                var cat = breakdown[i];
                float area = m_UseImperialUnits ? cat.FootprintSqM * SpaceAnalyticsData.SqMToSqFt : cat.FootprintSqM;
                string details = $"{cat.ItemCount} items • {cat.SeatCount} seats • {area:F1} {areaUnit} ({cat.PercentageOfTotalFurniture:F0}%)";
                CreateFallbackCategoryRow(cat.Category.ToString(), details, i);
            }
        }

        void CreateFallbackCategoryRow(string title, string details, int index)
        {
            GameObject row = new GameObject($"Row_{title}", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(m_CategoryListContainer, false);

            var titleGo = new GameObject("CatTitle", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(row.transform, false);
            var titleTmp = titleGo.GetComponent<TextMeshProUGUI>();
            titleTmp.text = title;
            titleTmp.fontSize = 14f;
            titleTmp.fontStyle = FontStyles.Bold;
            titleTmp.color = Color.white;

            if (!string.IsNullOrEmpty(details))
            {
                var detailGo = new GameObject("CatDetails", typeof(RectTransform), typeof(TextMeshProUGUI));
                detailGo.transform.SetParent(row.transform, false);
                var detailTmp = detailGo.GetComponent<TextMeshProUGUI>();
                detailTmp.text = details;
                detailTmp.fontSize = 13f;
                detailTmp.alignment = TextAlignmentOptions.Right;
                detailTmp.color = new Color(0.7f, 0.75f, 0.8f, 1f);
            }
        }
    }
}
