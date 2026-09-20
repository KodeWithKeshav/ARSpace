using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
using ARSpace.AR;
using ARSpace.Core;

namespace ARSpace.Placement
{
    /// <summary>
    /// Central touch router utilizing the Unity Input System's <see cref="EnhancedTouchSupport"/>.
    /// Exactly one system consumes touch input across the app, ensuring gestures never conflict.
    ///
    /// Routes:
    /// - 1-Finger Tap: Selects or deselects objects via <see cref="ObjectSelectionService"/>.
    /// - 1-Finger Drag: Moves the selected object — but only when the finger started ON that object, so a thumb
    ///   resting on the screen while the phone is moved can never drag furniture around.
    /// - 2-Finger Twist / Pinch: Rotate / scale the selected object (blocked while it is locked).
    /// UI touches are ignored entirely.
    /// </summary>
    public class GestureRouter : MonoBehaviour
    {
        [Header("Thresholds")]
        [Tooltip("Distance in pixels a finger must move before a tap becomes a drag.")]
        [SerializeField]
        float m_DragThresholdPixels = 24f;

        [Tooltip("Maximum duration in seconds for a tap gesture.")]
        [SerializeField]
        float m_MaxTapDuration = 0.8f;

        ObjectSelectionService m_SelectionService;
        ObjectManipulator m_Manipulator;

        enum GestureMode { None, TapPending, Dragging, MarkerDrag, MultiGesture }
        GestureMode m_CurrentMode = GestureMode.None;

        Vector2 m_TouchStartPos;
        float m_TouchStartTime;
        bool m_TouchStartedOverUI;
        bool m_TouchStartedOnSelected;

        float m_InitialPinchDistance;
        float m_InitialAngle;

        void Awake()
        {
            ServiceLocator.Register(this);
        }

        void OnDestroy()
        {
            ServiceLocator.Unregister<GestureRouter>();
        }

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        void Start()
        {
            m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();
            m_Manipulator = ServiceLocator.Get<ObjectManipulator>();
        }

        void Update()
        {
            if (m_SelectionService == null)
                m_SelectionService = ServiceLocator.Get<ObjectSelectionService>();
            if (m_Manipulator == null)
                m_Manipulator = ServiceLocator.Get<ObjectManipulator>();

            int touchCount = Touch.activeTouches.Count;

            if (touchCount > 0)
                ProcessTouchGestures(touchCount);
#if UNITY_EDITOR
            else
                ProcessMouseFallback();
#endif
        }

        void BeginPress(Vector2 position)
        {
            m_TouchStartPos = position;
            m_TouchStartTime = Time.unscaledTime;
            m_TouchStartedOverUI = UiPointer.IsOverUI(position);
            m_TouchStartedOnSelected = false;

            if (m_TouchStartedOverUI)
                return;

            m_CurrentMode = GestureMode.TapPending;
            if (m_SelectionService != null && m_SelectionService.HasSelection)
                m_TouchStartedOnSelected = m_SelectionService.IsPointerOnObject(position, m_SelectionService.SelectedObject);
        }

