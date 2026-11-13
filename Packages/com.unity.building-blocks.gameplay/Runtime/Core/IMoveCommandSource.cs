namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Produces the <see cref="MoveCommand"/> that drives a <see cref="KinematicMover"/> each tick.
    /// </summary>
    /// <remarks>
    /// Swap the source to change who drives the character: player input, AI, a cinematic or a
    /// recorded replay all produce the same command struct.
    /// </remarks>
    public interface IMoveCommandSource
    {
        /// <summary>Returns the command for the upcoming simulation tick.</summary>
        MoveCommand ReadCommand(float deltaTime);
    }
}
