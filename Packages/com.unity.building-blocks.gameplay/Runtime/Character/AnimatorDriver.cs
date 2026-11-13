using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Feeds an Animator Controller what the character is doing: how fast it moves, whether it is on
    /// the ground, when it jumps and lands. You name the parameters; this writes them every frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Bring your own controller; the playground's <c>PlaygroundHands</c> is an example. Point
    /// this at your Animator (empty finds the first one below) and give each field your controller's
    /// parameter name. A blank name is skipped; a name the controller lacks is skipped and reported
    /// once.
    /// </para>
    /// <para>
    /// Presentation only: reads <see cref="KinematicMover.GetState"/> in <c>LateUpdate</c> and never
    /// writes back. One-off states such as "holding the torch" belong to a Set Animator Parameter
    /// step. Use a parameter this driver does not write, or the driver overwrites it every frame.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Building Blocks/Animator Driver")]
    [DisallowMultipleComponent]
    public sealed class AnimatorDriver : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The Animator to drive. Empty finds the first one under this character.")]
        Animator m_Animator;

        [Header("Parameters")]
        [SerializeField]
        [Tooltip("Float. Speed along the ground in meters per second. Blank skips it.")]
        string m_SpeedParameter = "Speed";

        [SerializeField]
        [Tooltip("Float. Speed along the ground as a fraction of the top walking speed: 0 standing, 1 " +
                 "walking flat out, above 1 sprinting. Blank skips it.")]
        string m_NormalizedSpeedParameter = "";

        [SerializeField]
        [Tooltip("Float. Up and down speed in meters per second; negative is falling. Blank skips it.")]
        string m_VerticalSpeedParameter = "VerticalSpeed";

        [SerializeField]
        [Tooltip("Bool. Whether the character stands on something. Blank skips it.")]
        string m_GroundedParameter = "IsGrounded";

        [SerializeField]
        [Tooltip("Trigger. Fires the frame the character leaves the ground going up. Blank skips it.")]
        string m_JumpedParameter = "Jumped";

        [SerializeField]
        [Tooltip("Trigger. Fires the frame the character touches the ground again. Blank skips it.")]
        string m_LandedParameter = "Landed";

        [Header("Tuning")]
        [SerializeField, Min(0f)]
        [Tooltip("Seconds the speed values take to settle, so a blend tree does not flicker between " +
                 "idle and walk on a stutter step. 0 writes them raw.")]
        float m_SpeedSmoothing = k_DefaultSpeedSmoothing;

        [SerializeField, Min(0f)]
        [Tooltip("The speed that counts as 1 for Normalized Speed. 0 reads the Ground Locomotion " +
                 "processor's Max Speed on this character.")]
        float m_MaxSpeed;

        /// <summary>
        /// Default <see cref="SpeedSmoothing"/>: 0.1 s, enough to hide a one-tick dip at a step edge
        /// without making a stop feel late.
        /// </summary>
        const float k_DefaultSpeedSmoothing = 0.1f;

        /// <summary>
        /// One frame's worth of values before they reach the Animator, for readouts and tests.
        /// </summary>
        public struct Sample
        {
            /// <summary>Speed along the ground, meters per second.</summary>
            public float Speed;

            /// <summary><see cref="Speed"/> divided by the top speed in use; 0 when none is known.</summary>
            public float NormalizedSpeed;

            /// <summary>Vertical speed, meters per second. Negative is falling.</summary>
            public float VerticalSpeed;

            /// <summary>Whether the character stands on something.</summary>
            public bool IsGrounded;

            /// <summary>The character left the ground going up since the last sample.</summary>
            public bool Jumped;

            /// <summary>The character touched the ground since the last sample.</summary>
            public bool Landed;
        }

        KinematicMover m_Mover;
        Character m_Host;
        GroundLocomotionProcessor m_Locomotion;
        RuntimeAnimatorController m_KnownController;
        readonly HashSet<int> m_Present = new HashSet<int>();
        bool m_NamesDirty = true;
        bool m_HasSample;
        bool m_WasGrounded;
        Sample m_LastSample;

        int m_SpeedId;
        int m_NormalizedSpeedId;
        int m_VerticalSpeedId;
        int m_GroundedId;
        int m_JumpedId;
        int m_LandedId;

        /// <summary>The Animator being driven. <c>null</c> resolves the first one under this character.</summary>
        public Animator Animator
        {
            get => m_Animator;
            set
            {
                m_Animator = value;
                m_NamesDirty = true;
            }
        }

        /// <summary>Float parameter for ground speed in m/s. Default <c>"Speed"</c>; blank skips it.</summary>
        public string SpeedParameter
        {
            get => m_SpeedParameter;
            set => SetName(ref m_SpeedParameter, value);
        }

        /// <summary>Float parameter for ground speed as a fraction of top speed. Default blank.</summary>
        public string NormalizedSpeedParameter
        {
            get => m_NormalizedSpeedParameter;
            set => SetName(ref m_NormalizedSpeedParameter, value);
        }

        /// <summary>Float parameter for vertical speed. Default <c>"VerticalSpeed"</c>.</summary>
        public string VerticalSpeedParameter
        {
            get => m_VerticalSpeedParameter;
            set => SetName(ref m_VerticalSpeedParameter, value);
        }

        /// <summary>Bool parameter for standing on ground. Default <c>"IsGrounded"</c>.</summary>
        public string GroundedParameter
        {
            get => m_GroundedParameter;
            set => SetName(ref m_GroundedParameter, value);
        }

        /// <summary>Trigger fired on leaving the ground upward. Default <c>"Jumped"</c>.</summary>
        public string JumpedParameter
        {
            get => m_JumpedParameter;
            set => SetName(ref m_JumpedParameter, value);
        }

        /// <summary>Trigger fired on touching the ground. Default <c>"Landed"</c>.</summary>
        public string LandedParameter
        {
            get => m_LandedParameter;
            set => SetName(ref m_LandedParameter, value);
        }

        /// <summary>Seconds the speed values take to settle. Default <c>0.1</c>; <c>0</c> is raw.</summary>
        public float SpeedSmoothing
        {
            get => m_SpeedSmoothing;
            set => m_SpeedSmoothing = Mathf.Max(0f, value);
        }

        /// <summary>The speed that counts as 1 for the normalized value. Default <c>0</c>, which reads Ground Locomotion's Max Speed.</summary>
        public float MaxSpeed
        {
            get => m_MaxSpeed;
            set => m_MaxSpeed = Mathf.Max(0f, value);
        }

        /// <summary>The top speed actually in use for normalization: <see cref="MaxSpeed"/>, or the locomotion processor's when that is 0.</summary>
        public float MaxSpeedInUse => m_MaxSpeed > 0f ? m_MaxSpeed : Locomotion() != null ? m_Locomotion.MaxSpeed : 0f;

        /// <summary>
        /// The character's <see cref="GroundLocomotionProcessor"/>, re-resolved when the cached one
        /// leaves the list.
        /// </summary>
        GroundLocomotionProcessor Locomotion()
        {
            if (m_Host == null)
                return m_Locomotion;

            if (m_Locomotion == null || !StillListed(m_Locomotion))
                m_Locomotion = m_Host.GetProcessor<GroundLocomotionProcessor>();

            return m_Locomotion;
        }

        bool StillListed(MoveProcessor processor)
        {
            List<MoveProcessor> list = m_Host.Processors;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], processor))
                    return true;
            }

            return false;
        }

        /// <summary>The values written on the last frame. For readouts.</summary>
        public Sample LastSample => m_LastSample;

        /// <summary>
        /// Turns one tick's <see cref="MoveState"/> into the values the Animator gets. Pure, so
        /// it is easy to test.
        /// </summary>
        /// <param name="state">The state after the tick.</param>
        /// <param name="hadPrevious">Whether a previous sample exists. Without one, nothing counts as a jump or a landing.</param>
        /// <param name="wasGrounded">Whether the previous sample was grounded.</param>
        /// <param name="maxSpeed">The speed that counts as 1 for <see cref="Sample.NormalizedSpeed"/>; 0 gives 0.</param>
        public static Sample Evaluate(in MoveState state, bool hadPrevious, bool wasGrounded, float maxSpeed)
        {
            Vector3 v = state.Velocity;
            var sample = new Sample
            {
                Speed = new Vector2(v.x, v.z).magnitude,
                VerticalSpeed = v.y,
                IsGrounded = state.IsGrounded,
            };

            sample.NormalizedSpeed = maxSpeed > 0f ? sample.Speed / maxSpeed : 0f;
            sample.Jumped = hadPrevious && wasGrounded && !state.IsGrounded && v.y > 0f;
            sample.Landed = hadPrevious && !wasGrounded && state.IsGrounded;
            return sample;
        }

        /// <summary>
        /// Writes <paramref name="sample"/> to the Animator: every named parameter the controller has,
        /// speeds damped over <see cref="SpeedSmoothing"/>. Called from <c>LateUpdate</c>; call it
        /// yourself to push your own values.
        /// </summary>
        public void Push(in Sample sample, float deltaTime)
        {
            if (m_Animator == null)
                return;

            RefreshParameters();

            if (m_Present.Contains(m_SpeedId)) SetFloat(m_SpeedId, sample.Speed, deltaTime);
            if (m_Present.Contains(m_NormalizedSpeedId)) SetFloat(m_NormalizedSpeedId, sample.NormalizedSpeed, deltaTime);
            if (m_Present.Contains(m_VerticalSpeedId)) SetFloat(m_VerticalSpeedId, sample.VerticalSpeed, deltaTime);
            if (m_Present.Contains(m_GroundedId)) m_Animator.SetBool(m_GroundedId, sample.IsGrounded);
            if (sample.Jumped && m_Present.Contains(m_JumpedId)) m_Animator.SetTrigger(m_JumpedId);
            if (sample.Landed && m_Present.Contains(m_LandedId)) m_Animator.SetTrigger(m_LandedId);
        }

        void SetFloat(int id, float value, float deltaTime)
        {
            if (m_SpeedSmoothing > 0f && deltaTime > 0f)
                m_Animator.SetFloat(id, value, m_SpeedSmoothing, deltaTime);
            else
                m_Animator.SetFloat(id, value);
        }

        void SetName(ref string field, string value)
        {
            field = value ?? string.Empty;
            m_NamesDirty = true;
        }

        /// <summary>
        /// Re-reads the parameter list when the controller or a name changed. Names that exist go in
        /// <c>m_Present</c>; the rest are reported once per controller.
        /// </summary>
        void RefreshParameters()
        {
            RuntimeAnimatorController controller = m_Animator.runtimeAnimatorController;
            if (!m_NamesDirty && ReferenceEquals(controller, m_KnownController))
                return;

            m_NamesDirty = false;
            m_KnownController = controller;

            m_SpeedId = Hash(m_SpeedParameter);
            m_NormalizedSpeedId = Hash(m_NormalizedSpeedParameter);
            m_VerticalSpeedId = Hash(m_VerticalSpeedParameter);
            m_GroundedId = Hash(m_GroundedParameter);
            m_JumpedId = Hash(m_JumpedParameter);
            m_LandedId = Hash(m_LandedParameter);

            // Allocates the parameter array once per controller change, never per frame.
            var available = new HashSet<int>();
            foreach (AnimatorControllerParameter parameter in m_Animator.parameters)
                available.Add(parameter.nameHash);

            m_Present.Clear();
            var missing = new StringBuilder();
            Check(m_SpeedParameter, m_SpeedId, available, missing);
            Check(m_NormalizedSpeedParameter, m_NormalizedSpeedId, available, missing);
            Check(m_VerticalSpeedParameter, m_VerticalSpeedId, available, missing);
            Check(m_GroundedParameter, m_GroundedId, available, missing);
            Check(m_JumpedParameter, m_JumpedId, available, missing);
            Check(m_LandedParameter, m_LandedId, available, missing);

            if (missing.Length > 0 && controller != null)
            {
                Debug.LogWarning(
                    $"[Building Blocks] Animator Driver on '{name}': the controller '{controller.name}' has no " +
                    $"parameter named {missing}. Blank the field to skip it, or add the parameter.", this);
            }
        }

        void Check(string parameter, int id, HashSet<int> available, StringBuilder missing)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            if (available.Contains(id))
            {
                m_Present.Add(id);
                return;
            }

            if (missing.Length > 0) missing.Append(", ");
            missing.Append('\'').Append(parameter).Append('\'');
        }

        static int Hash(string parameter) => string.IsNullOrEmpty(parameter) ? 0 : Animator.StringToHash(parameter);

        void Reset()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>();
        }

        void Awake()
        {
            m_Mover = GetComponentInParent<KinematicMover>();
            m_Host = m_Mover != null ? m_Mover.GetComponent<Character>() : null;
            m_Locomotion = m_Host != null ? m_Host.GetProcessor<GroundLocomotionProcessor>() : null;

            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>();

            if (m_Mover == null)
            {
                Debug.LogWarning(
                    $"[Building Blocks] Animator Driver on '{name}' found no Kinematic Mover on this object or above " +
                    "it, so it has nothing to read. Put it on the character, or under it.", this);
            }
        }

        void OnEnable()
        {
            // A fresh start never reports a landing for the ground the character spawned on.
            m_HasSample = false;
        }

        void LateUpdate()
        {
            if (m_Animator == null || m_Mover == null)
                return;

            MoveState state = m_Mover.GetState();
            Sample sample = Evaluate(state, m_HasSample, m_WasGrounded, MaxSpeedInUse);
            m_HasSample = true;
            m_WasGrounded = state.IsGrounded;
            m_LastSample = sample;

            Push(sample, Time.deltaTime);
        }
    }
}
