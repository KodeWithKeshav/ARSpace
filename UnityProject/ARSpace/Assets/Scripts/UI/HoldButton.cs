using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ARSpace.UI
{
    /// <summary>A press-and-hold button: exposes <see cref="IsHeld"/> and fires <see cref="Pressed"/> on touch-down.</summary>
    [RequireComponent(typeof(Image))]
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool IsHeld { get; private set; }
        public event System.Action Pressed;

        Image m_Image;
        Color m_NormalColor;

        void Awake()
        {
            m_Image = GetComponent<Image>();
            m_NormalColor = m_Image.color;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            IsHeld = true;
            if (m_Image != null) m_Image.color = m_NormalColor * 0.75f;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData) => Release();
        public void OnPointerExit(PointerEventData eventData) => Release();

        void OnDisable() => Release();

        void Update()
        {
            if (!IsHeld)
                return;

            // Failsafe: if the "finger up" event was ever missed, release as soon as nothing is touching the screen.
            // (A stuck button would keep raising/lowering/moving whatever it controls, seen as things floating away.)
            bool touching = UnityEngine.InputSystem.EnhancedTouch.EnhancedTouchSupport.enabled
                            && UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0;
            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool mouseDown = mouse != null && mouse.leftButton.isPressed;

            if (!touching && !mouseDown)
                Release();
        }

        void Release()
        {
            IsHeld = false;
            if (m_Image != null) m_Image.color = m_NormalColor;
        }
    }
}
