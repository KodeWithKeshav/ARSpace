using UnityEngine;
using ARSpace.Core;
using ARSpace.Placement;

namespace ARSpace.UI
{
    /// <summary>
    /// Precision "Move" card for the selected object: Left / Right / Away / Closer (relative to where you are
    /// looking, along the floor) and Turn left / Turn right. Every button nudges a little on tap and glides —
    /// speeding up the longer it is held — and the object is re-anchored to the real world when you let go.
    /// Respects Lock. Up / Down height lives on the selection toolbar.
    /// </summary>
    public class ObjectNudgeControls : MonoBehaviour
    {
        [SerializeField] GameObject m_Card;
        [SerializeField] HoldButton m_LeftButton;
        [SerializeField] HoldButton m_RightButton;
        [SerializeField] HoldButton m_AwayButton;
        [SerializeField] HoldButton m_CloserButton;
        [SerializeField] HoldButton m_TurnLeftButton;
        [SerializeField] HoldButton m_TurnRightButton;

        const float TapStepMetres = 0.01f;
        const float SlowSpeed = 0.12f;      // m/s at the start of a hold
        const float FastSpeed = 0.60f;      // m/s after RampSeconds
        const float RampSeconds = 1.2f;
        const float TurnSpeed = 45f;        // degrees per second
        const float TapTurnDegrees = 1f;

        ObjectSelectionService m_Selection;
        AnchorService m_Anchors;
        float m_HoldTime;
        bool m_Adjusting;

        void Awake()
        {
            SetVisible(false);
            Wire(m_LeftButton, -1f, 0f, 0f);
            Wire(m_RightButton, 1f, 0f, 0f);
            Wire(m_AwayButton, 0f, 1f, 0f);
            Wire(m_CloserButton, 0f, -1f, 0f);
            Wire(m_TurnLeftButton, 0f, 0f, -1f);
            Wire(m_TurnRightButton, 0f, 0f, 1f);
        }

        void Wire(HoldButton button, float x, float z, float turn)
        {
            if (button == null)
                return;

            button.Pressed += () => OnPressed(x, z, turn);
        }

        void OnEnable() => GameEvents.ObjectSelectionChanged += OnSelectionChanged;
        void OnDisable() => GameEvents.ObjectSelectionChanged -= OnSelectionChanged;

        void OnSelectionChanged(GameObject selected)
        {
            m_Adjusting = false;
            m_HoldTime = 0f;
            SetVisible(selected != null);
        }

        void SetVisible(bool visible)
        {
            if (m_Card != null)
                m_Card.SetActive(visible);
        }

        PlacedObject Selected
        {
            get
            {
                if (m_Selection == null)
                    m_Selection = ServiceLocator.Get<ObjectSelectionService>();
                return m_Selection != null ? m_Selection.SelectedObject : null;
            }
        }

        static void FloorBasis(out Vector3 right, out Vector3 forward)
        {
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            right = cam != null ? cam.right : Vector3.right;
            forward = cam != null ? cam.forward : Vector3.forward;
            right.y = 0f;
            forward.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        }

        void OnPressed(float x, float z, float turn)
        {
            PlacedObject obj = Selected;
            if (obj == null)
                return;

            if (obj.IsLocked)
            {
                GameEvents.RaiseToastRequested("Unlock the object first to move it.");
                return;
            }

            // A single tap is a precise 1 cm / 1° nudge; holding then takes over in Update.
            FloorBasis(out Vector3 right, out Vector3 forward);
            obj.transform.position += (right * x + forward * z) * TapStepMetres;
            if (turn != 0f)
                obj.transform.Rotate(0f, turn * TapTurnDegrees, 0f, Space.World);

            m_Adjusting = true;
            obj.BeginManipulation();
        }

        void Update()
        {
            if (m_Card == null || !m_Card.activeSelf)
                return;

            PlacedObject obj = Selected;
            if (obj == null)
                return;

            float x = (m_RightButton != null && m_RightButton.IsHeld ? 1f : 0f) - (m_LeftButton != null && m_LeftButton.IsHeld ? 1f : 0f);
            float z = (m_AwayButton != null && m_AwayButton.IsHeld ? 1f : 0f) - (m_CloserButton != null && m_CloserButton.IsHeld ? 1f : 0f);
            float turn = (m_TurnRightButton != null && m_TurnRightButton.IsHeld ? 1f : 0f) - (m_TurnLeftButton != null && m_TurnLeftButton.IsHeld ? 1f : 0f);
            bool any = x != 0f || z != 0f || turn != 0f;

            if (any && !obj.IsLocked)
            {
                m_HoldTime += Time.unscaledDeltaTime;
                float speed = Mathf.Lerp(SlowSpeed, FastSpeed, Mathf.Clamp01(m_HoldTime / RampSeconds));

                if (x != 0f || z != 0f)
                {
                    FloorBasis(out Vector3 right, out Vector3 forward);
                    Vector3 dir = right * x + forward * z;
                    if (dir.sqrMagnitude > 1f)
                        dir.Normalize();
                    obj.transform.position += dir * (speed * Time.unscaledDeltaTime);
                }

                if (turn != 0f)
                    obj.transform.Rotate(0f, turn * TurnSpeed * Time.unscaledDeltaTime, 0f, Space.World);

                m_Adjusting = true;
            }
            else
            {
                m_HoldTime = 0f;
                if (m_Adjusting && !any)
                {
                    m_Adjusting = false;
                    Reanchor(obj);
                }
            }
        }

        async void Reanchor(PlacedObject obj)
        {
            if (m_Anchors == null)
                m_Anchors = ServiceLocator.Get<AnchorService>();
            if (m_Anchors != null && obj != null)
            {
                await m_Anchors.AttachToAnchorAsync(obj, new Pose(obj.transform.position, obj.transform.rotation), null);
                if (obj != null)
                    obj.EndManipulation();
            }
        }
    }
}
