using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;
using ARSpace.Core;

namespace ARSpace.Placement
{
    /// <summary>
    /// Central touch router utilizing the Unity Input System's <see cref="EnhancedTouchSupport"/>.
    /// Exactly one system consumes touch input across the app, ensuring gestures never conflict.
    ///
    /// Routes:
    /// - 1-Finger Tap: Selects or deselects objects via <see cref="ObjectSelectionService"/>.
    /// - 1-Finger Drag: Translates selected object along the floor plane.
    /// - 2-Finger Twist: Yaw rotation with magnetic snapping.
    /// - 2-Finger Pinch: Scaling within item boundaries.
    /// </summary>
    public class GestureRouter : MonoBehaviour
    {
        [Header("Thresholds")]
        [Tooltip("Distance in pixels a finger must move before a tap becomes a drag.")]
        [SerializeField]
        float m_DragThresholdPixels = 15f;

        [Tooltip("Maximum duration in seconds for a tap gesture.")]
        [SerializeField]
        float m_MaxTapDuration = 0.35f;

        ObjectSelectionService m_SelectionService;
        ObjectManipulator m_Manipulator;

        // Gesture state
        enum GestureMode { None, TapPending, Dragging, MultiGesture }
        GestureMode m_CurrentMode = GestureMode.None;

        Vector2 m_TouchStartPos;
        float m_TouchStartTime;
        bool m_TouchStartedOverUI;

        // 2-finger initial values
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

            // Touch input processing
            if (touchCount > 0)
            {
                ProcessTouchGestures(touchCount);
            }
            else
            {
                // In editor, fallback to mouse input for simulation testing
                ProcessMouseFallback();
            }
        }

        void ProcessTouchGestures(int touchCount)
        {
            if (touchCount == 1)
            {
                var touch = Touch.activeTouches[0];

                switch (touch.phase)
                {
                    case TouchPhase.Began:
                        m_TouchStartPos = touch.screenPosition;
                        m_TouchStartTime = Time.unscaledTime;
                        m_TouchStartedOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject(touch.finger.index);

                        if (!m_TouchStartedOverUI)
                        {
                            m_CurrentMode = GestureMode.TapPending;
                        }
                        break;

                    case TouchPhase.Moved:
                    case TouchPhase.Stationary:
                        if (m_TouchStartedOverUI) return;

                        if (m_CurrentMode == GestureMode.TapPending)
                        {
                            float moveDist = Vector2.Distance(touch.screenPosition, m_TouchStartPos);
                            if (moveDist > m_DragThresholdPixels)
                            {
                                // Transition from tap to drag if an object is selected
                                if (m_SelectionService != null && m_SelectionService.HasSelection)
                                {
                                    m_CurrentMode = GestureMode.Dragging;
                                    m_Manipulator.StartDrag(m_SelectionService.SelectedObject);
                                }
                            }
                        }

                        if (m_CurrentMode == GestureMode.Dragging && m_Manipulator != null)
                        {
                            m_Manipulator.OnDrag(touch.screenPosition);
                        }
                        break;

                    case TouchPhase.Ended:
                    case TouchPhase.Canceled:
                        if (!m_TouchStartedOverUI)
                        {
                            if (m_CurrentMode == GestureMode.TapPending)
                            {
                                float duration = Time.unscaledTime - m_TouchStartTime;
                                if (duration <= m_MaxTapDuration && m_SelectionService != null)
                                {
                                    m_SelectionService.ProcessTap(touch.screenPosition, touch.finger.index);
                                }
                            }
                            else if (m_CurrentMode == GestureMode.Dragging && m_Manipulator != null)
                            {
                                m_Manipulator.EndDrag();
                            }
                        }

                        m_CurrentMode = GestureMode.None;
                        m_TouchStartedOverUI = false;
                        break;
                }
            }
            else if (touchCount == 2)
            {
                var t0 = Touch.activeTouches[0];
                var t1 = Touch.activeTouches[1];

                if (t0.phase == TouchPhase.Began || t1.phase == TouchPhase.Began || m_CurrentMode != GestureMode.MultiGesture)
                {
                    m_CurrentMode = GestureMode.MultiGesture;
                    m_InitialPinchDistance = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                    m_InitialAngle = CalculateAngle(t0.screenPosition, t1.screenPosition);

                    if (m_SelectionService != null && m_SelectionService.HasSelection && m_Manipulator != null)
                    {
                        m_Manipulator.StartTwistAndPinch(m_SelectionService.SelectedObject);
                    }
                }
                else if (t0.phase == TouchPhase.Moved || t1.phase == TouchPhase.Moved)
                {
                    if (m_CurrentMode == GestureMode.MultiGesture && m_Manipulator != null)
                    {
                        float currentDist = Vector2.Distance(t0.screenPosition, t1.screenPosition);
                        float pinchRatio = m_InitialPinchDistance > 0.001f ? currentDist / m_InitialPinchDistance : 1.0f;

                        float currentAngle = CalculateAngle(t0.screenPosition, t1.screenPosition);
                        float deltaAngle = Mathf.DeltaAngle(m_InitialAngle, currentAngle);

                        m_Manipulator.OnTwistAndPinch(deltaAngle, pinchRatio);
                    }
                }
                else if (t0.phase == TouchPhase.Ended || t1.phase == TouchPhase.Ended ||
                         t0.phase == TouchPhase.Canceled || t1.phase == TouchPhase.Canceled)
                {
                    if (m_CurrentMode == GestureMode.MultiGesture && m_Manipulator != null)
                    {
                        m_Manipulator.EndTwistAndPinch();
                    }
                    m_CurrentMode = GestureMode.None;
                }
            }
        }

        void ProcessMouseFallback()
        {
            if (Input.GetMouseButtonDown(0))
            {
                m_TouchStartPos = Input.mousePosition;
                m_TouchStartTime = Time.unscaledTime;
                m_TouchStartedOverUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

                if (!m_TouchStartedOverUI)
                {
                    m_CurrentMode = GestureMode.TapPending;
                }
            }
            else if (Input.GetMouseButton(0))
            {
                if (m_TouchStartedOverUI) return;

                if (m_CurrentMode == GestureMode.TapPending)
                {
                    float dist = Vector2.Distance(Input.mousePosition, m_TouchStartPos);
                    if (dist > m_DragThresholdPixels && m_SelectionService != null && m_SelectionService.HasSelection)
                    {
                        m_CurrentMode = GestureMode.Dragging;
                        m_Manipulator.StartDrag(m_SelectionService.SelectedObject);
                    }
                }

                if (m_CurrentMode == GestureMode.Dragging && m_Manipulator != null)
                {
                    m_Manipulator.OnDrag(Input.mousePosition);
                }
            }
            else if (Input.GetMouseButtonUp(0))
            {
                if (!m_TouchStartedOverUI)
                {
                    if (m_CurrentMode == GestureMode.TapPending)
                    {
                        if (m_SelectionService != null)
                        {
                            m_SelectionService.ProcessTap(Input.mousePosition);
                        }
                    }
                    else if (m_CurrentMode == GestureMode.Dragging && m_Manipulator != null)
                    {
                        m_Manipulator.EndDrag();
                    }
                }

                m_CurrentMode = GestureMode.None;
                m_TouchStartedOverUI = false;
            }
        }

        static float CalculateAngle(Vector2 p0, Vector2 p1)
        {
            Vector2 dir = p1 - p0;
            return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        }
    }
}
