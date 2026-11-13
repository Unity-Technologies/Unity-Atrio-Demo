using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// One entry in a <see cref="Character"/>'s processor list: walking and gravity, a jump, facing.
    /// Derive from this class to add your own; it appears in the character's "Add a processor"
    /// palette on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain <c>[Serializable]</c> class stored inside the <see cref="Character"/>, not a component:
    /// mark tuning fields <c>[SerializeField]</c> or public. Everything it needs arrives as the
    /// <see cref="MoveState"/> and <see cref="MoveCommand"/> each tick.
    /// </para>
    /// <para>
    /// List order is run order, and every <see cref="UpdateVelocity"/> runs before any
    /// <see cref="UpdateRotation"/>.
    /// </para>
    /// <para>
    /// Keep it stateless: fields hold tuning only; per-tick movement state belongs in
    /// <see cref="MoveState"/>, or replays diverge. No per-tick allocations, no engine time, no
    /// touching the visual hierarchy. To skip the list, register any <see cref="IMoveProcessor"/> on
    /// the <see cref="KinematicMover"/> directly.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class MoveProcessor : IMoveProcessor
    {
        /// <summary>
        /// Off, the processor keeps its place in the list but does not run, from the next tick. Default
        /// <c>true</c>. UnityEvents can use <see cref="Character.EnableProcessor"/> and
        /// <see cref="Character.DisableProcessor"/>.
        /// </summary>
        [Tooltip("Off, this processor keeps its place in the list and does not run.")]
        public bool Enabled = true;

        /// <summary>
        /// Modify the state's velocity for this tick (gravity, acceleration, jumps, dashes…). The default
        /// does nothing.
        /// </summary>
        public virtual void UpdateVelocity(ref MoveState state, in MoveCommand command, float deltaTime)
        {
        }

        /// <summary>
        /// Modify the state's rotation for this tick (facing, turn speed…). The default does nothing.
        /// </summary>
        public virtual void UpdateRotation(ref MoveState state, in MoveCommand command, float deltaTime)
        {
        }
    }
}
