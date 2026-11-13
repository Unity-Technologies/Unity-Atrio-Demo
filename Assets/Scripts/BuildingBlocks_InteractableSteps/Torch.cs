using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Replaces the <see cref="RuntimeAnimatorController"/> on a target <see cref="Animator"/> with a replacement controller.
    /// Inspired by <see cref="PlayAnimationStep"/>, without camera animation or blending.
    /// </summary>
    [Serializable]
    [InteractionStep("Torch")]
    public sealed class Torch : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("The Animator to replace the controller on. If left empty, resolves the player's Animator (first Animator under the player character).")]
        Animator m_TargetAnimator;

        [SerializeField]
        [Tooltip("The RuntimeAnimatorController to assign to the target Animator.")]
        RuntimeAnimatorController m_ReplacementController;

        [SerializeField, Min(0.01f)]
        [Tooltip("Playback speed of the target animator after assignment. 1 is normal speed.")]
        float m_Speed = 1f;

        [SerializeField]
        [Tooltip("Optional name of the state in the new controller to play immediately. Leave empty to play default state.")]
        string m_StartingState;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds to hold this step before completing. 0 finishes immediately.")]
        float m_Duration;

        [SerializeField]
        [Tooltip("Put the target Animator back to its original controller when the interactable ends, whether it finished or was stopped.")]
        bool m_RevertWhenRunEnds;

        [Header("Camera Pitch Blend")]
        [SerializeField]
        [Tooltip("Drive a float parameter on the Animator (e.g. 'Blend') based on player camera pitch: -1 when looking down, 0 straight, 1 up.")]
        bool m_LinkPitchToBlend = true;

        [SerializeField]
        [Tooltip("The float parameter in the controller to drive with camera pitch.")]
        string m_PitchParameterName = "Blend";

        struct TargetSnapshot
        {
            public Animator Animator;
            public RuntimeAnimatorController Controller;
            public float Speed;
            public bool AddedDriver;
        }

        TargetSnapshot m_Snapshot;
        bool m_HasSnapshot;
        Interactable m_Watching;
        bool m_WarnedNoTarget;
        bool m_WarnedNoController;

        /// <summary>The Animator to replace the controller on. Resolves to the player if null.</summary>
        public Animator TargetAnimator
        {
            get => m_TargetAnimator;
            set => m_TargetAnimator = value;
        }

        /// <summary>The RuntimeAnimatorController to assign to the target Animator.</summary>
        public RuntimeAnimatorController ReplacementController
        {
            get => m_ReplacementController;
            set => m_ReplacementController = value;
        }

        /// <summary>Playback speed applied to the animator. Default 1.</summary>
        public float Speed
        {
            get => m_Speed;
            set => m_Speed = Mathf.Max(0.01f, value);
        }

        /// <summary>Optional state name to play on assignment.</summary>
        public string StartingState
        {
            get => m_StartingState;
            set => m_StartingState = value;
        }

        /// <summary>Duration to hold the step in seconds. Default 0.</summary>
        public float Duration
        {
            get => m_Duration;
            set => m_Duration = Mathf.Max(0f, value);
        }

        /// <summary>Whether to revert to the previous controller when the interactable ends.</summary>
        public bool RevertWhenRunEnds
        {
            get => m_RevertWhenRunEnds;
            set => m_RevertWhenRunEnds = value;
        }

        /// <summary>Whether to drive the pitch parameter with camera pitch.</summary>
        public bool LinkPitchToBlend
        {
            get => m_LinkPitchToBlend;
            set => m_LinkPitchToBlend = value;
        }

        /// <summary>The name of the pitch blend parameter. Default "Blend".</summary>
        public string PitchParameterName
        {
            get => m_PitchParameterName;
            set => m_PitchParameterName = value;
        }

        /// <summary>Alias for <see cref="TargetAnimator"/>.</summary>
        public Animator Target
        {
            get => TargetAnimator;
            set => TargetAnimator = value;
        }

        /// <summary>Alias for <see cref="ReplacementController"/>.</summary>
        public RuntimeAnimatorController Controller
        {
            get => ReplacementController;
            set => ReplacementController = value;
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            StopWatching();

            Animator target = ResolveTarget(interaction);
            if (target == null)
            {
                if (!m_WarnedNoTarget)
                {
                    m_WarnedNoTarget = true;
                    Debug.LogWarning("[Building Blocks] Torch: no target Animator specified and could not find an Animator on the player.", interaction?.Interactable);
                }
                yield break;
            }

            if (m_ReplacementController == null)
            {
                if (!m_WarnedNoController)
                {
                    m_WarnedNoController = true;
                    Debug.LogWarning("[Building Blocks] Torch: no Replacement Controller assigned.", interaction?.Interactable);
                }
                yield break;
            }

            bool addedDriver = false;
            if (m_LinkPitchToBlend)
            {
                var driver = target.GetComponent<CameraPitchAnimatorDriver>();
                if (driver == null)
                {
                    driver = target.gameObject.AddComponent<CameraPitchAnimatorDriver>();
                    addedDriver = true;
                }
                driver.ParameterName = m_PitchParameterName;
                driver.enabled = true;
            }

            m_Snapshot = new TargetSnapshot
            {
                Animator = target,
                Controller = target.runtimeAnimatorController,
                Speed = target.speed,
                AddedDriver = addedDriver
            };
            m_HasSnapshot = true;


            //Fix: Shoulder should move back to starting position
            if (target.isActiveAndEnabled)
                target.WriteDefaultValues();
            
            target.writeDefaultValuesOnDisable = true;

            target.runtimeAnimatorController = m_ReplacementController;
            target.speed = m_Speed;

            if (!string.IsNullOrEmpty(m_StartingState))
                target.Play(m_StartingState, 0, 0f);

            Interactable interactable = interaction?.Interactable;
            if (m_RevertWhenRunEnds && interactable != null)
            {
                m_Watching = interactable;
                interactable.Finished += OnInteractableEnded;
                interactable.Interrupted += OnInteractableEnded;
            }

            if (m_Duration > 0f)
                yield return interaction.Wait(m_Duration);
            else
                yield break;
        }

        /// <inheritdoc/>
        public override void Stop(Interaction interaction)
        {
            Revert();
        }

        /// <summary>
        /// Restores the target Animator to its controller and speed captured prior to execution.
        /// </summary>
        public void Revert()
        {
            StopWatching();

            if (m_HasSnapshot && m_Snapshot.Animator != null)
            {
                m_Snapshot.Animator.runtimeAnimatorController = m_Snapshot.Controller;
                m_Snapshot.Animator.speed = m_Snapshot.Speed;

                if (m_Snapshot.AddedDriver)
                {
                    var driver = m_Snapshot.Animator.GetComponent<CameraPitchAnimatorDriver>();
                    if (driver != null)
                        UnityEngine.Object.Destroy(driver);
                }
            }

            m_HasSnapshot = false;
            m_Snapshot = default;
        }

        Animator ResolveTarget(Interaction interaction)
        {
            if (m_TargetAnimator != null)
                return m_TargetAnimator;

            KinematicMover actor = interaction?.Player;
            Animator playerAnim = actor != null ? actor.GetComponentInChildren<Animator>() : null;
            if (playerAnim != null)
                return playerAnim;

            if (interaction?.Interactable != null)
            {
                Animator itAnim = interaction.Interactable.GetComponentInChildren<Animator>();
                if (itAnim != null)
                    return itAnim;
            }

            return null;
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
    }
}
