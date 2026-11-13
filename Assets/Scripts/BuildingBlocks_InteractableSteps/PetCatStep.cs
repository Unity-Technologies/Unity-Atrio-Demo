using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using Unity.BuildingBlocks.Gameplay;

/// <summary>What the view looks at while the player walks up.</summary>
public enum PetCatLookTarget
{
    /// <summary>A point straight ahead of the clip's first camera frame. Lands on the shot with no turn.</summary>
    WhereTheClipStarts,

    /// <summary>The Look Point transform, for example the cat's head.</summary>
    LookPoint,
}

/// <summary>Where the run is. Read by tests and debug UI.</summary>
public enum PetCatPhase
{
    Idle,
    Walking,
    Settling,
    Petting,
    HandingBack,
}

/// <summary>
/// One step for the whole "pet the cat" moment: the player walks up to the stand spot, the view eases
/// into the clip's first camera frame (tilt included), the clip plays on the arms and the cat, then
/// control comes back facing where the clip ended, with the view gently levelled.
/// </summary>
/// <remarks>
/// Use it after a Lock Player step, so no other interaction can start mid-pet (it locks the player
/// itself too). Leave the Interactable's "Stop When Player Leaves" off: the walk can cross the zone's edge.
/// </remarks>
[Serializable]
[InteractionStep("Pet Cat")]
public class PetCatStep : InteractionStep
{
    // ---------------------------------------------------------------- Setup

    [Header("Setup")]
    [SerializeField, SceneObject]
    [Tooltip("Where the player's feet stand while the clip plays (the cat's Start Point). The clip's " +
             "camera is authored relative to this spot and its facing.")]
    Transform m_StandSpot;

    [SerializeField]
    [Tooltip("The petting clip. Plays on the Arms Animator and on everything in Also Play On.")]
    AnimationClip m_Clip;

    [SerializeField, SceneObject]
    [Tooltip("The player's first-person arms Animator. Its GameObject is switched on for the clip and " +
             "back off after.")]
    Animator m_ArmsAnimator;

    [SerializeField, SceneObject]
    [Tooltip("Other Animators that play the same clip at the same time, such as the cat. These fade in " +
             "and out (see Clip > Also Play On Blend In / Out).")]
    Animator[] m_AlsoPlayOn = Array.Empty<Animator>();

    [SerializeField, SceneObject]
    [Tooltip("The camera bone the clip moves, under the Arms Animator. The player sees through it while " +
             "the clip plays.")]
    Transform m_ClipCamera;

    [SerializeField]
    [Tooltip("Which local axis of the Clip Camera looks forward. +Z for a Unity camera.")]
    AnchorAxis m_CameraForward = AnchorAxis.PositiveZ;

    [SerializeField]
    [Tooltip("Which local axis of the Clip Camera is up. +Y for a Unity camera.")]
    AnchorAxis m_CameraUp = AnchorAxis.PositiveY;

    [SerializeField]
    [Tooltip("Also copy field of view from a Camera component on the Clip Camera, if it has one.")]
    bool m_CopyFieldOfView;

    [SerializeField, SceneObject]
    [Tooltip("Objects hidden while the clip plays and shown again after (a prompt, a HUD). A Held " +
             "Torch lowers itself and does not need to be listed.")]
    GameObject[] m_HideWhilePetting = Array.Empty<GameObject>();

    // ---------------------------------------------------------------- Walk

    [Header("Walk Up")]
    [SerializeField, Min(0.1f)]
    [Tooltip("Top walking speed in m/s. Slower than normal walking reads as calm and gentle.")]
    float m_WalkSpeed = 1.2f;

    [SerializeField, Min(0.1f)]
    [Tooltip("How fast the walk speeds up, in m/s². Lower is a softer start.")]
    float m_Acceleration = 3f;

    [SerializeField, Min(0.1f)]
    [Tooltip("How fast the walk slows to a stop at the stand spot, in m/s². Lower starts slowing earlier " +
             "and stops more gently.")]
    float m_Deceleration = 2f;

    [SerializeField, Min(0f)]
    [Tooltip("Up-down head bob while walking, in meters. 0 turns it off.")]
    float m_HeadBob = 0.012f;

    // ---------------------------------------------------------------- View

    [Header("View While Walking")]
    [SerializeField]
    [Tooltip("What the view looks at while walking. Where The Clip Starts lands on the shot with no " +
             "extra turn (smoothest). Look Point aims at something first, such as the cat's head.")]
    PetCatLookTarget m_LookAt = PetCatLookTarget.WhereTheClipStarts;

    [SerializeField, SceneObject]
    [Tooltip("Used when Look At is Look Point.")]
    Transform m_LookPoint;

    [SerializeField, Min(0f)]
    [Tooltip("Over the last this many meters the view (and body) turn into the clip's first frame, tilt " +
             "included. Longer is softer.")]
    float m_TurnInDistance = 1.1f;

    [SerializeField, Min(0f)]
    [Tooltip("Pause at the stand spot before the clip starts, in seconds, while the view settles.")]
    float m_SettleTime;

    // ---------------------------------------------------------------- Clip

    [Header("Clip")]
    [SerializeField, Min(0.05f)]
    [Tooltip("Playback speed. 1 is as authored.")]
    float m_Speed = 1f;

    [SerializeField, Min(0f)]
    [Tooltip("Start the clip this many seconds in. Trims the start.")]
    float m_StartTime;

    [SerializeField, Min(0f)]
    [Tooltip("Stop the clip at this time in seconds. 0 plays to the end. Trims the end.")]
    float m_EndTime;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds the Also Play On animators (the cat) fade into the clip.")]
    float m_AlsoPlayOnBlendIn = 0.6f;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds the Also Play On animators fade back out before the clip ends.")]
    float m_AlsoPlayOnBlendOut = 0.8f;

    // ---------------------------------------------------------------- Skip

    [Header("Skip")]
    [SerializeField]
    [Tooltip("Let the player fast-forward the clip with the Skip Button.")]
    bool m_AllowSkip;

