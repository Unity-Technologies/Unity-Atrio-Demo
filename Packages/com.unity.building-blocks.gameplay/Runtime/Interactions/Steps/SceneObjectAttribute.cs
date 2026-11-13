using System;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Marks a step field that points into the scene (a Transform, Animator or GameObject).
    /// Leave it off asset references such as clips and curves.
    /// </summary>
    /// <remarks>
    /// Has no run-time effect: the reference is stored on the interactable as usual. It marks the
    /// fields that will become scene bindings if steps can later be saved as assets.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, Inherited = true)]
    public sealed class SceneObjectAttribute : Attribute
    {
    }
}
