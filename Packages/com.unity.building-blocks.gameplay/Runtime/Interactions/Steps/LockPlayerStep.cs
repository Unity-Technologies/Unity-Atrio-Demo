using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>What a <see cref="LockPlayerStep"/> takes from the player.</summary>
    public enum PlayerLockScope
    {
        /// <summary>Walking and looking: the view follows the body until control comes back.</summary>
        [InspectorName("Movement and look")]
        MovementAndLook = 0,

        /// <summary>
        /// Walking only: the player can still look around. A Move Player To or Look At step that turns or
        /// tilts the player takes the view while it runs and hands it back when it ends.
        /// </summary>
        [InspectorName("Movement only")]
        MovementOnly = 1,
    }

    /// <summary>
    /// Takes the player's control (input and simulation) for the rest of the run, so later steps can
    /// move or animate the player. Usually the first step of a door or a cinematic.
    /// </summary>
    /// <remarks>
    /// Control returns when the run ends or stops, or earlier through a Release Control step. Needs an
    /// <see cref="IControlLock"/> on the player (<c>Character</c> implements it); without one the
    /// interactable warns once. An interactable with this step does not play while another holds the player.
    /// Set <see cref="Lock"/> to movement only to leave looking free; that needs an <see cref="IFreeLook"/>
    /// on the player (the First Person Camera Rig), and without one the view stays locked too.
    /// </remarks>
    [Serializable]
    [InteractionStep("Lock Player")]
    public sealed class LockPlayerStep : InteractionStep
    {
        [SerializeField]
        [Tooltip("What the player loses until the run ends: walking and looking, or walking only so they can " +
                 "still look around.")]
        PlayerLockScope m_Lock = PlayerLockScope.MovementAndLook;

        /// <summary>What the player loses. Default <see cref="PlayerLockScope.MovementAndLook"/>.</summary>
        public PlayerLockScope Lock
        {
            get => m_Lock;
            set => m_Lock = value;
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            interaction?.Interactable?.TryLockPlayer(m_Lock);
            return null;
        }
    }
}
