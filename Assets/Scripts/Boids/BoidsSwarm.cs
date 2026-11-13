using UnityEngine;

/// <summary>
/// Lightweight boids/flocking manager.  Assign a target Transform and an array
/// of agent Transforms; each frame the agents separate, align, cohere, and
/// swarm around the target.  +Z is forward; agents bank into turns.
/// No Rigidbody, no allocations in the hot loop, O(n²) neighbour scan over a
/// per-frame position snapshot (fine for small flocks; a spatial grid only
/// pays off at a few hundred agents).
///
/// The swarm starts <see cref="SwarmState.Resting"/> (agents hold their initial
/// pose).  Call <see cref="WakeSwarm"/> to begin following the target, or
/// <see cref="RestSwarm"/> to swoop back to the captured resting poses.
/// Both are exposed as right-click context-menu items on the component.
///
/// Returning agents always land from above: they fly to a landing gate above
/// and slightly behind their resting pose, then glide forward-and-down onto
/// it, slowing as they near it.
///
/// Each agent's Animator (cached in Awake) is driven through two bool
/// parameters: Flying (any time the agent is not landed) and Flapping (on
/// take-off, while climbing steeply, and while coming in to land), plus an
/// IdleIndex int that plays occasional idle flourishes while landed.
///
/// Split across partial files: this one holds settings, state, the public API,
/// validation and gizmos; BoidsSwarm.Flocking, BoidsSwarm.Return and
/// BoidsSwarm.Animation hold the per-state behaviour.
/// </summary>
public partial class BoidsSwarm : MonoBehaviour
{
    // -------------------------------------------------------------------------
    //  State
    // -------------------------------------------------------------------------

    public enum SwarmState
    {
        Resting,    // agents hold their captured resting pose; no movement
        Following,  // agents flock and swarm around the target
        Returning   // agents swoop back to their resting poses
    }

    // Per-agent landing state, used only during the final "arrive" phase.
    private struct LandState
    {
        public bool       active;   // currently inside the arrive radius, landing
        public float      u;        // normalised landing progress 0..1
        public float      rate;     // du/dt, set at entry so speed matches velocity
        public Vector3    startPos; // position when the landing phase began
        public Quaternion startRot; // rotation when the landing phase began
    }

    // Per-agent animation state.
    private struct AgentAnim
    {
        public Animator animator;        // cached in Awake; null = agent not animated
        public bool     paramsChecked;   // parameter lookup is deferred until the Animator is active
        public bool     hasFlying;       // controller exposes the Flying parameter
        public bool     hasFlapping;     // controller exposes the Flapping parameter
        public bool     hasAboutToLand;  // controller exposes the AboutToLand parameter
        public ParticleSystem takeOffEffect; // cached child particle system, played on take-off
        public float    flapTimer;       // seconds of timed flapping left (take-off / steep climb)
        public bool     hasIdleIndex;    // controller exposes the IdleIndex parameter
        public float    idleTimer;       // seconds of idle-loop time left before the next flourish
        public bool     flourishPending; // IdleIndex set; waiting for the flourish to start
        public float    pendingTime;     // seconds IdleIndex has been set without the flourish starting
        public bool     idlePhaseSet;    // initial idle loop has been started at a random point
    }

    // -------------------------------------------------------------------------
    //  Inspector
    // -------------------------------------------------------------------------

    //  Ordered to match the custom inspector (BoidsSwarmEditor), which draws
    //  the section and sub-group labels; no [Header]s here, as PropertyField
    //  would draw them again inside those sections.  Limits live
    //  on the fields ([Min] / [Range]) so the inspector clamps as you type;
    //  paired min/max values are drawn as one row that keeps them ordered.

    [Tooltip("The Transform agents pursue and swarm around.  BoidsSwarmSequencer moves it.")]
    public Transform target;

    [Tooltip("All agent Transforms managed by this swarm.  Null entries are skipped.")]
    public Transform[] agents = new Transform[0];

