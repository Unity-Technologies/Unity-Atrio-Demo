using System;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Names an <see cref="InteractionStep"/> subclass in the interactable's "+ Add step" menu:
    /// <c>[InteractionStep("Ring Bell")]</c>. Without it the menu shows the class name minus
    /// <c>Step</c>, spaced out.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class InteractionStepAttribute : Attribute
    {
        /// <summary>What the menu and the row show.</summary>
        public string DisplayName { get; }

        /// <summary>Names the step <paramref name="displayName"/>.</summary>
        public InteractionStepAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
