using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ARSpace.Core
{
    /// <summary>
    /// Reliable "is this screen position over UI?" test.
    /// <c>EventSystem.IsPointerOverGameObject(fingerIndex)</c> is not reliable with the Input System UI module
    /// (its touch pointer ids are not finger indices), which made taps on toolbar buttons also count as taps on
    /// the world — deselecting the object before Rotate / Lock / Delete could act on it.
    /// </summary>
    public static class UiPointer
    {
        static readonly List<RaycastResult> s_Results = new List<RaycastResult>();

        public static bool IsOverUI(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            var data = new PointerEventData(eventSystem) { position = screenPosition };
            s_Results.Clear();
            eventSystem.RaycastAll(data, s_Results);
            return s_Results.Count > 0;
        }
    }
}
