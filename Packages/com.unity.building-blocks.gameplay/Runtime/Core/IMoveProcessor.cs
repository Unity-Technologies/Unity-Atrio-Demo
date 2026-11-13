namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Gameplay extension hooks called by <see cref="KinematicMover.Simulate"/> each tick,
    /// before collision solving.
    /// </summary>
    /// <remarks>
    /// Every force and ability is a processor that edits <see cref="MoveState"/>. Implementations
    /// must not allocate, read engine time, or touch the visual hierarchy.
    /// </remarks>
    public interface IMoveProcessor
    {
        /// <summary>Modify the state's velocity for this tick (gravity, acceleration, jumps, dashes…).</summary>
        void UpdateVelocity(ref MoveState state, in MoveCommand command, float deltaTime);

        /// <summary>Modify the state's rotation for this tick (facing, turn speed…).</summary>
        void UpdateRotation(ref MoveState state, in MoveCommand command, float deltaTime);
    }
}
