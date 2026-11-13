using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>Holds the interactable for a number of seconds. The pause between two other steps.</summary>
    [Serializable]
    [InteractionStep("Wait")]
    public sealed class WaitStep : InteractionStep
    {
        [SerializeField, Min(0f)]
        [Tooltip("Seconds to wait before the group is done.")]
        float m_Seconds = 1f;

        /// <summary>Seconds to wait. Default <c>1</c>.</summary>
        public float Seconds
        {
            get => m_Seconds;
            set => m_Seconds = Mathf.Max(0f, value);
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction) => interaction.Wait(m_Seconds);
    }
}
