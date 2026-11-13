using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Moves a visual root smoothly between simulation ticks of a <see cref="KinematicMover"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Assign the child that carries the model, animator and skeleton. The host calls
    /// <see cref="BeginTick"/> before and <see cref="EndTick"/> after each tick; <c>Character</c>
    /// does this. The visual root's world pose is overwritten every frame, so put offsets on a child.
    /// Make the camera rig a sibling of the visual root, never its child.
    /// </para>
    /// <para>
    /// <c>LateUpdate</c> blends with the engine's fixed-tick alpha. A host with its own clock
    /// (replay scrubber, netcode ghost) disables this component and calls <see cref="Interpolate"/>.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Building Blocks/Move Interpolator")]
    [RequireComponent(typeof(KinematicMover))]
    public sealed class MoveInterpolator : MonoBehaviour
    {
        [SerializeField] Transform m_VisualRoot;

        Vector3 m_PreviousPosition = Vector3.zero;
        Quaternion m_PreviousRotation = Quaternion.identity;
        Vector3 m_CurrentPosition = Vector3.zero;
        Quaternion m_CurrentRotation = Quaternion.identity;

        // Publish nothing before the first BeginTick/EndTick pair, or visuals flash at the origin.
        bool m_HasPose;

        /// <summary>
        /// The transform this component interpolates. <c>null</c> disables interpolation.
        /// </summary>
        public Transform VisualRoot
        {
            get => m_VisualRoot;
            set => m_VisualRoot = value;
        }

        /// <summary>The pose the last completed tick started from.</summary>
        public Vector3 PreviousPosition => m_PreviousPosition;

        /// <summary>The rotation the last completed tick started from.</summary>
        public Quaternion PreviousRotation => m_PreviousRotation;

        /// <summary>The pose the last completed tick ended at.</summary>
        public Vector3 CurrentPosition => m_CurrentPosition;

        /// <summary>The rotation the last completed tick ended at.</summary>
        public Quaternion CurrentRotation => m_CurrentRotation;

        /// <summary>
        /// Records the pose the upcoming tick starts from. Call from the host loop immediately
        /// before <see cref="KinematicMover.Simulate"/>.
        /// </summary>
        /// <param name="state">The mover's state before the tick.</param>
        public void BeginTick(in MoveState state)
        {
            m_PreviousPosition = state.Position;
            m_PreviousRotation = state.Rotation;
        }

        /// <summary>
        /// Records the pose the tick just ended at. Call from the host loop immediately after
        /// <see cref="KinematicMover.Simulate"/>.
        /// </summary>
        /// <param name="state">The mover's state after the tick.</param>
        public void EndTick(in MoveState state)
        {
            m_CurrentPosition = state.Position;
            m_CurrentRotation = state.Rotation;
            m_HasPose = true;
        }

        /// <summary>
        /// Writes the visual root's world pose as the blend between the last two recorded tick
        /// poses. <paramref name="alpha"/> is clamped to 0-1: 0 is the pose the tick started
        /// from, 1 the pose it ended at.
        /// </summary>
        /// <remarks>
        /// Rotation takes the shorter arc. Does nothing before a tick has completed or with no
        /// <see cref="VisualRoot"/>.
        /// </remarks>
        /// <param name="alpha">Blend factor, clamped to 0-1.</param>
        public void Interpolate(float alpha)
        {
            if (m_VisualRoot == null || !m_HasPose)
                return;

            alpha = Mathf.Clamp01(alpha);

            m_VisualRoot.SetPositionAndRotation(
                Vector3.Lerp(m_PreviousPosition, m_CurrentPosition, alpha),
                Quaternion.Slerp(m_PreviousRotation, m_CurrentRotation, alpha));
        }

        /// <summary>
        /// The fraction of the current fixed tick that has elapsed at this render frame, 0-1.
        /// </summary>
        /// <remarks>
        /// Presentation only: the alpha describes the frame, not the tick.
        /// </remarks>
        static float FixedTickAlpha()
        {
            float step = Time.fixedDeltaTime;
            if (step <= 0f)
                return 1f;

            return (Time.time - Time.fixedTime) / step;
        }

        void LateUpdate() => Interpolate(FixedTickAlpha());
    }
}
