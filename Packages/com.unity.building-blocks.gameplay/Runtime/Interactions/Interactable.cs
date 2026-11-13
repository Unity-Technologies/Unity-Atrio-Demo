using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>When an <see cref="Interactable"/> starts on its own. Numeric values are fixed so saved scenes keep their meaning.</summary>
    public enum InteractablePlayOn
    {
        /// <summary>Only when something calls <see cref="Interactable.Play()"/>. "Manual" in the inspector.</summary>
        Manual = 0,

        /// <summary>When the component is enabled, such as an intro on scene start. "On enable" in the inspector.</summary>
        Enable = 1,

        /// <summary>
        /// When <see cref="Interactable.Button"/> is pressed (E or the gamepad west button by default)
        /// while something <see cref="Interactable.Zone"/> detects is inside. Without a zone, works
        /// anywhere. "Volume, On press" in the inspector.
        /// </summary>
        Interact = 2,

        /// <summary>When something <see cref="Interactable.Zone"/> detects enters it. "Volume, On enter" in the inspector.</summary>
        EnterZone = 3,

        /// <summary>
        /// When <see cref="Interactable.Button"/> is pressed, wherever the player is, such as for a carried
        /// torch. <see cref="Interactable.Zone"/> is not read. "Anywhere" in the inspector.
        /// </summary>
        InteractAnywhere = 4,
    }

    /// <summary>What an <see cref="Interactable"/> does to itself once a run reaches the end.</summary>
    public enum InteractableAfterFinish
    {
        /// <summary>Stays as it is, ready to play again. "Plays: Every time" in the inspector.</summary>
        Nothing,

        /// <summary>Switches this component off after one full run. Tick it to re-arm. "Plays: Once, Then: Turn off".</summary>
        DisableComponent,

        /// <summary>Deactivates the GameObject it sits on, steps and all. "Plays: Once, Then: Hide object".</summary>
        DeactivateObject,

        /// <summary>Destroys the GameObject it sits on. "Plays: Once, Then: Destroy object".</summary>
        DestroyObject,
    }

    /// <summary>
    /// Runs a list of <see cref="StepGroup"/>s of <see cref="InteractionStep"/>s in order, like a small
    /// Timeline of steps. <see cref="Play()"/> starts a run; <see cref="Stop"/> cuts it short.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A door is an interactable with Lock Player, Move Player To and Play Animation steps. Add new
    /// behaviour as a step class, not a subclass of this. <see cref="PlayOn"/> sets when it starts;
    /// <see cref="Zone"/> is the <see cref="TriggerVolume"/> it listens to, with nothing to wire on the volume.
    /// </para>
    /// <para>
    /// The player is the character that entered the zone or pressed the button, else the object tagged
    /// <c>Player</c>. Steps read it from <see cref="Interaction.Player"/>. A run that would lock a player
    /// already held by another interactable does not play.
    /// </para>
    /// <para>
    /// A run stops early on <see cref="Stop"/>, on leaving the zone with <see cref="StopWhenPlayerLeaves"/>,
    /// or when the component is disabled or destroyed. Running steps get <see cref="InteractionStep.Stop"/>,
    /// control is returned, then <see cref="InterruptedGroups"/> run and <see cref="Interrupted"/> fires.
    /// Runs on frame time in <c>Update</c>, never inside a simulation tick.
    /// </para>
    /// </remarks>
    [Icon("Packages/com.unity.building-blocks.gameplay/Editor/Icons/Interactable.png")]
    [AddComponentMenu("Building Blocks/Interactable")]
    [DisallowMultipleComponent]
    public sealed class Interactable : MonoBehaviour, ISerializationCallbackReceiver, IPressGate
    {
        [Header("Starts")]
        [SerializeField]
        [Tooltip("What starts it. Volume: something the Trigger Volume detects enters it, or presses the " +
                 "button inside it. On enable: the moment this component turns on. Manual: only when something " +
                 "calls Play().")]
        InteractablePlayOn m_PlayOn = InteractablePlayOn.Manual;

        [SerializeField]
        [Tooltip("The Trigger Volume that starts it. Who counts is set on the volume's Detection card.")]
        TriggerVolume m_Zone;

        [SerializeField]
        [Tooltip("The button pressed inside the volume. E and the gamepad's west button by default. Use " +
                 "Reference to point at your input asset's Interact action, which on-screen touch controls also drive.")]
        InputActionProperty m_Button = new InputActionProperty(DefaultInputAction());

        [SerializeField]
        [Tooltip("The button works only while the camera looks toward the Look Point.")]
        bool m_RequireLook;

        [SerializeField]
        [Tooltip("What the player has to look at, such as the switch. Empty uses this object.")]
        Transform m_LookPoint;

        [SerializeField, Range(1f, 90f)]
        [Tooltip("How far the Look Point may be from the centre of the view, in degrees.")]
        float m_MaxLookAngle = 25f;

        [SerializeField, Range(0f, 15f)]
        [Tooltip("Extra degrees allowed once looking, so the prompt does not flicker at the edge.")]
        float m_LookMargin = 3f;

        [SerializeField, Min(0f)]
        [Tooltip("Starting Anywhere, how close the camera must be to the Look Point, in metres; 0 is any distance.")]
        float m_MaxLookDistance = 2.5f;

        [SerializeField]
        [Tooltip("Cut the run short when whatever started it leaves the volume mid-run. The \"If stopped " +
                 "early\" steps run then.")]
        bool m_StopWhenPlayerLeaves;

        [SerializeField, Min(0f)]
        [Tooltip("Wait between runs: seconds after a run ends before the next is accepted. 0 accepts the next at once.")]
        float m_Cooldown;

        [SerializeField]
        [Tooltip("Whether it can play again. Every time: it stays ready after each run. Once: after one full " +
                 "run it turns off (tick it to re-arm), hides its GameObject, or destroys it. A run cut short " +
                 "does not count.")]
        InteractableAfterFinish m_AfterFinish = InteractableAfterFinish.Nothing;

        [Header("Steps")]
        [SerializeReference]
        [Tooltip("The groups of steps, run top to bottom. Every step in a group starts together.")]
        List<StepGroup> m_Groups = new List<StepGroup>();

        [SerializeReference]
        [Tooltip("Groups run if the run is cut short, once the running steps were stopped and the " +
                 "player is free again. Close the door, hide the prompt.")]
        List<StepGroup> m_OnInterruptedGroups = new List<StepGroup>();

        /// <summary>One step in flight: which step, which group, when it was launched, and its enumerator stack.</summary>
        sealed class Slot
        {
            public InteractionStep Step;
            public int Group;
            public float StartedAt;
            public float RunsAt;
            public bool Begun;
            public readonly Stack<IEnumerator> Routine = new Stack<IEnumerator>();
        }

        /// <summary>Which list of groups the run is in.</summary>
        enum Phase
        {
            Idle,
            Main,
            Interrupted,
        }

        readonly List<Slot> m_Running = new List<Slot>();
        readonly List<Slot> m_Launching = new List<Slot>();
        readonly HashSet<InteractionStep> m_Finished = new HashSet<InteractionStep>();

        Interaction m_Interaction;
        Phase m_Phase;
        List<StepGroup> m_Active;
        int m_GroupIndex = -1;
        int m_GroupFinished;
        bool m_GroupStarted;
        bool m_Used;
        InputAction m_WiredAction;
        bool m_OwnsWiredAction;
        bool m_WarnedNoLock;
        bool m_Advancing;
        bool m_StopRequested;
        bool m_Stopping;
        bool m_Destroying;
        float m_CooldownRemaining;
        Coroutine m_Chain;
        KinematicMover m_Current;
        IControlLock m_HeldLock;

        // Set while this run holds a movement-only lock on a player whose view can stay free.
        IFreeLook m_FreeLook;
        bool m_FreeLookWas;
        bool m_KeepsLook;
        int m_ViewHolds;
        TriggerVolume m_ListeningTo;
        bool m_WarnedNoZone;
        bool m_WarnedZoneGone;
        bool m_WarnedNoCamera;

        // Whether the camera looked at the look point at the last check; the margin applies while true.
        bool m_LookedAt;

        // True between OnEnable and OnDisable; setters wire the zone only then. EditMode tests call
        // WireZone() themselves, so never gate on isActiveAndEnabled.
        bool m_Live;

        // Colliders the zone currently reports, newest last; the button plays for the newest one's character.
        readonly List<Collider> m_InZone = new List<Collider>();

        /// <summary>Every step finished. In the inspector, use a Unity Event step in the last group instead.</summary>
        public event Action<Interactable> Finished;

        /// <summary>The run was cut short by <see cref="Stop"/> and the "If stopped early" steps have played.</summary>
        public event Action<Interactable> Interrupted;

        /// <summary>When the interactable starts on its own. Default <see cref="InteractablePlayOn.Manual"/>.</summary>
        public InteractablePlayOn PlayOn
        {
            get => m_PlayOn;
            set
            {
                if (m_PlayOn == value) return;
                UnwireInput();
                UnwireZone();
                m_PlayOn = value;
                if (m_Live)
                {
                    WireInput();
                    WireZone();
                }
            }
        }

        /// <summary>
        /// The <see cref="TriggerVolume"/> this starts from. Required for
        /// <see cref="InteractablePlayOn.EnterZone"/>, optional for <see cref="InteractablePlayOn.Interact"/>.
        /// Default <c>null</c>. Someone already inside when listening starts does not trigger a run.
        /// With Interact and no zone, the button works anywhere. A destroyed zone reports nobody, so the
        /// button does nothing until a new zone is set.
        /// </summary>
        public TriggerVolume Zone
        {
            get => m_Zone;
            set
            {
                if (m_Zone == value) return;
                UnwireZone();
                m_Zone = value;
                if (m_Live) WireZone();
            }
        }

        /// <summary>Whether <see cref="PlayOn"/> is a mode that reads <see cref="Zone"/>.</summary>
        public bool UsesZone => m_PlayOn == InteractablePlayOn.EnterZone || m_PlayOn == InteractablePlayOn.Interact;

        /// <summary>Whether <see cref="PlayOn"/> is a mode that listens to <see cref="Button"/>: in a zone, or anywhere.</summary>
        public bool UsesButton => m_PlayOn == InteractablePlayOn.Interact || m_PlayOn == InteractablePlayOn.InteractAnywhere;

        /// <summary>Whether the zone currently reports the player inside. Always <c>false</c> without a zone or outside the zone modes.</summary>
        public bool IsPlayerInZone
        {
            get
            {
                // No zone, or a destroyed one: nobody is inside.
                if (m_Zone == null)
                    return false;

                for (int i = 0; i < m_InZone.Count; i++)
                {
                    if (m_InZone[i] != null) return true;
                }

                return false;
            }
        }

        /// <summary>Cut the run short when the player walks out of <see cref="Zone"/> mid-run. Default <c>false</c>.</summary>
        public bool StopWhenPlayerLeaves
        {
            get => m_StopWhenPlayerLeaves;
            set => m_StopWhenPlayerLeaves = value;
        }

        /// <summary>The button that plays this when <see cref="UsesButton"/>: the inline default, or the referenced asset action.</summary>
        public InputAction InputAction => m_Button.action;

        /// <summary>
        /// The button as serialized: an inline action or a reference into an <c>.inputactions</c> asset.
        /// A referenced action is shared, so it is enabled but never disabled here.
        /// </summary>
        public InputActionProperty Button
        {
            get => m_Button;
            set
            {
                if (m_Button == value) return;
                UnwireInput();
                m_Button = value;
                if (m_Live && UsesButton) WireInput();
            }
        }

        /// <summary>The button starts it only while the camera looks at <see cref="LookPoint"/>. Default <c>false</c>.</summary>
        public bool RequireLook
        {
            get => m_RequireLook;
            set
            {
                m_RequireLook = value;
                m_LookedAt = false;
            }
        }

        /// <summary>What the player has to look at. Falls back to this transform.</summary>
        public Transform LookPoint
        {
            get => m_LookPoint != null ? m_LookPoint : transform;
            set => m_LookPoint = value;
        }

        /// <summary>Degrees from the centre of the view within which <see cref="LookPoint"/> counts as looked at. Default 25.</summary>
        public float MaxLookAngle
        {
            get => m_MaxLookAngle;
            set => m_MaxLookAngle = Mathf.Clamp(value, 1f, 90f);
        }

        /// <summary>Extra degrees allowed once looking, before it counts as looking away. Default 3.</summary>
        public float LookMargin
        {
            get => m_LookMargin;
            set => m_LookMargin = Mathf.Clamp(value, 0f, 15f);
        }

        /// <summary>
        /// With <see cref="InteractablePlayOn.InteractAnywhere"/>, the camera's furthest distance from
        /// <see cref="LookPoint"/> in metres; 0 is any distance. Not read in other modes. Default 2.5.
        /// </summary>
        public float MaxLookDistance
        {
            get => m_MaxLookDistance;
            set => m_MaxLookDistance = Mathf.Max(0f, value);
        }

        /// <summary>Whether the camera looked at <see cref="LookPoint"/> at the last check. Always <c>true</c> while <see cref="RequireLook"/> is off.</summary>
        public bool IsLookedAt => !m_RequireLook || m_LookedAt;

        /// <summary>Seconds between the end of one run and the next accepted one. Default <c>0</c>.</summary>
        public float Cooldown
        {
            get => m_Cooldown;
            set => m_Cooldown = Mathf.Max(0f, value);
        }

        /// <summary>What the interactable does to itself after a run reaches the end. Default <see cref="InteractableAfterFinish.Nothing"/>.</summary>
        public InteractableAfterFinish AfterFinish
        {
            get => m_AfterFinish;
            set => m_AfterFinish = value;
        }

        /// <summary>The groups of steps, in run order. Add a <see cref="StepGroup"/> to build a run from code.</summary>
        public List<StepGroup> Groups => m_Groups;

        /// <summary>
        /// Groups that run after <see cref="Stop"/>, once the player is free again ("If stopped early"
        /// in the inspector). Empty by default.
        /// </summary>
        public List<StepGroup> InterruptedGroups => m_OnInterruptedGroups;

        /// <summary>Every step on this interactable: the main groups in order, then the "If stopped early" groups.</summary>
        public IEnumerable<InteractionStep> Steps
        {
            get
            {
                foreach (InteractionStep step in StepsOf(m_Groups)) yield return step;
                foreach (InteractionStep step in StepsOf(m_OnInterruptedGroups)) yield return step;
            }
        }

        /// <summary>Whether a run is in progress, the "If stopped early" steps included.</summary>
        public bool IsPlaying => m_Phase != Phase.Idle;

        /// <summary>Whether a stopped run is playing its "If stopped early" steps right now.</summary>
        public bool IsInterrupting => m_Phase == Phase.Interrupted;

        /// <summary>Whether a run has reached the end at least once. A run cut short by <see cref="Stop"/> does not count.</summary>
        public bool HasPlayed => m_Used;

        /// <summary>Whether this run currently holds the player's control. False after <see cref="ReleasePlayer"/>.</summary>
        public bool HoldsPlayer => m_HeldLock != null;

        /// <summary>The character of the current run, or <c>null</c> when idle.</summary>
        public KinematicMover CurrentActor => m_Current;

        /// <summary>The run in progress (what the steps are handed), or <c>null</c> when idle.</summary>
        public Interaction CurrentInteraction => IsPlaying ? m_Interaction : null;

        /// <summary>
        /// Index into <see cref="Groups"/> (or <see cref="InterruptedGroups"/> while
        /// <see cref="IsInterrupting"/>) of the group that started most recently, or <c>-1</c> when idle.
        /// </summary>
        public int CurrentGroupIndex => IsPlaying ? m_GroupIndex : -1;

        /// <summary>How many steps are running right now, delays included. Above 1 when steps overlap.</summary>
        public int RunningCount => m_Running.Count;

        /// <summary>Seconds since the run started, or <c>0</c> when idle. For progress readouts.</summary>
        public float Elapsed => IsPlaying ? m_Interaction.Elapsed : 0f;

        /// <summary>Seconds until <see cref="Play"/> is accepted again after the last run, or <c>0</c>.</summary>
        public float CooldownRemaining => Mathf.Max(0f, m_CooldownRemaining);

        /// <summary>Whether <see cref="Play()"/> would start a run right now: enabled, idle, off cooldown.</summary>
        public bool CanPlay =>
            enabled && gameObject.activeInHierarchy && !IsPlaying && m_CooldownRemaining <= 0f;

        /// <summary>Whether an enabled <see cref="LockPlayerStep"/> is in the main groups, so a run takes the player's control.</summary>
        public bool WouldLockPlayer
        {
            get
            {
                foreach (InteractionStep step in StepsOf(m_Groups))
                {
                    if (step is LockPlayerStep && step.Enabled) return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Plays for the object tagged Player. Ignored while running, on cooldown, or when this run would
        /// lock a player another interactable already holds.
        /// </summary>
        public void Play() => Play(null);

        /// <summary>
        /// Plays for the character that owns <paramref name="collider"/>, the shape a
        /// <see cref="TriggerVolume"/> hands over.
        /// </summary>
        internal void PlayFor(Collider collider)
        {
            KinematicMover mover = collider != null ? collider.GetComponentInParent<KinematicMover>() : null;
            Play(mover);
        }

        /// <summary>
        /// Plays for <paramref name="actor"/>; <c>null</c> falls back to the object tagged Player. With no
        /// character at all it still runs; steps that need one skip themselves.
        /// </summary>
        public void Play(KinematicMover actor)
        {
            if (!CanPlay)
                return;

            if (actor == null)
                actor = FindPlayer();

            // One owner at a time: a run that would lock a player already held elsewhere does not play.
            if (actor != null && WouldLockPlayer)
            {
                var controlLock = actor.GetComponent<IControlLock>();
                if (controlLock != null && controlLock.IsLocked)
                    return;
            }

            m_Current = actor;
            m_Finished.Clear();
            m_Running.Clear();

            if (m_Interaction == null)
                m_Interaction = new Interaction(this);
            m_Interaction.Begin(actor);

            EnterPhase(Phase.Main, m_Groups);

            // The first group starts on Play itself, before any frame passes.
            Advance(0f);

            if (IsPlaying && Application.isPlaying && gameObject.activeInHierarchy)
                m_Chain = StartCoroutine(Drive());
        }

        /// <summary>
        /// Stops a run in progress: begun steps get <see cref="InteractionStep.Stop"/> in start order,
        /// control is returned, the "If stopped early" groups play, then <see cref="Interrupted"/> fires.
        /// A second call while those play ends the run at once.
        /// </summary>
        public void Stop()
        {
            if (!IsPlaying)
                return;

            // A step that calls Stop from inside its Run: finish the frame first, then stop.
            if (m_Advancing)
            {
                m_StopRequested = true;
                return;
            }

            // Stop called from inside a step's Stop: the stop under way covers it. A nested call would
            // tear down the On Interrupted groups being set up.
            if (m_Stopping)
                return;

            m_Stopping = true;
            try
            {
                StopRunningSteps();

                if (m_Phase == Phase.Interrupted)
                {
                    Finish(stopped: true);
                    return;
                }

                // Control comes back before the "If stopped early" steps, so they cannot move the player.
                ReleasePlayer();
                EnterPhase(Phase.Interrupted, m_OnInterruptedGroups);
            }
            finally
            {
                m_Stopping = false;
            }

            Advance(0f);

            if (!IsPlaying)
                return;

            // Nothing drives the coroutine on an object going away: end now. Outside Play mode, tests
            // drive the run through Advance.
            if (m_Destroying || (Application.isPlaying && !gameObject.activeInHierarchy))
            {
                StopRunningSteps();
                Finish(stopped: true);
            }
            else if (m_Chain == null && Application.isPlaying)
            {
                m_Chain = StartCoroutine(Drive());
            }
        }

        /// <summary>
        /// Gives the player their control back now while the run keeps going. Does nothing when this run
        /// holds no lock. Called by the Release Control step.
        /// </summary>
        public void ReleasePlayer()
        {
            IControlLock held = m_HeldLock;
            m_HeldLock = null;

            // Unlock while the view is still free, so it stays where the player was looking.
            held?.Unlock();

            if (IsAlive(m_FreeLook))
                m_FreeLook.LookWhileLocked = m_FreeLookWas;

            // View holds outlive the lock: a step still turning the player releases its own when it ends.
            m_FreeLook = null;
            m_KeepsLook = false;
        }

        /// <summary>
        /// Takes the player's <see cref="IControlLock"/> for the rest of the run or until
        /// <see cref="ReleasePlayer"/>, walking and looking both. Returns whether this run holds the lock.
        /// </summary>
        public bool TryLockPlayer() => TryLockPlayer(PlayerLockScope.MovementAndLook);

        /// <summary>
        /// Takes the player's <see cref="IControlLock"/> for the rest of the run or until
        /// <see cref="ReleasePlayer"/>. Called by the Lock Player step. Returns whether this run holds the
        /// lock; warns once when there is no player or nothing on them can lock.
        /// </summary>
        /// <remarks>
        /// With <see cref="PlayerLockScope.MovementOnly"/> the player's <see cref="IFreeLook"/> keeps reading
        /// look input. A later call for movement and look, while this run holds the lock, takes the view too.
        /// </remarks>
        /// <param name="scope">Whether looking stays with the player.</param>
        public bool TryLockPlayer(PlayerLockScope scope)
        {
            if (!IsPlaying)
                return false;

            if (m_HeldLock != null)
            {
                if (scope == PlayerLockScope.MovementAndLook && m_KeepsLook)
                {
                    PinViewToBody();
                    m_KeepsLook = false;
                }

                return true;
            }

            if (m_Current == null)
                return false;

            var controlLock = m_Current.GetComponent<IControlLock>();
            if (controlLock == null)
            {
                if (!m_WarnedNoLock)
                {
                    m_WarnedNoLock = true;
                    Debug.LogWarning(
                        $"[Building Blocks] {name}: '{m_Current.name}' has nothing that can lock its control, " +
                        "so it keeps simulating under this interactable. Give it a Character.", m_Current);
                }

                return false;
            }

            if (controlLock.IsLocked)
                return false;

            // Free the view before locking: the body's facing lags the view by up to a tick, and a view
            // freed after the lock would start from that older facing.
            if (scope == PlayerLockScope.MovementOnly)
                FreeLook();

            controlLock.Lock();
            m_HeldLock = controlLock;
            return true;
        }

        /// <summary>
        /// Takes the view from a player who kept looking under a movement-only lock, for a step that turns
        /// or tilts it (Move Player To, Look At). Pair every call with <see cref="ReleaseView"/>.
        /// </summary>
        internal void HoldView()
        {
            m_ViewHolds++;
            if (m_ViewHolds == 1 && m_KeepsLook)
                PinViewToBody();
        }

        /// <summary>Hands the view back after <see cref="HoldView"/> once no step holds it.</summary>
        internal void ReleaseView()
        {
            if (m_ViewHolds == 0)
                return;

            m_ViewHolds--;
            if (m_ViewHolds == 0 && m_KeepsLook && IsAlive(m_FreeLook))
                m_FreeLook.LookWhileLocked = true;
        }

        void FreeLook()
        {
            IFreeLook look = m_Current != null ? m_Current.GetComponentInChildren<IFreeLook>() : null;
            if (look == null)
                return;

            m_FreeLook = look;
            m_FreeLookWas = look.LookWhileLocked;
            m_KeepsLook = true;
            look.LookWhileLocked = m_ViewHolds == 0;
        }

        /// <summary>
        /// Stops the free view and turns the body to face where it was looking first, so the view does not
        /// snap back to the body and a step that turns the player starts from what the player sees.
        /// </summary>
        void PinViewToBody()
        {
            if (!IsAlive(m_FreeLook))
                return;

            if (m_FreeLook.LookWhileLocked && m_Current != null)
                m_Current.Teleport(m_Current.GetState().Position, Quaternion.Euler(0f, m_FreeLook.Yaw, 0f));

            m_FreeLook.LookWhileLocked = false;
        }

        // An interface reference skips Unity's destroyed-object check, so ask the object itself.
        static bool IsAlive(IFreeLook look) => look is UnityEngine.Object unityObject ? unityObject != null : look != null;

        /// <summary>Whether <paramref name="step"/> is running right now, delay included.</summary>
        public bool IsRunning(InteractionStep step)
        {
            for (int i = 0; i < m_Running.Count; i++)
            {
                if (ReferenceEquals(m_Running[i].Step, step))
                    return true;
            }

            return false;
        }

        /// <summary>Whether <paramref name="step"/> has finished, or was skipped, in the run in progress. False when idle.</summary>
        public bool HasFinished(InteractionStep step) => IsPlaying && step != null && m_Finished.Contains(step);

        /// <summary>Seconds <paramref name="step"/> has been running, delay included, or <c>0</c> when it is not.</summary>
        public float ElapsedOf(InteractionStep step)
        {
            for (int i = 0; i < m_Running.Count; i++)
            {
                if (ReferenceEquals(m_Running[i].Step, step))
                    return m_Interaction.Elapsed - m_Running[i].StartedAt;
            }

            return 0f;
        }

        /// <summary>The first mover under the object tagged Player, or <c>null</c>.</summary>
        internal static KinematicMover FindPlayer() => InteractionUtility.FindPlayer();

        /// <summary>
        /// Advances the run by <paramref name="deltaTime"/> seconds. Called once per frame in Play mode;
        /// tests call it by hand. Returns whether the run is still going.
        /// </summary>
        internal bool Advance(float deltaTime)
        {
            if (!IsPlaying || m_Advancing)
                return IsPlaying;

            m_Advancing = true;
            m_Interaction.Advance(deltaTime);

            try
            {
                StepRunning();

                // Loop so a chain of instant groups runs in one frame.
                while (m_Active != null && GroupDone() && m_GroupIndex + 1 < m_Active.Count)
                {
                    m_GroupIndex++;
                    m_GroupFinished = 0;
                    m_GroupStarted = true;
                    LaunchGroup(m_Active[m_GroupIndex]);
                }
            }
            finally
            {
                m_Advancing = false;
            }

            if (m_StopRequested)
            {
                m_StopRequested = false;
                Stop();
                return IsPlaying;
            }

            if (m_Active == null || (GroupDone() && m_GroupIndex + 1 >= m_Active.Count && m_Running.Count == 0))
                Finish(stopped: m_Phase == Phase.Interrupted);

            return IsPlaying;
        }

        IEnumerator Drive()
        {
            while (IsPlaying)
            {
                yield return null;
                Advance(Time.deltaTime);
            }

            m_Chain = null;
        }

        void EnterPhase(Phase phase, List<StepGroup> groups)
        {
            m_Phase = phase;
            m_Active = groups;
            m_GroupIndex = -1;
            m_GroupFinished = 0;
            m_GroupStarted = false;
        }

        void LaunchGroup(StepGroup group)
        {
            if (group == null || group.Steps == null)
                return;

            m_Launching.Clear();
            foreach (InteractionStep step in group.Steps)
            {
                if (step == null)
                    continue;

                if (!step.Enabled)
                {
                    m_Finished.Add(step);
                    continue;
                }

                var slot = new Slot
                {
                    Step = step,
                    Group = m_GroupIndex,
                    StartedAt = m_Interaction.Elapsed,
                    RunsAt = m_Interaction.Elapsed + Mathf.Max(0f, step.Delay),
                };
                m_Running.Add(slot);
                m_Launching.Add(slot);
            }

            // A step with no delay runs the same frame its group starts.
            for (int i = 0; i < m_Launching.Count; i++)
                StepSlot(m_Launching[i]);
            m_Launching.Clear();
        }

        void StepRunning()
        {
            // Index loop: a finished slot removes itself.
            for (int i = 0; i < m_Running.Count;)
            {
                Slot slot = m_Running[i];
                StepSlot(slot);
                if (i < m_Running.Count && ReferenceEquals(m_Running[i], slot))
                    i++;
            }
        }

        /// <summary>
        /// Launches the slot once its delay is up, then steps its enumerator stack: a yielded
        /// <see cref="IEnumerator"/> runs inline, anything else waits a frame. Stepping by hand, not via
        /// nested coroutines, is what lets <see cref="Stop"/> stop a step.
        /// </summary>
        void StepSlot(Slot slot)
        {
            if (!slot.Begun)
            {
                if (m_Interaction.Elapsed < slot.RunsAt)
                    return;

                slot.Begun = true;
                IEnumerator routine = null;
                try
                {
                    routine = slot.Step.Run(m_Interaction);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }

                if (routine == null)
                {
                    Complete(slot);
                    return;
                }

                slot.Routine.Push(routine);
            }

            while (slot.Routine.Count > 0)
            {
                IEnumerator top = slot.Routine.Peek();
                bool more;
                try
                {
                    more = top.MoveNext();
                }
                catch (Exception exception)
                {
                    // A broken step must not hang the run: report it and treat it as finished.
                    Debug.LogException(exception, this);
                    slot.Routine.Clear();
                    break;
                }

                if (!more)
                {
                    slot.Routine.Pop();
                    continue;
                }

                if (top.Current is IEnumerator inner)
                {
                    slot.Routine.Push(inner);
                    continue;
                }

                return;
            }

            Complete(slot);
        }

        void Complete(Slot slot)
        {
            m_Finished.Add(slot.Step);
            if (slot.Group == m_GroupIndex)
                m_GroupFinished++;
            m_Running.Remove(slot);
        }

        /// <summary>Whether the current group is done by its advance rule. True before any group has started.</summary>
        bool GroupDone()
        {
            if (!m_GroupStarted || m_Active == null || m_GroupIndex < 0 || m_GroupIndex >= m_Active.Count)
                return true;

            StepGroup group = m_Active[m_GroupIndex];
            int remaining = 0;
            for (int i = 0; i < m_Running.Count; i++)
            {
                if (m_Running[i].Group == m_GroupIndex)
                    remaining++;
            }

            if (remaining == 0)
                return true;

            return group != null && group.Advance == StepGroupAdvance.WhenAnyFinishes && m_GroupFinished > 0;
        }

        /// <summary>Stop every step that has begun, in start order. One still inside its delay never ran, so it has nothing to undo.</summary>
        void StopRunningSteps()
        {
            for (int i = 0; i < m_Running.Count; i++)
            {
                Slot slot = m_Running[i];
                if (!slot.Begun)
                    continue;

                try
                {
                    slot.Step.Stop(m_Interaction);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            m_Running.Clear();
        }

        void Finish(bool stopped)
        {
            if (m_Chain != null && Application.isPlaying)
                StopCoroutine(m_Chain);
            m_Chain = null;

            m_Phase = Phase.Idle;
            m_Active = null;
            m_GroupIndex = -1;
            m_GroupStarted = false;
            m_Current = null;
            m_Running.Clear();
            m_Interaction?.End();
            m_CooldownRemaining = m_Cooldown;

            ReleasePlayer();

            // Every step has ended or been stopped by now; a step that threw never released its hold.
            m_ViewHolds = 0;

            if (stopped)
            {
                Interrupted?.Invoke(this);
                return;
            }

            // Only a run that reached the end counts as played; a stopped one may try again.
            m_Used = true;

            Finished?.Invoke(this);

            // Only a run that reached the end disposes of the interactable; Stop leaves it alone.
            switch (m_AfterFinish)
            {
                case InteractableAfterFinish.DisableComponent:
                    enabled = false;
                    break;
                case InteractableAfterFinish.DeactivateObject:
                    gameObject.SetActive(false);
                    break;
                case InteractableAfterFinish.DestroyObject:
                    // Destroy is refused outside Play mode.
                    if (Application.isPlaying) Destroy(gameObject);
                    else DestroyImmediate(gameObject);
                    break;
            }
        }

        static IEnumerable<InteractionStep> StepsOf(List<StepGroup> groups)
        {
            if (groups == null) yield break;

            foreach (StepGroup group in groups)
            {
                if (group?.Steps == null) continue;
                foreach (InteractionStep step in group.Steps)
                {
                    if (step != null) yield return step;
                }
            }
        }

        void Update()
        {
            if (m_CooldownRemaining > 0f)
                m_CooldownRemaining -= Time.deltaTime;

            // Checked every frame, not only at the press, so the margin holds between presses as it does
            // for a prompt. Never mid-run: a step that turns the camera away must not matter.
            if (m_RequireLook && !IsPlaying)
                RefreshLook();
        }

        void OnEnable()
        {
            m_Live = true;

            // The cooldown only counts down in Update; clear it so an On enable interactable can fire again.
            m_CooldownRemaining = 0f;
            m_LookedAt = false;

            WireInput();
            WireZone();

            if (m_PlayOn == InteractablePlayOn.Enable)
                Play();
        }

        void OnDisable()
        {
            m_Live = false;
            UnwireInput();
            UnwireZone();

            // Disabling cuts a run short and frees the player.
            if (IsPlaying)
                Stop();
        }

        void OnDestroy()
        {
            m_Destroying = true;
            if (IsPlaying)
                Stop();
        }

        /// <summary>Listens to the button in the button mode. Idempotent; follows a swapped action.</summary>
        internal void WireInput()
        {
            InputAction action = m_Button.action;
            if (m_WiredAction == action || !UsesButton)
                return;

            UnwireInput();
            if (action == null)
                return;

            // An asset action is shared with other readers: enable it, never disable it.
            m_OwnsWiredAction = m_Button.reference == null;
            action.performed += OnInputPerformed;
            action.Enable();
            m_WiredAction = action;
        }

        internal void UnwireInput()
        {
            if (m_WiredAction == null)
                return;

            m_WiredAction.performed -= OnInputPerformed;
            if (m_OwnsWiredAction) m_WiredAction.Disable();
            m_WiredAction = null;
        }

        /// <summary>
        /// Listens to <see cref="Zone"/> in a zone mode, retargeting if the zone or mode changed.
        /// Idempotent. EditMode tests call this in place of <c>OnEnable</c>.
        /// </summary>
        internal void WireZone()
        {
            if (m_Zone == null || !UsesZone)
            {
                UnwireZone();
                return;
            }

            if (ReferenceEquals(m_ListeningTo, m_Zone))
                return;

            UnwireZone();
            m_ListeningTo = m_Zone;
            m_Zone.TriggerEntered += OnZoneEntered;
            m_Zone.TriggerExited += OnZoneExited;

            IReadOnlyList<Collider> occupants = m_Zone.Occupants;
            for (int i = 0; i < occupants.Count; i++)
            {
                Collider occupant = occupants[i];
                if (occupant != null && !m_InZone.Contains(occupant))
                    m_InZone.Add(occupant);
            }
        }

        /// <summary>Stops listening to the zone and forgets who was inside. See <see cref="WireZone"/>.</summary>
        internal void UnwireZone()
        {
            if (m_ListeningTo != null)
            {
                m_ListeningTo.TriggerEntered -= OnZoneEntered;
                m_ListeningTo.TriggerExited -= OnZoneExited;
                m_ListeningTo = null;
            }

            m_InZone.Clear();
        }

        void OnZoneEntered(TriggerVolume volume, Collider collider)
        {
            if (collider == null || m_InZone.Contains(collider))
                return;

            // A character with several colliders is one entrant, so an instant interactable plays once.
            KinematicMover mover = collider.GetComponentInParent<KinematicMover>();
            bool alreadyInside = mover != null && StillInZone(mover);
            m_InZone.Add(collider);

            if (alreadyInside)
                return;

            if (m_PlayOn == InteractablePlayOn.EnterZone)
                PlayFor(collider);
        }

        void OnZoneExited(TriggerVolume volume, Collider collider)
        {
            m_InZone.Remove(collider);

            if (!m_StopWhenPlayerLeaves || !IsPlaying || m_Phase != Phase.Main)
                return;

            // Only the current player's leaving stops the run, and only once every collider of theirs is out.
            if (m_Current != null)
            {
                KinematicMover left = collider != null ? collider.GetComponentInParent<KinematicMover>() : null;
                if (left != m_Current || StillInZone(m_Current))
                    return;
            }

            Stop();
        }

        bool StillInZone(KinematicMover mover)
        {
            for (int i = 0; i < m_InZone.Count; i++)
            {
                Collider inside = m_InZone[i];
                if (inside != null && inside.GetComponentInParent<KinematicMover>() == mover)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// The button was pressed. With a zone, plays for the character inside it, or nothing; otherwise
        /// plays for the tagged Player. With <see cref="RequireLook"/>, only while the camera looks at
        /// <see cref="LookPoint"/>. Tests call this in place of the input action.
        /// </summary>
        internal void OnInteractPressed()
        {
            // Anywhere mode: the zone is not read.
            if (m_PlayOn == InteractablePlayOn.InteractAnywhere)
            {
                if (m_RequireLook && !RefreshLook())
                    return;

                Play();
                return;
            }

            if (m_PlayOn != InteractablePlayOn.Interact)
                return;

            if (m_RequireLook && !RefreshLook())
                return;

            // No zone: the button works anywhere. A destroyed zone is not "no zone"; it reports nobody.
            if (ReferenceEquals(m_Zone, null))
            {
                Play();
                return;
            }

            if (m_Zone == null)
            {
                m_InZone.Clear();
                WarnZoneGone();
                return;
            }

            for (int i = m_InZone.Count - 1; i >= 0; i--)
            {
                Collider inside = m_InZone[i];
                if (inside == null)
                {
                    m_InZone.RemoveAt(i);
                    continue;
                }

                PlayFor(inside);
                return;
            }
        }

        bool IPressGate.AdmitsPress() => !m_RequireLook || RefreshLook();

        /// <summary>Whether <paramref name="point"/> is within <paramref name="maxAngle"/> degrees of the view and, above 0, <paramref name="maxDistance"/> metres.</summary>
        internal static bool WithinLook(Vector3 eye, Vector3 forward, Vector3 point, float maxAngle, float maxDistance)
        {
            Vector3 toPoint = point - eye;
            if (maxDistance > 0f && toPoint.sqrMagnitude > maxDistance * maxDistance)
                return false;

            return Vector3.Angle(forward, toPoint) <= maxAngle;
        }

        /// <summary>Checks the camera against <see cref="LookPoint"/> now and returns <see cref="IsLookedAt"/>.</summary>
        internal bool RefreshLook()
        {
            if (!m_RequireLook)
                return m_LookedAt = true;

            // Mid-run a press is turned away anyway; checking now would drop the margin the player had.
            if (IsPlaying)
                return m_LookedAt;

            Transform eye = InteractionUtility.Eye();
            if (eye == null)
            {
                WarnNoCamera();
                return m_LookedAt = false;
            }

            // Once looking, a few degrees more are allowed so the edge of the cone does not flicker.
            float angle = m_LookedAt ? m_MaxLookAngle + m_LookMargin : m_MaxLookAngle;
            float reach = m_PlayOn == InteractablePlayOn.InteractAnywhere ? m_MaxLookDistance : 0f;
            return m_LookedAt = WithinLook(eye.position, eye.forward, LookPoint.position, angle, reach);
        }

        void WarnNoCamera()
        {
            if (m_WarnedNoCamera)
                return;

            m_WarnedNoCamera = true;
            Debug.LogWarning(
                $"[Building Blocks] {name}: Must look at it is on, but no camera is tagged MainCamera, so the " +
                "button does nothing here.", this);
        }

        void WarnZoneGone()
        {
            if (m_WarnedZoneGone)
                return;

            m_WarnedZoneGone = true;
            Debug.LogWarning(
                $"[Building Blocks] {name}: its Trigger Volume has been destroyed, so the button does nothing here. " +
                "Pick a new Volume under Starts.", this);
        }

        void WarnNoZone()
        {
            if (m_WarnedNoZone || m_PlayOn != InteractablePlayOn.EnterZone || m_Zone != null)
                return;

            m_WarnedNoZone = true;
            Debug.LogWarning(
                $"[Building Blocks] {name}: Starts on Volume, but no Trigger Volume is picked, so nothing starts it. " +
                "Pick one under Starts, or press + Add.", this);
        }

        void Start()
        {
            WarnNoZone();
        }

#if UNITY_EDITOR
        /// <summary>A Play-mode inspector edit writes fields directly; retarget the listeners to match.</summary>
        void OnValidate()
        {
            if (!Application.isPlaying || !m_Live)
                return;

            WireZone();
            if (UsesButton) WireInput();
            else UnwireInput();
        }

        // Selected with Must look at it: the cone from the camera, and the look point green while inside it.
        // Outside Play mode the Scene view camera stands in for the player's.
        void OnDrawGizmosSelected()
        {
            if (!m_RequireLook)
                return;

            Transform eye = Application.isPlaying ? InteractionUtility.Eye() : null;
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null &&
                UnityEditor.SceneView.lastActiveSceneView.camera != null)
                eye = UnityEditor.SceneView.lastActiveSceneView.camera.transform;

            Vector3 point = LookPoint.position;
            bool reach = m_PlayOn == InteractablePlayOn.InteractAnywhere && m_MaxLookDistance > 0f;
            bool inside = Application.isPlaying
                ? m_LookedAt
                : eye != null && WithinLook(eye.position, eye.forward, point, m_MaxLookAngle, reach ? m_MaxLookDistance : 0f);
            Color colour = inside ? new Color(0.3f, 1f, 0.4f, 1f) : new Color(1f, 0.35f, 0.3f, 1f);

            Gizmos.color = colour;
            Gizmos.DrawWireSphere(point, 0.06f);
            if (reach)
            {
                Gizmos.color = new Color(colour.r, colour.g, colour.b, 0.25f);
                Gizmos.DrawWireSphere(point, m_MaxLookDistance);
            }

            if (eye == null)
                return;

            // The rings sit at the look point's depth, so the point is looked at while it is inside the inner one.
            float depth = Mathf.Max(0.1f, Vector3.Dot(point - eye.position, eye.forward));
            Vector3 centre = eye.position + eye.forward * depth;
            Vector3 start = eye.position + eye.forward * 0.3f;
            float radius = depth * Mathf.Tan(m_MaxLookAngle * Mathf.Deg2Rad);
            float outer = depth * Mathf.Tan(Mathf.Min(89f, m_MaxLookAngle + m_LookMargin) * Mathf.Deg2Rad);

            UnityEditor.Handles.color = new Color(colour.r, colour.g, colour.b, 0.8f);
            UnityEditor.Handles.DrawWireDisc(centre, eye.forward, radius);
            for (int i = 0; i < 4; i++)
            {
                Vector3 edge = Quaternion.AngleAxis(i * 90f, eye.forward) * eye.up;
                UnityEditor.Handles.DrawLine(start, centre + edge * radius);
            }

            UnityEditor.Handles.DrawDottedLine(start, point, 3f);
            UnityEditor.Handles.color = new Color(colour.r, colour.g, colour.b, 0.3f);
            UnityEditor.Handles.DrawWireDisc(centre, eye.forward, outer);
        }
#endif

        /// <inheritdoc/>
        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        /// <summary>Gives an inline Button action a fresh binding cache so its saved bindings take effect.</summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_Button.reference == null && m_Button.action != null)
                m_Button = new InputActionProperty(InteractionUtility.Uncached(m_Button.action));
        }

        void OnInputPerformed(InputAction.CallbackContext context) => OnInteractPressed();

        /// <summary>E, and the west face button.</summary>
        static InputAction DefaultInputAction()
        {
            var action = new InputAction("Interact", InputActionType.Button);
            action.AddBinding("<Keyboard>/e");
            action.AddBinding("<Gamepad>/buttonWest");
            return action;
        }
    }
}
