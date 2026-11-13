using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Fires a UnityEvent: call any function wired in the Inspector, such as toggling a <c>Light</c>.
    /// </summary>
    /// <remarks>
    /// The argument is the player's root GameObject, or <c>null</c> when there is no player.
    /// </remarks>
    [Serializable]
    [InteractionStep("Unity Event")]
    public sealed class UnityEventStep : InteractionStep
    {
        [SerializeField]
        [Tooltip("What happens. The argument is the player's root GameObject, or nothing.")]
        UnityEvent<GameObject> m_OnApplied = new UnityEvent<GameObject>();

        /// <summary>Inspector event: the step fired.</summary>
        public UnityEvent<GameObject> OnApplied => m_OnApplied;

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            KinematicMover actor = interaction?.Player;
            m_OnApplied.Invoke(actor != null ? actor.gameObject : null);
            return null;
        }
    }
}
