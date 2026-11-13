using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Claims facing: writes the mover's rotation from <see cref="MoveCommand.Yaw"/> each tick,
    /// as a yaw-only rotation about world up.
    /// </summary>
    /// <remarks>
    /// The only built-in processor that turns the character; leave it out for one that should not
    /// turn. Yaw only: the capsule stays upright, and first-person pitch belongs on the camera.
    /// Stateless, so replays reproduce the same facing.
    /// </remarks>
    [Serializable]
    public sealed class YawRotationProcessor : MoveProcessor
    {
        [SerializeField, Min(0f)]
        [Tooltip("Degrees per second the character turns toward the commanded heading. " +
                 "0 = instant, which is the default and what a first-person body wants: a body " +
                 "that lags the camera driving it reads as input latency. Raise above 0 for a " +
                 "third-person character that should visibly turn.")]
        float m_TurnRate = k_DefaultTurnRate;

        /// <summary>
        /// Default <see cref="TurnRate"/>: 0, meaning instant.
        /// </summary>
        /// <remarks>
        /// In first person the camera owns the heading, so any turn rate makes the body trail the view.
        /// </remarks>
        internal const float k_DefaultTurnRate = 0f;

        /// <summary>
        /// Degrees per second the character turns toward <see cref="MoveCommand.Yaw"/>, or
        /// <c>0</c> for instant. Default <c>0</c>.
        /// </summary>
        public float TurnRate => m_TurnRate;

        /// <summary>Does nothing: facing does not author velocity.</summary>
        public override void UpdateVelocity(ref MoveState state, in MoveCommand command, float deltaTime)
        {
        }

        /// <summary>
        /// Sets <see cref="MoveState.Rotation"/> to a yaw-only rotation of
        /// <see cref="MoveCommand.Yaw"/> degrees about world up. Writes nothing else.
        /// </summary>
        /// <remarks>
        /// <see cref="MoveCommand.Yaw"/> is an absolute heading, not a delta. With a
        /// <see cref="TurnRate"/> above 0 the rotation moves toward it at that rate instead.
        /// </remarks>
        public override void UpdateRotation(ref MoveState state, in MoveCommand command, float deltaTime)
        {
            if (m_TurnRate <= 0f)
            {
                state.Rotation = Quaternion.Euler(0f, command.Yaw, 0f);
                return;
            }

            // Read the current heading from MoveState so a snapshot restore reproduces the same turn.
            float current = state.Rotation.eulerAngles.y;
            float yaw = Mathf.MoveTowardsAngle(current, command.Yaw, m_TurnRate * deltaTime);

            state.Rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
