namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Names the custom buttons a command source puts into <see cref="MoveCommand.PressedButtons"/>
    /// and <see cref="MoveCommand.HeldButtons"/>: which bit is "Dash".
    /// </summary>
    /// <remarks>
    /// Implement on the command source alongside <see cref="IMoveCommandSource"/>. The host calls it
    /// once per registration to bind each processor's <see cref="InputSlot"/>, never per tick.
    /// Swapping sources rebinds every processor, so record the layout together with any recorded commands.
    /// </remarks>
    public interface IMoveCommandLayout
    {
        /// <summary>
        /// The custom button called <paramref name="name"/> (0 to 31), or <see cref="InputSlot.Unbound"/>
        /// when there is none. Case and surrounding spaces are ignored.
        /// </summary>
        int FindButton(string name);
    }
}
