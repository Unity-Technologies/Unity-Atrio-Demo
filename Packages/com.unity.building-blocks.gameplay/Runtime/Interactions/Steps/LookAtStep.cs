using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Turns the player to look at a target: the body turns to face it and the view tilts to meet it.
    /// </summary>
    /// <remarks>
    /// Needs a Lock Player step before it. The view tilts through <see cref="ILookPitch"/> (the First
    /// Person Camera Rig); without one only the body turns. In the same group as a Move To Pose, turn
    /// <see cref="TurnBody"/> off. Nothing is undone on stop.
    /// </remarks>
    [Serializable]
    [InteractionStep("Look At")]
    public sealed class LookAtStep : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("What the player ends up looking at: the door handle, a lever.")]
        Transform m_Target;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds to turn the head. 0 snaps.")]
        float m_Duration = k_DefaultDuration;

        [SerializeField]
        [Tooltip("Easing: input is elapsed 0-1, output is how far around the view has turned.")]
        AnimationCurve m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        [Tooltip("Turn the body to face the target, the way Move Player To does. Turn it off when this " +
                 "runs in the same group as a Move Player To, which sets the facing itself.")]
        bool m_TurnBody = true;

        [SerializeField]
        [Tooltip("Tilt the view up or down to meet the target. Needs a camera rig on the character the " +
                 "interactable can reach; the First Person Camera Rig is one. Off leaves the pitch alone.")]
        bool m_TiltView = true;

        /// <summary>Default <see cref="Duration"/> in seconds.</summary>
        const float k_DefaultDuration = 0.4f;

        bool m_Warned;
        bool m_HoldingView;

        /// <summary>What to look at.</summary>
        public Transform Target
        {
            get => m_Target;
            set => m_Target = value;
        }

        /// <summary>Seconds of easing. Default <c>0.4</c>; <c>0</c> snaps.</summary>
        public float Duration
        {
            get => m_Duration;
            set => m_Duration = Mathf.Max(0f, value);
        }

        /// <summary>Easing curve, 0-1 in, 0-1 out.</summary>
        public AnimationCurve Curve
        {
            get => m_Curve;
            set => m_Curve = value;
        }

        /// <summary>Whether the body turns to face the target. Default <c>true</c>.</summary>
        public bool TurnBody
        {
            get => m_TurnBody;
            set => m_TurnBody = value;
        }

        /// <summary>Whether the view tilts to meet the target. Default <c>true</c>.</summary>
        public bool TiltView
        {
            get => m_TiltView;
            set => m_TiltView = value;
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            KinematicMover actor = interaction?.Player;
            if (actor == null || m_Target == null)
                yield break;

            ILookPitch look = m_TiltView ? actor.GetComponentInChildren<ILookPitch>() : null;
            if (!m_TurnBody && look == null)
                yield break;

            if (m_TurnBody)
                WarnIfUnlocked(actor, interaction);

            HoldView(interaction);
            IEnumerator turn = Turn(actor, look, interaction);
            try
            {
                while (turn.MoveNext())
                    yield return turn.Current;
            }
            finally
            {
                ReleaseView(interaction);
            }
        }

        /// <summary>A view taken from a player who kept looking goes back to them; nothing else is undone.</summary>
        public override void Stop(Interaction interaction) => ReleaseView(interaction);

        IEnumerator Turn(KinematicMover actor, ILookPitch look, Interaction interaction)
        {
            MoveState state = actor.GetState();
            float yawFrom = state.Rotation.eulerAngles.y;
            float pitchFrom = look != null ? look.Pitch : 0f;

            if (m_Duration <= 0f)
            {
                Aim(actor, look, yawFrom, pitchFrom, 1f);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < m_Duration)
            {
                elapsed += interaction.DeltaTime;

                if (actor == null || m_Target == null)
                    yield break;

                float t = Mathf.Clamp01(elapsed / m_Duration);
                float along = m_Curve != null ? m_Curve.Evaluate(t) : t;
                Aim(actor, look, yawFrom, pitchFrom, along);

                yield return null;
            }
        }

        /// <summary>The yaw and pitch, in degrees, from <paramref name="eye"/> to <paramref name="target"/>.</summary>
        public static void AnglesTo(Vector3 eye, Vector3 target, out float yaw, out float pitch)
        {
            Vector3 to = target - eye;
            yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float flat = new Vector2(to.x, to.z).magnitude;

            // Negative pitch looks up, matching the camera's local X rotation.
            pitch = -Mathf.Atan2(to.y, flat) * Mathf.Rad2Deg;
        }

        void Aim(KinematicMover actor, ILookPitch look, float yawFrom, float pitchFrom, float along)
        {
            MoveState state = actor.GetState();
            Vector3 eye = look != null ? look.EyePosition : state.Position;
            AnglesTo(eye, m_Target.position, out float yaw, out float pitch);

            if (m_TurnBody)
                actor.Teleport(state.Position, Quaternion.Euler(0f, Mathf.LerpAngle(yawFrom, yaw, along), 0f));

            if (look != null)
                look.SetPitch(Mathf.Lerp(pitchFrom, pitch, along));
        }

        // Under a movement-only lock the player keeps looking; the view is this step's while it turns them.
        void HoldView(Interaction interaction)
        {
            if (m_HoldingView || interaction?.Interactable == null)
                return;

            m_HoldingView = true;
            interaction.Interactable.HoldView();
        }

        void ReleaseView(Interaction interaction)
        {
            if (!m_HoldingView)
                return;

            m_HoldingView = false;
            interaction?.Interactable?.ReleaseView();
        }

        void WarnIfUnlocked(KinematicMover actor, Interaction interaction)
        {
            if (m_Warned)
                return;

            var controlLock = actor.GetComponent<IControlLock>();
            if (controlLock != null && controlLock.IsLocked)
                return;

            m_Warned = true;
            Debug.LogWarning(
                $"[Building Blocks] Look At: turning '{actor.name}' while its control is not locked. The host " +
                "keeps simulating and will fight the turn. Put a Lock Player step before this one.",
                interaction?.Interactable);
        }
    }
}