    [Tooltip("Weight for the separation rule (personal-space avoidance).")]
    [Range(0f, 5f)] public float separationWeight = 1.5f;

    [Tooltip("Agents closer than this push apart (personal space).")]
    [Min(0f)] public float separationRadius = 1.2f;

    [Tooltip("Weight for the alignment rule (match neighbour headings).")]
    [Range(0f, 5f)] public float alignmentWeight = 1.0f;

    [Tooltip("Weight for the cohesion rule (steer toward neighbour centre).")]
    [Range(0f, 5f)] public float cohesionWeight = 1.0f;

    [Tooltip("Agents closer than this count as neighbours for alignment and cohesion.")]
    [Min(0f)] public float neighborRadius = 3f;

    [Tooltip("Weight for seeking and orbiting the target.")]
    [Range(0f, 5f)] public float targetWeight = 1.2f;

    [Tooltip("Desired swarm distance from the target; agents orbit at this radius.")]
    [Min(0f)] public float orbitRadius = 2f;

    [Tooltip("Maximum agent speed (units/s).")]
    [Min(0f)] public float maxSpeed = 6f;

    [Tooltip("Maximum steering acceleration (units/s²).  Scaled at runtime by MaxSteerMultiplier (set per sequencer event).")]
    [Min(0f)] public float maxSteer = 8f;

    [Tooltip("How quickly agents rotate to face their velocity (Slerp rate per second).")]
    [Min(0f)] public float turnSpeed = 8f;

    [Tooltip("Converts horizontal turn rate into a bank (roll) angle.  0 = no banking.")]
    [Min(0f)] public float bankStrength = 1.5f;

    [Tooltip("Maximum roll angle in degrees during a hard turn.")]
    [Range(0f, 89f)] public float maxBankAngle = 45f;

    [Tooltip("How quickly the bank angle eases in and out (higher = snappier).")]
    [Min(0f)] public float bankSmoothing = 6f;

    [Tooltip("0 = the up-vector fully follows the bank, 1 = always strict world-up.")]
    [Range(0f, 1f)] public float worldUpBias = 0.25f;

    [Tooltip("Within this distance of the landing gate, agents slow from Max Speed down to the descent's start speed, so they never overshoot.")]
    [Min(0.01f)] public float returnSlowRadius = 4f;

    [Tooltip("Maximum sideways offset of the approach waypoint, used when the resting facing is opposite the agent's current facing (aligned = straight in).")]
    [Min(0f)] public float swingWidth = 4f;

    [Tooltip("Multiplies steering and turn rate while returning, tightening the approach arc so agents spiral into the landing gate instead of orbiting it.  1 = no boost.")]
    [Min(1f)] public float restTurnBoost = 2.5f;

    [Tooltip("Height of the landing gate above the resting point.  The final approach always descends from here.")]
    [Min(0f)] public float landingApproachHeight = 1.5f;

    [Tooltip("How far behind the resting point (opposite its facing) the landing gate sits, so the descent glides forward-and-down.  0 = straight down.")]
    [Min(0f)] public float landingApproachDistance = 0.75f;

    [Tooltip("Agents begin the descent within this distance of the landing gate (or of the glide path when already on it).  Also the look-ahead that keeps them on the path.")]
    [Min(0.01f)] public float descentEntryRadius = 0.6f;

    [Tooltip("Descent speed (units/s) at the landing gate.  Drawn with Descent Min Speed as one Descent Speed range.")]
    [Min(0.01f)] public float descentSpeed = 2f;

    [Tooltip("Descent speed (units/s) just above the resting point.  Drawn with Descent Speed as one range.")]
    [Min(0.01f)] public float descentMinSpeed = 0.3f;

    [Tooltip("Distance from the resting point at which the final eased landing begins.")]
    [Min(0f)] public float arriveRadius = 0.4f;

    [Tooltip("Animator bool set while the agent is flying (anything other than landed at rest).")]
    public string flyingParameter = "Flying";

