using UnityEngine;

namespace ARSpace.UI
{
    /// <summary>Keeps a UI root inside the device safe area (notches, punch-hole cameras, gesture bars).</summary>
    [RequireComponent(typeof(RectTransform))]
    public class SafeAreaFitter : MonoBehaviour
    {
        Rect m_LastSafeArea;
        Vector2Int m_LastScreen;

        void Update()
        {
            Rect safe = Screen.safeArea;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (safe == m_LastSafeArea && size == m_LastScreen)
                return;

            m_LastSafeArea = safe;
            m_LastScreen = size;
            if (size.x <= 0 || size.y <= 0)
                return;

            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / size.x, safe.yMin / size.y);
            rect.anchorMax = new Vector2(safe.xMax / size.x, safe.yMax / size.y);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
