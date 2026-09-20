using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.AR
{
    /// <summary>
    /// Floor-area tool: tap the corners of a room or zone on the floor and it outlines the shape and reports the
    /// area and perimeter. Uses the manual floor, so it needs no ARCore plane detection. Tapping the first corner
    /// again (or after twelve corners) starts a new shape.
    /// </summary>
    public class AreaTool : MonoBehaviour
    {
        [SerializeField] Material m_LineMaterial;
        [Tooltip("Where the result is written (a text chip in the tools tray).")]
        [SerializeField] TextMeshProUGUI m_Readout;

        public static bool IsActive { get; private set; }

        const int MaxPoints = 12;

        readonly List<Vector3> m_Points = new List<Vector3>();
        readonly List<LineRenderer> m_Dots = new List<LineRenderer>();
        LineRenderer m_Outline;

        static readonly Color s_PointColor = new Color(1f, 0.42f, 0f, 1f);
        static readonly Color s_LineColor = new Color(0.45f, 1f, 0.6f, 1f);

        void Awake()
        {
            ServiceLocator.Register(this);
            m_Outline = CreateLine("AreaOutline", 0, false, s_LineColor, 0.014f);
        }

        void OnEnable() => GameEvents.FurnitureSelected += OnFurnitureSelected;
        void OnDisable() => GameEvents.FurnitureSelected -= OnFurnitureSelected;

        void OnFurnitureSelected(string itemId) => SetActive(false);

        void OnDestroy()
        {
            IsActive = false;
            ServiceLocator.Unregister<AreaTool>();
        }

        public void Toggle() => SetActive(!IsActive);

        public void SetActive(bool active)
        {
            if (active == IsActive)
                return;

            IsActive = active;
            Clear();

            if (active)
            {
                GameEvents.RaisePlacementCancelled();
                var selection = ServiceLocator.Get<ObjectSelectionService>();
                if (selection != null)
                    selection.Deselect();
                GameEvents.RaiseToastRequested("Area: tap the corners of the space on the floor");
            }
        }

        public void AddPoint(Vector2 screenPoint)
        {
            if (!ServiceLocator.TryGet(out ManualFloor floor) || !floor.HasFloor)
            {
                GameEvents.RaiseToastRequested("Floor not ready yet — hold the phone steady for a moment.");
                return;
            }

            if (!floor.GetPoint(screenPoint, out Vector3 point))
                return;

            if (m_Points.Count >= MaxPoints)
                Clear();

            m_Points.Add(PlacementSnap.Apply(new Vector3(point.x, floor.FloorY + 0.012f, point.z)));
            Refresh();
        }

        public void Clear()
        {
            m_Points.Clear();
            Refresh();
        }

        void Refresh()
        {
            while (m_Dots.Count < m_Points.Count)
                m_Dots.Add(CreateLine("AreaDot" + m_Dots.Count, 20, true, s_PointColor, 0.012f));

            for (int i = 0; i < m_Dots.Count; i++)
            {
                bool has = i < m_Points.Count;
                m_Dots[i].enabled = has;
                if (has)
                    SetCircle(m_Dots[i], m_Points[i], 0.05f);
            }

            m_Outline.enabled = m_Points.Count >= 2;
            m_Outline.loop = m_Points.Count >= 3;
            m_Outline.positionCount = m_Points.Count;
            for (int i = 0; i < m_Points.Count; i++)
                m_Outline.SetPosition(i, m_Points[i]);

            if (m_Readout == null)
                return;

            if (m_Points.Count < 3)
            {
                m_Readout.text = IsActive ? "Area: tap 3 or more corners" : string.Empty;
                return;
            }

            float area = 0f, perimeter = 0f;
            for (int i = 0; i < m_Points.Count; i++)
            {
                Vector3 a = m_Points[i];
                Vector3 b = m_Points[(i + 1) % m_Points.Count];
                area += a.x * b.z - b.x * a.z;
                perimeter += new Vector2(b.x - a.x, b.z - a.z).magnitude;
            }
            area = Mathf.Abs(area) * 0.5f;
            m_Readout.text = $"{area:0.0} m²  ·  {area * 10.7639f:0} sq ft  ·  edge {perimeter:0.0} m";
        }

        LineRenderer CreateLine(string name, int positions, bool loop, Color color, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.positionCount = positions;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.numCapVertices = 4;
            lr.alignment = LineAlignment.View;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = color;
            lr.endColor = color;
            if (m_LineMaterial != null)
                lr.sharedMaterial = m_LineMaterial;
            lr.enabled = false;
            return lr;
        }

        static void SetCircle(LineRenderer lr, Vector3 center, float radius)
        {
            int n = lr.positionCount;
            for (int i = 0; i < n; i++)
            {
                float a = (i / (float)n) * Mathf.PI * 2f;
                lr.SetPosition(i, center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
            }
        }
    }
}
