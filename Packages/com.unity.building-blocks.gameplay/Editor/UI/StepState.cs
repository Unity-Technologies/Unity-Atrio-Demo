namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>Where a row stands in the current run, for lists that show one live.</summary>
    public enum StepState
    {
        /// <summary>Nothing is running.</summary>
        None,

        /// <summary>A run is in progress and has not reached this row yet.</summary>
        Pending,

        /// <summary>This row is the one running.</summary>
        Running,

        /// <summary>The run has passed this row.</summary>
        Done,
    }
}
