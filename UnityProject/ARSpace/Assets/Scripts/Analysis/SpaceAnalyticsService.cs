using System;
using System.Collections.Generic;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Furniture;
using ARSpace.Placement;

namespace ARSpace.Analysis
{
    public enum CirculationCompliance
    {
        Compliant,    // Circulation >= 45% (Meets accessibility, egress, and corporate guidelines)
        Constrained,  // Circulation 30% - 45% (Acceptable for dense project teams, tight aisles)
        NonCompliant  // Circulation < 30% (Severe congestion, violates fire code clearance)
    }

    public enum DensityHealth
    {
        Sparse,       // < 8 seats / 100 m² (Under-utilized commercial real estate)
        Optimal,      // 8 - 14 seats / 100 m² (Corporate workplace standard)
        HighDensity,  // 14 - 20 seats / 100 m² (High-density agile / tech team pods)
        Overcrowded   // > 20 seats / 100 m² (Excessive density, degraded workplace acoustics and comfort)
    }

    [Serializable]
    public class CategoryMetrics
    {
        public FurnitureCategory Category;
        public int ItemCount;
        public int SeatCount;
        public float FootprintSqM;
        public float PercentageOfTotalFurniture;
    }

    [Serializable]
    public class SpaceAnalyticsData
    {
        public float TotalFloorAreaSqM;
        public float TotalFloorAreaSqFt;
        public float TotalFootprintSqM;
        public float TotalFootprintSqFt;
        public float CirculationSqM;
        public float CirculationSqFt;
        public float FootprintRatio;    // 0.0 to 1.0
        public float CirculationRatio;  // 0.0 to 1.0
        public int TotalSeats;
        public float SeatingDensityPer100SqM;
        public int TotalFurnitureCount;
        public int ConflictCount;
        public CirculationCompliance CirculationCompliance;
        public DensityHealth DensityHealth;
        public List<CategoryMetrics> CategoryBreakdown = new List<CategoryMetrics>();

        public const float SqMToSqFt = 10.7639f;
    }

    /// <summary>
    /// Service providing real-time commercial real estate spatial analytics:
    /// - Floor utilization vs circulation egress ratios
    /// - Seating capacity and density per 100 m²
    /// - Functional workplace category breakdown
    /// - Regulatory compliance and workplace health scoring
    /// </summary>
    public class SpaceAnalyticsService : MonoBehaviour
    {
        PlacedObjectRegistry m_Registry;
        FloorAreaCalculator m_FloorCalculator;

        SpaceAnalyticsData m_CurrentMetrics = new SpaceAnalyticsData();

        public SpaceAnalyticsData CurrentMetrics => m_CurrentMetrics;

        public event Action<SpaceAnalyticsData> OnMetricsUpdated;

        void Awake()
        {
            ServiceLocator.Register(this);
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<SpaceAnalyticsService>();
        }

        void OnEnable()
        {
            GameEvents.ObjectPlaced += OnSceneChanged;
            GameEvents.ObjectRemoved += OnSceneChanged;
            GameEvents.FloorAreaUpdated += OnFloorAreaUpdated;
            GameEvents.LayoutLoaded += OnLayoutLoaded;
        }

        void OnDisable()
        {
            GameEvents.ObjectPlaced -= OnSceneChanged;
            GameEvents.ObjectRemoved -= OnSceneChanged;
            GameEvents.FloorAreaUpdated -= OnFloorAreaUpdated;
            GameEvents.LayoutLoaded -= OnLayoutLoaded;
        }

        void Start()
        {
            m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();
            m_FloorCalculator = ServiceLocator.Get<FloorAreaCalculator>();

            Recalculate();
        }

        void OnSceneChanged(GameObject go)
        {
            Recalculate();
        }

        void OnFloorAreaUpdated(float area)
        {
            Recalculate();
        }

        void OnLayoutLoaded(string layoutName)
        {
            Recalculate();
        }

