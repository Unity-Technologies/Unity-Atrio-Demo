using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// A jump with coyote time: on a tick where <see cref="MoveCommand.Jump"/> is set and the
    /// mover either is grounded or left the ground less than <see cref="CoyoteTime"/> ago while
    /// falling, vertical velocity is set to <see cref="JumpSpeed"/>.
    /// </summary>
    /// <remarks>
    /// Holds no state: coyote time reads <see cref="MoveState.TimeSinceGrounded"/>. Jump buffering
    /// (a press just before landing) is not supported. Register after
    /// <see cref="GroundLocomotionProcessor"/>.
    /// </remarks>
    [Serializable]
    public sealed class JumpProcessor : MoveProcessor
    {
        [SerializeField, Min(0f)]
        [Tooltip("Upward velocity applied on jump, in m/s. Apex height is this and gravity " +
                 "together: height = v² / 2g. At the defaults (4.5 m/s, -9.81 m/s²) that is " +
                 "about 1.03 m.")]
        float m_JumpSpeed = k_DefaultJumpSpeed;

        [SerializeField, Min(0f)]
        [Tooltip("How long after walking off a ledge a jump is still accepted, in seconds. " +
                 "Forgives leaving a ledge; never forgives being airborne by your own doing: a " +
                 "rising character is refused regardless of this value.")]
        float m_CoyoteTime = k_DefaultCoyoteTime;

        /// <summary>
        /// Default <see cref="JumpSpeed"/>: 4.5 m/s, about a 1.03 m apex against the default
        /// -9.81 m/s² gravity (<c>height = v² / 2g</c>).
        /// </summary>
        /// <remarks>
        /// Jump height depends on this and <see cref="GroundLocomotionProcessor.Gravity"/> together.
        /// A held jump only stops re-firing while
        /// <c>JumpSpeed * fixedDeltaTime &gt; </c><see cref="KinematicMover.GroundProbeDistance"/>
        /// (0.075 m against 0.05 m at defaults); lowering this, raising the probe distance or running a
        /// faster tick can make a held jump climb.
        /// </remarks>
        internal const float k_DefaultJumpSpeed = 4.5f;

        /// <summary>
        /// Default <see cref="CoyoteTime"/>: 0.1 s, six ticks at 60 Hz.
        /// </summary>
        /// <remarks>
        /// Typical platformer range is 0.08-0.15 s; above about 0.2 s it reads as a bug. <c>0</c>
        /// requires contact on the tick of the press.
        /// </remarks>
        internal const float k_DefaultCoyoteTime = 0.1f;

        /// <summary>
        /// Upward velocity applied on a jump, in m/s. Default <c>4.5</c>. Jump height also depends on
        /// gravity and the ground probe distance.
        /// </summary>
        public float JumpSpeed => m_JumpSpeed;

        /// <summary>
        /// How long after leaving the ground a jump is still allowed, in seconds. Default
        /// <c>0.1</c>; <c>0</c> requires contact on the tick of the press.
        /// </summary>
        public float CoyoteTime => m_CoyoteTime;

        /// <summary>
        /// Sets vertical velocity to <see cref="JumpSpeed"/> when a jump is commanded and the
        /// mover may jump: grounded, or inside the coyote window. Does nothing otherwise.
        /// </summary>
        /// <remarks>
        /// Reads grounding from the previous tick's probe. Vertical velocity is set, not added, so jump
        /// height is fixed. Horizontal velocity is untouched; steering in the air is
        /// <see cref="GroundLocomotionProcessor.AirControl"/>.
        /// </remarks>
        public override void UpdateVelocity(ref MoveState state, in MoveCommand command, float deltaTime)
        {
            if (!command.Jump || !CanJump(in state))
                return;

            Vector3 velocity = state.Velocity;
            velocity.y = m_JumpSpeed;
            state.Velocity = velocity;
        }

        /// <summary>
        /// Whether the mover may jump this tick: grounded, or airborne inside the coyote window and not
        /// rising. The not-rising check stops a held jump from re-firing inside the window.
        /// </summary>
        bool CanJump(in MoveState state)
        {
            if (state.IsGrounded)
                return true;

            return state.TimeSinceGrounded <= m_CoyoteTime && state.Velocity.y <= 0f;
        }

        /// <summary>Does nothing: a jump does not turn the mover.</summary>
        public override void UpdateRotation(ref MoveState state, in MoveCommand command, float deltaTime)
        {
        }
    }
}
