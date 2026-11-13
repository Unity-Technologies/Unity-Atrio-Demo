using System.Runtime.CompilerServices;

// Character reads raw command bits through this grant, so InputSlot needs no public bypass.
[assembly: InternalsVisibleTo("Unity.BuildingBlocks.Gameplay.Character")]
