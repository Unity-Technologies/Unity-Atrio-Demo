using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Plays one <see cref="Clip"/> on every listed <see cref="Animator"/> at once, from a single
    /// <see cref="PlayableGraph"/>, and waits for it to end.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All animators share one clock, so they cannot drift. An Animator with a controller can fade the
    /// clip in and out against it (<see cref="BlendIn"/>, <see cref="BlendOut"/>) and resumes it after.
    /// An Animator without one snaps in and keeps the clip's last pose. The step ends when the clip does.
    /// </para>
    /// <para>
    /// Assign a <see cref="PointOfView"/> the clip moves (often an exported camera node) and the player
    /// sees through it for the clip's length, via <see cref="ICameraAnchor"/> (the First Person Camera
    /// Rig). A <see cref="Camera"/> on it is only read for field of view and is switched off. If the node
    /// looks down another axis, set <see cref="CameraForward"/> and <see cref="CameraUp"/>.
    /// </para>
    /// </remarks>
    [Serializable]
    [InteractionStep("Play Animation")]
    public sealed class PlayAnimationStep : InteractionStep
    {
        [SerializeField]
        [Tooltip("The clip every listed Animator plays, in lockstep, from one graph.")]
        AnimationClip m_Clip;

        [SerializeField, SceneObject]
        [Tooltip("Every Animator that plays the clip: the door's, the player's hands. Each is bound " +
                 "to its own output on the same graph, so they cannot drift apart.")]
        Animator[] m_Animators = Array.Empty<Animator>();

        [SerializeField, Min(0.01f)]
        [Tooltip("Playback speed. 1 is the clip as authored, 2 twice as fast. The step ends when the " +
                 "clip does at this speed.")]
        float m_Speed = 1f;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds to fade the clip in over whatever the Animator was playing. Only an Animator " +
                 "with an Animator Controller can fade; one without snaps in.")]
        float m_BlendIn;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds to fade back to the Animator Controller before the clip ends. An Animator " +
                 "without a controller keeps the clip's last pose instead.")]
        float m_BlendOut;

        [Header("Camera")]
        [SerializeField, SceneObject]
        [Tooltip("Optional. An object the clip moves, usually the camera node exported with the door. " +
                 "The player sees through it while the clip plays, then the view comes back to the " +
                 "head. Any object works; add a Camera to it only if the clip animates field of view.")]
        GameObject m_PointOfView;

        [SerializeField]
        [Tooltip("Which of the Point Of View's local axes points where the artist was looking. +Z for a " +
                 "Unity camera. A camera from a DCC often arrives looking down -Z or another axis; " +
                 "change this until the shot looks the right way instead of fixing the export.")]
        AnchorAxis m_CameraForward = AnchorAxis.PositiveZ;

        [SerializeField]
        [Tooltip("Which of the Point Of View's local axes is up. +Y for a Unity camera.")]
        AnchorAxis m_CameraUp = AnchorAxis.PositiveY;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds for the view to ease from the player's head onto the camera when the clip " +
                 "starts. 0 snaps.")]
        float m_CameraBlendIn = k_DefaultCameraBlend;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds for the view to ease back onto the head when the clip ends or is stopped. " +
                 "0 snaps.")]
        float m_CameraBlendOut = k_DefaultCameraBlend;

        [SerializeField]
        [Tooltip("Take the field of view from the Camera on the Point Of View too, and put the " +
                 "player's own back afterwards. Does nothing when that object has no Camera.")]
        bool m_CopyFieldOfView = true;

        /// <summary>Default camera blend in and out, in seconds.</summary>
        const float k_DefaultCameraBlend = 0.25f;

        ICameraAnchor m_Ridden;
        bool m_WarnedNoAnchor;
        bool m_WarnedAxes;
        bool m_WarnedEnabled;

        readonly List<AnimationPlayableOutput> m_Blended = new List<AnimationPlayableOutput>();

        PlayableGraph m_Graph;
        AnimationClipPlayable m_Lead;
        float m_Weight = 1f;

        /// <summary>The clip every animator plays.</summary>
        public AnimationClip Clip
        {
            get => m_Clip;
            set => m_Clip = value;
        }

        /// <summary>The animators that play <see cref="Clip"/> in lockstep.</summary>
        public Animator[] Animators
        {
            get => m_Animators;
            set => m_Animators = value ?? Array.Empty<Animator>();
        }

        /// <summary>Playback speed. Default <c>1</c>.</summary>
        public float Speed
        {
            get => m_Speed;
            set => m_Speed = Mathf.Max(0.01f, value);
        }

        /// <summary>Seconds to fade in against a controller. Default <c>0</c>.</summary>
        public float BlendIn
        {
            get => m_BlendIn;
            set => m_BlendIn = Mathf.Max(0f, value);
        }

        /// <summary>Seconds to fade out to the controller before the end. Default <c>0</c>.</summary>
        public float BlendOut
        {
            get => m_BlendOut;
            set => m_BlendOut = Mathf.Max(0f, value);
        }

        /// <summary>
        /// The object the player sees through while the clip plays, or <c>null</c> to leave the view
        /// alone. Any object the clip moves; a <see cref="Camera"/> on it is only read for field of
        /// view. Default <c>null</c>.
        /// </summary>
        public GameObject PointOfView
        {
            get => m_PointOfView;
            set => m_PointOfView = value;
        }

        /// <summary>Which local axis of <see cref="PointOfView"/> looks ahead. Default <see cref="AnchorAxis.PositiveZ"/>.</summary>
        public AnchorAxis CameraForward
        {
            get => m_CameraForward;
            set => m_CameraForward = value;
        }

        /// <summary>Which local axis of <see cref="PointOfView"/> is up. Default <see cref="AnchorAxis.PositiveY"/>.</summary>
        public AnchorAxis CameraUp
        {
            get => m_CameraUp;
            set => m_CameraUp = value;
        }

        /// <summary>Seconds to ease the view onto <see cref="PointOfView"/>. Default <c>0.25</c>.</summary>
        public float CameraBlendIn
        {
            get => m_CameraBlendIn;
            set => m_CameraBlendIn = Mathf.Max(0f, value);
        }

        /// <summary>Seconds to ease the view back to the head. Default <c>0.25</c>.</summary>
        public float CameraBlendOut
        {
            get => m_CameraBlendOut;
            set => m_CameraBlendOut = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Whether the field of view of a <see cref="Camera"/> on <see cref="PointOfView"/> is copied
        /// for the ride. No effect when the object has none. Default <c>true</c>.
        /// </summary>
        public bool CopyFieldOfView
        {
            get => m_CopyFieldOfView;
            set => m_CopyFieldOfView = value;
        }

        /// <summary>Whether the player's view is riding <see cref="PointOfView"/> right now.</summary>
        public bool IsRidingCamera => m_Ridden != null;

        /// <summary>
        /// The rotation applied after the camera node's own, from <see cref="CameraForward"/> and
        /// <see cref="CameraUp"/>. Identity for a node that already looks down +Z with +Y up.
        /// </summary>
        public Quaternion CameraCorrection =>
            AnchorAxes.TryCorrection(m_CameraForward, m_CameraUp, out Quaternion correction) ? correction : Quaternion.identity;

        /// <summary>How long the step takes: the clip's length at <see cref="Speed"/>, or 0 with no clip.</summary>
        public float Duration => m_Clip != null ? m_Clip.length / m_Speed : 0f;

        /// <summary>Whether the clip is playing right now.</summary>
        public bool IsPlaying => m_Graph.IsValid();

        /// <summary>Seconds into the clip, on the graph's clock. 0 when not playing.</summary>
        public float ClipTime => m_Graph.IsValid() && m_Lead.IsValid() ? (float)m_Lead.GetTime() : 0f;

        /// <summary>The clip's weight against the controllers right now, 0 to 1. Animators without a controller always play at full weight.</summary>
        public float BlendWeight => m_Graph.IsValid() ? m_Weight : 1f;

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            if (m_Clip == null || m_Animators.Length == 0)
                yield break;

            DestroyGraph();

            string owner = interaction?.Interactable != null ? interaction.Interactable.name : "Interactable";
            m_Graph = PlayableGraph.Create($"{owner} (Play Animation)");
            m_Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            m_Blended.Clear();

            bool haveLead = false;
            for (int i = 0; i < m_Animators.Length; i++)
            {
                Animator animator = m_Animators[i];
                if (animator == null)
                    continue;

                // One clip playable per animator, all on one graph: one clock, no drift.
                var clipPlayable = AnimationClipPlayable.Create(m_Graph, m_Clip);
                clipPlayable.SetSpeed(m_Speed);

                var output = AnimationPlayableOutput.Create(m_Graph, animator.name, animator);
                output.SetSourcePlayable(clipPlayable);

                // Only an Animator with a controller has a pose to fade against.
                if (animator.runtimeAnimatorController != null)
                    m_Blended.Add(output);

                if (!haveLead)
                {
                    m_Lead = clipPlayable;
                    haveLead = true;
                }
            }

            if (!haveLead)
            {
                DestroyGraph();
                yield break;
            }

            float length = m_Clip.length;

            // Set the first weight before the graph evaluates once, so a fade-in starts from nothing.
            SetWeight(WeightAt(0f, length));
            m_Graph.Play();

            // Same frame as the first pose, so the view never sees the camera node at rest.
            BeginRide(interaction);

            while (m_Graph.IsValid())
            {
                float time = (float)m_Lead.GetTime();
                if (time >= length)
                    break;

                SetWeight(WeightAt(time, length));
                yield return null;
            }

            EndRide();
            DestroyGraph();
        }

        /// <summary>Tears the graph down and brings the view home; the animators keep the pose they were at.</summary>
        public override void Stop(Interaction interaction)
        {
            EndRide();
            DestroyGraph();
        }

        void BeginRide(Interaction interaction)
        {
            if (m_PointOfView == null)
                return;

            Interactable context = interaction?.Interactable;
            KinematicMover actor = interaction?.Player;
            ICameraAnchor anchor = actor != null ? actor.GetComponentInChildren<ICameraAnchor>() : null;
            if (anchor == null)
            {
                if (!m_WarnedNoAnchor)
                {
                    m_WarnedNoAnchor = true;
                    Debug.LogWarning(
                        "[Building Blocks] Play Animation: a Point Of View is assigned but the player has no view that can ride " +
                        "it. Give the character a First Person Camera Rig, or anything implementing ICameraAnchor.", context);
                }

                return;
            }

            if (!AnchorAxes.TryCorrection(m_CameraForward, m_CameraUp, out Quaternion correction) && !m_WarnedAxes)
            {
                m_WarnedAxes = true;
                Debug.LogWarning(
                    "[Building Blocks] Play Animation: Camera Forward and Camera Up are the same axis, so they cannot describe " +
                    "an orientation. Using the node as is.", context);
            }

            // Only the motion is borrowed. A second rendering camera would draw over the player's.
            var camera = m_PointOfView.GetComponent<Camera>();
            if (camera != null && camera.enabled)
            {
                camera.enabled = false;
                if (!m_WarnedEnabled)
                {
                    m_WarnedEnabled = true;
                    Debug.LogWarning(
                        $"[Building Blocks] Play Animation: '{m_PointOfView.name}' has a Camera that was enabled and would render too. " +
                        "Switched it off; the player's camera plays the shot. Untick its Camera component to silence this.", camera);
                }
            }

            anchor.AttachTo(m_PointOfView.transform, correction, m_CopyFieldOfView, m_CameraBlendIn);
            m_Ridden = anchor;
        }

        void EndRide()
        {
            ICameraAnchor ridden = m_Ridden;
            m_Ridden = null;

            // The rig may have been destroyed with its scene.
            if (ridden == null || (ridden is UnityEngine.Object o && o == null))
                return;

            ridden.Release(m_CameraBlendOut);
        }

        /// <summary>
        /// The clip's weight against a controller at <paramref name="clipTime"/> seconds into a clip of
        /// <paramref name="length"/> seconds. Blends are in real seconds.
        /// </summary>
        public float WeightAt(float clipTime, float length)
        {
            float elapsed = clipTime / m_Speed;
            float remaining = (length - clipTime) / m_Speed;

            float weight = 1f;
            if (m_BlendIn > 0f) weight = Mathf.Min(weight, elapsed / m_BlendIn);
            if (m_BlendOut > 0f) weight = Mathf.Min(weight, remaining / m_BlendOut);
            return Mathf.Clamp01(weight);
        }

        void SetWeight(float weight)
        {
            m_Weight = weight;
            for (int i = 0; i < m_Blended.Count; i++)
                m_Blended[i].SetWeight(weight);
        }

        void DestroyGraph()
        {
            if (m_Graph.IsValid())
                m_Graph.Destroy();

            m_Blended.Clear();
            m_Weight = 1f;
        }
    }
}
