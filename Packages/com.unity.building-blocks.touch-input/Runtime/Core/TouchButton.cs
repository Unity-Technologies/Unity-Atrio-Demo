using System;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// An on-screen button with <see cref="Pressed"/> and <see cref="Released"/> events and a hold time.
    /// For example: <c>rig.Get&lt;TouchButton&gt;("Jump").Pressed += OnJump</c>.
    /// </summary>
    /// <remarks>
    /// Use the events for taps, not <c>IsPressed</c>: a tap can start and end between two <c>Update</c>s.
    /// The button stays pressed if the finger slides off it, until the finger lifts.
    /// </remarks>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Button")]
    public sealed class TouchButton : TouchControl
    {
        double m_PressTime;
        float m_HoldSeconds;

        /// <summary>A finger landed. <see cref="TouchControl.IsPressed"/> is <c>true</c> inside the handler.</summary>
        public event Action Pressed;

        /// <summary>
        /// The finger lifted, was taken away by the OS, or <see cref="TouchControl.Cancel"/> was called.
        /// <see cref="TouchControl.IsPressed"/> is <c>false</c> inside the handler; <see cref="HoldSeconds"/>
        /// still reads the length of the press.
        /// </summary>
        public event Action Released;

        /// <summary>
        /// Seconds the button has been held. It keeps the last value after release until the next press;
        /// <see cref="TouchControl.Cancel"/> resets it to zero after <see cref="Released"/> fires.
        /// </summary>
        public float HoldSeconds => m_HoldSeconds;

        /// <inheritdoc/>
        protected override void OnBegan(in PointerSample sample)
        {
            m_PressTime = sample.Time;
            m_HoldSeconds = 0f;
            Pressed?.Invoke();
        }

        /// <inheritdoc/>
        protected override void OnMoved(in PointerSample sample) => Hold(sample.Time);

        /// <inheritdoc/>
        protected override void OnEnded(in PointerSample sample, bool canceled)
        {
            Hold(sample.Time);
            Released?.Invoke();
        }

        /// <inheritdoc/>
        protected override void OnCancel(bool wasPressed)
        {
            if (wasPressed)
                Released?.Invoke();

            m_HoldSeconds = 0f;
        }

        void Hold(double now) => m_HoldSeconds = (float)Math.Max(0d, now - m_PressTime);
    }
}
