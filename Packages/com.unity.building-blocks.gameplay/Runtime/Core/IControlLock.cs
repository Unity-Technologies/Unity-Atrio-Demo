namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Takes control of a character away from whoever is driving it, and gives it back. Used by
    /// interactables, cutscenes and menus to freeze the player.
    /// </summary>
    /// <remarks>
    /// While locked the character is not simulated and reads no input; whoever holds the lock may
    /// place it with <see cref="KinematicMoverExtensions.Teleport"/>. The shipped implementation is
    /// <c>Character</c>.
    /// </remarks>
    public interface IControlLock
    {
        /// <summary>Whether control is currently held away from the player.</summary>
        bool IsLocked { get; }

        /// <summary>Takes control away. A second call while already locked does nothing.</summary>
        void Lock();

        /// <summary>Gives control back. A call while not locked does nothing.</summary>
        void Unlock();
    }
}