        void ProcessTouchGestures(int touchCount)
        {
            if (touchCount == 1)
            {
                var touch = Touch.activeTouches[0];

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        BeginPress(touch.screenPosition);
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        HandleMove(touch.screenPosition);
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        HandleRelease(touch.screenPosition);
                        break;
                }
            }
            else if (touchCount == 2)
            {
                var t0 = Touch.activeTouches[0];
                var t1 = Touch.activeTouches[1];

                if (m_TouchStartedOverUI)
                    return;

                if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began || m_CurrentMode != GestureMode.MultiGesture)
                {
                    if (UiPointer.IsOverUI(t0.screenPosition) || UiPointer.IsOverUI(t1.screenPosition))
                        return;

                    m_CurrentMode = GestureMode.MultiGesture;
                    m_InitialPinchDistance = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                    m_InitialAngle = CalculateAngle(t0.screenPosition, t1.screenPosition);

                    if (m_SelectionService != null && m_SelectionService.HasSelection && m_Manipulator != null)
                        m_Manipulator.StartTwistAndPinch(m_SelectionService.SelectedObject);
                }
                else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
                {
                    if (m_Manipulator != null)
                    {
                        float currentDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                        float pinchRatio = m_InitialPinchDistance > 0.001f ? currentDist / m_InitialPinchDistance : 1.0f;
                        float deltaAngle = Mathf.DeltaAngle(m_InitialAngle, CalculateAngle(t0.screenPosition, t1.screenPosition));
                        m_Manipulator.OnTwistAndPinch(deltaAngle, pinchRatio);
                    }
                }
                else if (t0.phase == TouchPhase.Ended || t1.phase == TouchPhase.Ended ||
                         t0.phase == TouchPhase.Canceled || t1.phase == TouchPhase.Canceled)
                {
                    if (m_Manipulator != null)
                        m_Manipulator.EndTwistAndPinch();
                    m_CurrentMode = GestureMode.None;
                }
            }
        }

        void HandleMove(Vector2 position)
        {
            if (m_TouchStartedOverUI)
                return;

            if (m_CurrentMode == GestureMode.TapPending && m_TouchStartedOnSelected &&
                Vector2.Distance(position, m_TouchStartPos) > m_DragThresholdPixels &&
                m_SelectionService != null && m_SelectionService.HasSelection && m_Manipulator != null)
            {
                m_CurrentMode = GestureMode.Dragging;
                m_Manipulator.StartDrag(m_SelectionService.SelectedObject);
            }

            if (m_CurrentMode == GestureMode.Dragging && m_Manipulator != null)
                m_Manipulator.OnDrag(position);

            // While positioning a catalogue item, dragging on the floor slides the placement marker under the finger.
            if (m_CurrentMode == GestureMode.TapPending && !m_TouchStartedOnSelected &&
                Vector2.Distance(position, m_TouchStartPos) > m_DragThresholdPixels)
            {
                var reticle = ServiceLocator.Get<PlacementReticle>();
                if (reticle != null && reticle.IsPlacementActive)
                    m_CurrentMode = GestureMode.MarkerDrag;
            }

            if (m_CurrentMode == GestureMode.MarkerDrag)
            {
                var reticle = ServiceLocator.Get<PlacementReticle>();
                if (reticle != null)
                    reticle.TryPinAtScreenPoint(position, refineFloor: false);
            }
        }

        void HandleRelease(Vector2 position)
        {
            if (!m_TouchStartedOverUI)
            {
                if (m_CurrentMode == GestureMode.TapPending)
                {
                    float duration = Time.unscaledTime - m_TouchStartTime;
                    bool moved = Vector2.Distance(position, m_TouchStartPos) > m_DragThresholdPixels;
                    if (duration <= m_MaxTapDuration && !moved && MeasureTool.IsActive)
                    {
                        if (ServiceLocator.TryGet(out MeasureTool measure))
                            measure.AddPoint(position);
                    }
                    else if (duration <= m_MaxTapDuration && !moved && m_SelectionService != null)
                    {
                        // A tap on furniture selects/deselects it; a tap on empty floor while positioning a
                        // catalogue item moves the placement marker there.
                        if (!m_SelectionService.ProcessTap(position))
                        {
                            var reticle = ServiceLocator.Get<PlacementReticle>();
                            if (reticle != null)
                                reticle.TryPinAtScreenPoint(position);
                        }
                    }
                }
                else if (m_CurrentMode == GestureMode.Dragging && m_Manipulator != null)
                {
                    m_Manipulator.EndDrag();
                }
            }

            m_CurrentMode = GestureMode.None;
            m_TouchStartedOverUI = false;
            m_TouchStartedOnSelected = false;
        }

#if UNITY_EDITOR
        // Editor-only mouse simulation (the legacy UnityEngine.Input class is disabled in this project).
        void ProcessMouseFallback()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null)
                return;

            Vector2 pos = mouse.position.ReadValue();
            if (mouse.leftButton.wasPressedThisFrame)
                BeginPress(pos);
            else if (mouse.leftButton.isPressed)
                HandleMove(pos);
            else if (mouse.leftButton.wasReleasedThisFrame)
                HandleRelease(pos);
        }
#endif

        static float CalculateAngle(Vector2 p0, Vector2 p1)
        {
            Vector2 dir = p1 - p0;
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
    }
}
