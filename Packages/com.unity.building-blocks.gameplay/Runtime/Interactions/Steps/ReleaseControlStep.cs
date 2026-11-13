using System;
using System.Collections;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Gives the player their control back now, while the rest of the interactable keeps running.
    /// </summary>
    /// <remarks>
    /// Does nothing without a Lock Player step before it. To release partway into a long step, put it
    /// in the same group and give it a <see cref="InteractionStep.Delay"/>. Once released, this run
    /// does not take control again.
    /// </remarks>
    [Serializable]
    [InteractionStep("Release Control")]
    public sealed class ReleaseControlStep : InteractionStep
    {
        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            interaction?.Interactable?.ReleasePlayer();
            return null;
        }
    }
}
