using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.AR
{
    /// <summary>
    /// Room measuring tool: switch it on, tap two points on the floor and it draws a line between them with the
    /// distance in metres and feet. Uses the manual floor, so it needs no ARCore plane detection. Tapping a third
    /// time starts a new measurement.
    /// </summary>
    public class MeasureTool : MonoBehaviour
    {
        [SerializeField] Material m_LineMaterial;
        [SerializeField] TextMeshProUGUI m_Label;
        [Tooltip("Container moved over the measured line (label plus its background). Defaults to the label itself.")]
        [SerializeField] RectTransform m_LabelRoot;
        [SerializeField] RectTransform m_LabelParent;

        public static bool IsActive { get; private set; }
        public int PointCount => m_Points.Count;

        readonly List<Vector3> m_Points = new List<Vector3>();
        LineRenderer m_Segment;
        readonly LineRenderer[] m_Dots = new LineRenderer[2];
        Camera m_Camera;

        static readonly Color s_PointColor = new Color(1f, 0.42f, 0f, 1f);
        static readonly Color s_LineColor = new Color(0.35f, 0.85f, 1f, 1f);

        void Awake()
        {
            ServiceLocator.Register(this);

            m_Segment = CreateLine("MeasureSegment", 2, false, s_LineColor, 0.014f);
            m_Dots[0] = CreateLine("MeasureDotA", 24, true, s_PointColor, 0.014f);
            m_Dots[1] = CreateLine("MeasureDotB", 24, true, s_PointColor, 0.014f);
            SetLabelVisible(false);
        }

        void OnEnable() => GameEvents.FurnitureSelected += OnFurnitureSelected;
        void OnDisable() => GameEvents.FurnitureSelected -= OnFurnitureSelected;

        // Picking a catalogue item means the user wants to place furniture, not measure.
        void OnFurnitureSelected(string itemId) => SetActive(false);

        void OnDestroy()
        {
            IsActive = false;
            ServiceLocator.Unregister<MeasureTool>();
        }

        public void Toggle() => SetActive(!IsActive);

        public void SetActive(bool active)
        {
            if (active == IsActive)
                return;

            IsActive = active;
            ClearPoints();

            if (active)
            {
                // Leave placement / editing so taps mean "measure here".
                GameEvents.RaisePlacementCancelled();
                var selection = ServiceLocator.Get<ObjectSelectionService>();
                if (selection != null)
                    selection.Deselect();
                GameEvents.RaiseToastRequested("Measure: tap two points on the floor");
            }
        }

        /// <summary>Adds a measuring point where the screen position meets the floor.</summary>
        public void AddPoint(Vector2 screenPoint)
        {
            if (!ServiceLocator.TryGet(out ManualFloor floor) || !floor.HasFloor)
            {
                GameEvents.RaiseToastRequested("Floor not ready yet — hold the phone steady for a moment.");
                return;
            }

            if (!floor.GetPoint(screenPoint, out Vector3 point))
                return;

            if (m_Points.Count >= 2)
                ClearPoints();

            m_Points.Add(new Vector3(point.x, floor.FloorY + 0.012f, point.z));
            Refresh();
        }

        void ClearPoints()
        {
            m_Points.Clear();
            Refresh();
        }

        void Refresh()
        {
            for (int i = 0; i < m_Dots.Length; i++)
            {
                bool has = i < m_Points.Count;
                m_Dots[i].enabled = has;
                if (has)
                    SetCircle(m_Dots[i], m_Points[i], 0.07f);
            }

            bool two = m_Points.Count == 2;
            m_Segment.enabled = two;
            if (two)
            {
                m_Segment.SetPosition(0, m_Points[0]);
                m_Segment.SetPosition(1, m_Points[1]);

                Vector3 d = m_Points[1] - m_Points[0];
                d.y = 0f;
                float metres = d.magnitude;
                if (m_Label != null)
                    m_Label.text = $"{metres:0.00} m  ·  {metres * 3.28084f:0.0} ft";
            }
            else
            {
                SetLabelVisible(false);
            }
        }

        void LateUpdate()
        {
            if (!IsActive || m_Points.Count != 2 || m_Label == null || m_LabelParent == null)
                return;

            if (m_Camera == null)
                m_Camera = Camera.main;
            if (m_Camera == null)
                return;

            Vector3 mid = (m_Points[0] + m_Points[1]) * 0.5f + Vector3.up * 0.10f;
            Vector3 screen = m_Camera.WorldToScreenPoint(mid);
            if (screen.z <= 0f)
            {
                SetLabelVisible(false);
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(m_LabelParent, screen, null, out Vector2 local);
            LabelRect.anchoredPosition = local;
            SetLabelVisible(true);
        }

        RectTransform LabelRect => m_LabelRoot != null ? m_LabelRoot : (m_Label != null ? m_Label.rectTransform : null);

        void SetLabelVisible(bool visible)
        {
            RectTransform rect = LabelRect;
            if (rect != null && rect.gameObject.activeSelf != visible)
                rect.gameObject.SetActive(visible);
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
