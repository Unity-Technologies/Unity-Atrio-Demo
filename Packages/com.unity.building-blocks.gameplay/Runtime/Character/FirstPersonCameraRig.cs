using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The first-person head: turns mouse and stick look into a yaw the character follows and a
    /// pitch the character never sees.
    /// </summary>
    /// <remarks>
    /// This transform carries world yaw only; <see cref="PitchTransform"/> (the camera underneath)
    /// carries pitch. Assign this transform to <see cref="PlayerInputSource.YawSource"/>. One Look
    /// action carries mouse and stick: a pointer delta is applied as distance (<see cref="Sensitivity"/>),
    /// a stick as a rate (<see cref="StickSensitivity"/>); see <see cref="ReadsAsDelta"/>.
    /// While the <see cref="Character"/> is locked the rig ignores input and mirrors the body's yaw,
    /// unless <see cref="LookWhileLocked"/> leaves look to the player.
    /// Interactions can tilt it through <see cref="ILookPitch"/> and hand it to an animated camera
    /// through <see cref="ICameraAnchor"/> (<see cref="AttachTo"/> / <see cref="Release"/>); control
    /// returns without a snap.
    /// </remarks>
    [AddComponentMenu("Building Blocks/First Person Camera Rig")]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class FirstPersonCameraRig : MonoBehaviour, ILookPitch, ICameraAnchor, IFreeLook
    {
        [Header("Hierarchy")]
        [SerializeField] Transform m_PitchTransform;
        [SerializeField] Transform m_Follow;
        [SerializeField] MoveInterpolator m_Interpolator;

        [Header("Look")]
        [SerializeField]
        [Tooltip("Look input from any device. Mouse delta by default plus the right stick; tick Use Reference to " +
                 "drag in your asset's Look action. A pointer is read as a distance, a stick as a rate (see " +
                 "Sensitivity and Stick Sensitivity), decided per device, so one action carries both.")]
        InputActionProperty m_Look = new InputActionProperty(DefaultLookAction());

        [SerializeField]
        [Tooltip("Hands the cursor back to the desktop. Escape by default; tick Use Reference to drag in your " +
                 "asset's Unlock Cursor action.")]
        InputActionProperty m_Unlock = new InputActionProperty(DefaultUnlockAction());

        [SerializeField, Min(0f)]
        [Tooltip("Degrees of rotation per unit of look input (per mouse pixel). 0.1 is a 360° " +
                 "turn per 3600 px of mouse travel.")]
        float m_Sensitivity = k_DefaultSensitivity;

        [SerializeField, Min(0f)]
        [Tooltip("Degrees per second at full stick deflection. A stick is a rate, not a movement, " +
                 "so it cannot share the mouse's per-pixel sensitivity.")]
        float m_StickSensitivity = k_DefaultStickSensitivity;

        [SerializeField, Min(0f)]
        [Tooltip("Seconds the view takes to settle onto where the input is pointing. 0 is instant. " +
                 "Use it for live play. A recorded walkthrough reads better around 0.05-0.15.")]
        float m_LookSmoothing = k_DefaultLookSmoothing;

        [SerializeField]
        [Tooltip("Invert the vertical look axis: push forward to look up. A player preference, " +
                 "not a design decision; expose it in your own settings UI.")]
        bool m_InvertLookY;

        [SerializeField, Range(-89f, 0f)]
        [Tooltip("How far down the camera may pitch, in degrees.")]
        float m_MinPitch = k_DefaultMinPitch;

        [SerializeField, Range(0f, 89f)]
        [Tooltip("How far up the camera may pitch, in degrees. Stops one degree short of " +
                 "straight up, where yaw becomes undefined.")]
        float m_MaxPitch = k_DefaultMaxPitch;

        [Header("Placement")]
        [SerializeField, Min(0f)]
        [Tooltip("Eye height above the character's feet, in meters.")]
        float m_EyeHeight = k_DefaultEyeHeight;

        [SerializeField]
        [Tooltip("How far the eye sits ahead of the capsule's axis, in meters, along the rig's own " +
                 "facing. A few centimetres stops the view from looking as if it were mounted in the " +
                 "middle of the head. Negative pulls it back.")]
        float m_EyeForward = k_DefaultEyeForward;

        [SerializeField] bool m_LockCursor = true;

        /// <summary>Degrees of rotation per unit of look input (per mouse pixel). Default 0.1.</summary>
        const float k_DefaultSensitivity = 0.1f;

        /// <summary>Degrees per second at full right-stick deflection. Default 180, a half turn per second.</summary>
        const float k_DefaultStickSensitivity = 180f;

        /// <summary>Seconds of look smoothing. Default 0, instant.</summary>
        const float k_DefaultLookSmoothing = 0f;

        /// <summary>
        /// Pitch clamp, in degrees. Stops one short of straight up and down, where yaw is undefined.
        /// </summary>
        const float k_DefaultMinPitch = -89f;

        /// <inheritdoc cref="k_DefaultMinPitch"/>
        const float k_DefaultMaxPitch = 89f;

        /// <summary>
        /// Eye height above the character's feet, in meters. Default 1.6, a little under the
        /// mover's default 1.8 m capsule, so the view sits in the head rather than on top of it.
        /// </summary>
        const float k_DefaultEyeHeight = 1.6f;

        /// <summary>Eye offset ahead of the capsule axis, in meters. Default 0.</summary>
        const float k_DefaultEyeForward = 0f;

        // Where the input says the view should be, and where the view is. Equal unless smoothing.
        float m_TargetYaw;
        float m_TargetPitch;
        float m_Yaw;
        float m_Pitch;

        Transform m_Root;
        Character m_ControlLock;
        bool m_ControlLocked;
        bool m_LookEnabled = true;
        bool m_LookWhileLocked;

        // The actions OnEnable enabled, so OnDisable undoes exactly that. Only inline ones are
        // disabled again; asset actions are shared.
        InputAction m_LookAction;
        InputAction m_UnlockAction;
        bool m_OwnsLook;
        bool m_OwnsUnlock;

        // None: the rig places the view. Riding: the view copies m_Anchor. Releasing: easing back to
        // the head.
        enum RideState { None, Riding, Releasing }

        RideState m_RideState;
        Transform m_Anchor;
        Camera m_AnchorCamera;
        Quaternion m_AnchorCorrection = Quaternion.identity;
        bool m_CopyFieldOfView;
        Camera m_Camera;
        float m_BaseFieldOfView;
        float m_BlendSeconds;
        float m_BlendElapsed;
        Vector3 m_BlendFromPosition;
        Quaternion m_BlendFromRotation = Quaternion.identity;
        float m_BlendFromFieldOfView;

        /// <summary>
        /// The camera transform that carries pitch. Rotated about its local X axis only.
        /// </summary>
        public Transform PitchTransform
        {
            get => m_PitchTransform;
            set => m_PitchTransform = value;
        }

        /// <summary>
        /// The transform this rig copies its position from, normally the interpolated visual
        /// root. <c>null</c> leaves placement to parenting, which renders at the tick rate.
        /// </summary>
        public Transform Follow
        {
            get => m_Follow;
            set => m_Follow = value;
        }

        /// <summary>Current yaw in degrees, world space. This is what the character follows.</summary>
        public float Yaw => m_Yaw;

        /// <summary>Current pitch in degrees, clamped. The character never sees this value.</summary>
        public float Pitch => m_Pitch;

        /// <summary>Where the eye is, in world space. This transform sits at the eye, so it is its position.</summary>
        public Vector3 EyePosition => transform.position;

        /// <summary>Where the look input is pointing the yaw. Equals <see cref="Yaw"/> unless smoothing.</summary>
        public float TargetYaw => m_TargetYaw;

        /// <summary>Where the look input is pointing the pitch. Equals <see cref="Pitch"/> unless smoothing.</summary>
        public float TargetPitch => m_TargetPitch;

        /// <summary>Degrees of rotation per unit of look input. Default <c>0.1</c>.</summary>
        public float Sensitivity
        {
            get => m_Sensitivity;
            set => m_Sensitivity = value;
        }

        /// <summary>Degrees per second at full stick deflection. Default <c>180</c>.</summary>
        public float StickSensitivity
        {
            get => m_StickSensitivity;
            set => m_StickSensitivity = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Seconds the view takes to settle onto the look target. Default <c>0</c>, instant.
        /// </summary>
        /// <remarks>Settable at runtime, for example from a settings menu.</remarks>
        public float LookSmoothing
        {
            get => m_LookSmoothing;
            set => m_LookSmoothing = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Whether pushing the mouse forward looks down instead of up. Default <c>false</c>.
        /// </summary>
        /// <remarks>Settable at runtime, for a settings screen.</remarks>
        public bool InvertLookY
        {
            get => m_InvertLookY;
            set => m_InvertLookY = value;
        }

        /// <summary>Eye height above the character's feet, in meters. Default <c>1.6</c>.</summary>
        public float EyeHeight
        {
            get => m_EyeHeight;
            set => m_EyeHeight = value;
        }

        /// <summary>Eye offset ahead of the capsule axis along the facing, in meters. Default <c>0</c>.</summary>
        public float EyeForward
        {
            get => m_EyeForward;
            set => m_EyeForward = value;
        }

        /// <summary>
        /// The eye's offset from the followed transform, in world space: up by <see cref="EyeHeight"/>
        /// and along the rig's current facing by <see cref="EyeForward"/>.
        /// </summary>
        public Vector3 EyeOffset => Vector3.up * m_EyeHeight + YawForward() * m_EyeForward;

        /// <summary>
        /// Whether the rig reads look input. <c>false</c> holds the view where it is (a pause menu,
        /// a dialogue). A locked <see cref="Character"/> overrides this while held.
        /// </summary>
        public bool LookEnabled
        {
            get => m_LookEnabled;
            set => m_LookEnabled = value;
        }

        /// <summary>Whether the <see cref="Character"/> currently has its control locked.</summary>
        public bool IsControlLocked => m_ControlLocked;

        /// <summary>
        /// Whether look input still turns the view while the <see cref="Character"/> is locked. Default
        /// <c>false</c>: a locked view follows the body.
        /// </summary>
        /// <remarks>
        /// A Lock Player step set to movement only turns it on for its run. While on, the body keeps the
        /// facing the lock left it with; turned on while locked, the view starts from the body's facing.
        /// When control comes back the view stays put and the body turns to it.
        /// </remarks>
        public bool LookWhileLocked
        {
            get => m_LookWhileLocked;
            set
            {
                // Handed back while locked: start from where the body faces, which a step may have just
                // turned, so the view does not stay behind.
                if (value && !m_LookWhileLocked && m_ControlLocked)
                {
                    MirrorRootYaw();
                    Publish();
                }

                m_LookWhileLocked = value;
            }
        }

        // Locked, and look not left to the player: the view mirrors the body and reads no input.
        bool FollowsBody => m_ControlLocked && !m_LookWhileLocked;

        /// <summary>
        /// Whether the rig takes the cursor while enabled and ignores look input while the cursor is
        /// free. Default <c>true</c>.
        /// </summary>
        /// <remarks>
        /// Turn it off for a build that draws its own cursor, or a test without a window. Applies on the
        /// next enable.
        /// </remarks>
        public bool LockCursor
        {
            get => m_LockCursor;
            set => m_LockCursor = value;
        }

        /// <inheritdoc/>
        public bool IsAnchored => m_RideState == RideState.Riding;

        /// <summary>The transform the view is riding, or <c>null</c> when it sits on the head.</summary>
        public Transform Anchor => m_RideState == RideState.Riding ? m_Anchor : null;

        /// <summary>The transform that carries the full view: the camera under the rig, or the rig itself.</summary>
        Transform ViewTransform => m_PitchTransform != null ? m_PitchTransform : transform;

        /// <summary>
        /// The look action in use: the inline one, or the asset's when the field is a reference.
        /// <c>null</c> when it is a reference to nothing. Carries pointer and stick input; see
        /// <see cref="ReadsAsDelta"/>.
        /// </summary>
        public InputAction LookAction => m_Look.action;

        /// <summary>The cursor-unlock action in use (see <see cref="LookAction"/> for what "in use" means).</summary>
        public InputAction UnlockAction => m_Unlock.action;

        /// <summary>
        /// Whether a look value from <paramref name="control"/> is a distance (a pointer
        /// <c>delta</c>, applied once) rather than a rate (a stick, integrated over time). A composite
        /// of keys reads as a rate.
        /// </summary>
        public static bool ReadsAsDelta(InputControl control)
        {
            if (control == null)
                return false;

            if (control is DeltaControl)
                return true;

            // On older Input System versions a pointer delta is a Vector2Control.
            return control.device is Pointer pointer && ReferenceEquals(control, pointer.delta);
        }

        /// <summary>
        /// Applies one frame's worth of mouse look and republishes both rotations.
        /// </summary>
        /// <remarks>
        /// Drives the rig without a device. <paramref name="lookDelta"/> is in look-action units (mouse
        /// pixels), scaled by <see cref="Sensitivity"/>, and honours <see cref="LookSmoothing"/>.
        /// </remarks>
        /// <param name="lookDelta">Look input for this frame. X turns, Y pitches.</param>
        public void ApplyLook(Vector2 lookDelta)
        {
            Aim(lookDelta.x * m_Sensitivity, lookDelta.y * m_Sensitivity);
        }

        /// <summary>
        /// Applies a held stick for <paramref name="deltaTime"/> seconds: <paramref name="stick"/> is
        /// a deflection in -1..1 per axis, turned into degrees at <see cref="StickSensitivity"/>.
        /// </summary>
        public void ApplyLookRate(Vector2 stick, float deltaTime)
        {
            float degrees = m_StickSensitivity * deltaTime;
            Aim(stick.x * degrees, stick.y * degrees);
        }

        /// <summary>
        /// Points the rig at an absolute yaw and pitch, skipping smoothing. Use it for spawns,
        /// cutscenes and restored saves.
        /// </summary>
        /// <param name="yaw">Yaw in degrees, world space.</param>
        /// <param name="pitch">Pitch in degrees; clamped to the configured range.</param>
        public void SetLook(float yaw, float pitch)
        {
            m_TargetYaw = m_Yaw = yaw;
            m_TargetPitch = m_Pitch = Mathf.Clamp(pitch, m_MinPitch, m_MaxPitch);

            Publish();
        }

        /// <summary>
        /// Points the view up or down and keeps the yaw, skipping smoothing. Clamped like any other
        /// input. While control is locked the yaw is the body's, so a body turned this frame is
        /// faced at once.
        /// </summary>
        public void SetPitch(float pitch)
        {
            // An interaction may have turned the body this frame; the next LateUpdate would mirror it,
            // but anything reading the camera before then would see the old facing.
            if (FollowsBody)
                MirrorRootYaw();

            SetLook(m_Yaw, pitch);
        }

        /// <inheritdoc/>
        public void AttachTo(Transform anchor, Quaternion correction, bool copyFieldOfView, float blendSeconds)
        {
            if (anchor == null)
            {
                Release(blendSeconds);
                return;
            }

            if (m_Camera == null && m_PitchTransform != null)
                m_Camera = m_PitchTransform.GetComponent<Camera>();

            // Restore the FOV from before the first ride, not one a previous anchor left mid-blend.
            if (m_RideState == RideState.None && m_Camera != null)
                m_BaseFieldOfView = m_Camera.fieldOfView;

            BeginBlend(blendSeconds);

            m_Anchor = anchor;
            m_AnchorCamera = copyFieldOfView ? anchor.GetComponent<Camera>() : null;
            m_AnchorCorrection = correction;
            m_CopyFieldOfView = m_AnchorCamera != null && m_Camera != null;
            m_RideState = RideState.Riding;
        }

        /// <inheritdoc/>
        public void Release(float blendSeconds)
        {
            if (m_RideState != RideState.Riding)
                return;

            BeginBlend(blendSeconds);

            // Control comes back looking where the shot ended; a locked view that follows the body
            // overrides the yaw.
            SeedFromView();

            m_Anchor = null;
            m_AnchorCamera = null;
            m_RideState = RideState.Releasing;
        }

        /// <summary>
        /// Moves the view on by one frame: input, smoothing, the follow, and any ride in progress.
        /// <c>LateUpdate</c> calls this with the frame time; call it yourself to drive the rig with
        /// your own clock.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (m_RideState == RideState.Riding)
            {
                if (m_Anchor != null)
                {
                    AdvanceRide(deltaTime);
                    return;
                }

                // The anchor was destroyed: go home over the same blend.
                Release(m_BlendSeconds);
            }

            if (m_RideState == RideState.Releasing)
            {
                AdvanceRelease(deltaTime);
                return;
            }

            if (FollowsBody)
            {
                MirrorRootYaw();
            }
            else if (m_LookEnabled && (!m_LockCursor || Cursor.lockState == CursorLockMode.Locked))
            {
                // Ignore look while the cursor is free, so moving the mouse on the desktop does not spin the view.
                ReadLook(deltaTime);
            }

            Settle(deltaTime);
            Publish();

            if (m_Follow != null)
                transform.position = m_Follow.position + EyeOffset;
        }

        /// <summary>
        /// Reads the Look action: a pointer delta as distance, a stick as a rate over
        /// <paramref name="deltaTime"/>. If both move, the Input System reports the larger.
        /// </summary>
        void ReadLook(float deltaTime)
        {
            InputAction look = m_LookAction;
            if (look == null || !look.enabled)
                return;

            Vector2 value = look.ReadValue<Vector2>();
            if (value == Vector2.zero)
                return;

            if (ReadsAsDelta(look.activeControl))
                ApplyLook(value);
            else
                ApplyLookRate(value, deltaTime);
        }

        void BeginBlend(float seconds)
        {
            m_BlendSeconds = Mathf.Max(0f, seconds);
            m_BlendElapsed = 0f;

            Transform view = ViewTransform;
            m_BlendFromPosition = view.position;
            m_BlendFromRotation = view.rotation;
            m_BlendFromFieldOfView = m_Camera != null ? m_Camera.fieldOfView : 0f;
        }

        /// <summary>Advances the blend clock and returns how far along it is, eased, 0 to 1.</summary>
        float BlendProgress(float deltaTime)
        {
            if (m_BlendSeconds <= 0f)
                return 1f;

            m_BlendElapsed += deltaTime;
            float t = Mathf.Clamp01(m_BlendElapsed / m_BlendSeconds);
            return t * t * (3f - 2f * t);
        }

        void AdvanceRide(float deltaTime)
        {
            float along = BlendProgress(deltaTime);

            Vector3 position = Vector3.Lerp(m_BlendFromPosition, m_Anchor.position, along);
            Quaternion rotation = Quaternion.Slerp(m_BlendFromRotation, m_Anchor.rotation * m_AnchorCorrection, along);
            PlaceView(position, rotation);

            if (m_CopyFieldOfView && m_AnchorCamera != null && m_Camera != null)
                m_Camera.fieldOfView = Mathf.Lerp(m_BlendFromFieldOfView, m_AnchorCamera.fieldOfView, along);
        }

        void AdvanceRelease(float deltaTime)
        {
            float along = BlendProgress(deltaTime);

            if (FollowsBody)
                MirrorRootYaw();

            Vector3 position = Vector3.Lerp(m_BlendFromPosition, HeadPosition(), along);
            Quaternion rotation = Quaternion.Slerp(m_BlendFromRotation, Quaternion.Euler(m_Pitch, m_Yaw, 0f), along);
            PlaceView(position, rotation);

            if (m_CopyFieldOfView && m_Camera != null)
                m_Camera.fieldOfView = Mathf.Lerp(m_BlendFromFieldOfView, m_BaseFieldOfView, along);

            if (along < 1f)
                return;

            // Home. From the next frame the ordinary path places the view again.
            if (m_CopyFieldOfView && m_Camera != null)
                m_Camera.fieldOfView = m_BaseFieldOfView;

            m_CopyFieldOfView = false;
            m_RideState = RideState.None;
        }

        /// <summary>Where the eye sits when nothing is riding: the followed root plus the eye offset.</summary>
        Vector3 HeadPosition()
        {
            if (m_Follow != null)
                return m_Follow.position + EyeOffset;

            if (transform.parent != null)
                return transform.parent.TransformPoint(new Vector3(0f, m_EyeHeight, m_EyeForward));

            return m_BlendFromPosition;
        }

        /// <summary>
        /// Puts the view at a world pose: yaw on this transform, everything else on the camera
        /// underneath.
        /// </summary>
        void PlaceView(Vector3 position, Quaternion rotation)
        {
            YawAndPitchOf(rotation, out float yaw, out float pitch);
            m_TargetYaw = m_Yaw = yaw;
            m_TargetPitch = m_Pitch = Mathf.Clamp(pitch, m_MinPitch, m_MaxPitch);

            var yawOnly = Quaternion.Euler(0f, yaw, 0f);
            transform.rotation = yawOnly;

            if (m_PitchTransform != null)
            {
                transform.position = position - yawOnly * m_PitchTransform.localPosition;
                m_PitchTransform.rotation = rotation;
            }
            else
            {
                transform.position = position;
            }
        }

        /// <summary>Re-seeds yaw and pitch from where the view is looking right now.</summary>
        void SeedFromView()
        {
            YawAndPitchOf(ViewTransform.rotation, out float yaw, out float pitch);
            m_TargetYaw = m_Yaw = yaw;
            m_TargetPitch = m_Pitch = Mathf.Clamp(pitch, m_MinPitch, m_MaxPitch);
        }

        /// <summary>The yaw about world up and the pitch (positive down) of a rotation's forward.</summary>
        void YawAndPitchOf(Quaternion rotation, out float yaw, out float pitch)
        {
            Vector3 forward = rotation * Vector3.forward;
            float flat = new Vector2(forward.x, forward.z).magnitude;

            // Straight up or down has no yaw; keep the one we had.
            yaw = flat > 1e-4f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : m_Yaw;
            pitch = -Mathf.Atan2(forward.y, flat) * Mathf.Rad2Deg;
        }

        /// <summary>Moves the look target by degrees, then publishes, instantly unless smoothing.</summary>
        void Aim(float yawDegrees, float pitchDegrees)
        {
            m_TargetYaw += yawDegrees;

            // Mouse forward reports +Y and must look up, which is negative pitch.
            float pitchDelta = pitchDegrees * (m_InvertLookY ? 1f : -1f);
            m_TargetPitch = Mathf.Clamp(m_TargetPitch + pitchDelta, m_MinPitch, m_MaxPitch);

            if (m_LookSmoothing <= 0f)
            {
                m_Yaw = m_TargetYaw;
                m_Pitch = m_TargetPitch;
            }

            Publish();
        }

        /// <summary>
        /// Eases the view toward the target exponentially, so smoothing feels the same at any frame
        /// rate.
        /// </summary>
        void Settle(float deltaTime)
        {
            if (m_LookSmoothing <= 0f)
            {
                m_Yaw = m_TargetYaw;
                m_Pitch = m_TargetPitch;
                return;
            }

            float k = 1f - Mathf.Exp(-deltaTime / m_LookSmoothing);
            m_Yaw = Mathf.LerpAngle(m_Yaw, m_TargetYaw, k);
            m_Pitch = Mathf.Lerp(m_Pitch, m_TargetPitch, k);
        }

        void Publish()
        {
            // A ride places the view itself.
            if (m_RideState != RideState.None)
                return;

            // World, not local: the parent is yawed to this value, so a local write would compound.
            transform.rotation = Quaternion.Euler(0f, m_Yaw, 0f);

            if (m_PitchTransform != null)
                m_PitchTransform.localRotation = Quaternion.Euler(m_Pitch, 0f, 0f);
        }

        Vector3 YawForward() => Quaternion.Euler(0f, m_Yaw, 0f) * Vector3.forward;

        /// <summary>
        /// While locked, the head follows the body's yaw with no smoothing.
        /// </summary>
        void MirrorRootYaw()
        {
            if (m_Root == null)
                return;

            m_TargetYaw = m_Yaw = m_Root.eulerAngles.y;
        }

        void OnControlLocked() => m_ControlLocked = true;

        void OnControlUnlocked()
        {
            m_ControlLocked = false;

            // A view the player kept turning stays put; the body turns to it on the next tick.
            if (m_LookWhileLocked)
                return;

            // Face where the interactable left the body. During a ride, Publish is a no-op until home.
            MirrorRootYaw();
            Publish();
        }

        void Reset()
        {
            m_Interpolator = GetComponentInParent<MoveInterpolator>();

            if (m_Interpolator != null)
                m_Follow = m_Interpolator.VisualRoot;

            var camera = GetComponentInChildren<Camera>();
            if (camera != null)
                m_PitchTransform = camera.transform;

            PlaceForEditing();
        }

        /// <summary>
        /// Keeps the rig where the eye will be at runtime, so the scene view shows the camera at eye
        /// height instead of at the character's feet. Play mode overrides this every frame.
        /// </summary>
        void OnValidate()
        {
            if (!Application.isPlaying)
                PlaceForEditing();
        }

        void PlaceForEditing()
        {
            var local = new Vector3(0f, m_EyeHeight, m_EyeForward);
            if ((transform.localPosition - local).sqrMagnitude > 1e-8f)
                transform.localPosition = local;
        }

        void Awake()
        {
            if (m_Interpolator == null)
                m_Interpolator = GetComponentInParent<MoveInterpolator>();

            // Follow the smooth visual root, not the stepped character root.
            if (m_Follow == null && m_Interpolator != null)
                m_Follow = m_Interpolator.VisualRoot;

            var mover = GetComponentInParent<KinematicMover>();
            m_Root = mover != null ? mover.transform : null;
            m_ControlLock = GetComponentInParent<Character>();

            // Start facing where the prefab was placed.
            SetLook(transform.eulerAngles.y, 0f);
        }

        void OnEnable()
        {
            m_LookAction = m_Look.action;
            m_UnlockAction = m_Unlock.action;
            m_OwnsLook = m_Look.reference == null && m_LookAction != null;
            m_OwnsUnlock = m_Unlock.reference == null && m_UnlockAction != null;
            m_LookAction?.Enable();
            m_UnlockAction?.Enable();

            if (m_ControlLock != null)
            {
                m_ControlLock.Locked += OnControlLocked;
                m_ControlLock.Unlocked += OnControlUnlocked;
                m_ControlLocked = m_ControlLock.IsLocked;
            }

            if (m_LockCursor)
                SetCursorLocked(true);
        }

        void OnDisable()
        {
            // Only inline actions go off; asset actions are shared and stay enabled.
            if (m_OwnsLook) m_LookAction?.Disable();
            if (m_OwnsUnlock) m_UnlockAction?.Disable();
            m_LookAction = null;
            m_UnlockAction = null;

            if (m_ControlLock != null)
            {
                m_ControlLock.Locked -= OnControlLocked;
                m_ControlLock.Unlocked -= OnControlUnlocked;
            }

            if (m_LockCursor)
                SetCursorLocked(false);
        }

        void LateUpdate()
        {
            if (m_LockCursor)
                UpdateCursorLock();

            Advance(Time.deltaTime);
        }

        /// <summary>
        /// Escape frees the cursor; clicking back into the window takes it again.
        /// </summary>
        /// <remarks>
        /// The re-lock reads the mouse directly so it cannot be rebound away.
        /// </remarks>
        void UpdateCursorLock()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                if (m_UnlockAction != null && m_UnlockAction.enabled && m_UnlockAction.WasPressedThisFrame())
                    SetCursorLocked(false);

                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                SetCursorLocked(true);
        }

        static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

#if UNITY_EDITOR
        /// <summary>The eye, and which way it looks, in the scene view.</summary>
        void OnDrawGizmosSelected()
        {
            Vector3 eye = Application.isPlaying && m_Follow != null
                ? m_Follow.position + EyeOffset
                : transform.position;

            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(eye, 0.06f);
            Gizmos.DrawLine(eye, eye + transform.forward * 0.5f);
        }
#endif

        /// <summary>
        /// Mouse delta and the right stick on one action, like Unity's default input asset.
        /// </summary>
        static InputAction DefaultLookAction()
        {
            var action = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            action.AddBinding("<Mouse>/delta");
            action.AddBinding("<Gamepad>/rightStick");
            return action;
        }

        /// <summary>Escape, to hand the cursor back to the desktop.</summary>
        static InputAction DefaultUnlockAction()
        {
            var action = new InputAction("Unlock", InputActionType.Button);
            action.AddBinding("<Keyboard>/escape");
            return action;
        }
    }
}