    [SerializeField]
    [Tooltip("The button that fast-forwards. E, gamepad west and the touch Action button by default: the " +
             "same button that starts petting, so Skip After keeps the first press from skipping.")]
    InputActionProperty m_SkipButton = new InputActionProperty(DefaultSkipAction());

    [SerializeField, Min(0f)]
    [Tooltip("Seconds into the clip before Skip works.")]
    float m_SkipAfter = 1f;

    [SerializeField, Min(1f)]
    [Tooltip("Playback speed while skipping. Higher is quicker but rushes the motion.")]
    float m_SkipSpeed = 2.5f;

    // ---------------------------------------------------------------- Hand back

    [Header("Hand Back")]
    [SerializeField, Min(0f)]
    [Tooltip("Start handing back this many seconds before the clip ends. Use it when the clip's camera " +
             "holds still at the end, so there is no pause. The arms and cat finish the clip anyway.")]
    float m_HandBackEarly = 0.5f;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds for the view to ease off the clip camera back to the player's head.")]
    float m_ReleaseBlend = 0.35f;

    [SerializeField]
    [Tooltip("Ease the view up from the clip's last (downward) look before giving control back. Off: " +
             "control comes back looking where the clip ended.")]
    bool m_LevelView;

    [SerializeField, Range(-30f, 60f)]
    [Tooltip("Pitch to level to, in degrees. Positive looks down. 0 is straight ahead.")]
    float m_LevelPitch = 10f;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds to level the view. Runs alongside Release Blend.")]
    float m_LevelTime = 0.7f;

    [SerializeField, Min(0f)]
    [Tooltip("Extra seconds the player stays still after the view settles.")]
    float m_HoldAfter;

    // ---------------------------------------------------------------- Advanced

    [Header("Advanced")]
    [SerializeField, Min(0.01f)]
    [Tooltip("Seconds the view takes to catch up with where it wants to look. Higher is lazier and " +
             "softer; lower is snappier.")]
    float m_ViewSmoothing = 0.25f;

    [SerializeField, Min(10f)]
    [Tooltip("Fastest the view may turn, in degrees per second.")]
    float m_MaxTurnSpeed = 140f;

    [SerializeField, Min(10f)]
    [Tooltip("How fast the view may speed up or slow down its turning, in degrees per second². Stops " +
             "the view from kicking into a turn.")]
    float m_MaxTurnAcceleration = 600f;

    [SerializeField]
    [Tooltip("How the view blends into the clip's first frame over Turn In Distance. Input 0 = start of " +
             "turn-in, 1 = arrived.")]
    AnimationCurve m_TurnInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [SerializeField, Min(0.1f)]
    [Tooltip("Meters per step, for the head bob rhythm.")]
    float m_StepLength = 0.7f;

    [SerializeField, Min(0f)]
    [Tooltip("Closer than this, the player is eased into place instead of taking steps.")]
    float m_MinWalkDistance = 0.2f;

    [SerializeField, Min(0.5f)]
    [Tooltip("If walking takes longer than this, the rest of the way is eased instead.")]
    float m_MaxWalkTime = 5f;

