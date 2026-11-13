namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>A component that can turn a press away right now, such as an Interactable the player is not looking at.</summary>
    interface IPressGate
    {
        /// <summary>Whether a press would be taken now. Called every frame a prompt could show in full, so it must not allocate.</summary>
        bool AdmitsPress();
    }
}
