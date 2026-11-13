using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>Where a <see cref="TouchJoystick"/> puts its centre when a finger lands.</summary>
    public enum JoystickOriginMode
    {
        /// <summary>
        /// The centre stays at <see cref="TouchJoystick.RestScreenPosition"/>. A finger landing off-centre
        /// deflects the stick at once.
        /// </summary>
        Fixed,

        /// <summary>
        /// The centre is where the finger lands, so the stick always starts from zero.
        /// </summary>
        Floating,

        /// <summary>
        /// Like <see cref="Floating"/>, but the centre follows the finger past the rim, so a drifted thumb
        /// can reverse at once.
        /// </summary>
        FloatingWithLeash,
    }

    /// <summary>
    /// An on-screen thumbstick. Its <see cref="Value"/> has length 0 to 1 and follows one finger, for example
    /// <c>rig.Get&lt;TouchJoystick&gt;("Move").Value</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="TouchJoystickView"/> sets <see cref="RadiusPixels"/> and <see cref="RestScreenPosition"/> from
    /// its rect every frame. Set them yourself only for a joystick with no view.
    /// </remarks>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Joystick")]
    public sealed class TouchJoystick : TouchControl
    {
        /// <summary>Default <see cref="DeadZone"/>: 15% of the radius.</summary>
        public const float k_DefaultDeadZone = 0.15f;

        /// <summary>Default <see cref="Saturation"/>: 90% of the radius.</summary>
        public const float k_DefaultSaturation = 0.9f;

        /// <summary>Default <see cref="RadiusPixels"/> until a view measures the real one.</summary>
        public const float k_DefaultRadiusPixels = 100f;

        [SerializeField]
        [Tooltip("Where the centre goes when a finger lands: fixed, under the finger, or under the finger and following it.")]
        JoystickOriginMode m_OriginMode = JoystickOriginMode.Fixed;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the radius that reads as zero so a resting thumb does not creep, typically 0.1–0.15.")]
        float m_DeadZone = k_DefaultDeadZone;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Fraction of the radius where the value reaches full length, typically 0.85–0.9.")]
        float m_Saturation = k_DefaultSaturation;

        [SerializeField]
        [Tooltip("Return to zero when the finger lifts; off keeps the last value, like a throttle.")]
        bool m_RecenterOnRelease = true;

        [SerializeField]
        [Min(1f)]
        [Tooltip("Full-deflection radius in screen pixels, which the view overwrites every frame.")]
        float m_RadiusPixels = k_DefaultRadiusPixels;

        Vector2 m_RestScreenPosition;
        Vector2 m_Origin;
        Vector2 m_Finger;
        Vector2 m_Value;
        bool m_HasOrigin;

        /// <summary>Where the centre goes when a finger lands. Default <see cref="JoystickOriginMode.Fixed"/>.</summary>
        public JoystickOriginMode OriginMode
        {
            get => m_OriginMode;
            set => m_OriginMode = value;
        }

        /// <summary>Fraction of the radius (0..1) that reads as zero. Default 0.15.</summary>
        public float DeadZone
        {
            get => m_DeadZone;
            set => m_DeadZone = Mathf.Clamp01(value);
        }

        /// <summary>Fraction of the radius (0..1) at which the value reaches magnitude 1. Default 0.9.</summary>
        public float Saturation
        {
            get => m_Saturation;
            set => m_Saturation = Mathf.Clamp01(value);
        }

        /// <summary>Whether the value snaps back to zero on release. Default <c>true</c>.</summary>
        public bool RecenterOnRelease
        {
            get => m_RecenterOnRelease;
            set => m_RecenterOnRelease = value;
        }

        /// <summary>Radius in screen pixels for a full deflection. The view sets it from its rect.</summary>
        public float RadiusPixels
        {
            get => m_RadiusPixels;
            set => m_RadiusPixels = Mathf.Max(1f, value);
        }

        /// <summary>
        /// Screen position of the stick at rest, in pixels, set by the view from its rect. It is the centre in
        /// <see cref="JoystickOriginMode.Fixed"/> mode and where a floating base returns.
        /// </summary>
        public Vector2 RestScreenPosition
        {
            get => m_RestScreenPosition;
            set => m_RestScreenPosition = value;
        }

        /// <summary>The stick's deflection: magnitude ≤ 1, zero at rest, dead zone and saturation applied.</summary>
        public Vector2 Value => m_Value;

        /// <summary>Unit direction of <see cref="Value"/>, or zero at rest.</summary>
        public Vector2 Direction => m_Value.normalized;

        /// <summary>Length of <see cref="Value"/>, 0..1.</summary>
        public float Magnitude => m_Value.magnitude;

        /// <summary>The current centre in screen pixels. The view draws the base here.</summary>
        public Vector2 OriginScreenPosition => m_HasOrigin ? m_Origin : m_RestScreenPosition;

        /// <summary>The knob position in screen pixels: the finger, clamped to the rim.</summary>
        public Vector2 KnobScreenPosition =>
            m_HasOrigin ? m_Origin + Vector2.ClampMagnitude(m_Finger - m_Origin, m_RadiusPixels) : m_RestScreenPosition;

        /// <inheritdoc/>
        protected override bool IsAtRest => !IsPressed && !m_HasOrigin;

        /// <inheritdoc/>
        protected override void OnBegan(in PointerSample sample)
        {
            m_Origin = m_OriginMode == JoystickOriginMode.Fixed ? m_RestScreenPosition : sample.ScreenPosition;
            m_Finger = sample.ScreenPosition;
            m_HasOrigin = true;
            UpdateValue();
        }

        /// <inheritdoc/>
        protected override void OnMoved(in PointerSample sample)
        {
            m_Finger = sample.ScreenPosition;

            if (m_OriginMode == JoystickOriginMode.FloatingWithLeash)
                JoystickMath.AdvanceLeash(ref m_Origin, m_Finger, m_RadiusPixels);

            UpdateValue();
        }

        /// <inheritdoc/>
        protected override void OnEnded(in PointerSample sample, bool canceled)
        {
            m_Finger = sample.ScreenPosition;

            if (m_RecenterOnRelease || canceled)
                Rest();
            else
                UpdateValue();
        }

        /// <inheritdoc/>
        protected override void OnCancel(bool wasPressed) => Rest();

        void Rest()
        {
            m_HasOrigin = false;
            m_Origin = m_RestScreenPosition;
            m_Finger = m_RestScreenPosition;
            m_Value = Vector2.zero;
        }

        void UpdateValue() =>
            m_Value = JoystickMath.Normalize(m_Finger - m_Origin, m_RadiusPixels, m_DeadZone, m_Saturation);
    }
}
