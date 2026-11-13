using System.Runtime.CompilerServices;

// The Pipeline commands reuse the tuning internals (rig scan, quiet apply, JSON writer) instead of copying them.
[assembly: InternalsVisibleTo("Unity.BuildingBlocks.TouchInput.Pipeline")]

// The Input System assembly reports what the hardware says (TouchInputHints) for TouchRig.ShowWhen.
[assembly: InternalsVisibleTo("Unity.BuildingBlocks.TouchInput.InputSystem")]