        /// <summary>
        /// Recomputes all spatial utilization metrics across the active scene.
        /// </summary>
        public SpaceAnalyticsData Recalculate()
        {
            if (m_Registry == null)
                m_Registry = ServiceLocator.Get<PlacedObjectRegistry>();
            if (m_FloorCalculator == null)
                m_FloorCalculator = ServiceLocator.Get<FloorAreaCalculator>();

            float floorArea = m_FloorCalculator != null ? m_FloorCalculator.DeDuplicatedAreaSqM : 0f;
            float footprintArea = m_Registry != null ? m_Registry.CalculateTotalFootprintArea() : 0f;
            int seats = m_Registry != null ? m_Registry.CalculateTotalSeatingCapacity() : 0;
            int totalObjects = m_Registry != null ? m_Registry.Count : 0;

            IReadOnlyList<PlacedObject> objects = m_Registry != null ? m_Registry.AllObjects : Array.Empty<PlacedObject>();

            int conflicts = 0;
            var categoryMap = new Dictionary<FurnitureCategory, CategoryMetrics>();

            foreach (FurnitureCategory cat in Enum.GetValues(typeof(FurnitureCategory)))
            {
                categoryMap[cat] = new CategoryMetrics
                {
                    Category = cat,
                    ItemCount = 0,
                    SeatCount = 0,
                    FootprintSqM = 0f,
                    PercentageOfTotalFurniture = 0f
                };
            }

            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];
                if (obj == null) continue;

                if (obj.HasConflict) conflicts++;

                if (obj.Item != null)
                {
                    var cat = obj.Item.Category;
                    if (categoryMap.TryGetValue(cat, out var metrics))
                    {
                        metrics.ItemCount++;
                        metrics.SeatCount += obj.Item.SeatCount;
                        metrics.FootprintSqM += obj.Item.Footprint.x * obj.Item.Footprint.y;
                    }
                }
            }

            // Circulation area cannot be negative
            float circulationArea = Mathf.Max(0f, floorArea - footprintArea);

            float footprintRatio = floorArea > 0f ? Mathf.Clamp01(footprintArea / floorArea) : 0f;
            float circulationRatio = floorArea > 0f ? Mathf.Clamp01(circulationArea / floorArea) : 1f;
            float seatingDensity = floorArea > 0f ? (seats / floorArea) * 100f : 0f;

            // Compliance determination
            CirculationCompliance circComp = CirculationCompliance.Compliant;
            if (floorArea > 0f)
            {
                if (circulationRatio < 0.30f)
                    circComp = CirculationCompliance.NonCompliant;
                else if (circulationRatio < 0.45f)
                    circComp = CirculationCompliance.Constrained;
            }

            // Density health determination
            DensityHealth densityHealth = DensityHealth.Optimal;
            if (floorArea > 0f)
            {
                if (seatingDensity < 8f)
                    densityHealth = DensityHealth.Sparse;
                else if (seatingDensity <= 14f)
                    densityHealth = DensityHealth.Optimal;
                else if (seatingDensity <= 20f)
                    densityHealth = DensityHealth.HighDensity;
                else
                    densityHealth = DensityHealth.Overcrowded;
            }

            var breakdownList = new List<CategoryMetrics>();
            foreach (var kvp in categoryMap)
            {
                if (kvp.Value.ItemCount > 0)
                {
                    kvp.Value.PercentageOfTotalFurniture = footprintArea > 0f
                        ? (kvp.Value.FootprintSqM / footprintArea) * 100f
                        : 0f;
                    breakdownList.Add(kvp.Value);
                }
            }

            breakdownList.Sort((a, b) => b.FootprintSqM.CompareTo(a.FootprintSqM));

            // Populate data model
            m_CurrentMetrics = new SpaceAnalyticsData
            {
                TotalFloorAreaSqM = floorArea,
                TotalFloorAreaSqFt = floorArea * SpaceAnalyticsData.SqMToSqFt,
                TotalFootprintSqM = footprintArea,
                TotalFootprintSqFt = footprintArea * SpaceAnalyticsData.SqMToSqFt,
                CirculationSqM = circulationArea,
                CirculationSqFt = circulationArea * SpaceAnalyticsData.SqMToSqFt,
                FootprintRatio = footprintRatio,
                CirculationRatio = circulationRatio,
                TotalSeats = seats,
                SeatingDensityPer100SqM = seatingDensity,
                TotalFurnitureCount = totalObjects,
                ConflictCount = conflicts,
                CirculationCompliance = circComp,
                DensityHealth = densityHealth,
                CategoryBreakdown = breakdownList
            };

            OnMetricsUpdated?.Invoke(m_CurrentMetrics);
            return m_CurrentMetrics;
        }
    }
}
