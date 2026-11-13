using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The complete simulation state of a <see cref="KinematicMover"/> at one tick.
    /// </summary>
    /// <remarks>
    /// Every value that affects movement lives here; state kept elsewhere breaks re-simulation.
    /// Snapshot with <see cref="KinematicMover.GetState"/> and restore with
    /// <see cref="KinematicMover.ApplyState"/> for rollback or replay. Keep it blittable.
    /// </remarks>
    public struct MoveState
    {
        /// <summary>Built-in movement mode: on the ground.</summary>
        public const byte ModeGrounded = 0;

        /// <summary>Built-in movement mode: airborne.</summary>
        public const byte ModeAirborne = 1;

        /// <summary>
        /// First mode value reserved for user-defined modes (dash, glide, swim, cinematic…).
        /// Values below this are owned by the package. Custom modes don't work yet: the ground probe
        /// overwrites MovementMode after processors run, on the same tick.
        /// </summary>
        public const byte ModeCustomStart = 64;

        /// <summary>
        /// A valid zero state: identity rotation, upward ground normal, airborne.
        /// </summary>
        /// <remarks>
        /// Use this instead of <c>default(MoveState)</c>, whose zero <see cref="Rotation"/> is not a
        /// valid quaternion.
        /// </remarks>
        public static readonly MoveState Default = new MoveState
        {
            Position = Vector3.zero,
            Rotation = Quaternion.identity,
            Velocity = Vector3.zero,
            IsGrounded = false,
            GroundNormal = Vector3.up,
            MovementMode = ModeAirborne,
            TimeSinceGrounded = 0f,
        };

        /// <summary>Root position (feet), world space.</summary>
        public Vector3 Position;

        /// <summary>Root rotation.</summary>
        public Quaternion Rotation;

        /// <summary>Velocity in world space, units per second.</summary>
        public Vector3 Velocity;

        /// <summary>Whether the mover's own ground probe found standable ground this tick.</summary>
        public bool IsGrounded;

        /// <summary>Normal of the ground found by the probe. Valid only when <see cref="IsGrounded"/>.</summary>
        public Vector3 GroundNormal;

        /// <summary>
        /// Current movement mode. See the Mode* constants. Custom modes don't work yet: the ground
        /// probe overwrites MovementMode after processors run, on the same tick.
        /// </summary>
        public byte MovementMode;

        /// <summary>
        /// Seconds since the mover last had standable ground under it: <c>0</c> on any tick that
        /// ends grounded, and one <c>deltaTime</c> more on each tick that does not.
        /// </summary>
        /// <remarks>
        /// Read it for coyote time. Written after the ground snap, so a snapped mover reads <c>0</c>.
        /// Not clamped.
        /// </remarks>
        public float TimeSinceGrounded;
    }
}
