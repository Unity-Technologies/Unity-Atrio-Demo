using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The default walking processor: gravity, ground acceleration and deceleration, a speed
    /// cap, a sprint multiplier, and reduced control while airborne.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Gravity lives here: <see cref="KinematicMover"/> applies no forces itself, so a mover with no
    /// processor that adds gravity does not fall. Gravity is a serialized value, not
    /// <c>Physics.gravity</c>, so replays do not depend on project settings.
    /// </para>
    /// <para>
    /// Register before <see cref="JumpProcessor"/>; otherwise the jump tick already has that tick's
    /// gravity subtracted. Defaults are not playtested; tune them per project.
    /// </para>
    /// </remarks>
    [Serializable]
    public sealed class GroundLocomotionProcessor : MoveProcessor
    {
        [Header("Gravity")]
        [SerializeField]
        [Tooltip("Downward acceleration in m/s². Negative. Not read from Project " +
                 "Settings, because a project setting is not captured in a MoveState snapshot.")]
        float m_Gravity = k_DefaultGravity;

        [SerializeField, Min(0f)]
        [Tooltip("Extra gravity applied while falling, as a multiple of Gravity. 1 = symmetric " +
                 "arc. Above 1 gives the classic 'floaty up, snappy down' jump; this is the " +
                 "single biggest lever on how a jump reads.")]
        float m_FallGravityMultiplier = k_DefaultFallGravityMultiplier;

        [SerializeField, Min(0f)]
        [Tooltip("Terminal velocity in m/s. Caps how fast a long fall gets. 0 = uncapped.")]
        float m_MaxFallSpeed = k_DefaultMaxFallSpeed;

        [Header("Speed")]
        [SerializeField, Min(0f)]
        [Tooltip("Top ground speed in m/s when moving forward unsprinted.")]
        float m_MaxSpeed = k_DefaultMaxSpeed;

        [SerializeField, Min(1f)]
        [Tooltip("Top speed multiplier while the Sprint input is held.")]
        float m_SprintMultiplier = k_DefaultSprintMultiplier;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Top speed multiplier while moving backwards. 1 = no penalty.")]
        float m_BackwardSpeedMultiplier = k_DefaultBackwardSpeedMultiplier;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Top speed multiplier while strafing sideways. Blended smoothly with the " +
                 "forward/backward multiplier, so a diagonal is not a sudden change of gear.")]
        float m_StrafeSpeedMultiplier = k_DefaultStrafeSpeedMultiplier;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Top speed multiplier while the Crouch input is held. This is the only thing " +
                 "that reads MoveCommand.Crouch. The capsule does not resize.")]
        float m_CrouchSpeedMultiplier = k_DefaultCrouchSpeedMultiplier;

        [Header("Control")]
        [SerializeField, Min(0f)]
        [Tooltip("How fast velocity climbs toward the commanded speed, in m/s². Far above " +
                 "anything physical on purpose: this is responsiveness, not realism.")]
        float m_Acceleration = k_DefaultAcceleration;

        [SerializeField, Min(0f)]
        [Tooltip("How fast velocity bleeds off when there is no input, in m/s². Higher stops " +
                 "sharper.")]
        float m_Deceleration = k_DefaultDeceleration;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Scales Acceleration while airborne. 0 = no steering in the air.")]
        float m_AirControl = k_DefaultAirControl;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("Scales Deceleration while airborne: how much the character coasts after " +
                 "releasing input mid-jump. Separate from Air Control so drifting and steering " +
                 "can be tuned apart.")]
        float m_AirDeceleration = k_DefaultAirDeceleration;

        /// <summary>
        /// Default <see cref="Gravity"/>: -9.81 m/s², Earth.
        /// </summary>
        /// <remarks>
        /// Y axis only; the capsule is always upright. Platformers often use 1.5-3x Earth. Jump height
        /// depends on this and <see cref="JumpProcessor.JumpSpeed"/> together (<c>height = v² / 2g</c>).
        /// </remarks>
        internal const float k_DefaultGravity = -9.81f;

        /// <summary>
        /// Default <see cref="FallGravityMultiplier"/>: 1, a symmetric jump arc.
        /// </summary>
        /// <remarks>
        /// Around 1.5-2.5 gives a jump that hangs on the way up and returns quickly.
        /// </remarks>
        internal const float k_DefaultFallGravityMultiplier = 1f;

        /// <summary>
        /// Default <see cref="MaxFallSpeed"/>: 55 m/s, roughly human terminal velocity.
        /// </summary>
        /// <remarks>
        /// At default gravity this takes about 5.6 s of fall to reach. It stops a long drop from
        /// building a speed that tunnels through geometry in one tick. 0 disables the cap.
        /// </remarks>
        internal const float k_DefaultMaxFallSpeed = 55f;

        /// <summary>
        /// Default <see cref="BackwardSpeedMultiplier"/>: 1, no penalty for walking backwards.
        /// </summary>
        /// <remarks>
        /// Lower it if retreating should be slower than advancing.
        /// </remarks>
        internal const float k_DefaultBackwardSpeedMultiplier = 1f;

        /// <summary>Default <see cref="StrafeSpeedMultiplier"/>: 1, no penalty for strafing.</summary>
        internal const float k_DefaultStrafeSpeedMultiplier = 1f;

        /// <summary>
        /// Default <see cref="CrouchSpeedMultiplier"/>: 0.5.
        /// </summary>
        /// <remarks>
        /// Holding crouch halves top speed. The capsule does not resize.
        /// </remarks>
        internal const float k_DefaultCrouchSpeedMultiplier = 0.5f;

        /// <summary>
        /// Default <see cref="AirDeceleration"/>: 0.3, matching <see cref="AirControl"/>.
        /// </summary>
        /// <remarks>
        /// Air control is how much you can steer a jump; air deceleration is how much you coast after
        /// letting go of the stick.
        /// </remarks>
        internal const float k_DefaultAirDeceleration = 0.3f;

        /// <summary>
        /// Default <see cref="MaxSpeed"/>: 5 m/s.
        /// </summary>
        /// <remarks>
        /// A brisk run (walking is about 1.4 m/s). The <see cref="KinematicMover.GroundSnapDistance"/>
        /// default is sized for 10 m/s downhill; going faster can make the character flicker airborne.
        /// </remarks>
        internal const float k_DefaultMaxSpeed = 5f;

        /// <summary>
        /// Default <see cref="SprintMultiplier"/>: 1.6x, so sprint is 8 m/s at the default
        /// <see cref="MaxSpeed"/>.
        /// </summary>
        /// <remarks>
        /// A multiplier, so tuning <see cref="MaxSpeed"/> keeps the ratio between walk and sprint.
        /// </remarks>
        internal const float k_DefaultSprintMultiplier = 1.6f;

        /// <summary>
        /// Default <see cref="Acceleration"/>: 50 m/s², which reaches the default
        /// <see cref="MaxSpeed"/> in 0.1 s (6 ticks at 60 Hz).
        /// </summary>
        /// <remarks>
        /// Far above physical on purpose: in first person a slow ramp reads as input lag.
        /// </remarks>
        internal const float k_DefaultAcceleration = 50f;

        /// <summary>
        /// Default <see cref="Deceleration"/>: 60 m/s², stopping from the default
        /// <see cref="MaxSpeed"/> in about 0.083 s (5 ticks at 60 Hz).
        /// </summary>
        /// <remarks>
        /// Slightly higher than <see cref="Acceleration"/> so stopping feels more definite than starting.
        /// </remarks>
        internal const float k_DefaultDeceleration = 60f;

        /// <summary>
        /// Default <see cref="AirControl"/>: 0.3, so airborne steering is 30% as strong as
        /// grounded.
        /// </summary>
        /// <remarks>
        /// Zero commits a jump to its starting direction; 1 makes a jump feel like walking in the air.
        /// </remarks>
        internal const float k_DefaultAirControl = 0.3f;

        /// <summary>
        /// Downward acceleration in m/s², applied on the Y axis whenever the mover is not resting
        /// on ground. Default <c>-9.81</c>. Negative is down.
        /// </summary>
        public float Gravity => m_Gravity;

        /// <summary>Top horizontal speed without sprint, in m/s. Default <c>5</c>.</summary>
        public float MaxSpeed => m_MaxSpeed;

        /// <summary>
        /// Multiplier applied to <see cref="MaxSpeed"/> while <see cref="MoveCommand.Sprint"/> is
        /// held. Default <c>1.6</c>.
        /// </summary>
        public float SprintMultiplier => m_SprintMultiplier;

        /// <summary>
        /// How fast horizontal velocity approaches the commanded velocity, in m/s². Default
        /// <c>50</c>.
        /// </summary>
        public float Acceleration => m_Acceleration;

        /// <summary>
        /// How fast horizontal velocity approaches zero with no input, in m/s². Default
        /// <c>60</c>.
        /// </summary>
        public float Deceleration => m_Deceleration;

        /// <summary>
        /// Fraction of <see cref="Acceleration"/> available while airborne, 0-1. Default
        /// <c>0.3</c>. Deceleration in air is <see cref="AirDeceleration"/>.
        /// </summary>
        public float AirControl => m_AirControl;

        /// <summary>
        /// Fraction of <see cref="Deceleration"/> available while airborne (0-1): how much the
        /// character coasts after input is released mid-jump. Default <c>0.3</c>.
        /// </summary>
        public float AirDeceleration => m_AirDeceleration;

        /// <summary>
        /// Multiplier applied to <see cref="Gravity"/> while descending, so the jump arc can be
        /// asymmetric. Default <c>1</c> (symmetric).
        /// </summary>
        public float FallGravityMultiplier => m_FallGravityMultiplier;

        /// <summary>
        /// Terminal velocity in m/s, or <c>0</c> for uncapped. Default <c>55</c>.
        /// </summary>
        public float MaxFallSpeed => m_MaxFallSpeed;

        /// <summary>
        /// Multiplier applied to <see cref="MaxSpeed"/> when moving backwards relative to facing,
        /// 0-1. Default <c>1</c> (no penalty).
        /// </summary>
        public float BackwardSpeedMultiplier => m_BackwardSpeedMultiplier;

        /// <summary>
        /// Multiplier applied to <see cref="MaxSpeed"/> when strafing, 0-1, blended with
        /// <see cref="BackwardSpeedMultiplier"/> across diagonals. Default <c>1</c>.
        /// </summary>
        public float StrafeSpeedMultiplier => m_StrafeSpeedMultiplier;

        /// <summary>
        /// Multiplier applied to <see cref="MaxSpeed"/> while <see cref="MoveCommand.Crouch"/> is
        /// held, 0-1. Default <c>0.5</c>. The capsule does not resize.
        /// </summary>
        public float CrouchSpeedMultiplier => m_CrouchSpeedMultiplier;

        /// <summary>
        /// Applies gravity to the vertical axis and steers the horizontal axis toward the
        /// commanded velocity.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The command stays horizontal on slopes; the solver slides the capsule up the face.
        /// </para>
        /// <para>
        /// A grounded mover that is not rising has vertical velocity zeroed, so standing still does not
        /// build up downward speed. Horizontal velocity moves toward the target and never overshoots it;
        /// speed above the cap bleeds off at <see cref="Acceleration"/> while input is held.
        /// </para>
        /// </remarks>
        public override void UpdateVelocity(ref MoveState state, in MoveCommand command, float deltaTime)
        {
            Vector3 velocity = state.Velocity;

            if (state.IsGrounded && velocity.y <= 0f)
            {
                velocity.y = 0f;
            }
            else
            {
                // Test velocity, not IsGrounded, so the heavier pull starts exactly at the apex.
                float scale = velocity.y < 0f ? m_FallGravityMultiplier : 1f;
                velocity.y += m_Gravity * scale * deltaTime;

                if (m_MaxFallSpeed > 0f && velocity.y < -m_MaxFallSpeed)
                    velocity.y = -m_MaxFallSpeed;
            }

            var wish = new Vector3(command.MoveDirection.x, 0f, command.MoveDirection.z);

            // Clamp, never normalize: a half-deflected stick must stay half.
            float wishMagnitude = wish.magnitude;
            if (wishMagnitude > 1f)
                wish /= wishMagnitude;

            float maxSpeed = m_MaxSpeed * (command.Sprint ? m_SprintMultiplier : 1f);
            if (command.Crouch)
                maxSpeed *= m_CrouchSpeedMultiplier;

            maxSpeed *= DirectionalSpeedMultiplier(wish, state.Rotation);

            Vector3 target = wish * maxSpeed;

            bool accelerating = target.sqrMagnitude > 0f;
            float rate = accelerating ? m_Acceleration : m_Deceleration;
            if (!state.IsGrounded)
                rate *= accelerating ? m_AirControl : m_AirDeceleration;

            var horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, target, rate * deltaTime);

            state.Velocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        }

        /// <summary>
        /// The top-speed multiplier for the direction <paramref name="wish"/> points, relative to
        /// the facing in <paramref name="rotation"/>.
        /// </summary>
        float DirectionalSpeedMultiplier(Vector3 wish, Quaternion rotation)
        {
            if (m_BackwardSpeedMultiplier >= 1f && m_StrafeSpeedMultiplier >= 1f)
                return 1f;

            float magnitude = wish.magnitude;
            if (magnitude <= 0f)
                return 1f;

            Vector3 local = Quaternion.Inverse(rotation) * (wish / magnitude);

            float alongAxis = local.z >= 0f ? 1f : m_BackwardSpeedMultiplier;
            float sideways = Mathf.Clamp01(Mathf.Abs(local.x));

            return Mathf.Lerp(alongAxis, m_StrafeSpeedMultiplier, sideways);
        }

        /// <summary>
        /// Does nothing: <see cref="YawRotationProcessor"/> owns facing.
        /// </summary>
        public override void UpdateRotation(ref MoveState state, in MoveCommand command, float deltaTime)
        {
        }
    }
}
