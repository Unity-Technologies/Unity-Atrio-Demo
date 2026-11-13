using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>The kind of Animator parameter a <see cref="SetAnimatorParameterStep"/> sets.</summary>
    public enum AnimatorParameterKind
    {
        /// <summary>A number with a decimal point. Can ease to its value over <see cref="SetAnimatorParameterStep.Duration"/>.</summary>
        Float,

        /// <summary>A whole number. Can ease to its value.</summary>
        Int,

        /// <summary>True or false.</summary>
        Bool,

        /// <summary>Fires once; the controller resets it after the transition it drives.</summary>
        Trigger,
    }

    /// <summary>
    /// Sets one parameter on an Animator Controller, to drive a state machine the project already has.
    /// For a single clip with no controller, use Play Animation.
    /// </summary>
    /// <remarks>
    /// An empty <see cref="Animator"/> uses the first Animator under the player. A parameter name the
    /// controller does not have is reported once and skipped.
    /// </remarks>
    [Serializable]
    [InteractionStep("Set Animator Parameter")]
    public sealed class SetAnimatorParameterStep : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("The Animator to set it on. Empty uses the player's: the first Animator under the " +
                 "character this interactable acts on.")]
        Animator m_Animator;

        [SerializeField]
        [Tooltip("The parameter's name, spelled the way the Animator Controller spells it.")]
        string m_Parameter;

        [SerializeField]
        [Tooltip("What kind of parameter it is. Float and Int take a value and can ease there; Bool " +
                 "takes true or false; Trigger just fires.")]
        AnimatorParameterKind m_Kind = AnimatorParameterKind.Bool;

        [SerializeField]
        [Tooltip("The value to set.")]
        float m_FloatValue;

        [SerializeField]
        [Tooltip("The value to set.")]
        int m_IntValue;

        [SerializeField]
        [Tooltip("The value to set.")]
        bool m_BoolValue = true;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds to ease a Float or Int to its value. 0 sets it at once. The step holds its " +
                 "group for that long.")]
        float m_Duration;

        bool m_Warned;

        /// <summary>The Animator to write to. <c>null</c> resolves the player's.</summary>
        public Animator Animator
        {
            get => m_Animator;
            set => m_Animator = value;
        }

        /// <summary>The parameter name.</summary>
        public string Parameter
        {
            get => m_Parameter;
            set => m_Parameter = value;
        }

        /// <summary>Float, Int, Bool or Trigger. Default Bool.</summary>
        public AnimatorParameterKind Kind
        {
            get => m_Kind;
            set => m_Kind = value;
        }

        /// <summary>The value written when <see cref="Kind"/> is Float.</summary>
        public float FloatValue
        {
            get => m_FloatValue;
            set => m_FloatValue = value;
        }

        /// <summary>The value written when <see cref="Kind"/> is Int.</summary>
        public int IntValue
        {
            get => m_IntValue;
            set => m_IntValue = value;
        }

        /// <summary>The value written when <see cref="Kind"/> is Bool. Default <c>true</c>.</summary>
        public bool BoolValue
        {
            get => m_BoolValue;
            set => m_BoolValue = value;
        }

        /// <summary>Seconds a Float or Int eases to its value. Default <c>0</c>, at once.</summary>
        public float Duration
        {
            get => m_Duration;
            set => m_Duration = Mathf.Max(0f, value);
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            Animator animator = Resolve(interaction);
            if (animator == null || string.IsNullOrEmpty(m_Parameter))
                yield break;

            if (!TryFind(animator, m_Parameter, out AnimatorControllerParameterType actual))
            {
                WarnOnce(animator, interaction, $"has no parameter named '{m_Parameter}'. Check the spelling against the controller");
                yield break;
            }

            if (!Matches(m_Kind, actual))
                WarnOnce(animator, interaction, $"has '{m_Parameter}' as a {actual}, not a {m_Kind}. The step writes it as a {m_Kind} anyway");

            int id = Animator.StringToHash(m_Parameter);

            switch (m_Kind)
            {
                case AnimatorParameterKind.Trigger:
                    animator.SetTrigger(id);
                    yield break;

                case AnimatorParameterKind.Bool:
                    animator.SetBool(id, m_BoolValue);
                    yield break;

                case AnimatorParameterKind.Int when m_Duration <= 0f:
                    animator.SetInteger(id, m_IntValue);
                    yield break;

                case AnimatorParameterKind.Float when m_Duration <= 0f:
                    animator.SetFloat(id, m_FloatValue);
                    yield break;
            }

            // Ease a number toward its value.
            float from = m_Kind == AnimatorParameterKind.Float ? animator.GetFloat(id) : animator.GetInteger(id);
            float to = m_Kind == AnimatorParameterKind.Float ? m_FloatValue : m_IntValue;
            float elapsed = 0f;

            while (elapsed < m_Duration)
            {
                elapsed += interaction.DeltaTime;
                if (animator == null)
                    yield break;

                float value = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / m_Duration));
                if (m_Kind == AnimatorParameterKind.Float)
                    animator.SetFloat(id, value);
                else
                    animator.SetInteger(id, Mathf.RoundToInt(value));

                yield return null;
            }
        }

        Animator Resolve(Interaction interaction)
        {
            if (m_Animator != null)
                return m_Animator;

            KinematicMover actor = interaction?.Player;
            return actor != null ? actor.GetComponentInChildren<Animator>() : null;
        }

        /// <summary>Whether <paramref name="animator"/>'s controller has a parameter called <paramref name="parameter"/>, and of what type.</summary>
        public static bool TryFind(Animator animator, string parameter, out AnimatorControllerParameterType type)
        {
            type = default;
            if (animator == null || string.IsNullOrEmpty(parameter))
                return false;

            // Allocates the parameter array; a one-off per step, not a tick.
            foreach (AnimatorControllerParameter candidate in animator.parameters)
            {
                if (candidate.name == parameter)
                {
                    type = candidate.type;
                    return true;
                }
            }

            return false;
        }

        static bool Matches(AnimatorParameterKind kind, AnimatorControllerParameterType type)
        {
            switch (kind)
            {
                case AnimatorParameterKind.Float: return type == AnimatorControllerParameterType.Float;
                case AnimatorParameterKind.Int: return type == AnimatorControllerParameterType.Int;
                case AnimatorParameterKind.Bool: return type == AnimatorControllerParameterType.Bool;
                default: return type == AnimatorControllerParameterType.Trigger;
            }
        }

        void WarnOnce(Animator animator, Interaction interaction, string what)
        {
            if (m_Warned)
                return;

            m_Warned = true;
            string controller = animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "(no controller)";
            Debug.LogWarning($"[Building Blocks] Set Animator Parameter: the controller '{controller}' on '{animator.name}' {what}.", interaction?.Interactable);
        }
    }
}
