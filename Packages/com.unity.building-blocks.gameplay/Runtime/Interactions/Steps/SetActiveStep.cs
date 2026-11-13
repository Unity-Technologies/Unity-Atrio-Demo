using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>What <see cref="SetActiveStep"/> does to each of its targets.</summary>
    public enum SetActiveMode
    {
        /// <summary>Switch every target on.</summary>
        On,

        /// <summary>Switch every target off.</summary>
        Off,

        /// <summary>Flip each target from whatever it is.</summary>
        Toggle,
    }

    /// <summary>
    /// Switches GameObjects, or any component with an enabled checkbox, on or off.
    /// </summary>
    /// <remarks>
    /// Targets can be GameObjects, Behaviours, Renderers and Colliders; anything else is skipped with
    /// one warning. With <see cref="RevertWhenRunEnds"/> on, targets go back when the run ends or stops.
    /// </remarks>
    [Serializable]
    [InteractionStep("Set Active")]
    public sealed class SetActiveStep : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("What to switch: GameObjects, or components with an enabled checkbox: a Light, a " +
                 "Renderer, a Collider, one of your scripts.")]
        Object[] m_Targets = Array.Empty<Object>();

        [SerializeField]
        [Tooltip("On and Off set every target. Toggle flips each one from whatever it is now.")]
        SetActiveMode m_Mode = SetActiveMode.On;

        [SerializeField]
        [Tooltip("Put every target back the way it was when the interactable ends, whether it finished or " +
                 "was stopped. For a prompt or a glow that should only show during the run.")]
        bool m_RevertWhenRunEnds;

        bool[] m_Before;
        Interactable m_Watching;
        bool m_WarnedUnsupported;

        /// <summary>The objects and components this switches.</summary>
        public Object[] Targets
        {
            get => m_Targets;
            set => m_Targets = value ?? Array.Empty<Object>();
        }

        /// <summary>On, Off or Toggle. Default On.</summary>
        public SetActiveMode Mode
        {
            get => m_Mode;
            set => m_Mode = value;
        }

        /// <summary>Whether the targets go back to their previous state when the run ends. Default <c>false</c>.</summary>
        public bool RevertWhenRunEnds
        {
            get => m_RevertWhenRunEnds;
            set => m_RevertWhenRunEnds = value;
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            StopWatching();

            if (m_Before == null || m_Before.Length != m_Targets.Length)
                m_Before = new bool[m_Targets.Length];

            for (int i = 0; i < m_Targets.Length; i++)
            {
                Object target = m_Targets[i];
                if (!TryGetState(target, out bool was))
                {
                    WarnUnsupported(target, interaction);
                    continue;
                }

                m_Before[i] = was;

                bool next = m_Mode == SetActiveMode.On || (m_Mode == SetActiveMode.Toggle && !was);
                SetState(target, next);
            }

            Interactable interactable = interaction?.Interactable;
            if (m_RevertWhenRunEnds && interactable != null)
            {
                m_Watching = interactable;
                interactable.Finished += OnInteractableEnded;
                interactable.Interrupted += OnInteractableEnded;
            }

            return null;
        }

        /// <summary>
        /// Puts every target back to its state before <see cref="Run"/> ran. Called for you when
        /// <see cref="RevertWhenRunEnds"/> is on.
        /// </summary>
        public void Revert()
        {
            StopWatching();

            if (m_Before == null)
                return;

            int count = Mathf.Min(m_Before.Length, m_Targets.Length);
            for (int i = 0; i < count; i++)
            {
                if (TryGetState(m_Targets[i], out _))
                    SetState(m_Targets[i], m_Before[i]);
            }
        }

        /// <summary>Whether <paramref name="target"/> can be switched, and its current active or enabled state.</summary>
        public static bool TryGetState(Object target, out bool state)
        {
            switch (target)
            {
                case GameObject go: state = go.activeSelf; return true;
                case Behaviour behaviour: state = behaviour.enabled; return true;
                case Renderer renderer: state = renderer.enabled; return true;
                case Collider collider: state = collider.enabled; return true;
                default: state = false; return false;
            }
        }

        internal static void SetState(Object target, bool state)
        {
            switch (target)
            {
                case GameObject go: go.SetActive(state); break;
                case Behaviour behaviour: behaviour.enabled = state; break;
                case Renderer renderer: renderer.enabled = state; break;
                case Collider collider: collider.enabled = state; break;
            }
        }

        void OnInteractableEnded(Interactable interactable) => Revert();

        void StopWatching()
        {
            if (m_Watching == null)
                return;

            m_Watching.Finished -= OnInteractableEnded;
            m_Watching.Interrupted -= OnInteractableEnded;
            m_Watching = null;
        }

        void WarnUnsupported(Object target, Interaction interaction)
        {
            if (m_WarnedUnsupported || target == null)
                return;

            m_WarnedUnsupported = true;
            Debug.LogWarning(
                $"[Building Blocks] Set Active: '{target.name}' ({target.GetType().Name}) has no on/off switch this " +
                "step knows. Drop in the GameObject, or a Behaviour, Renderer or Collider on it.", interaction?.Interactable);
        }
    }
}
