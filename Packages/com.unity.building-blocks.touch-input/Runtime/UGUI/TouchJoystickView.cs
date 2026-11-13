using UnityEngine;
using UnityEngine.UI;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Draws a <see cref="TouchJoystick"/> as a base and a knob. Sets the joystick's rest position to the touch
    /// area's centre and its radius from the base's size.
    /// </summary>
    /// <remarks>
    /// This GameObject's rect is the touch area. The view moves the base and knob every frame, so their anchors
    /// do not matter. Make the area bigger than the base so a floating stick can be grabbed anywhere in it.
    /// </remarks>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Joystick View")]
    public sealed class TouchJoystickView : TouchControlView
    {
        /// <summary>Default <see cref="TouchControlView.IdleAlpha"/> of a joystick: 40%.</summary>
        public const float k_DefaultIdleAlpha = 0.4f;

        /// <summary>Default <see cref="KnobTravel"/>, which stops the default knob at its ring.</summary>
        public const float k_DefaultKnobTravel = 0.7f;

        [SerializeField]
        [Tooltip("Optional ring at the stick's centre whose size sets the joystick's radius.")]
        Image m_Base;

        [SerializeField]
        [Tooltip("Optional dot drawn under the finger, clamped to the rim.")]
        Image m_Knob;

        [SerializeField]
        [Tooltip("Hide the base and knob while no finger is down, as floating sticks usually do.")]
        bool m_HideWhenIdle;

        [SerializeField]
        [Range(0.2f, 1f)]
        [Tooltip("How far the knob is drawn at full deflection, as a fraction of the radius (visual only).")]
        float m_KnobTravel = k_DefaultKnobTravel;

        readonly Vector3[] m_Corners = new Vector3[4];

        /// <summary>A joystick view: idle at <see cref="k_DefaultIdleAlpha"/>.</summary>
        public TouchJoystickView() : base(k_DefaultIdleAlpha) { }

        /// <summary>The ring at the centre; its screen size sets the joystick's radius. May be <c>null</c>.</summary>
        public Image Base
        {
            get => m_Base;
            set => m_Base = value;
        }

        /// <summary>The dot under the finger. May be <c>null</c>.</summary>
        public Image Knob
        {
            get => m_Knob;
            set => m_Knob = value;
        }

        /// <summary>Hide base and knob while the stick is at rest.</summary>
        public bool HideWhenIdle
        {
            get => m_HideWhenIdle;
            set => m_HideWhenIdle = value;
        }

        /// <summary>How far the knob is drawn at full deflection, as a fraction of the radius (visual only). Default 0.7.</summary>
        public float KnobTravel
        {
            get => m_KnobTravel;
            set => m_KnobTravel = Mathf.Clamp(value, 0.2f, 1f);
        }

        /// <summary>The joystick this view draws, or <c>null</c> if the sibling control is not a joystick.</summary>
        public TouchJoystick Joystick => Control as TouchJoystick;

        /// <inheritdoc/>
        protected override bool IsActive => Joystick != null && (Joystick.IsPressed || Joystick.Value != Vector2.zero);

        /// <inheritdoc/>
        protected override void OnActivated() => Measure();

        /// <inheritdoc/>
        protected override void Draw()
        {
            TouchJoystick joystick = Joystick;
            if (joystick == null)
                return;

            Measure();

            bool visible = !m_HideWhenIdle || joystick.IsPressed || joystick.Value != Vector2.zero;

            if (m_Base != null)
            {
                m_Base.rectTransform.position = ScreenToWorld(joystick.OriginScreenPosition);
                m_Base.enabled = visible;
            }

            if (m_Knob != null)
            {
                Vector2 origin = joystick.OriginScreenPosition;
                m_Knob.rectTransform.position = ScreenToWorld(origin + (joystick.KnobScreenPosition - origin) * m_KnobTravel);
                m_Knob.enabled = visible;
            }
        }

        /// <summary>
        /// Sets the joystick's rest position to this rect's centre and its radius to half the base's smaller
        /// side (this rect's, without a base), in screen pixels.
        /// </summary>
        internal void Measure()
        {
            TouchJoystick joystick = Joystick;
            if (joystick == null)
                return;

            RectTransform area = Rect;
            joystick.RestScreenPosition = WorldToScreen(area.TransformPoint(area.rect.center));

            RectTransform sized = m_Base != null ? m_Base.rectTransform : area;
            sized.GetWorldCorners(m_Corners);
            Vector2 bottomLeft = WorldToScreen(m_Corners[0]);
            float height = Vector2.Distance(bottomLeft, WorldToScreen(m_Corners[1]));
            float width = Vector2.Distance(bottomLeft, WorldToScreen(m_Corners[3]));

            float radius = 0.5f * Mathf.Min(width, height);
            if (radius >= 1f)
                joystick.RadiusPixels = radius;
        }
    }
}
