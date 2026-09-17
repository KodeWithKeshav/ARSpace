using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using ARSpace.AR;
using ARSpace.Core;

namespace ARSpace.Analysis
{
    /// <summary>
    /// Computes accurate, de-duplicated floor area from detected AR planes.
    ///
    /// Multiple overlapping or sub-divided horizontal planes frequently occur during
    /// AR room mapping. Standard plane area summation double-counts shared regions.
    /// FloorAreaCalculator projects all floor plane boundaries onto a 2D spatial occupancy grid
    /// to calculate true non-overlapping usable room square meterage.
    /// </summary>
    public class FloorAreaCalculator : MonoBehaviour
    {
        [Header("Grid Sampling Configuration")]
        [Tooltip("Grid resolution in metres. 0.25 m = 16 sample cells per m², providing sub-percent accuracy.")]
        [SerializeField]
        float m_GridCellSize = 0.25f;

        [Tooltip("Minimum plane area in m² to be included in floor calculations (filters noise fragments).")]
        [SerializeField]
        float m_MinPlaneArea = 0.5f;

        [Header("References")]
        [SerializeField]
        PlaneDetectionManager m_PlaneDetectionManager;

        float m_DeDuplicatedAreaSqM = 0f;
        float m_RawPlaneAreaSum = 0f;
        int m_ActiveFloorPlaneCount = 0;

        public float DeDuplicatedAreaSqM => m_DeDuplicatedAreaSqM;
        public float RawPlaneAreaSum => m_RawPlaneAreaSum;
        public float OverlapRatio => m_RawPlaneAreaSum > 0f ? Mathf.Max(0f, (m_RawPlaneAreaSum - m_DeDuplicatedAreaSqM) / m_RawPlaneAreaSum) : 0f;
        public int ActiveFloorPlaneCount => m_ActiveFloorPlaneCount;

        void Awake()
        {
            ServiceLocator.Register(this);

            if (m_PlaneDetectionManager == null)
            {
                m_PlaneDetectionManager = FindFirstObjectByType<PlaneDetectionManager>();
            }
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<FloorAreaCalculator>();
        }

        void OnEnable()
        {
            if (m_PlaneDetectionManager != null)
            {
                m_PlaneDetectionManager.FloorPlaneAdded += OnFloorPlaneChanged;
                m_PlaneDetectionManager.FloorPlaneUpdated += OnFloorPlaneChanged;
                m_PlaneDetectionManager.FloorPlaneRemoved += OnFloorPlaneChanged;
            }
        }

        void OnDisable()
        {
            if (m_PlaneDetectionManager != null)
            {
                m_PlaneDetectionManager.FloorPlaneAdded -= OnFloorPlaneChanged;
                m_PlaneDetectionManager.FloorPlaneUpdated -= OnFloorPlaneChanged;
                m_PlaneDetectionManager.FloorPlaneRemoved -= OnFloorPlaneChanged;
            }
        }

        void Start()
        {
            RecalculateArea();
        }

        void OnFloorPlaneChanged(ARPlane plane)
        {
            RecalculateArea();
        }

        /// <summary>
        /// Recalculates total non-overlapping floor area across all tracked floor planes.
        /// </summary>
        public void RecalculateArea()
        {
            if (m_PlaneDetectionManager == null)
            {
                m_PlaneDetectionManager = ServiceLocator.Get<PlaneDetectionManager>();
                if (m_PlaneDetectionManager == null)
                    m_PlaneDetectionManager = FindFirstObjectByType<PlaneDetectionManager>();
            }

            if (m_PlaneDetectionManager == null || m_PlaneDetectionManager.FloorPlanes == null)
            {
                m_DeDuplicatedAreaSqM = 0f;
                m_RawPlaneAreaSum = 0f;
                m_ActiveFloorPlaneCount = 0;
                return;
            }

            IReadOnlyList<ARPlane> planes = m_PlaneDetectionManager.FloorPlanes;
            m_ActiveFloorPlaneCount = planes.Count;

            if (planes.Count == 0)
            {
                m_DeDuplicatedAreaSqM = 0f;
                m_RawPlaneAreaSum = 0f;
                GameEvents.RaiseFloorAreaUpdated(0f);
                return;
            }

            // 1. Extract world-space 2D boundary polygons and compute raw summation
            var worldPolygons = new List<Vector2[]>();
            float rawSum = 0f;

            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;

            for (int i = 0; i < planes.Count; i++)
            {
                var plane = planes[i];
                if (plane == null) continue;

                var boundary = plane.boundary;
                if (!boundary.IsCreated || boundary.Length < 3) continue;

                Transform planeTransform = plane.transform;
                Vector2[] worldPoints = new Vector2[boundary.Length];

                for (int p = 0; p < boundary.Length; p++)
                {
                    Vector2 localPoint = boundary[p];
                    Vector3 worldPoint3 = planeTransform.TransformPoint(new Vector3(localPoint.x, 0f, localPoint.y));
                    Vector2 worldPoint = new Vector2(worldPoint3.x, worldPoint3.z);
                    worldPoints[p] = worldPoint;

                    if (worldPoint.x < minX) minX = worldPoint.x;
                    if (worldPoint.x > maxX) maxX = worldPoint.x;
                    if (worldPoint.y < minZ) minZ = worldPoint.y;
                    if (worldPoint.y > maxZ) maxZ = worldPoint.y;
                }

                float planeArea = CalculateShoelaceArea(worldPoints);
                if (planeArea >= m_MinPlaneArea)
                {
                    rawSum += planeArea;
                    worldPolygons.Add(worldPoints);
                }
            }

            m_RawPlaneAreaSum = rawSum;

            // If no valid polygons pass filter, exit early
            if (worldPolygons.Count == 0)
            {
                m_DeDuplicatedAreaSqM = 0f;
                GameEvents.RaiseFloorAreaUpdated(0f);
                return;
            }

            // 2. Sample 2D Spatial Occupancy Grid to deduplicate overlaps
            float cellSize = Mathf.Max(0.1f, m_GridCellSize);
            int cols = Mathf.Clamp(Mathf.CeilToInt((maxX - minX) / cellSize), 1, 400);
            int rows = Mathf.Clamp(Mathf.CeilToInt((maxZ - minZ) / cellSize), 1, 400);

            int occupiedCells = 0;

            for (int c = 0; c < cols; c++)
            {
                float sampleX = minX + (c + 0.5f) * cellSize;
                for (int r = 0; r < rows; r++)
                {
                    float sampleZ = minZ + (r + 0.5f) * cellSize;
                    Vector2 samplePt = new Vector2(sampleX, sampleZ);

                    // Check if sample point is inside ANY detected floor plane polygon
                    for (int p = 0; p < worldPolygons.Count; p++)
                    {
                        if (IsPointInPolygon(samplePt, worldPolygons[p]))
                        {
                            occupiedCells++;
                            break; // Count cell at most once regardless of how many planes cover it
                        }
                    }
                }
            }

            m_DeDuplicatedAreaSqM = occupiedCells * (cellSize * cellSize);

            // Notify systems of verified non-overlapping usable floor area
            GameEvents.RaiseFloorAreaUpdated(m_DeDuplicatedAreaSqM);
        }

        static float CalculateShoelaceArea(Vector2[] points)
        {
            float area = 0f;
            int n = points.Length;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                area += points[i].x * points[j].y;
                area -= points[j].x * points[i].y;
            }
            return Mathf.Abs(area) * 0.5f;
        }

        static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            int count = polygon.Length;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}
