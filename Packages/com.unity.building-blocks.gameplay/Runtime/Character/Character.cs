using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Holds the character's <see cref="MoveProcessor"/> list and drives a <see cref="KinematicMover"/>
    /// with it and an <see cref="IMoveCommandSource"/> each <c>FixedUpdate</c>. Also takes the
    /// player's control away and gives it back (<see cref="Lock"/> / <see cref="Unlock"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The default host loop: each tick it reads a <see cref="MoveCommand"/> from
    /// <see cref="Source"/>, feeds the <see cref="MoveInterpolator"/> and calls
    /// <see cref="KinematicMover.Simulate"/> with <c>Time.fixedDeltaTime</c>. A netcode or replay loop
    /// can drive the same mover without this component.
    /// </para>
    /// <para>
    /// <see cref="Lock"/> disables this host, the source and the interpolator, so the mover does not
    /// simulate at all and the lock holder can place it with
    /// <see cref="KinematicMoverExtensions.Teleport"/>. To keep gravity and collision running (AI),
    /// swap <see cref="Source"/> instead. Lock and Unlock are idempotent and callable from UnityEvents.
    /// </para>
    /// </remarks>
    [Icon("Packages/com.unity.building-blocks.gameplay/Editor/Icons/Character.png")]
    [AddComponentMenu("Building Blocks/Character")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KinematicMover))]
    public sealed class Character : MonoBehaviour, IControlLock
    {
        [Header("Processors")]
        [SerializeReference]
        [Tooltip("What moves this character, top to bottom: walking and gravity, a jump, facing. Order " +
                 "is run order; an unticked processor keeps its place and does not run.")]
        List<MoveProcessor> m_Processors = new List<MoveProcessor>();

        [Header("Lock")]
        [SerializeField]
        [Tooltip("Extra behaviours to switch off while control is locked and restore on unlock (a " +
                 "HUD, an ability). The command source and the Move Interpolator on this GameObject " +
                 "are always handled and do not need listing.")]
        Behaviour[] m_AlsoDisableWhenLocked = Array.Empty<Behaviour>();

        [Header("Events")]
        [SerializeField]
        [Tooltip("Control was taken away.")]
        UnityEvent m_OnLocked = new UnityEvent();

        [SerializeField]
        [Tooltip("Control was given back.")]
        UnityEvent m_OnUnlocked = new UnityEvent();

        KinematicMover m_Mover;
        MoveInterpolator m_Interpolator;
        IMoveCommandSource m_Source;
        bool m_Resolved;

        // Slot i holds the processor registered for list entry i, or null. Kept apart from the list so
        // a list edited between ticks still unregisters exactly what was registered.
        MoveProcessor[] m_Registered = Array.Empty<MoveProcessor>();
        int m_RegisteredCount;

        // What the last bind pass saw. Rebind only when the list or source differs, so lock/unlock
        // re-logs nothing.
        MoveProcessor[] m_Bound = Array.Empty<MoveProcessor>();
        int m_BoundCount;
        IMoveCommandSource m_BoundSource;
        bool m_InputsDirty = true;

        bool m_Locked;
        bool m_HostWasEnabled;
        bool m_SourceWasEnabled;
        bool m_InterpolatorWasEnabled;
        bool[] m_AlsoWasEnabled = Array.Empty<bool>();

        /// <summary>Control was taken away. Code-side twin of <see cref="OnLocked"/>.</summary>
        public event Action Locked;

        /// <summary>Control was given back. Code-side twin of <see cref="OnUnlocked"/>.</summary>
        public event Action Unlocked;

        /// <summary>The mover this host drives. Resolved on the same GameObject.</summary>
        public KinematicMover Mover
        {
            get
            {
                Resolve();
                return m_Mover;
            }
        }

        /// <summary>
        /// The interpolator fed each tick, or <c>null</c> when the character has no visuals to
        /// smooth. Resolved on the same GameObject.
        /// </summary>
        public MoveInterpolator Interpolator
        {
            get
            {
                Resolve();
                return m_Interpolator;
            }
        }

        /// <summary>
        /// The command source driving this character. Defaults to the first
        /// <see cref="IMoveCommandSource"/> component on the GameObject, in component order.
        /// </summary>
        /// <remarks>
        /// Assign at runtime to hand the character to AI, a cutscene or a replay. <c>null</c> means
        /// no input: the tick still runs with a default <see cref="MoveCommand"/>, so the character
        /// still falls and collides.
        /// </remarks>
        public IMoveCommandSource Source
        {
            get
            {
                Resolve();
                return m_Source;
            }
            set
            {
                Resolve();
                m_Source = value;

                // A new source may name its buttons differently, so rebind every InputSlot.
                BindInputs();
            }
        }

        /// <inheritdoc/>
        public bool IsLocked => m_Locked;

        /// <summary>Extra behaviours switched off while locked.</summary>
        public Behaviour[] AlsoDisableWhenLocked
        {
            get => m_AlsoDisableWhenLocked;
            set => m_AlsoDisableWhenLocked = value ?? Array.Empty<Behaviour>();
        }

        /// <summary>Inspector event: control was taken away.</summary>
        public UnityEvent OnLocked => m_OnLocked;

        /// <summary>Inspector event: control was given back.</summary>
        public UnityEvent OnUnlocked => m_OnUnlocked;

        /// <summary>
        /// Takes the player's control away: this host, the command source and the interpolator are
        /// switched off, so nothing simulates or reads input until <see cref="Unlock"/>. A second
        /// call while locked does nothing.
        /// </summary>
        public void Lock()
        {
            if (m_Locked)
                return;

            Resolve();

            m_HostWasEnabled = Switch(this, false);
            m_SourceWasEnabled = Switch(m_Source as Behaviour, false);
            m_InterpolatorWasEnabled = Switch(m_Interpolator, false);

            if (m_AlsoWasEnabled.Length != m_AlsoDisableWhenLocked.Length)
                m_AlsoWasEnabled = new bool[m_AlsoDisableWhenLocked.Length];

            for (int i = 0; i < m_AlsoDisableWhenLocked.Length; i++)
                m_AlsoWasEnabled[i] = Switch(m_AlsoDisableWhenLocked[i], false);

            m_Locked = true;

            Locked?.Invoke();
            m_OnLocked.Invoke();
        }

        /// <summary>
        /// Gives control back, restoring exactly what <see cref="Lock"/> switched off. A call while
        /// not locked does nothing.
        /// </summary>
        public void Unlock()
        {
            if (!m_Locked)
                return;

            // Re-seed first so the interpolator blends from where the lock left the mover, not from
            // the tick before the lock.
            if (m_Mover != null && m_Interpolator != null)
            {
                MoveState state = m_Mover.GetState();
                m_Interpolator.BeginTick(state);
                m_Interpolator.EndTick(state);
                m_Interpolator.Interpolate(1f);
            }

            if (m_SourceWasEnabled) Switch(m_Source as Behaviour, true);
            if (m_InterpolatorWasEnabled) Switch(m_Interpolator, true);

            for (int i = 0; i < m_AlsoDisableWhenLocked.Length && i < m_AlsoWasEnabled.Length; i++)
            {
                if (m_AlsoWasEnabled[i]) Switch(m_AlsoDisableWhenLocked[i], true);
            }

            m_Locked = false;

            if (m_HostWasEnabled) Switch(this, true);

            Unlocked?.Invoke();
            m_OnUnlocked.Invoke();
        }

        /// <summary>
        /// The processors this character runs, in run order. Edit freely (add, remove, reorder, toggle
        /// <see cref="MoveProcessor.Enabled"/>); changes apply on the next tick
        /// (<see cref="SyncProcessors"/>).
        /// </summary>
        /// <remarks>
        /// Order matters: moving <see cref="JumpProcessor"/> above
        /// <see cref="GroundLocomotionProcessor"/> changes the jump tick. <c>null</c> entries (a deleted
        /// processor class) are skipped.
        /// </remarks>
        public List<MoveProcessor> Processors => m_Processors;

        /// <summary>
        /// The first processor of type <typeparamref name="T"/> in the list, or <c>null</c>.
        /// </summary>
        /// <typeparam name="T">A processor class, or an interface one implements.</typeparam>
        public T GetProcessor<T>() where T : class
        {
            for (int i = 0; i < m_Processors.Count; i++)
            {
                if (m_Processors[i] is T match)
                    return match;
            }

            return null;
        }

        /// <summary>
        /// Ticks or unticks every processor of type <typeparamref name="T"/>. Takes effect on the next
        /// tick. Returns <c>false</c> when the list has none.
        /// </summary>
        /// <param name="enabled"><c>true</c> to run it, <c>false</c> to keep it in the list and skip it.</param>
        public bool SetProcessorEnabled<T>(bool enabled) where T : class
        {
            bool found = false;
            for (int i = 0; i < m_Processors.Count; i++)
            {
                if (m_Processors[i] is T)
                {
                    m_Processors[i].Enabled = enabled;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Turns on every processor called <paramref name="processorName"/>: its row name ("Jump") or
        /// class name ("JumpProcessor"), ignoring case and spaces. For UnityEvents, which cannot call
        /// <see cref="SetProcessorEnabled{T}"/>. An unknown name logs a warning.
        /// </summary>
        public void EnableProcessor(string processorName) => SetProcessorEnabled(processorName, true);

        /// <summary>
        /// Turns off every processor called <paramref name="processorName"/>; it keeps its place in the
        /// list. See <see cref="EnableProcessor"/> for how the name is matched.
        /// </summary>
        public void DisableProcessor(string processorName) => SetProcessorEnabled(processorName, false);

        void SetProcessorEnabled(string processorName, bool enabled)
        {
            bool found = false;
            for (int i = 0; i < m_Processors.Count; i++)
            {
                MoveProcessor processor = m_Processors[i];
                if (processor != null && NameMatches(processor.GetType(), processorName))
                {
                    processor.Enabled = enabled;
                    found = true;
                }
            }

            if (!found)
                Debug.LogWarning($"{name}: no processor called '{processorName}' to {(enabled ? "enable" : "disable")}.", this);
        }

        /// <summary>"Jump", "jump processor" and "JumpProcessor" all name <see cref="JumpProcessor"/>.</summary>
        internal static bool NameMatches(Type type, string processorName)
        {
            if (type == null || string.IsNullOrEmpty(processorName))
                return false;

            string wanted = processorName.Replace(" ", string.Empty);
            string full = type.Name;
            const string suffix = "Processor";
            string shortName = full.EndsWith(suffix, StringComparison.Ordinal) && full.Length > suffix.Length
                ? full.Substring(0, full.Length - suffix.Length)
                : full;

            return string.Equals(wanted, full, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(wanted, shortName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Re-registers the enabled processors in list order, rebinding <see cref="InputSlot"/> fields
        /// first if the list or <see cref="Source"/> changed. Runs on enable and after list changes;
        /// call it only to apply a change before the next tick.
        /// </summary>
        public void RefreshProcessors()
        {
            Resolve();
            BindInputs();

            if (m_Mover == null)
                return;

            UnregisterAll();
            RegisterActive();
        }

        /// <summary>
        /// Rebinds every processor's <see cref="InputSlot"/> fields against <see cref="Source"/>. Call
        /// it for changes the character cannot see: a slot name edited at runtime, or a source that
        /// gained a custom button.
        /// </summary>
        public void RebindInputs()
        {
            Resolve();
            m_InputsDirty = true;
            BindInputs();
        }

        /// <summary>
        /// Binds processor slots to the source's buttons, only when the list or source changed (or
        /// <see cref="RebindInputs"/> forced it), so each warning logs once per change.
        /// </summary>
        void BindInputs()
        {
            if (!m_InputsDirty && !InputsChangedSinceBound())
                return;

            m_InputsDirty = false;

            int count = m_Processors.Count;
            if (m_Bound.Length < count)
                m_Bound = new MoveProcessor[count];

            for (int i = 0; i < count; i++)
            {
                MoveProcessor processor = m_Processors[i];
                m_Bound[i] = processor;
                if (processor != null)
                    InputSlotBinder.BindToSource(processor, m_Source, this);
            }

            for (int i = count; i < m_BoundCount; i++)
                m_Bound[i] = null;

            m_BoundCount = count;
            m_BoundSource = m_Source;
        }

        /// <summary>Whether the list's identities or the source differ from what the last bind pass saw.</summary>
        bool InputsChangedSinceBound()
        {
            if (!ReferenceEquals(m_Source, m_BoundSource) || m_Processors.Count != m_BoundCount)
                return true;

            for (int i = 0; i < m_BoundCount; i++)
            {
                if (!ReferenceEquals(m_Processors[i], m_Bound[i]))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Re-registers the processors when the list changed since the last tick: an entry added,
        /// removed, replaced or reordered, or its <see cref="MoveProcessor.Enabled"/> flipped. The host
        /// calls this every tick. Allocates nothing unless the list grew.
        /// </summary>
        public void SyncProcessors()
        {
            if (m_Mover == null)
                return;

            if (m_Processors.Count != m_RegisteredCount)
            {
                RefreshProcessors();
                return;
            }

            for (int i = 0; i < m_Processors.Count; i++)
            {
                MoveProcessor processor = m_Processors[i];
                MoveProcessor expected = IsActive(processor) ? processor : null;
                if (ReferenceEquals(expected, m_Registered[i]))
                    continue;

                // Rebuild the whole registration so a re-enabled processor keeps its place in order.
                RefreshProcessors();
                return;
            }
        }

        void RegisterActive()
        {
            int count = m_Processors.Count;
            if (m_Registered.Length < count)
                m_Registered = new MoveProcessor[count];

            for (int i = 0; i < count; i++)
            {
                MoveProcessor processor = m_Processors[i];
                bool active = IsActive(processor);
                m_Registered[i] = active ? processor : null;
                if (active)
                    m_Mover.RegisterProcessor(processor);
            }

            m_RegisteredCount = count;
        }

        void UnregisterAll()
        {
            for (int i = 0; i < m_Registered.Length; i++)
            {
                if (m_Registered[i] != null)
                    m_Mover.UnregisterProcessor(m_Registered[i]);

                m_Registered[i] = null;
            }

            m_RegisteredCount = 0;
        }

        static bool IsActive(MoveProcessor processor) => processor != null && processor.Enabled;

        void Resolve()
        {
            if (m_Resolved)
                return;

            m_Resolved = true;
            m_Mover = GetComponent<KinematicMover>();
            m_Interpolator = GetComponent<MoveInterpolator>();

            // Resolved once, so an assigned Source survives the component being toggled.
            m_Source = GetComponent<IMoveCommandSource>();
        }

        void Awake() => Resolve();

        void OnEnable()
        {
            RefreshProcessors();
        }

        void OnDisable()
        {
            if (m_Mover == null)
                return;

            // Don't leave processors registered for another host to trip over.
            UnregisterAll();
        }

        /// <summary>Never leave a player frozen by a component that went away.</summary>
        void OnDestroy()
        {
            if (m_Locked)
                Unlock();
        }

        /// <summary>
        /// The host loop: read a command, record the pre-tick pose, simulate, record the post-tick pose.
        /// </summary>
        /// <remarks>
        /// <c>Time.fixedDeltaTime</c> is read once and passed to both the source and the mover.
        /// </remarks>
        void FixedUpdate()
        {
            if (m_Mover == null)
                return;

            SyncProcessors();

            float deltaTime = Time.fixedDeltaTime;

            MoveCommand command = m_Source != null ? m_Source.ReadCommand(deltaTime) : default;

            if (m_Interpolator != null)
                m_Interpolator.BeginTick(m_Mover.GetState());

            m_Mover.Simulate(command, deltaTime);

            if (m_Interpolator != null)
                m_Interpolator.EndTick(m_Mover.GetState());
        }

        /// <summary>Sets a behaviour's enabled flag and returns what it was, tolerating a missing one.</summary>
        static bool Switch(Behaviour behaviour, bool enabled)
        {
            if (behaviour == null)
                return false;

            bool was = behaviour.enabled;
            behaviour.enabled = enabled;
            return was;
        }
    }
}