    [SerializeField]
    [Tooltip("Keep the player's feet on the floor they walk on instead of at the stand spot's exact " +
             "height. Avoids a small bob when the stand spot sits slightly above or below the floor.")]
    bool m_KeepFeetOnFloor = true;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds the view blends from the walk-up onto the clip camera once the clip starts. The " +
             "walk already lines them up; a longer blend softens the change into the clip's own camera move.")]
    float m_HandOffBlend = 0.6f;

    [SerializeField]
    [Tooltip("Log each phase and how long it took to the Console.")]
    bool m_LogPhases;

    // ---------------------------------------------------------------- Runtime state

    const float k_ArriveTolerance = 0.03f;
    const float k_SettledAngle = 0.25f;

    [NonSerialized] PetCatPhase m_Phase;
    [NonSerialized] Interaction m_Interaction;
    [NonSerialized] KinematicMover m_Actor;
    [NonSerialized] Character m_Character;
    [NonSerialized] FirstPersonCameraRig m_Rig;
    [NonSerialized] Transform m_Body;

    [NonSerialized] Pose m_ClipStartLocal;
    [NonSerialized] Pose m_ClipStartWorld;
    [NonSerialized] Vector3 m_StandPosition;
    [NonSerialized] float m_StandYaw;

    [NonSerialized] GameObject m_StandIn;
    [NonSerialized] PetCatViewDriver m_Driver;
    [NonSerialized] Vector3 m_ViewEuler;   // pitch, yaw, roll in degrees
    [NonSerialized] Vector3 m_ViewVelocity;
    [NonSerialized] float m_BobPhase;
    [NonSerialized] bool m_ViewArrived;

    [NonSerialized] Walker m_Walker;
    [NonSerialized] IMoveCommandSource m_OldSource;
    [NonSerialized] bool m_WalkerInstalled;
    [NonSerialized] bool m_HostWasEnabled;
    [NonSerialized] bool m_InterpolatorWasEnabled;
    [NonSerialized] float m_GlideSpeed;

    [NonSerialized] PlayableGraph m_Graph;
    [NonSerialized] List<AnimationClipPlayable> m_Playables;
    [NonSerialized] List<AnimationPlayableOutput> m_Fading;
    [NonSerialized] bool m_ArmsWereActive;
    [NonSerialized] bool m_ArmsTouched;
    [NonSerialized] Camera m_DisabledClipCamera;
    [NonSerialized] bool[] m_HiddenWereActive;
    [NonSerialized] ICameraAnchor m_Ridden;

    [NonSerialized] InputAction m_SkipAction;
    [NonSerialized] bool m_Skipping;

    [NonSerialized] float m_PhaseStarted;
    [NonSerialized] float m_WalkSeconds;

    // ---------------------------------------------------------------- Public read-outs

    /// <summary>Where the current run is.</summary>
    public PetCatPhase Phase => m_Phase;

    /// <summary>Seconds the last walk-up took.</summary>
    public float WalkSeconds => m_WalkSeconds;

    /// <summary>Whether the last run walked (false: it eased into place).</summary>
    public bool Walked { get; private set; }

    /// <summary>Whether the player fast-forwarded the last run.</summary>
    public bool Skipped => m_Skipping;

    public Transform StandSpot { get => m_StandSpot; set => m_StandSpot = value; }
    public AnimationClip Clip { get => m_Clip; set => m_Clip = value; }
    public Animator ArmsAnimator { get => m_ArmsAnimator; set => m_ArmsAnimator = value; }
    public Animator[] AlsoPlayOn { get => m_AlsoPlayOn; set => m_AlsoPlayOn = value ?? Array.Empty<Animator>(); }
    public Transform ClipCamera { get => m_ClipCamera; set => m_ClipCamera = value; }
    public Transform LookPoint { get => m_LookPoint; set => m_LookPoint = value; }
    public GameObject[] HideWhilePetting { get => m_HideWhilePetting; set => m_HideWhilePetting = value ?? Array.Empty<GameObject>(); }
    public float WalkSpeed { get => m_WalkSpeed; set => m_WalkSpeed = Mathf.Max(0.1f, value); }
    public float Speed { get => m_Speed; set => m_Speed = Mathf.Max(0.05f, value); }
    public bool AllowSkip { get => m_AllowSkip; set => m_AllowSkip = value; }
    public bool LogPhases { get => m_LogPhases; set => m_LogPhases = value; }

    // ---------------------------------------------------------------- Run

    public override IEnumerator Run(Interaction interaction)
    {
        ResetRunState();
        return Guarded(Body(interaction));
    }

    /// <summary>
    /// Runs the body like the Interactable would, but if anything throws, puts the player back in
    /// working order before giving up (the Interactable alone would leave them frozen mid-walk).
    /// </summary>
    IEnumerator Guarded(IEnumerator body)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0)
        {
            bool moved;
            try
            {
                moved = stack.Peek().MoveNext();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Cleanup(0.25f);
                yield break;
            }

            if (!moved)
            {
                stack.Pop();
                continue;
            }

            if (stack.Peek().Current is IEnumerator nested)
            {
                stack.Push(nested);
                continue;
            }

            yield return stack.Peek().Current;
        }
    }

    IEnumerator Body(Interaction interaction)
    {
        KinematicMover actor = interaction?.Player;
        Interactable owner = interaction?.Interactable;
        if (actor == null || m_StandSpot == null || m_Clip == null)
        {
            Debug.LogWarning("Pet Cat: needs a player, a Stand Spot and a Clip. Skipping.", owner);
            yield break;
        }

        m_Interaction = interaction;
        m_Actor = actor;
        m_Character = actor.GetComponent<Character>();
        m_Rig = actor.GetComponentInChildren<FirstPersonCameraRig>();
        m_Body = m_Character != null && m_Character.Interpolator != null && m_Character.Interpolator.VisualRoot != null
            ? m_Character.Interpolator.VisualRoot
            : actor.transform;

        if (m_Rig == null)
        {
            Debug.LogWarning("Pet Cat: the player has no First Person Camera Rig. Skipping.", owner);
            yield break;
        }

        if (!owner.TryLockPlayer(PlayerLockScope.MovementAndLook) && m_Character != null && m_Character.IsLocked)
        {
            Debug.LogWarning("Pet Cat: something else already controls the player. Skipping.", owner);
            yield break;
        }

        Validate(owner);

        // Where the clip's camera starts, relative to the body standing at the stand spot.
        SampleClipCamera();

        m_StandPosition = m_StandSpot.position;
        if (m_KeepFeetOnFloor)
            m_StandPosition.y = actor.GetState().Position.y;
        m_StandYaw = m_StandSpot.eulerAngles.y;
        UpdateClipStartWorld();

        // ------------------------------------------------ Walk up
        EnterPhase(PetCatPhase.Walking);
        BeginView();

        float distance = FlatDistance(actor.GetState().Position, m_StandPosition);
        Walked = m_Character != null && distance > m_MinWalkDistance;
        float walkStart = interaction.Elapsed;
        m_GlideSpeed = 0f;

        if (Walked)
        {
            InstallWalker();
            float blockedFor = 0f;

            while (!m_Walker.Arrived)
            {
                MoveState state = actor.GetState();
                float d = FlatDistance(state.Position, m_StandPosition);

                // Blocked: told to walk, but pressing into something or sliding along it instead of
                // getting closer. Judged on the real foot speed, so a slow start never counts.
                Vector3 velocity = state.Velocity;
                velocity.y = 0f;
                Vector3 toward = m_StandPosition - state.Position;
                toward.y = 0f;
                float closing = toward.sqrMagnitude > 1e-6f ? Vector3.Dot(velocity, toward.normalized) : 0f;
                float footSpeed = velocity.magnitude;
                bool pushing = m_Walker.CurrentSpeed > 0.3f && footSpeed < 0.05f;
                bool sliding = footSpeed > 0.1f && closing < 0.35f * footSpeed;
                blockedFor = d > 0.1f && (pushing || sliding) ? blockedFor + interaction.DeltaTime : 0f;
                bool stuck = blockedFor > 0.5f;

                bool tooLong = interaction.Elapsed - walkStart > m_MaxWalkTime;
                if (stuck || tooLong)
                {
                    Debug.LogWarning($"Pet Cat: the walk to the stand spot was {(stuck ? "blocked" : "too long")} " +
                                     $"{d:F2} m out; easing the rest of the way. Check the path is clear.", owner);
                    break;
                }

                yield return null;
            }

            m_GlideSpeed = m_Walker.Arrived ? m_Walker.HandOffSpeed : ClosingSpeed();
            RemoveWalker();
        }

        // Ease the last few centimetres (or a short distance, or a blocked walk), carrying on at the
        // walk's speed so there is no stop-start.
        yield return Glide(interaction, m_GlideSpeed);
        UpdateClipStartWorld();
        m_WalkSeconds = interaction.Elapsed - walkStart;

        // ------------------------------------------------ Settle
        EnterPhase(PetCatPhase.Settling);
        m_ViewArrived = true;
        float settleStart = interaction.Elapsed;
        while (true)
        {
            float waited = interaction.Elapsed - settleStart;
            bool settled = ViewAngleToClipStart() < k_SettledAngle && m_ViewVelocity.magnitude < 5f;
            if ((waited >= m_SettleTime && settled) || waited >= m_SettleTime + 1f)
                break;
            yield return null;
        }

        // ------------------------------------------------ Pet
        EnterPhase(PetCatPhase.Petting);
        SetHidden(true);
        if (!StartClip(owner))
        {
            EndView(m_ReleaseBlend);
            SetHidden(false);
            yield break;
        }

        // The view rides the clip camera from here; the stand-in has done its job.
        if (m_ClipCamera != null)
        {
            EndView(-1f);
            m_Rig.AttachTo(m_ClipCamera, CameraCorrection(), m_CopyFieldOfView, m_HandOffBlend);
            m_Ridden = m_Rig;
        }
        else
        {
            EndView(m_HandOffBlend);
        }

        EnableSkip();
        float start = ClipStart;
        float end = ClipEnd;
        float speedNow = m_Speed;
        while (m_Graph.IsValid())
        {
            float time = ClipTime;
            if ((end - time) / Mathf.Max(0.05f, speedNow) <= m_HandBackEarly || time >= end)
                break;

            if (!m_Skipping && m_AllowSkip && m_SkipAction != null && time - start >= m_SkipAfter &&
                m_SkipAction.WasPressedThisFrame())
            {
                m_Skipping = true;
                if (m_LogPhases)
                    Debug.Log($"Pet Cat: skipped at {time:F2} s.", owner);
            }

            if (m_Skipping)
            {
                // Ramp up instead of jumping, so the motion never pops.
                speedNow = Mathf.MoveTowards(speedNow, m_SkipSpeed, m_SkipSpeed * 4f * interaction.DeltaTime);
                SetSpeed(speedNow);
            }

            UpdateFade(time);
            yield return null;
        }
        DisableSkip();

        // ------------------------------------------------ Hand back
        EnterPhase(PetCatPhase.HandingBack);

        // Release first: the rig blends from where the view is now, in world space.
        Pose last = m_ClipCamera != null
            ? new Pose(m_ClipCamera.position, m_ClipCamera.rotation * CameraCorrection())
            : new Pose(m_ClipStartWorld.position, m_ClipStartWorld.rotation);
        ReleaseRide(m_ReleaseBlend);

        // Stand the body under the last camera pose, facing where it looked, so control comes back there.
        float endYaw = YawOf(last.rotation);
        Quaternion endFacing = Quaternion.Euler(0f, endYaw, 0f);
        Vector3 feet = last.position - Vector3.up * m_Rig.EyeHeight - endFacing * Vector3.forward * m_Rig.EyeForward;
        if (m_Rig.PitchTransform != null)
            feet -= endFacing * Vector3.ProjectOnPlane(m_Rig.PitchTransform.localPosition, Vector3.up);
        feet.y = m_StandPosition.y;
        if (Blocked(feet))
            feet = m_StandPosition;
        actor.Teleport(feet, endFacing);

        // If even the stand spot overlaps something (a prop placed in the way), ease out of it now,
        // while the view is still blending, instead of being shoved when control comes back.
        Vector3 depenetrate = Penetration(feet);

        // Level the view while the release blend runs; the arms and cat finish the clip meanwhile.
        float startPitch = PitchOf(last.rotation);
        float handBackTime = Mathf.Max(m_ReleaseBlend, m_LevelView ? m_LevelTime : 0f);
        float handBackStart = interaction.Elapsed;
        while (interaction.Elapsed - handBackStart < handBackTime || (m_Graph.IsValid() && ClipTime < end))
        {
            float since = interaction.Elapsed - handBackStart;
            if (m_LevelView && m_LevelTime > 0f && since <= m_LevelTime)
            {
                float t = Mathf.Clamp01(since / m_LevelTime);
                t = t * t * (3f - 2f * t);
                m_Rig.SetPitch(Mathf.Lerp(startPitch, m_LevelPitch, t));
            }

            if (depenetrate.sqrMagnitude > 1e-6f)
            {
                Vector3 step = Vector3.ClampMagnitude(depenetrate, 0.6f * interaction.DeltaTime);
                depenetrate -= step;
                feet += step;
                actor.Teleport(feet, endFacing);
            }

            if (m_Graph.IsValid())
            {
                if (ClipTime >= end)
                    StopClip();
                else
                    UpdateFade(ClipTime);
            }

            yield return null;
        }

        StopClip();
        SetHidden(false);
        if (m_LevelView)
            m_Rig.SetPitch(m_LevelPitch);

        if (m_HoldAfter > 0f)
            yield return interaction.Wait(m_HoldAfter);

        EnterPhase(PetCatPhase.Idle);
    }

    public override void Stop(Interaction interaction) => Cleanup(0.25f);

    /// <summary>Puts everything this step touched back. Safe to call any number of times.</summary>
    void Cleanup(float viewBlend)
    {
        DisableSkip();
        RemoveWalker();
        EndView(viewBlend);
        ReleaseRide(viewBlend);
        StopClip();
        SetHidden(false);
        m_Phase = PetCatPhase.Idle;
    }

    void ResetRunState()
    {
        // One step instance is reused for every run; a run cut short may have left things behind.
        Cleanup(0f);
        m_Skipping = false;
        m_ViewArrived = false;
        m_WalkSeconds = 0f;
        m_BobPhase = 0f;
        m_ViewVelocity = Vector3.zero;
        Walked = false;
    }

    void EnterPhase(PetCatPhase phase)
    {
        if (m_LogPhases && m_Interaction != null && m_Phase != PetCatPhase.Idle)
            Debug.Log($"Pet Cat: {m_Phase} took {m_Interaction.Elapsed - m_PhaseStarted:F2} s.");

        m_Phase = phase;
        m_PhaseStarted = m_Interaction != null ? m_Interaction.Elapsed : 0f;
    }

    void Validate(UnityEngine.Object context)
    {
        if (m_ClipCamera != null && m_ArmsAnimator != null && !m_ClipCamera.IsChildOf(m_ArmsAnimator.transform))
            Debug.LogWarning("Pet Cat: Clip Camera is not under the Arms Animator, so the walk can't line up with " +
                             "the clip's first frame.", context);
        if (m_ClipCamera == null)
            Debug.LogWarning("Pet Cat: no Clip Camera; the view stays on the player's head during the clip.", context);
        if (m_LookAt == PetCatLookTarget.LookPoint && m_LookPoint == null)
            Debug.LogWarning("Pet Cat: Look At is Look Point but no Look Point is set; using where the clip starts.", context);
    }

    // ---------------------------------------------------------------- Clip sampling

    float ClipStart => m_Clip != null ? Mathf.Clamp(m_StartTime, 0f, m_Clip.length) : 0f;

    float ClipEnd
    {
        get
        {
            if (m_Clip == null) return 0f;
            float end = m_EndTime > 0f ? Mathf.Min(m_EndTime, m_Clip.length) : m_Clip.length;
            return Mathf.Max(end, ClipStart);
        }
    }

    float ClipTime => m_Graph.IsValid() && m_Playables != null && m_Playables.Count > 0 ? (float)m_Playables[0].GetTime() : 0f;

    Quaternion CameraCorrection() =>
        AnchorAxes.TryCorrection(m_CameraForward, m_CameraUp, out Quaternion c) ? c : Quaternion.identity;

    void SampleClipCamera()
    {
        // Without a clip camera, the shot is "the head at the stand spot, looking ahead".
        m_ClipStartLocal = new Pose(HeadOffset(0f), Quaternion.identity);

        if (m_ClipCamera == null || m_ArmsAnimator == null || m_Clip == null)
            return;

        m_Clip.SampleAnimation(m_ArmsAnimator.gameObject, ClipStart);
        Quaternion bodyYaw = Quaternion.Euler(0f, m_Body.eulerAngles.y, 0f);
        Quaternion inverse = Quaternion.Inverse(bodyYaw);
        m_ClipStartLocal = new Pose(
            inverse * (m_ClipCamera.position - m_Body.position),
            inverse * (m_ClipCamera.rotation * CameraCorrection()));
    }

    void UpdateClipStartWorld()
    {
        Quaternion stand = Quaternion.Euler(0f, m_StandYaw, 0f);
        m_ClipStartWorld = new Pose(m_StandPosition + stand * m_ClipStartLocal.position, stand * m_ClipStartLocal.rotation);
    }

    /// <summary>Where the rig puts the eye relative to the feet, for a body facing yaw.</summary>
    Vector3 HeadOffset(float yaw)
    {
        Quaternion facing = Quaternion.Euler(0f, yaw, 0f);
        Vector3 offset = Vector3.up * m_Rig.EyeHeight + facing * Vector3.forward * m_Rig.EyeForward;
        if (m_Rig.PitchTransform != null)
            offset += facing * m_Rig.PitchTransform.localPosition;
        return offset;
    }

    // ---------------------------------------------------------------- View

    void BeginView()
    {
        Transform view = m_Rig.PitchTransform != null ? m_Rig.PitchTransform : m_Rig.transform;

        m_StandIn = new GameObject("[Pet Cat View]");
        m_StandIn.hideFlags = HideFlags.DontSave;
        m_StandIn.transform.SetParent(m_Actor.transform, false);
        m_StandIn.transform.SetPositionAndRotation(view.position, view.rotation);

        Vector3 e = view.rotation.eulerAngles;
        m_ViewEuler = new Vector3(Mathf.DeltaAngle(0f, e.x), e.y, Mathf.DeltaAngle(0f, e.z));

        m_Driver = m_StandIn.AddComponent<PetCatViewDriver>();
        m_Driver.Tick = UpdateView;

        // Riding our own stand-in lets the view turn freely (and roll) while the body walks.
        m_Rig.AttachTo(m_StandIn.transform, Quaternion.identity, false, 0f);
        m_Ridden = m_Rig;
    }

    /// <summary>Removes the stand-in. A negative blend leaves the rig alone (it is moving to another anchor).</summary>
    void EndView(float releaseBlend)
    {
        if (m_Driver != null)
            m_Driver.Tick = null;

        if (releaseBlend >= 0f && m_Rig != null && m_StandIn != null && m_Rig.Anchor == m_StandIn.transform)
        {
            m_Rig.Release(releaseBlend);
            m_Ridden = null;
        }

        if (m_StandIn != null)
            UnityEngine.Object.Destroy(m_StandIn);

        m_StandIn = null;
        m_Driver = null;
    }

    void ReleaseRide(float blend)
    {
        ICameraAnchor ridden = m_Ridden;
        m_Ridden = null;
        if (ridden == null || (ridden is UnityEngine.Object o && o == null))
            return;

        if (ridden.IsAnchored)
            ridden.Release(blend);
    }

    void UpdateView(float deltaTime)
    {
        if (m_StandIn == null || m_Body == null || deltaTime <= 0f)
            return;

        Vector3 feet = m_Body.position;
        float distance = FlatDistance(feet, m_StandPosition);

        // 0 while far, 1 once at the stand spot.
        float along = m_ViewArrived ? 1f
            : m_TurnInDistance > 0f ? 1f - Mathf.Clamp01(distance / m_TurnInDistance)
            : distance <= k_ArriveTolerance ? 1f : 0f;
        float blend = Mathf.Clamp01(m_TurnInCurve != null && m_TurnInCurve.length > 0 ? m_TurnInCurve.Evaluate(along) : along);

        // Eye position: the rig's own head placement, nudged onto the clip camera as we arrive.
        Vector3 eye = feet + HeadOffset(m_ViewEuler.y);
        Vector3 standEye = m_StandPosition + HeadOffset(m_StandYaw);
        Vector3 position = eye + (m_ClipStartWorld.position - standEye) * blend;

        // Gentle head bob, fading out as we arrive.
        float speed = m_Walker != null && m_WalkerInstalled ? m_Walker.CurrentSpeed : m_GlideSpeedNow;
        if (m_HeadBob > 0f && speed > 0.01f)
        {
            m_BobPhase += speed * deltaTime / m_StepLength * Mathf.PI;
            float amount = m_HeadBob * Mathf.Clamp01(speed / Mathf.Max(0.1f, m_WalkSpeed)) * (1f - blend);
            position.y += (Mathf.Abs(Mathf.Sin(m_BobPhase)) - 0.5f) * 2f * amount;
        }

        // Where to look: the chosen target while far, the clip's first frame (with its tilt) on arrival.
        Vector3 lookPoint = m_LookAt == PetCatLookTarget.LookPoint && m_LookPoint != null
            ? m_LookPoint.position
            : m_ClipStartWorld.position + m_ClipStartWorld.rotation * Vector3.forward * 1.5f;
        Vector3 toLook = lookPoint - position;
        Quaternion look = toLook.sqrMagnitude > 1e-4f
            ? Quaternion.LookRotation(toLook, Vector3.up)
            : m_ClipStartWorld.rotation;
        Quaternion goal = Quaternion.Slerp(look, m_ClipStartWorld.rotation, blend);

        Vector3 g = goal.eulerAngles;
        m_ViewEuler.x = Ease(m_ViewEuler.x, Mathf.DeltaAngle(0f, g.x), ref m_ViewVelocity.x, deltaTime, false);
        m_ViewEuler.y = Ease(m_ViewEuler.y, g.y, ref m_ViewVelocity.y, deltaTime, true);
        m_ViewEuler.z = Ease(m_ViewEuler.z, g.z, ref m_ViewVelocity.z, deltaTime, true);

        m_StandIn.transform.SetPositionAndRotation(position, Quaternion.Euler(m_ViewEuler.x, m_ViewEuler.y, m_ViewEuler.z));
    }

    /// <summary>Smooth follow with a speed cap and an acceleration cap, so turns never kick in or stop dead.</summary>
    float Ease(float current, float target, ref float velocity, float deltaTime, bool angle)
    {
        float before = velocity;
        float wanted = velocity;
        if (angle)
            Mathf.SmoothDampAngle(current, target, ref wanted, m_ViewSmoothing, m_MaxTurnSpeed, deltaTime);
        else
            Mathf.SmoothDamp(current, target, ref wanted, m_ViewSmoothing, m_MaxTurnSpeed, deltaTime);

        float maxChange = m_MaxTurnAcceleration * deltaTime;
        velocity = before + Mathf.Clamp(wanted - before, -maxChange, maxChange);

        // Never step past the target.
        float remaining = angle ? Mathf.DeltaAngle(current, target) : target - current;
        float step = velocity * deltaTime;
        if (Mathf.Abs(step) > Mathf.Abs(remaining) && Mathf.Sign(step) == Mathf.Sign(remaining))
        {
            step = remaining;
            velocity = 0f;
        }

        return current + step;
    }

    float ViewAngleToClipStart() =>
        m_StandIn != null ? Quaternion.Angle(m_StandIn.transform.rotation, m_ClipStartWorld.rotation) : 0f;

    // ---------------------------------------------------------------- Walking

    [NonSerialized] float m_GlideSpeedNow;

    void InstallWalker()
    {
        var locomotion = m_Character.GetProcessor<GroundLocomotionProcessor>();
        m_Walker = new Walker
        {
            Mover = m_Actor,
            Target = m_StandPosition,
            TargetYaw = m_StandYaw,
            Speed = m_WalkSpeed,
            Acceleration = m_Acceleration,
            Deceleration = m_Deceleration,
            TurnIn = m_TurnInDistance,
            MaxSpeed = locomotion != null && locomotion.MaxSpeed > 0f ? locomotion.MaxSpeed : 5f,
        };

        // The lock switched the character's host, input and smoothing off. Walking needs the host and
        // smoothing back on, reading our walker instead of the player's input.
        m_OldSource = m_Character.Source;
        m_Character.Source = m_Walker;
        m_HostWasEnabled = m_Character.enabled;
        MoveInterpolator interpolator = m_Character.Interpolator;
        m_InterpolatorWasEnabled = interpolator != null && interpolator.enabled;

        if (interpolator != null)
        {
            MoveState state = m_Actor.GetState();
            interpolator.BeginTick(state);
            interpolator.EndTick(state);
            interpolator.enabled = true;
        }

        m_Character.enabled = true;
        m_WalkerInstalled = true;
    }

    void RemoveWalker()
    {
        if (!m_WalkerInstalled)
            return;

        m_WalkerInstalled = false;
        if (m_Character != null)
        {
            // If something unlocked the player mid-walk, leave the character running, not frozen.
            bool locked = m_Character.IsLocked;
            m_Character.enabled = !locked || m_HostWasEnabled;
            m_Character.Source = m_OldSource;
            if (m_Character.Interpolator != null)
                m_Character.Interpolator.enabled = !locked || m_InterpolatorWasEnabled;
        }

        if (m_Walker != null)
            m_Walker.CurrentSpeed = 0f;
    }

    /// <summary>
    /// Eases the feet onto the stand spot along a curve that starts at <paramref name="startSpeed"/> and
    /// ends at rest, so a walk that hands over at speed carries straight on, and a start from rest eases in.
    /// </summary>
    IEnumerator Glide(Interaction interaction, float startSpeed)
    {
        // Start from where the body is drawn: it trails the simulation by up to a tick.
        Vector3 from = m_Body.position;
        float fromYaw = m_Body.eulerAngles.y;
        Vector3 target = m_StandPosition;
        if (m_KeepFeetOnFloor)
            target.y = from.y;

        m_Actor.Teleport(from, Quaternion.Euler(0f, fromYaw, 0f));

        float d = Vector3.Distance(from, target);
        if (d > 0.0005f)
        {
            float v = Mathf.Max(0f, startSpeed);
            // Short hand-off at walking speed: steady slow-down. From rest or a long way (a blocked walk):
            // an unhurried ease that never outpaces the walk.
            float duration = v > 0.05f && d < 0.3f ? 2f * d / v : Mathf.Max(0.6f, 1.6f * d / m_WalkSpeed + 0.2f);
            if (v > 0.01f)
                duration = Mathf.Min(duration, 3f * d / v); // No overshoot.
            duration = Mathf.Clamp(duration, 0.15f, 3f);

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += interaction.DeltaTime;
                float u = Mathf.Clamp01(elapsed / duration);

                // Cubic with start speed v and end speed 0 (Hermite).
                float u2 = u * u, u3 = u2 * u;
                float s = (u3 - 2f * u2 + u) * duration * v + (-2f * u3 + 3f * u2) * d;
                float along = Mathf.Clamp01(d > 0f ? s / d : 1f);
                float sv = ((3f * u2 - 4f * u + 1f) * duration * v + (-6f * u2 + 6f * u) * d) / duration;
                m_GlideSpeedNow = Mathf.Max(0f, sv);

                float yawT = u * u * (3f - 2f * u);
                m_Actor.Teleport(Vector3.Lerp(from, target, along),
                    Quaternion.Euler(0f, Mathf.LerpAngle(fromYaw, m_StandYaw, yawT), 0f));
                yield return null;
            }
        }

        m_GlideSpeedNow = 0f;
        m_Actor.Teleport(target, Quaternion.Euler(0f, m_StandYaw, 0f));
        m_StandPosition = target;
    }

    /// <summary>How fast the feet are really moving toward the stand spot.</summary>
    float ClosingSpeed()
    {
        MoveState state = m_Actor.GetState();
        Vector3 velocity = state.Velocity;
        velocity.y = 0f;
        Vector3 toward = m_StandPosition - state.Position;
        toward.y = 0f;
        return toward.sqrMagnitude > 1e-6f ? Mathf.Max(0f, Vector3.Dot(velocity, toward.normalized)) : 0f;
    }

    /// <summary>Which way and how far the capsule at feet must move to stop overlapping the world.</summary>
    Vector3 Penetration(Vector3 feet)
    {
        var capsule = m_Actor.GetComponent<CapsuleCollider>();
        if (capsule == null)
            return Vector3.zero;

        Vector3 push = Vector3.zero;
        float radius = capsule.radius;
        float bottom = Mathf.Max(capsule.center.y - capsule.height * 0.5f + radius, radius) + 0.05f;
        float top = capsule.center.y + capsule.height * 0.5f - radius;
        Collider[] hits = Physics.OverlapCapsule(feet + Vector3.up * bottom, feet + Vector3.up * Mathf.Max(top, bottom),
            radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider hit in hits)
        {
            if (hit.GetComponentInParent<KinematicMover>() == m_Actor)
                continue;
            if (Physics.ComputePenetration(capsule, feet + push, capsule.transform.rotation, hit,
                    hit.transform.position, hit.transform.rotation, out Vector3 dir, out float dist))
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 1e-6f)
                    push += flat.normalized * dist;
            }
        }

        return push;
    }

    /// <summary>Whether the player's capsule standing at feet would overlap the world.</summary>
    bool Blocked(Vector3 feet)
    {
        var capsule = m_Actor.GetComponent<CapsuleCollider>();
        if (capsule == null)
            return false;

        float radius = capsule.radius * 0.9f;
        float bottom = Mathf.Max(capsule.center.y - capsule.height * 0.5f + capsule.radius, radius) + 0.05f;
        float top = capsule.center.y + capsule.height * 0.5f - capsule.radius;
        Collider[] hits = Physics.OverlapCapsule(feet + Vector3.up * bottom, feet + Vector3.up * Mathf.Max(top, bottom),
            radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider hit in hits)
        {
            if (hit.GetComponentInParent<KinematicMover>() == m_Actor)
                continue;
            return true;
        }

        return false;
    }

    /// <summary>Feeds the character "walk to the stand spot" commands instead of player input.</summary>
    sealed class Walker : IMoveCommandSource
    {
        public KinematicMover Mover;
        public Vector3 Target;
        public float TargetYaw;
        public float Speed;
        public float Acceleration;
        public float Deceleration;
        public float TurnIn;
        public float MaxSpeed;
        public float CurrentSpeed;
        public float HandOffSpeed;
        public bool Arrived;

        public MoveCommand ReadCommand(float deltaTime)
        {
            MoveState state = Mover.GetState();
            Vector3 to = Target - state.Position;
            to.y = 0f;
            float distance = to.magnitude;

            if (distance <= k_ArriveTolerance || deltaTime <= 0f)
            {
                if (distance <= k_ArriveTolerance && !Arrived)
                {
                    Arrived = true;
                    HandOffSpeed = CurrentSpeed;
                }

                CurrentSpeed = 0f;
                return new MoveCommand { Yaw = TargetYaw };
            }

            // Speed up gently, and slow down so we would stop right on the spot.
            float stopping = Mathf.Sqrt(2f * Deceleration * distance);
            float wanted = Mathf.Min(Speed, stopping);
            CurrentSpeed = Mathf.Min(wanted, CurrentSpeed + Acceleration * deltaTime);
            CurrentSpeed = Mathf.Min(CurrentSpeed, distance / deltaTime);

            Vector3 direction = to / distance;
            float travelYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float turn = TurnIn > 0f ? 1f - Mathf.Clamp01(distance / TurnIn) : 0f;
            turn = turn * turn * (3f - 2f * turn);

            return new MoveCommand
            {
                MoveDirection = direction * Mathf.Clamp01(CurrentSpeed / MaxSpeed),
                Yaw = Mathf.LerpAngle(travelYaw, TargetYaw, turn),
            };
        }
    }

    // ---------------------------------------------------------------- Clip playback

    bool StartClip(Interactable owner)
    {
        m_Playables = new List<AnimationClipPlayable>();
        m_Fading = new List<AnimationPlayableOutput>();

        if (m_ArmsAnimator != null)
        {
            m_ArmsWereActive = m_ArmsAnimator.gameObject.activeSelf;
            m_ArmsTouched = true;
            m_ArmsAnimator.gameObject.SetActive(true);
        }

        // Only the motion is borrowed: a camera exported on the bone would render over the player's.
        if (m_ClipCamera != null && m_ClipCamera.TryGetComponent(out Camera boneCamera) && boneCamera.enabled)
        {
            boneCamera.enabled = false;
            m_DisabledClipCamera = boneCamera;
            Debug.LogWarning($"Pet Cat: '{m_ClipCamera.name}' has an enabled Camera; switched it off while petting. " +
                             "Untick that Camera to silence this.", boneCamera);
        }

        m_Graph = PlayableGraph.Create($"{(owner != null ? owner.name : "Pet Cat")} (Pet Cat)");
        m_Graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        AddOutput(m_ArmsAnimator, false);
        if (m_AlsoPlayOn != null)
            for (int i = 0; i < m_AlsoPlayOn.Length; i++)
                AddOutput(m_AlsoPlayOn[i], true);

        if (m_Playables.Count == 0)
        {
            Debug.LogWarning("Pet Cat: no Animators to play the clip on.", owner);
            StopClip();
            return false;
        }

        UpdateFade(ClipStart);
        m_Graph.Play();
        return true;
    }

    void AddOutput(Animator animator, bool fades)
    {
        if (animator == null)
            return;

        var playable = AnimationClipPlayable.Create(m_Graph, m_Clip);
        playable.SetTime(ClipStart);
        playable.SetSpeed(m_Speed);

        var output = AnimationPlayableOutput.Create(m_Graph, animator.name, animator);
        output.SetSourcePlayable(playable);

        // Only an Animator with a controller has a pose to fade against.
        if (fades && animator.runtimeAnimatorController != null)
            m_Fading.Add(output);

        m_Playables.Add(playable);
    }

    void SetSpeed(float speed)
    {
        for (int i = 0; i < m_Playables.Count; i++)
            m_Playables[i].SetSpeed(speed);
    }

    void UpdateFade(float clipTime)
    {
        float weight = 1f;
        if (m_AlsoPlayOnBlendIn > 0f) weight = Mathf.Min(weight, (clipTime - ClipStart) / m_AlsoPlayOnBlendIn);
        if (m_AlsoPlayOnBlendOut > 0f) weight = Mathf.Min(weight, (ClipEnd - clipTime) / m_AlsoPlayOnBlendOut);
        weight = Mathf.Clamp01(weight);

        for (int i = 0; i < m_Fading.Count; i++)
            m_Fading[i].SetWeight(weight);
    }

    void StopClip()
    {
        if (m_Graph.IsValid())
            m_Graph.Destroy();

        m_Playables?.Clear();
        m_Fading?.Clear();

        if (m_ArmsTouched && m_ArmsAnimator != null)
            m_ArmsAnimator.gameObject.SetActive(m_ArmsWereActive);
        m_ArmsTouched = false;

        if (m_DisabledClipCamera != null)
            m_DisabledClipCamera.enabled = true;
        m_DisabledClipCamera = null;
    }

    void SetHidden(bool hidden)
    {
        if (m_HideWhilePetting == null || m_HideWhilePetting.Length == 0)
            return;

        if (hidden)
        {
            m_HiddenWereActive = new bool[m_HideWhilePetting.Length];
            for (int i = 0; i < m_HideWhilePetting.Length; i++)
            {
                GameObject go = m_HideWhilePetting[i];
                m_HiddenWereActive[i] = go != null && go.activeSelf;
                if (go != null) go.SetActive(false);
            }

            return;
        }

        if (m_HiddenWereActive == null)
            return;

        for (int i = 0; i < m_HideWhilePetting.Length && i < m_HiddenWereActive.Length; i++)
        {
            if (m_HideWhilePetting[i] != null && m_HiddenWereActive[i])
                m_HideWhilePetting[i].SetActive(true);
        }

        m_HiddenWereActive = null;
    }

    // ---------------------------------------------------------------- Skip input

    void EnableSkip()
    {
        if (!m_AllowSkip)
            return;

        InputAction action = m_SkipButton.action;
        if (action == null)
            return;

        // An inline action keeps a stale binding cache after loading; a JSON copy starts clean.
        m_SkipAction = m_SkipButton.reference == null ? Uncached(action) : action;
        m_SkipAction.Enable();
    }

    void DisableSkip()
    {
        if (m_SkipAction != null && m_SkipButton.reference == null)
        {
            m_SkipAction.Disable();
            m_SkipAction.Dispose();
        }

        m_SkipAction = null;
    }

    static InputAction Uncached(InputAction action)
    {
        var copy = new InputAction();
        JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(action), copy);
        return copy;
    }

    static InputAction DefaultSkipAction()
    {
        var action = new InputAction("Skip", InputActionType.Button);
        action.AddBinding("<Keyboard>/e");
        action.AddBinding("<Gamepad>/buttonWest");
        action.AddBinding("<TouchRig>/buttonAction");
        return action;
    }

    // ---------------------------------------------------------------- Math

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    static float YawOf(Quaternion rotation)
    {
        Vector3 f = rotation * Vector3.forward;
        return Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
    }

    static float PitchOf(Quaternion rotation)
    {
        Vector3 f = rotation * Vector3.forward;
        return -Mathf.Atan2(f.y, new Vector2(f.x, f.z).magnitude) * Mathf.Rad2Deg;
    }
}
