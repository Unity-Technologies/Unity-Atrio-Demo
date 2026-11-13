using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Keeps a <see cref="RectTransform"/> fitted to <see cref="Screen.safeArea"/>, so controls inside it stay
    /// clear of notches, rounded corners and the home indicator.
    /// </summary>
    /// <remarks>
    /// The rect must be a direct child of the Canvas or of another full-screen rect, because its anchors are
    /// set as fractions of the screen. It refits when the safe area or screen size changes.
    /// </remarks>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Safe Area")]
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class TouchSafeArea : MonoBehaviour
    {
        Rect m_AppliedSafeArea;
        int m_AppliedWidth;
        int m_AppliedHeight;

        /// <summary>The safe area last applied, in screen pixels.</summary>
        public Rect AppliedSafeArea => m_AppliedSafeArea;

        /// <summary>
        /// Sets <paramref name="rect"/>'s anchors to <paramref name="safeArea"/> as fractions of the screen size
        /// and zeroes its offsets.
        /// </summary>
        public static void Fit(RectTransform rect, Rect safeArea, int screenWidth, int screenHeight)
        {
            if (rect == null || screenWidth <= 0 || screenHeight <= 0)
                return;

            var min = new Vector2(safeArea.xMin / screenWidth, safeArea.yMin / screenHeight);
            var max = new Vector2(safeArea.xMax / screenWidth, safeArea.yMax / screenHeight);

            rect.anchorMin = new Vector2(Mathf.Clamp01(min.x), Mathf.Clamp01(min.y));
            rect.anchorMax = new Vector2(Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Applies the current <see cref="Screen.safeArea"/> now, whether or not it changed.</summary>
        public void Apply()
        {
            m_AppliedSafeArea = Screen.safeArea;
            m_AppliedWidth = Screen.width;
            m_AppliedHeight = Screen.height;
            Fit(transform as RectTransform, m_AppliedSafeArea, m_AppliedWidth, m_AppliedHeight);
        }

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.width != m_AppliedWidth || Screen.height != m_AppliedHeight || Screen.safeArea != m_AppliedSafeArea)
                Apply();
        }
    }
}
