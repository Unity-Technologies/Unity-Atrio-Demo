using System.Runtime.CompilerServices;

// The layout planner and the bridge's lifecycle hooks stay internal. The inspector and the Pipeline
// commands read the plan through these grants.
[assembly: InternalsVisibleTo("Unity.BuildingBlocks.TouchInput.Editor")]
[assembly: InternalsVisibleTo("Unity.BuildingBlocks.TouchInput.Pipeline")]
