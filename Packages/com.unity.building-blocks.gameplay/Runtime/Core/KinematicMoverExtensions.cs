using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Host-side helpers for acting on a <see cref="KinematicMover"/> from outside
    /// <see cref="KinematicMover.Simulate"/>.
    /// </summary>
    public static class KinematicMoverExtensions
    {
        /// <summary>
        /// Places the mover at a pose with zero velocity, and re-seeds its <see cref="MoveInterpolator"/>
        /// so the visual root arrives on the same frame instead of easing over from the old pose.
        /// </summary>
        /// <remarks>
        /// Use this instead of <see cref="KinematicMover.ApplyState"/> alone, which leaves the visual
        /// at the old pose until the next tick (never, while <see cref="IControlLock"/> is locked).
        /// Grounding is not recomputed; the next tick's probe does that.
        /// </remarks>
        /// <param name="mover">The mover to place.</param>
        /// <param name="position">World position of the feet.</param>
        /// <param name="rotation">World rotation; only its yaw is applied.</param>
        public static void Teleport(this KinematicMover mover, Vector3 position, Quaternion rotation)
        {
            if (mover == null)
                return;

            MoveState state = mover.GetState();
            state.Position = position;
            state.Rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
            state.Velocity = Vector3.zero;
            mover.ApplyState(state);

            if (mover.TryGetComponent<MoveInterpolator>(out var interpolator))
            {
                interpolator.BeginTick(state);
                interpolator.EndTick(state);
                interpolator.Interpolate(1f);
            }
        }
    }
}
