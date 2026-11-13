using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>When a <see cref="StepGroup"/> is done and the next one may start.</summary>
    public enum StepGroupAdvance
    {
        /// <summary>The next group starts once every step in this one has finished. The default.</summary>
        WhenAllFinish,

        /// <summary>The next group starts as soon as one step here finishes; the others keep running until the run ends or stops.</summary>
        WhenAnyFinishes,
    }

    /// <summary>
    /// Steps that start together. An <see cref="Interactable"/> runs its groups in order; the next
    /// group starts when this one is done by its <see cref="Advance"/> rule.
    /// </summary>
    /// <remarks>
    /// Each enabled step starts after its own <see cref="InteractionStep.Delay"/>. A group with no
    /// enabled steps, or no steps at all, is done at once and the next group starts the same frame, so an
    /// empty group can stand as a named placeholder.
    /// </remarks>
    [Serializable]
    public sealed class StepGroup
    {
        /// <summary>Optional label shown in the inspector: "Approach", "The door opens". Empty is fine.</summary>
        [Tooltip("A label for this group. Optional.")]
        public string Name;

        /// <summary>When the next group starts. Default <see cref="StepGroupAdvance.WhenAllFinish"/>.</summary>
        [Tooltip("When All Finish: the next group waits for every step here. When Any Finishes: the next " +
                 "group starts as soon as one step here ends; the rest keep running.")]
        public StepGroupAdvance Advance = StepGroupAdvance.WhenAllFinish;

        /// <summary>The steps that start together. Any <see cref="InteractionStep"/> subclass, yours included.</summary>
        [SerializeReference]
        public List<InteractionStep> Steps = new List<InteractionStep>();

        /// <summary>An empty group.</summary>
        public StepGroup()
        {
        }

        /// <summary>A group of <paramref name="steps"/> that advances when all of them finish.</summary>
        public StepGroup(params InteractionStep[] steps)
        {
            if (steps != null) Steps.AddRange(steps);
        }

        /// <summary>A group of <paramref name="steps"/> with the given advance rule.</summary>
        public StepGroup(StepGroupAdvance advance, params InteractionStep[] steps) : this(steps)
        {
            Advance = advance;
        }
    }
}
