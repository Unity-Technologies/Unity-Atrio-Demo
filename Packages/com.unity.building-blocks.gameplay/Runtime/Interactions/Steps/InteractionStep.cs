using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// One step of an <see cref="Interactable"/>: wait, move the player, play a clip, fire an event.
    /// Derive from it to add your own; it appears in the "+ Add step" menu, named by
    /// <see cref="InteractionStepAttribute"/> or the class name minus <c>Step</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A step is a plain <c>[Serializable]</c> class stored on the <see cref="Interactable"/>, not a
    /// component. Serialize fields as on a MonoBehaviour. Run-time context comes through the
    /// <see cref="Interaction"/>; undo work goes in <see cref="Stop"/>. Mark scene references with
    /// <see cref="SceneObjectAttribute"/>.
    /// </para>
    /// <para>
    /// <see cref="Run"/> is a coroutine: <c>yield return null</c> waits a frame, a yielded
    /// <see cref="IEnumerator"/> runs to its end first. Unity yield instructions such as
    /// <c>WaitForSeconds</c> wait only one frame; use <c>interaction.Wait(seconds)</c>. Private fields
    /// may hold per-run state (one run at a time); reset them at the top of <see cref="Run"/>.
    /// Return an enumerator even for instant work (<c>yield break</c>); returning <c>null</c> means
    /// nothing to do this time.
    /// </para>
    /// </remarks>
    [Serializable]
    public abstract class InteractionStep
    {
        /// <summary>Off, the run skips this step but it keeps its place. Default <c>true</c>.</summary>
        [Tooltip("Off skips this step; it keeps its place in the list.")]
        public bool Enabled = true;

        /// <summary>Seconds after its group starts before this step runs. Default <c>0</c>.</summary>
        [Min(0f)]
        [Tooltip("Seconds after the group starts before this step runs. The other steps in the group " +
                 "are already going by then.")]
        public float Delay;

        /// <summary>Runs the step. The interactable steps the enumerator until it ends.</summary>
        /// <param name="interaction">The run this step is part of: the player, the interactable, the clock.</param>
        /// <returns>An enumerator; <c>null</c> to do nothing this time.</returns>
        public abstract IEnumerator Run(Interaction interaction);

        /// <summary>
        /// Called when the run is cut short while this step is running, before the On Interrupted steps.
        /// Restore anything <see cref="Run"/> changed. Not called for a step that finished or never began.
        /// </summary>
        /// <param name="interaction">The same run <see cref="Run"/> received.</param>
        public virtual void Stop(Interaction interaction)
        {
        }
    }
}