    [Tooltip("Animator bool set while the agent is flapping.")]
    public string flappingParameter = "Flapping";

    [Tooltip("Seconds of flapping when an agent takes off from its resting pose.")]
    [Min(0f)] public float takeOffFlapDuration = 1.2f;

    [Tooltip("Climb angle (degrees above horizontal) at or above which an agent flaps.")]
    [Range(0f, 90f)] public float steepClimbAngle = 35f;

    [Tooltip("Minimum speed (units/s) for a climb to count as steep, so hovering agents don't flap.")]
    [Min(0f)] public float steepClimbMinSpeed = 0.5f;

    [Tooltip("Seconds flapping continues after a steep climb ends.")]
    [Min(0f)] public float steepClimbFlapDuration = 0.5f;

    [Tooltip("While returning, agents flap from this distance to the resting point, and throughout the descent.")]
    [Min(0f)] public float landingFlapDistance = 1.875f;

    [Tooltip("Flapping stops this many seconds before touch-down, so the agent glides onto its resting pose.  At or above Landing Flying Stop Time, the agent flaps on into the blend to idle.  0 = flap until landed.")]
    [Min(0f)] public float landingFlapStopTime = 0.6f;

    [Tooltip("Flying clears this many seconds before touch-down, so the landing / idle animation starts ahead of touch-down.  Set it to the time from the start of the landing clip to its touch-down frame (1 s).  0 = clear on touch-down.")]
    [Min(0f)] public float landingFlyingStopTime = 1f;

    [Tooltip("Animator bool set while the agent is about to land.")]
    public string aboutToLandParameter = "AboutToLand";

    [Tooltip("The AboutToLand Animator bool is set this many seconds before touch-down.  Only drives that parameter; Flying and Flapping use their own stop times.")]
    [Min(0f)] public float landingTriggerLeadTime = 2.5f;

    [Tooltip("Name of the child ParticleSystem on each agent that plays when the agent takes off from its resting pose.  Empty = none.")]
    public string takeOffEffectName = "Feathers";

    [Tooltip("Animator state of the landed idle loop.  Flourishes only trigger while settled in it, and each agent starts it at a random point.")]
    public string idleLoopState = "Idle Loop";

    [Tooltip("Animator int that picks an idle flourish (1..Idle Flourish Count); 0 = plain idle loop.")]
    public string idleIndexParameter = "IdleIndex";

    [Tooltip("Number of idle flourishes; one is picked at random (IdleIndex 1..count).  0 = no flourishes.")]
    [Min(0)] public int idleFlourishCount = 2;

    [Tooltip("Minimum seconds of idle loop between flourishes.  Drawn with the max as one Flourish Interval range.")]
    [Min(0f)] public float idleFlourishIntervalMin = 4f;

    [Tooltip("Maximum seconds of idle loop between flourishes.")]
    [Min(0f)] public float idleFlourishIntervalMax = 10f;

    // -------------------------------------------------------------------------
    //  Runtime state (read-only for other scripts)
    // -------------------------------------------------------------------------

    public SwarmState State => _state;
    private SwarmState _state = SwarmState.Resting;

    /// <summary>
    /// Runtime multiplier on <see cref="maxSteer"/> (never negative).  Set by
    /// <see cref="BoidsSwarmSequencer"/> per event; not saved with the component.
    /// </summary>
    public float MaxSteerMultiplier
    {
        get => _maxSteerMultiplier;
        set => _maxSteerMultiplier = Mathf.Max(0f, value);
    }
    [System.NonSerialized] private float _maxSteerMultiplier = 1f;

    /// <summary>The steering acceleration actually used: maxSteer × MaxSteerMultiplier.</summary>
    public float EffectiveMaxSteer => maxSteer * _maxSteerMultiplier;

    // -------------------------------------------------------------------------
    //  Private caches  (index-matched to agents[])
    // -------------------------------------------------------------------------

