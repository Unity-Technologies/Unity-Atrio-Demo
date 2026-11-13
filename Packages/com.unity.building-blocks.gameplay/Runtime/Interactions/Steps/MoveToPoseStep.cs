using System;
using System.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Eases the player to a placed <see cref="Target"/>: where it stands, which way it faces and how
    /// far the view tilts, so an animation starts from a known pose.
    /// </summary>
    /// <remarks>
    /// Needs a Lock Player step before it. An axis switched off (<see cref="MoveX"/>,
    /// <see cref="MoveY"/>, <see cref="MoveZ"/>) keeps the player's own value; turn Y off to let the
    /// ground decide height, so a mark inside the floor cannot cause a hop when control returns.
    /// <see cref="TurnBody"/> takes the target's left-right facing and <see cref="TiltView"/> its up-down
    /// tilt, through <see cref="ILookPitch"/> and within that view's pitch limits. The target's roll is
    /// ignored: this step never rolls the body or the view.
    /// </remarks>
    [Serializable]
    [InteractionStep("Move Player To")]
    public sealed class MoveToPoseStep : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("Where the player should stand, face and look when this finishes: its blue (Z) arrow is " +
                 "the way to look, and its roll is ignored.")]
        Transform m_Target;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds to ease there. 0 snaps.")]
        float m_Duration = k_DefaultDuration;

        [SerializeField]
        [Tooltip("Easing: input is elapsed 0-1, output is how far along the path the player is.")]
        AnimationCurve m_Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField]
        [Tooltip("Move along the world X axis. Off keeps the player's current X.")]
        bool m_MoveX = true;

        [SerializeField]
        [Tooltip("Move along the world Y axis (height). Off keeps the player's current height, so the " +
                 "floor decides it rather than the mark. Turn this off when the mark is not exactly " +
                 "on the ground the player stands on.")]
        bool m_MoveY = true;

        [SerializeField]
        [Tooltip("Move along the world Z axis. Off keeps the player's current Z.")]
        bool m_MoveZ = true;

        [SerializeField]
        [Tooltip("Turn the player left or right to face the way the target faces; off keeps the player's " +
                 "facing.")]
        bool m_TurnBody = true;

        [SerializeField]
        [Tooltip("Tilt the view up or down to the target's tilt, within the camera rig's pitch limits; turn " +
                 "it off when a Look At in the same group tilts the view.")]
        bool m_TiltView = true;

        /// <summary>Default <see cref="Duration"/> in seconds.</summary>
        const float k_DefaultDuration = 0.5f;

        bool m_Warned;
        bool m_HoldingView;

        /// <summary>Where the player ends up: position, facing and view tilt.</summary>
        public Transform Target
        {
            get => m_Target;
            set => m_Target = value;
        }

        /// <summary>Seconds of easing. Default <c>0.5</c>; <c>0</c> snaps.</summary>
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

        /// <summary>Whether the player's world X follows the target. Default <c>true</c>; off keeps the current value.</summary>
        public bool MoveX
        {
            get => m_MoveX;
            set => m_MoveX = value;
        }

        /// <summary>Whether the player's height follows the target. Default <c>true</c>; off leaves it to the ground.</summary>
        public bool MoveY
        {
            get => m_MoveY;
            set => m_MoveY = value;
        }

        /// <summary>Whether the player's world Z follows the target. Default <c>true</c>; off keeps the current value.</summary>
        public bool MoveZ
        {
            get => m_MoveZ;
            set => m_MoveZ = value;
        }

        /// <summary>Whether the player turns left or right to the target's facing. Default <c>true</c>.</summary>
        public bool TurnBody
        {
            get => m_TurnBody;
            set => m_TurnBody = value;
        }

        /// <summary>
        /// Whether the view tilts up or down to the target's tilt. Default <c>true</c>. Needs an
        /// <see cref="ILookPitch"/> under the player; the view clamps the tilt to its own limits.
        /// </summary>
        public bool TiltView
        {
            get => m_TiltView;
            set => m_TiltView = value;
        }

        /// <summary>
        /// The view tilt <paramref name="target"/> asks for, in degrees: positive looks down, the sign
        /// <see cref="ILookPitch.Pitch"/> uses. <c>0</c> without a target.
        /// </summary>
        public static float PitchOf(Transform target) =>
            target != null ? Mathf.DeltaAngle(0f, target.eulerAngles.x) : 0f;

        /// <summary>Where the player ends up: the target's position on the axes that move, <paramref name="current"/> on the rest.</summary>
        public Vector3 DestinationFrom(Vector3 current)
        {
            Vector3 target = m_Target != null ? m_Target.position : current;
            return new Vector3(
                m_MoveX ? target.x : current.x,
                m_MoveY ? target.y : current.y,
                m_MoveZ ? target.z : current.z);
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            KinematicMover actor = interaction?.Player;
            if (actor == null || m_Target == null)
                yield break;

            WarnIfUnlocked(actor, interaction);

            // A move that neither turns nor tilts leaves a player who kept looking free to look.
            ILookPitch look = m_TiltView ? actor.GetComponentInChildren<ILookPitch>() : null;
            if (m_TurnBody || look != null)
                HoldView(interaction);

            IEnumerator approach = Approach(actor, look, interaction);
            try
            {
                while (approach.MoveNext())
                    yield return approach.Current;
            }
            finally
            {
                ReleaseView(interaction);
            }
        }

        IEnumerator Approach(KinematicMover actor, ILookPitch look, Interaction interaction)
        {
            MoveState state = actor.GetState();
            Vector3 fromPosition = state.Position;
            Quaternion fromRotation = state.Rotation;

            // The body only yaws, so the path does not dip and Teleport keeps the mover upright; the
            // tilt goes to the view.
            Quaternion toRotation = m_TurnBody ? Quaternion.Euler(0f, m_Target.eulerAngles.y, 0f) : fromRotation;

            float pitchFrom = 0f;
            float pitchTo = 0f;
            if (look != null)
            {
                pitchFrom = look.Pitch;

                // Let the view clamp the end tilt now, so the ease lands on its limit instead of
                // reaching it early and sitting there.
                look.SetPitch(PitchOf(m_Target));
                pitchTo = look.Pitch;
                look.SetPitch(pitchFrom);
            }

            if (m_Duration <= 0f)
            {
                actor.Teleport(DestinationFrom(fromPosition), toRotation);
                if (look != null)
                    look.SetPitch(pitchTo);
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < m_Duration)
            {
                elapsed += interaction.DeltaTime;

                // Target or actor destroyed mid-way: end where we are.
                if (actor == null || m_Target == null)
                    yield break;

                float t = Mathf.Clamp01(elapsed / m_Duration);
                float along = m_Curve != null ? m_Curve.Evaluate(t) : t;

                // Re-read each frame so a moving target is followed.
                actor.Teleport(
                    Vector3.LerpUnclamped(fromPosition, DestinationFrom(fromPosition), along),
                    Quaternion.SlerpUnclamped(fromRotation, toRotation, along));
                if (IsAlive(look))
                    look.SetPitch(Mathf.LerpUnclamped(pitchFrom, pitchTo, along));

                yield return null;
            }
        }

        /// <summary>
        /// Leaves the player wherever the approach got to. A view taken from a player who kept looking goes
        /// back to them.
        /// </summary>
        public override void Stop(Interaction interaction) => ReleaseView(interaction);

        // An interface reference skips Unity's destroyed-object check, so ask the object itself.
        static bool IsAlive(ILookPitch look) => look is Object unityObject ? unityObject != null : look != null;

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
                $"[Building Blocks] Move Player To: moving '{actor.name}' while its control is not locked. The " +
                "host keeps simulating and will fight the approach. Put a Lock Player step before this one.",
                interaction?.Interactable);
        }
    }
}
