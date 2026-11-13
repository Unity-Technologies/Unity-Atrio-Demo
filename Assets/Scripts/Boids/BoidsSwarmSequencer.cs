using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Drives a <see cref="BoidsSwarm"/> through authored sequences of events.
///
/// Two sequences are provided — one intended to play "on wake" and one "on
/// rest" — but both are just ordered lists of <see cref="SequenceEvent"/>s that
/// you can trigger via the public methods / context menu.  Each event can:
///   • change the swarm state (None / Wake / Rest),
///   • scale the swarm's Max Steer by a per-event multiplier,
///   • teleport the swarm's target to a random point inside a world-space AABB
///     on entry, and optionally re-randomise it on a random interval while active,
///   • hold for a set duration before advancing to the next event.
///
/// The AABBs are plain <see cref="Bounds"/> (world-space, axis-aligned, never
/// rotated) and are edited with scalable box handles in the custom editor,
/// which also keeps every event's values in range.
/// </summary>
public class BoidsSwarmSequencer : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  Types
    // -------------------------------------------------------------------------

    /// <summary>What to do to the referenced swarm when an event begins.</summary>
    public enum StateChange
    {
        None, // no-op: leave the swarm in its current state
        Wake, // call BoidsSwarm.WakeSwarm()
        Rest  // call BoidsSwarm.RestSwarm()
    }

    /// <summary>How the final event of a sequence is treated.</summary>
    private enum LastEventMode
    {
        Normal,          // last event holds for its duration like any other, then the sequence ends
        RunIndefinitely, // last event never ends; keeps holding and re-randomising the target on interval
        EntryOnly        // last event applies only its entry changes (state + initial target), then the sequence ends
    }

    [System.Serializable]
    public class SequenceEvent
    {
        [Tooltip("Label shown in the inspector and on the scene handles.")]
        public string name = "Event";

        [Tooltip("State change applied to the swarm when this event begins.")]
        public StateChange stateChange = StateChange.None;

        [Tooltip("How long (seconds) to remain in this event before advancing to the next.")]
        [Min(0f)] public float eventDuration = 3f;

        [Tooltip("Multiplier applied to the swarm's Max Steer when this event begins (1 = unchanged).  Stays in effect until the next event sets its own.")]
        [Min(0f)] public float maxSteerMultiplier = 1f;

        [Tooltip("World-space, axis-aligned volume in which the swarm's target is randomly placed on entry and on each retarget.")]
        public Bounds randomTargetArea = new Bounds(Vector3.zero, Vector3.one * 5f);

        // Retarget interval range.  Max 0 = retargeting off: the target is placed
        // once on entry only.  The inspector draws these as a toggle + range.
        [Tooltip("Minimum seconds between retargets.")]
        [Min(0f)] public float targetUpdateIntervalMin = 1f;

        [Tooltip("Maximum seconds between retargets.  0 = retargeting off.")]
        [Min(0f)] public float targetUpdateIntervalMax = 1f;

        /// <summary>True when the target is re-randomised during the event.</summary>
        public bool Retargets => targetUpdateIntervalMax > 0f;

        /// <summary>
        /// Random wait time in [min, max] for the next retarget, or 0 when
        /// retargeting is off.
        /// </summary>
        public float NextUpdateInterval()
        {
            if (!Retargets) return 0f;
            return Random.Range(targetUpdateIntervalMin, targetUpdateIntervalMax);
        }
    }

    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------

    [Tooltip("The swarm this sequencer drives.  Its Target is the Transform moved into each event's Random Target Area.")]
    public BoidsSwarm swarm;

    [Tooltip("Events to run through after waking the swarm.  The last event runs until the sequence is stopped or replaced.")]
    public List<SequenceEvent> wakeSequence = new List<SequenceEvent>();

    [Tooltip("Events to run through when resting the swarm.  The last event applies only its entry changes, then the sequence ends.")]
    public List<SequenceEvent> restSequence = new List<SequenceEvent>();

    [Tooltip("Pressing the toggle hotkey plays the rest sequence while the swarm is following, otherwise the wake sequence.")]
    public bool enableToggleHotkey = true;

    [Tooltip("Key that toggles wake/rest.")]
    public Key toggleKey = Key.B;

    [Tooltip("Require Shift to be held alongside the toggle key.")]
    public bool requireShift = true;

    // -------------------------------------------------------------------------
    //  Runtime state
    // -------------------------------------------------------------------------

    /// <summary>True while a sequence coroutine is running.</summary>
    public bool IsPlaying => _running != null;

    private Coroutine _running;

    // -------------------------------------------------------------------------
    //  Unity lifecycle
    // -------------------------------------------------------------------------

    private void Update()
    {
        PollToggleHotkey();
    }

    // -------------------------------------------------------------------------
    //  Public API / context menu
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggle the swarm between waking and resting via the sequences: if the
    /// swarm is currently following the target, play the rest sequence;
    /// otherwise (resting or mid-return, or no swarm assigned) play the wake
    /// sequence.
    /// </summary>
    [ContextMenu("Toggle Sequence")]
    public void ToggleSequence()
    {
        bool following = swarm != null && swarm.State == BoidsSwarm.SwarmState.Following;
        if (following)
            PlayRestSequence();
        else
            PlayWakeSequence();
    }

    /// <summary>
    /// Play the wake sequence from the start (interrupts any running sequence).
    /// The final event runs indefinitely, continuing to re-randomise the target
    /// on its interval until the sequence is stopped or replaced.
    /// </summary>
    [ContextMenu("Play Wake Sequence")]
    public void PlayWakeSequence()
    {
        PlaySequence(wakeSequence, LastEventMode.RunIndefinitely);
    }

    /// <summary>
    /// Play the rest sequence from the start (interrupts any running sequence).
    /// The final event applies only its entry changes (state change + initial
    /// target placement) and then the sequence ends — it does not hold for its
    /// duration or keep updating the target.
    /// </summary>
    [ContextMenu("Play Rest Sequence")]
    public void PlayRestSequence()
    {
        PlaySequence(restSequence, LastEventMode.EntryOnly);
    }

    /// <summary>Stop whichever sequence is currently running.</summary>
    [ContextMenu("Stop Sequence")]
    public void StopSequence()
    {
        if (_running != null)
        {
            StopCoroutine(_running);
            _running = null;
        }
    }

    /// <summary>Play an arbitrary sequence, interrupting any that is already running.</summary>
    public void PlaySequence(List<SequenceEvent> sequence)
    {
        PlaySequence(sequence, LastEventMode.Normal);
    }

    /// <summary>Play an arbitrary sequence with an explicit last-event behaviour.</summary>
    private void PlaySequence(List<SequenceEvent> sequence, LastEventMode lastMode)
    {
        if (sequence == null || sequence.Count == 0) return;
        if (!isActiveAndEnabled) return; // coroutines require an active component

        StopSequence();
        _running = StartCoroutine(RunSequence(sequence, lastMode));
    }

    // -------------------------------------------------------------------------
    //  Sequence coroutine
    // -------------------------------------------------------------------------

    private IEnumerator RunSequence(List<SequenceEvent> sequence, LastEventMode lastMode)
    {
        for (int i = 0; i < sequence.Count; i++)
        {
            SequenceEvent evt = sequence[i];
            if (evt == null) continue;

            bool isLast = (i == sequence.Count - 1);

            // --- Entry: apply steer multiplier and state change, place the target ---
            if (swarm != null) swarm.MaxSteerMultiplier = evt.maxSteerMultiplier;
            ApplyStateChange(evt.stateChange);
            MoveTargetTo(RandomPointIn(evt.randomTargetArea));

            // Last event, entry-only mode (rest sequence): stop right after the
            // entry changes — no duration hold, no interval updates.
            if (isLast && lastMode == LastEventMode.EntryOnly)
                break;

            // Last event, indefinite mode (wake sequence): hold forever, still
            // re-randomising the target on its interval.
            bool indefinite = isLast && lastMode == LastEventMode.RunIndefinitely;

            // --- Hold for the event duration, re-randomising on interval ---
            float elapsed      = 0f;
            float sinceUpdate  = 0f;
            float nextInterval = evt.NextUpdateInterval(); // 0 or less = updates disabled

            while (indefinite || elapsed < evt.eventDuration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;

                if (nextInterval > 0f)
                {
                    sinceUpdate += dt;
                    if (sinceUpdate >= nextInterval)
                    {
                        sinceUpdate -= nextInterval;
                        MoveTargetTo(RandomPointIn(evt.randomTargetArea));
                        nextInterval = evt.NextUpdateInterval(); // fresh random wait for next update
                    }
                }

                yield return null;
            }
        }

        _running = null;
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    private void ApplyStateChange(StateChange change)
    {
        if (swarm == null) return;

        switch (change)
        {
            case StateChange.Wake:
                swarm.WakeSwarm();
                break;
            case StateChange.Rest:
                swarm.RestSwarm();
                break;
            case StateChange.None:
            default:
                break; // no-op
        }
    }

    private void MoveTargetTo(Vector3 worldPos)
    {
        if (swarm == null || swarm.target == null) return;
        swarm.target.position = worldPos;
    }

    /// <summary>
    /// Polls the New Input System for the wake/rest toggle hotkey
    /// (Shift + toggleKey by default) and toggles the swarm on press.
    /// </summary>
    private void PollToggleHotkey()
    {
        if (!enableToggleHotkey) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return; // no keyboard connected

        if (!kb[toggleKey].wasPressedThisFrame) return;

        if (requireShift && !(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed))
            return;

        ToggleSequence();
    }

    /// <summary>Uniformly random point inside a world-space AABB.</summary>
    private static Vector3 RandomPointIn(Bounds b)
    {
        Vector3 e = b.extents;
        return b.center + new Vector3(
            Random.Range(-e.x, e.x),
            Random.Range(-e.y, e.y),
            Random.Range(-e.z, e.z));
    }
}