    private Vector3[]    _velocities    = new Vector3[0];
    private Vector3[]    _positions     = new Vector3[0]; // per-frame snapshot read by the neighbour scan
    private Vector3[]    _steering      = new Vector3[0]; // per-frame steering, computed before any agent moves
    private float[]      _banks         = new float[0];
    private Vector3[]    _restPositions = new Vector3[0];
    private Quaternion[] _restRotations = new Quaternion[0];
    private bool[]       _rested        = new bool[0];
    private LandState[]  _land          = new LandState[0];
    private bool[]       _descending    = new bool[0];
    private AgentAnim[]  _anim          = new AgentAnim[0];

    private int _flyingHash;
    private int _flappingHash;
    private int _aboutToLandHash;
    private int _idleIndexHash;
    private int _idleLoopStateHash;

    // Give up on a flourish request that hasn't started within this many seconds
    // (e.g. the Animator was mid-transition), so IdleIndex never sticks.
    private const float FlourishPendingTimeout = 1f;

    // -------------------------------------------------------------------------
    //  Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        SyncCaches();
        CaptureRestState();
        CacheAnimators();
        _state = SwarmState.Resting; // start at rest until Wake Swarm is called
    }

    private void Update()
    {
        if (agents == null || agents.Length == 0) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        switch (_state)
        {
            case SwarmState.Resting:
                // Agents hold their pose; nothing to do.
                break;
            case SwarmState.Following:
                UpdateFollowing(dt);
                break;
            case SwarmState.Returning:
                UpdateReturning(dt);
                break;
        }

        UpdateAnimators(dt);
    }

    // -------------------------------------------------------------------------
    //  Public API / context menu
    // -------------------------------------------------------------------------

    /// <summary>Begin following/swarming the target.</summary>
    [ContextMenu("Wake Swarm")]
    public void WakeSwarm()
    {
        SyncCaches();

        // Agents taking off from their resting pose get a burst of flapping.
        // Agents already in the air (mid-return) just carry on flying.
        for (int i = 0; i < _anim.Length; i++)
        {
            if (IsLanded(i))
            {
                _anim[i].flapTimer = Mathf.Max(_anim[i].flapTimer, takeOffFlapDuration);
                if (_anim[i].takeOffEffect != null) _anim[i].takeOffEffect.Play(true);
            }
            _descending[i] = false;
        }

        _state = SwarmState.Following;
    }

    /// <summary>Swoop the agents back to their captured resting poses.</summary>
    [ContextMenu("Rest Swarm")]
    public void RestSwarm()
    {
        SyncCaches();
        _state = SwarmState.Returning;

        // Mark all agents as not-yet-arrived and clear any stale landing state.
        for (int i = 0; i < _rested.Length; i++)
            _rested[i] = false;
        for (int i = 0; i < _land.Length; i++)
            _land[i].active = false;
        for (int i = 0; i < _descending.Length; i++)
            _descending[i] = false;
    }

    /// <summary>
    /// Toggle the swarm between waking and resting: if it is currently following
    /// the target it will return to rest, otherwise (resting or mid-return) it
    /// will begin following.
    /// </summary>
    [ContextMenu("Toggle Swarm")]
    public void ToggleSwarm()
    {
        if (_state == SwarmState.Following)
            RestSwarm();
        else
            WakeSwarm();
    }

    /// <summary>
    /// Re-capture the current agent poses as the new resting poses.  Called
    /// automatically in Awake; call manually if you reposition agents at runtime
    /// and want their new poses to become the rest targets.
    /// </summary>
    [ContextMenu("Capture Rest State")]
    public void CaptureRestState()
    {
        SyncCaches();
        if (agents == null) return;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] == null) continue;
            _restPositions[i] = agents[i].position;
            _restRotations[i] = agents[i].rotation;
            _velocities[i]    = Vector3.zero;
            _banks[i]         = 0f;
        }
    }

    // -------------------------------------------------------------------------
    //  Validation & gizmos
    // -------------------------------------------------------------------------

    private void OnValidate()
    {
        // Per-field limits are [Min]/[Range] attributes and the inspector keeps
        // the paired ranges ordered; this only backs the pairs up for values set
        // some other way (Debug inspector, scripts, presets).
        descentMinSpeed         = Mathf.Min(descentMinSpeed, descentSpeed);
        idleFlourishIntervalMax = Mathf.Max(idleFlourishIntervalMin, idleFlourishIntervalMax);

        SyncCaches();
    }

    // Toggle with the Scene view's Gizmos menu (BoidsSwarm entry).
    private void OnDrawGizmosSelected()
    {
        // Radii around this manager (centre of swarm)
        Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, neighborRadius);

        Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
        Gizmos.DrawWireSphere(transform.position, separationRadius);

        // Orbit radius around target
        if (target != null)
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.4f);
            Gizmos.DrawWireSphere(target.position, orbitRadius);

            // Line from manager to target
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(transform.position, target.position);
        }

        // Per-agent axes
        if (agents != null)
        {
            foreach (Transform agent in agents)
            {
                if (agent == null) continue;

                // +Z forward (blue)
                Gizmos.color = Color.blue;
                Gizmos.DrawRay(agent.position, agent.forward * 0.8f);

                // Current up (green)
                Gizmos.color = Color.green;
                Gizmos.DrawRay(agent.position, agent.up * 0.5f);
            }
        }

        // Resting poses (only meaningful once captured in play mode)
        if (_restPositions != null && _restRotations != null)
        {
            int count = Mathf.Min(_restPositions.Length, _restRotations.Length);
            for (int i = 0; i < count; i++)
            {
                Vector3    rp = _restPositions[i];
                Quaternion rr = _restRotations[i];
                if (rp == Vector3.zero) continue; // uncaptured slot

                Gizmos.color = new Color(1f, 1f, 0.2f, 0.7f);
                Gizmos.DrawWireCube(rp, Vector3.one * 0.25f);
                Gizmos.color = new Color(1f, 0.8f, 0f, 0.9f);
                Gizmos.DrawRay(rp, (rr * Vector3.forward) * 0.6f); // resting forward
                // Arrive radius (where the eased landing begins)
                Gizmos.color = new Color(1f, 1f, 0.2f, 0.25f);
                Gizmos.DrawWireSphere(rp, arriveRadius);
                // Landing gate, its entry radius, and the glide path down to rest
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.6f);
                Vector3 gate = LandingGate(rp, rr);
                Gizmos.DrawLine(gate, rp);
                Gizmos.DrawWireSphere(gate, 0.1f);
                Gizmos.color = new Color(1f, 0.6f, 0.2f, 0.2f);
                Gizmos.DrawWireSphere(gate, descentEntryRadius);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ensures all per-agent caches are sized to match <see cref="agents"/>.Length,
    /// preserving existing values where possible.
    /// </summary>
    private void SyncCaches()
    {
        int count = agents != null ? agents.Length : 0;

        _velocities    = Resize(_velocities, count);
        _positions     = Resize(_positions, count);
        _steering      = Resize(_steering, count);
        _restPositions = Resize(_restPositions, count);
        _banks         = Resize(_banks, count);
        _rested        = Resize(_rested, count);
        _restRotations = Resize(_restRotations, count, Quaternion.identity);
        _land          = Resize(_land, count);
        _descending    = Resize(_descending, count);
        _anim          = Resize(_anim, count);
    }

    /// <summary>
    /// Returns <paramref name="src"/> resized to <paramref name="count"/>, keeping
    /// existing elements and filling new ones with <paramref name="fill"/>.
    /// Returns <paramref name="src"/> itself when it is already the right size.
    /// </summary>
    private static T[] Resize<T>(T[] src, int count, T fill = default)
    {
        if (src != null && src.Length == count) return src;

        T[] dst  = new T[count];
        int copy = src != null ? Mathf.Min(src.Length, count) : 0;
        if (copy > 0) System.Array.Copy(src, dst, copy);
        for (int k = copy; k < count; k++) dst[k] = fill;
        return dst;
    }
}
