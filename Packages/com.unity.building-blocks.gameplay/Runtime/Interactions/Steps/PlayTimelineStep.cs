using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Playables;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Plays a <see cref="PlayableDirector"/> (usually a Timeline) and waits until it is done.
    /// </summary>
    /// <remarks>
    /// Empty uses the director on the interactable's GameObject. Needs no Timeline package to compile.
    /// A Timeline can take and return control itself by calling <see cref="Character.Lock"/> and
    /// <see cref="Character.Unlock"/> from a Signal.
    /// </remarks>
    [Serializable]
    [InteractionStep("Play Timeline")]
    public sealed class PlayTimelineStep : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("The Playable Director to play. Empty looks for one on the interactable's GameObject.")]
        PlayableDirector m_Director;

        [SerializeField]
        [Tooltip("Hold the interactable until the director stops. Off fires it and moves on.")]
        bool m_WaitUntilFinished = true;

        /// <summary>Director found on the interactable when <see cref="m_Director"/> is empty. Not serialized, so the fallback follows the object.</summary>
        [NonSerialized]
        PlayableDirector m_Fallback;

        /// <summary>The director this plays. Empty resolves the one on the interactable's GameObject.</summary>
        public PlayableDirector Director
        {
            get => m_Director;
            set => m_Director = value;
        }

        /// <summary>Whether the interactable waits for the director to stop. Default <c>true</c>.</summary>
        public bool WaitUntilFinished
        {
            get => m_WaitUntilFinished;
            set => m_WaitUntilFinished = value;
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            PlayableDirector director = Resolve(interaction);
            if (director == null)
                yield break;

            director.Play();

            if (!m_WaitUntilFinished)
                yield break;

            // Let the director take its first step before asking whether it is still going.
            yield return null;

            while (director != null && director.state == PlayState.Playing)
                yield return null;
        }

        /// <summary>Stops the director where it is.</summary>
        public override void Stop(Interaction interaction)
        {
            PlayableDirector director = Resolve(interaction);
            if (director != null && director.state == PlayState.Playing)
                director.Stop();
        }

        PlayableDirector Resolve(Interaction interaction)
        {
            if (m_Director != null)
                return m_Director;

            if (m_Fallback == null && interaction?.Interactable != null)
                m_Fallback = interaction.Interactable.GetComponent<PlayableDirector>();

            return m_Fallback;
        }
    }
}
