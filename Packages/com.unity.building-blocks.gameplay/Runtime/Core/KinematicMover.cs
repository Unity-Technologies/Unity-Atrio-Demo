using System.Collections.Generic;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// A kinematic character mover: a fixed-tick capsule simulation with snapshotable state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mover does not drive itself: a host calls <see cref="Simulate"/> each tick with
    /// <c>deltaTime</c>. It applies no forces; gravity and acceleration come from
    /// <see cref="IMoveProcessor"/>s. All movement state is in <see cref="MoveState"/>, and
    /// <see cref="MoveInterpolator"/> moves the visuals between ticks.
    /// </para>
    /// <para>
    /// The mover owns its <see cref="CapsuleCollider"/> (sized from <see cref="Height"/> and
    /// <see cref="Radius"/>) and forces its <see cref="Rigidbody"/> kinematic. The collider is used
    /// for overlap recovery and so other bodies and triggers see the character.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Building Blocks/Kinematic Mover")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CapsuleCollider))]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class KinematicMover : MonoBehaviour
    {
        [Header("Capsule")]
        [SerializeField, Min(0.1f)]
        [Tooltip("Total capsule height in meters. Written onto the CapsuleCollider. Do not edit " +
                 "that component by hand.")]
        float m_Height = 1.8f;

        [SerializeField, Min(0.05f)]
        [Tooltip("Capsule radius in meters. Written onto the CapsuleCollider.")]
        float m_Radius = 0.35f;

        [Header("Grounding")]
        [SerializeField, Range(1f, 89f)]
        [Tooltip("Steepest surface, in degrees, the character may stand on AND walk up. One " +
                 "definition on purpose, so the two can never disagree. Lowering this to make " +
                 "grounding stricter also makes more geometry unclimbable.")]
        float m_SlopeLimit = 50f;

        [SerializeField, Min(0f)]
        [Tooltip("Tallest obstruction, in meters, resolved by stepping up rather than blocking. " +
                 "Stairs with risers below this are climbed without a jump.")]
        float m_StepOffset = 0.3f;

        [SerializeField, Range(0f, 1f)]
        [Tooltip("How fast the character walks straight up or down the steepest ramp it can walk " +
                 "(Slope Limit), as a share of walking speed. Flatter ramps blend toward full " +
                 "speed; flat ground and walking across a ramp are always full speed. 1 = ramps " +
                 "cost nothing. Changing Slope Limit moves every ramp's speed with it. Stepped " +
                 "stairs are climbed at full speed either way.")]
        float m_SpeedAtSlopeLimit = k_DefaultSpeedAtSlopeLimit;

        [SerializeField, Min(0f)]
        [Tooltip("How far below the feet the ground probe looks each tick. Coupled to jump speed " +
                 "and tick rate: while Jump Speed × fixedDeltaTime is NOT greater than this, a " +
                 "held jump re-fires and climbs.")]
        float m_GroundProbeDistance = k_DefaultGroundProbeDistance;

        [SerializeField, Min(0f)]
        [Tooltip("How far the character is pulled back down to ground that fell away faster than " +
                 "the probe reaches, such as stepping from level ground onto a descending slope. " +
                 "Also glues you slightly past the lip of a drop.")]
        float m_GroundSnapDistance = k_DefaultGroundSnapDistance;

        [Header("Collision")]
        [SerializeField]
        [Tooltip("Which layers the capsule collides with. Left at Everything so two characters " +
                 "block each other; narrow it for a character that walks through its own kind.")]
        LayerMask m_CollisionMask = ~0;

        [SerializeField, Min(0.0001f)]
        [Tooltip("Gap the solver keeps between the capsule and everything it touches, in meters. " +
                 "Too small causes jitter against geometry; too large makes the character float.")]
        float m_SkinWidth = k_DefaultSkinWidth;

        [SerializeField, Range(1, 8)]
        [Tooltip("How many times one tick's motion may be re-projected after being blocked. Each " +
                 "iteration is one more surface the character can slide along in a single tick. " +
                 "Raise it for tight, complex geometry (corners, clutter), lower it for speed.")]
        int m_MaxSlideIterations = k_DefaultMaxSlideIterations;

        /// <summary>
        /// Default <see cref="SkinWidth"/>: 2 cm.
        /// </summary>
        /// <remarks>
        /// Smallest gap that keeps the next sweep from re-hitting the same plane. Larger floats the
        /// character off walls; smaller jitters in corners.
        /// </remarks>
        const float k_DefaultSkinWidth = 0.02f;

        /// <summary>
        /// Default <see cref="GroundProbeDistance"/>: 5 cm.
        /// </summary>
        /// <remarks>
        /// Must exceed <see cref="SkinWidth"/>, or a mover resting on the floor reads as airborne.
        /// </remarks>
        const float k_DefaultGroundProbeDistance = 0.05f;

        /// <summary>
        /// Default <see cref="GroundSnapDistance"/>: 20 cm.
        /// </summary>
        /// <remarks>
        /// Covers a 10 m/s sprint stepping onto a 50 degree slope at 60 Hz (a 0.198 m drop in the
        /// first tick) and stays below <see cref="StepOffset"/>. Larger values also glue the mover further past a ledge.
        /// </remarks>
        const float k_DefaultGroundSnapDistance = 0.2f;

        /// <summary>
        /// Default <see cref="SpeedAtSlopeLimit"/>: 40%.
        /// </summary>
        /// <remarks>
        /// At the default <see cref="SlopeLimit"/> this matches the geometric slowdown of sliding up a ramp.
        /// </remarks>
        const float k_DefaultSpeedAtSlopeLimit = 0.4f;

        /// <summary>
        /// Default <see cref="MaxSlideIterations"/>: 4. Enough for an ordinary inside corner.
        /// </summary>
        const int k_DefaultMaxSlideIterations = 4;

        /// <summary>
        /// Motion shorter than this is treated as zero, so a near-zero vector is never normalized.
        /// </summary>
        const float k_MinMoveDistance = 1e-5f;

        /// <summary>
        /// How many colliders one depenetration pass will resolve.
        /// </summary>
        /// <remarks>
        /// The buffer has one spare slot because the overlap query returns the mover's own collider.
        /// </remarks>
        const int k_OverlapBufferCapacity = 8;

        /// <summary>
        /// Hard cap on depenetration passes per tick.
        /// </summary>
        /// <remarks>
        /// More than one pass is needed where two colliders push the capsule into each other.
        /// Acute crevices converge over several ticks instead of one; the loop stops as soon as a
        /// pass separates nothing.
        /// </remarks>
        const int k_MaxDepenetrationIterations = 3;

        /// <summary>
        /// Radius of the thin downward sweep that reads the surface just past a contact
        /// (<see cref="TrySurfacePast"/>): small enough to land on a stair tread without touching the riser.
        /// </summary>
        const float k_SurfaceProbeRadius = 0.01f;

        /// <summary>
        /// How far past the contact point, horizontally, the surface sweep starts.
        /// </summary>
        const float k_SurfaceProbeInset = 0.03f;

        /// <summary>
        /// How far above the contact point the surface sweep starts, and half of how far it looks down.
        /// </summary>
        const float k_SurfaceProbeHeight = 0.05f;

        /// <summary>
        /// How much flatter (in <c>normal.y</c>) a step's landing must be than the blocking face;
        /// otherwise it is a ramp, which the slide handles.
        /// </summary>
        const float k_FlatterEpsilon = 1e-3f;

        /// <summary>
        /// Contacts whose normal rises less than this are walls, not ledge edges, and are not probed
        /// for a surface past them.
        /// </summary>
        const float k_SurfaceProbeMinRise = 0.05f;

        // Field initializers, not Awake: adding a component in EditMode never calls Awake.
        MoveState m_State = MoveState.Default;

        // Allocated once here (not in Awake, for the same reason); Simulate must not allocate.
        readonly List<IMoveProcessor> m_Processors = new List<IMoveProcessor>();
        readonly Collider[] m_OverlapBuffer = new Collider[k_OverlapBufferCapacity + 1];

        ICollisionBackend m_Backend;

        /// <summary>Capsule height in meters.</summary>
        public float Height => m_Height;

        /// <summary>Capsule radius in meters.</summary>
        public float Radius => m_Radius;

        /// <summary>
        /// Steepest standable slope, in degrees, inclusive: a face exactly at this angle is
        /// still standable, one steeper is not.
        /// </summary>
        public float SlopeLimit => m_SlopeLimit;

        /// <summary>Maximum step height the mover resolves as a step, in meters.</summary>
        public float StepOffset => m_StepOffset;

        /// <summary>
        /// Share of horizontal speed, 0 to 1, kept walking straight up or down a ramp at
        /// <see cref="SlopeLimit"/>. Default <c>0.4</c>; <c>1</c> means ramps cost nothing. Flatter
        /// ramps follow <see cref="SpeedOnSlope"/>; stepped stairs are unaffected.
        /// </summary>
        public float SpeedAtSlopeLimit
        {
            get => m_SpeedAtSlopeLimit;
            set => m_SpeedAtSlopeLimit = Mathf.Clamp01(value);
        }

        /// <summary>
        /// The share of horizontal speed, 0 to 1, kept walking straight up or down a ramp of
        /// <paramref name="degrees"/>: 1 on the flat, <see cref="SpeedAtSlopeLimit"/> at
        /// <see cref="SlopeLimit"/>, blended by <c>sin²</c> of the angle between.
        /// </summary>
        public float SpeedOnSlope(float degrees)
        {
            float slope = Mathf.Sin(Mathf.Clamp(degrees, 0f, 90f) * Mathf.Deg2Rad);
            return SlopeFactor(slope * slope);
        }

        /// <summary>
        /// How far below the capsule the ground probe looks for standable ground, in meters.
        /// Default <c>0.05</c>.
        /// </summary>
        public float GroundProbeDistance => m_GroundProbeDistance;

        /// <summary>
        /// How far below the capsule a mover that was grounded last tick will be pulled back
        /// down to standable ground, in meters. Default <c>0.2</c>; <c>0</c> disables snapping.
        /// </summary>
        public float GroundSnapDistance => m_GroundSnapDistance;

        /// <summary>
        /// Gap the solver keeps between the capsule and the geometry it slides along, in
        /// meters. Default <c>0.02</c>.
        /// </summary>
        public float SkinWidth => m_SkinWidth;

        /// <summary>Layers this mover collides with.</summary>
        public LayerMask CollisionMask => m_CollisionMask;

        /// <summary>
        /// How many times one tick's motion may be re-projected after being blocked, 1-8. Default
        /// <c>4</c>. Motion still unresolved after this many planes is discarded.
        /// </summary>
        public int MaxSlideIterations => m_MaxSlideIterations;

        /// <summary>
        /// The collision backend used by <see cref="Simulate"/>. Assigning <c>null</c> makes
        /// the next tick rebuild the default <see cref="CapsuleSweepBackend"/>.
        /// </summary>
        /// <remarks>
        /// Internal; tests and samples use it to inject a backend.
        /// </remarks>
        internal ICollisionBackend Backend
        {
            get => m_Backend;
            set => m_Backend = value;
        }

        /// <summary>The current simulation state. Snapshot for rollback, netcode, or replay.</summary>
        public MoveState GetState() => m_State;

        /// <summary>
        /// Restores a snapshot taken with <see cref="GetState"/> and syncs the root transform.
        /// Use for rollback and replay; to place a character, prefer <see cref="KinematicMoverExtensions.Teleport"/>.
        /// </summary>
        public void ApplyState(in MoveState state)
        {
            m_State = state;
            transform.SetPositionAndRotation(state.Position, state.Rotation);
        }

        /// <summary>
        /// Registers a processor. Processors run in registration order; one registered twice runs twice.
        /// Registering during a <see cref="Simulate"/> call is undefined.
        /// </summary>
        /// <param name="processor">The processor to add. Ignored when <c>null</c>.</param>
        public void RegisterProcessor(IMoveProcessor processor)
        {
            if (processor == null)
                return;

            m_Processors.Add(processor);
        }

        /// <summary>
        /// Removes the first registration of a processor, so it no longer runs during
        /// <see cref="Simulate"/>. Unregistering during a <see cref="Simulate"/> call is
        /// undefined.
        /// </summary>
        /// <param name="processor">The processor to remove.</param>
        /// <returns>
        /// <c>true</c> if the processor was registered and has been removed; <c>false</c> if
        /// it was not registered (or is <c>null</c>).
        /// </returns>
        public bool UnregisterProcessor(IMoveProcessor processor)
        {
            if (processor == null)
                return false;

            return m_Processors.Remove(processor);
        }

        /// <summary>
        /// Advances the simulation by one tick. Call from the host loop: <c>FixedUpdate</c>
        /// in a plain project, a prediction loop or replay runner elsewhere.
        /// </summary>
        /// <remarks>
        /// Every processor's <see cref="IMoveProcessor.UpdateVelocity"/> runs before any
        /// <see cref="IMoveProcessor.UpdateRotation"/>, in registration order. With no processors
        /// registered the mover does not fall.
        /// </remarks>
        /// <param name="command">Input for this tick, from an <see cref="IMoveCommandSource"/>.</param>
        /// <param name="deltaTime">Tick duration in seconds. The host owns time.</param>
        public void Simulate(in MoveCommand command, float deltaTime)
        {
            // Indexed loops, not foreach, to keep the sim path allocation-free.
            for (int i = 0; i < m_Processors.Count; i++)
                m_Processors[i].UpdateVelocity(ref m_State, in command, deltaTime);

            for (int i = 0; i < m_Processors.Count; i++)
                m_Processors[i].UpdateRotation(ref m_State, in command, deltaTime);

            var backend = ResolveBackend();

            CollideAndSlide(backend, SlowedDownhill(m_State.Velocity * deltaTime));
            ResolveOverlaps(backend);

            // Captured before the probe overwrites it: the snap only applies to a mover grounded last tick.
            bool wasGrounded = m_State.IsGrounded;

            ProbeGround(backend);
            SnapToGround(backend, wasGrounded);
            AccumulateAirTime(deltaTime);

            transform.SetPositionAndRotation(m_State.Position, m_State.Rotation);
        }

        /// <summary>
        /// Moves the capsule by <paramref name="motion"/>, sliding along whatever blocks it.
        /// </summary>
        /// <remarks>
        /// Each iteration sweeps, stops just short of the hit, and removes only the part of motion
        /// and velocity moving into the plane. A face too steep to stand on is also too steep to
        /// climb. A grounded mover's velocity never keeps upward climb, so it does not launch off ramps.
        /// Climbing a standable ramp keeps the pressed horizontal heading instead of turning it across the slope.
        /// </remarks>
        void CollideAndSlide(ICollisionBackend backend, Vector3 motion)
        {
            bool slopeApplied = false;
            for (int i = 0; i < m_MaxSlideIterations; i++)
            {
                float distance = motion.magnitude;
                if (distance <= k_MinMoveDistance)
                    return;

                Vector3 direction = motion / distance;

                // Sweep one skin further so a plane exactly at the end of the motion is still found.
                if (!backend.Sweep(CurrentShape(), direction, distance + m_SkinWidth, out var hit))
                {
                    m_State.Position += motion;
                    return;
                }

                float travel = hit.distance - m_SkinWidth;
                if (travel >= distance)
                {
                    // The plane is beyond this tick's motion; nothing to slide against.
                    m_State.Position += motion;
                    return;
                }

                if (travel > 0f)
                {
                    m_State.Position += direction * travel;
                    motion -= direction * travel;
                }

                // Try a step first for every blocking face: the contact normal on an edge is the
                // hemisphere's curvature, so it cannot tell a kerb from a ramp. TryStepOver rejects ramps.
                if (TryStepOver(backend, in hit, ref motion))
                    continue;

                // One answer for both "may I stand here" and "may I climb here".
                bool standable = IsStandableContact(backend, in hit, out Vector3 surface);

                // Per iteration, so a corner's second plane cannot re-grant climb the first denied.
                float motionRise = motion.y;
                float velocityRise = m_State.Velocity.y;
                Vector3 motionBefore = motion;
                Vector3 velocityBefore = m_State.Velocity;

                ClipAgainstPlane(ref motion, hit.normal);
                ClipAgainstPlane(ref m_State.Velocity, hit.normal);

                if (!standable)
                    LimitClimb(ref motion, motionRise);

                // A grounded mover's velocity keeps no climb, or it would launch off the top of ramps.
                if (!standable || m_State.IsGrounded)
                    LimitClimb(ref m_State.Velocity, velocityRise);

                // Replace the slide's geometric slowdown with Speed At Slope Limit, once per tick.
                // The clip only removes the uphill part, so it would turn the heading across the
                // slope; keep the pressed heading and take the rise from the plane instead.
                // Velocity keeps its horizontal part so the loss does not compound across ticks.
                // Uphill only: a downhill motion was already slowed and laid on the ground before the sweep.
                // The surface normal, not the contact's: on an edge the contact's can be nearly flat
                // and would turn the rise into a spike.
                if (standable && m_State.IsGrounded && !slopeApplied && surface.y > 0f
                    && motionBefore.x * surface.x + motionBefore.z * surface.z < 0f)
                {
                    float across = Horizontal(motionBefore);
                    if (across > k_MinMoveDistance)
                    {
                        slopeApplied = true;
                        float scale = SlopeFactor(surface, new Vector3(motionBefore.x, 0f, motionBefore.z) / across);
                        float x = motionBefore.x * scale;
                        float z = motionBefore.z * scale;
                        // On the plane, plus whatever part of the motion already left it.
                        float rise = -(surface.x * x + surface.z * z) / surface.y;
                        float away = Mathf.Max(Vector3.Dot(motionBefore, surface), 0f) / surface.y;
                        motion = new Vector3(x, rise + away, z);
                        m_State.Velocity.x = velocityBefore.x;
                        m_State.Velocity.z = velocityBefore.z;
                    }
                }
            }

            // Iteration cap reached: discard the remaining motion.
        }

        /// <summary>
        /// Tries to resolve a blocked sweep as a step up rather than a wall: raises the capsule,
        /// carries the remaining horizontal motion forward, and drops it back onto whatever it
        /// finds. Returns <c>false</c>, leaving the position untouched, when the obstruction is
        /// not a step, so the caller falls back to sliding along it.
        /// </summary>
        /// <remarks>
        /// Three sweeps: up (capped at <see cref="StepOffset"/> and by headroom), forward (a wall
        /// still blocks), and down onto a landing judged by <see cref="IsStepLanding"/>. Ramps are
        /// rejected because the landing is the same slope. On success velocity is kept, so stairs do
        /// not feel like small walls. Only runs for a mover grounded last tick.
        /// </remarks>
        bool TryStepOver(ICollisionBackend backend, in RaycastHit blocking, ref Vector3 motion)
        {
            if (m_StepOffset <= 0f || !m_State.IsGrounded)
                return false;

            var horizontal = new Vector3(motion.x, 0f, motion.z);
            float horizontalDistance = horizontal.magnitude;
            if (horizontalDistance <= k_MinMoveDistance)
                return false;

            Vector3 forwardDirection = horizontal / horizontalDistance;
            Vector3 origin = m_State.Position;

            // 1. Up, one skin further so a ceiling exactly at step height is found.
            float rise = m_StepOffset;
            if (backend.Sweep(CurrentShape(), Vector3.up, m_StepOffset + m_SkinWidth, out var ceiling))
                rise = Mathf.Min(rise, ceiling.distance - m_SkinWidth);

            // A skin width or less is the solver's own gap, not clearance.
            if (rise <= m_SkinWidth)
                return false;

            m_State.Position = origin + Vector3.up * rise;

            // 2. Forward.
            float forward = horizontalDistance;
            if (backend.Sweep(CurrentShape(), forwardDirection, horizontalDistance + m_SkinWidth, out var ahead))
                forward = Mathf.Min(forward, ahead.distance - m_SkinWidth);

            if (forward <= m_SkinWidth)
            {
                m_State.Position = origin;
                return false;
            }

            m_State.Position += forwardDirection * forward;

            // 3. Down.
            if (!backend.Sweep(CurrentShape(), Vector3.down, rise + m_SkinWidth, out var landing)
                || !IsStepLanding(backend, in blocking, in landing, origin.y))
            {
                m_State.Position = origin;
                return false;
            }

            m_State.Position += Vector3.down * Mathf.Max(0f, landing.distance - m_SkinWidth);

            motion -= forwardDirection * forward;
            return true;
        }

        /// <summary>
        /// Whether a step may end on <paramref name="landing"/>, having been blocked by
        /// <paramref name="blocking"/>.
        /// </summary>
        /// <remarks>
        /// The landing must be standable, flatter than the blocking face (else it is a ramp), and no
        /// higher than <see cref="StepOffset"/> above the start. On an edge contact the surface is read
        /// with <see cref="TrySurfacePast"/>.
        /// </remarks>
        bool IsStepLanding(ICollisionBackend backend, in RaycastHit blocking, in RaycastHit landing,
                           float originHeight)
        {
            Vector3 normal = landing.normal;
            Vector3 point = landing.point;

            if (!IsStandable(normal)
                && (normal.y <= k_SurfaceProbeMinRise
                    || !TrySurfacePast(backend, in landing, out normal, out point)
                    || !IsStandable(normal)))
                return false;

            if (normal.y <= blocking.normal.y + k_FlatterEpsilon)
                return false;

            return point.y - originHeight <= m_StepOffset + m_SkinWidth;
        }

        /// <summary>
        /// Whether the mover may stand on what a sweep hit, and the normal of that surface.
        /// </summary>
        /// <remarks>
        /// On an edge the contact normal is the hemisphere's curvature, not the surface. So a
        /// non-standable contact that rises above <see cref="k_SurfaceProbeMinRise"/> is decided by the
        /// surface past it (<see cref="TrySurfacePast"/>), which must be no higher than
        /// <see cref="StepOffset"/> above the feet. That surface's normal is returned.
        /// </remarks>
        bool IsStandableContact(ICollisionBackend backend, in RaycastHit contact, out Vector3 normal)
        {
            normal = contact.normal;
            if (IsStandable(normal))
                return true;

            if (normal.y <= k_SurfaceProbeMinRise)
                return false;

            if (!TrySurfacePast(backend, in contact, out var surface, out var point) || !IsStandable(surface))
                return false;

            if (point.y - m_State.Position.y > m_StepOffset)
                return false;

            normal = surface;
            return true;
        }

        /// <summary>
        /// Reads the surface just past a contact: a thin downward sweep starting a few centimetres
        /// beyond the contact point, away from the capsule's axis, and a few centimetres up.
        /// </summary>
        /// <remarks>
        /// Probes along the reversed horizontal contact normal, over the edge. A vertical normal is
        /// not probed. Starting inside a wall finds nothing, which callers treat as "trust the contact".
        /// </remarks>
        static bool TrySurfacePast(ICollisionBackend backend, in RaycastHit contact,
                                   out Vector3 normal, out Vector3 point)
        {
            normal = Vector3.zero;
            point = Vector3.zero;

            var outward = new Vector3(-contact.normal.x, 0f, -contact.normal.z);
            float length = outward.magnitude;
            if (length <= k_MinMoveDistance)
                return false;

            Vector3 origin = contact.point
                + outward * (k_SurfaceProbeInset / length)
                + Vector3.up * k_SurfaceProbeHeight;

            var probe = new CapsuleShape(origin, origin, k_SurfaceProbeRadius);
            if (!backend.Sweep(probe, Vector3.down, k_SurfaceProbeHeight * 2f, out var hit))
                return false;

            normal = hit.normal;
            point = hit.point;
            return true;
        }

        /// <summary>
        /// Pushes the capsule out of anything it already overlaps, along each collider's
        /// minimum translation, then one <see cref="SkinWidth"/> clear of it where there is room.
        /// </summary>
        /// <remarks>
        /// Moves position only; velocity is left for the next sweep to clip. Runs up to
        /// <see cref="k_MaxDepenetrationIterations"/> passes and stops when one separates nothing.
        /// </remarks>
        void ResolveOverlaps(ICollisionBackend backend)
        {
            Vector3 pushed = Vector3.zero;
            for (int pass = 0; pass < k_MaxDepenetrationIterations; pass++)
            {
                var shape = CurrentShape();
                int count = backend.Overlap(shape, m_OverlapBuffer);
                bool separated = false;

                for (int i = 0; i < count; i++)
                {
                    var other = m_OverlapBuffer[i];
                    m_OverlapBuffer[i] = null; // Don't hold collider references past the tick.

                    if (other == null)
                        continue;

                    if (!backend.Depenetrate(shape, m_State.Rotation, other, out var direction, out var distance))
                        continue;

                    m_State.Position += direction * distance;
                    pushed += direction * distance;
                    separated = true;

                    // Re-derive the shape: each push changes where the capsule is, so the next
                    // collider must be resolved against the corrected position.
                    shape = CurrentShape();
                }

                // Nothing overlapped, or nothing that overlapped produced a separation: another
                // pass would ask the same question of the same pose and get the same answer.
                if (!separated)
                {
                    StepClear(backend, pushed);
                    return;
                }
            }
        }

        /// <summary>
        /// Moves a capsule that was pushed out to touching one <see cref="SkinWidth"/> further the same
        /// way, when that spot is free.
        /// </summary>
        /// <remarks>
        /// Left touching, the next sweeps start in contact and the backend drops that collider's hit.
        /// On one mesh for a ramp and its railing that hides the ramp too, so the mover drops out of
        /// grounded and bounces against the railing. In a gap narrower than that, it stays touching
        /// rather than shaking between both sides.
        /// </remarks>
        void StepClear(ICollisionBackend backend, Vector3 pushed)
        {
            float length = pushed.magnitude;
            if (length <= k_MinMoveDistance)
                return;

            Vector3 before = m_State.Position;
            m_State.Position += pushed / length * m_SkinWidth;
            int count = backend.Overlap(CurrentShape(), m_OverlapBuffer);
            for (int i = 0; i < count; i++)
                m_OverlapBuffer[i] = null;

            if (count > 0)
                m_State.Position = before;
        }

        /// <summary>
        /// Looks for standable ground under the capsule and writes
        /// <see cref="MoveState.IsGrounded"/>, <see cref="MoveState.GroundNormal"/> and
        /// <see cref="MoveState.MovementMode"/> from what it finds.
        /// </summary>
        /// <remarks>
        /// Grounding is recomputed from scratch every tick. A face steeper than
        /// <see cref="SlopeLimit"/> leaves the mover airborne and its normal is not adopted. Velocity is
        /// not touched. <see cref="MoveState.MovementMode"/> is always overwritten here, after
        /// processors run, so a custom mode set by a processor does not survive the tick and custom
        /// modes don't work yet.
        /// </remarks>
        void ProbeGround(ICollisionBackend backend)
        {
            m_State.IsGrounded = false;
            m_State.GroundNormal = Vector3.up;
            m_State.MovementMode = MoveState.ModeAirborne;

            // Start a skin above the capsule: a sweep that begins touching the floor reports no hit.
            var shape = CurrentShape().Translated(Vector3.up * m_SkinWidth);
            if (!backend.Sweep(shape, Vector3.down, m_GroundProbeDistance + m_SkinWidth, out var hit))
                return;

            // Perched on a tread edge, the surface past the edge decides.
            if (!IsStandableContact(backend, in hit, out var groundNormal))
                return;

            m_State.IsGrounded = true;
            m_State.GroundNormal = groundNormal;
            m_State.MovementMode = MoveState.ModeGrounded;
        }

        /// <summary>
        /// Pulls a mover that was grounded last tick back down onto standable ground the probe
        /// could not reach, so stepping onto a downhill slope does not flicker airborne.
        /// </summary>
        /// <remarks>
        /// Only when the mover was grounded, the probe found nothing, and it is not moving up (so
        /// jumps survive). It parks one <see cref="SkinWidth"/> above the surface and leaves velocity
        /// alone. Limitation: it cannot tell a slope from a ledge, so a drop shorter than
        /// <see cref="GroundSnapDistance"/> is snapped down instead of falling.
        /// </remarks>
        void SnapToGround(ICollisionBackend backend, bool wasGrounded)
        {
            if (!wasGrounded || m_State.IsGrounded || m_GroundSnapDistance <= 0f)
                return;

            if (Vector3.Dot(m_State.Velocity, Vector3.up) > 0f)
                return;

            var shape = CurrentShape().Translated(Vector3.up * m_SkinWidth);
            if (!backend.Sweep(shape, Vector3.down, m_GroundSnapDistance + m_SkinWidth, out var hit))
                return;

            if (!IsStandableContact(backend, in hit, out var groundNormal))
                return;

            m_State.Position += Vector3.down * Mathf.Max(0f, hit.distance - 2f * m_SkinWidth);
            m_State.IsGrounded = true;
            m_State.GroundNormal = groundNormal;
            m_State.MovementMode = MoveState.ModeGrounded;
        }

        /// <summary>
        /// Writes <see cref="MoveState.TimeSinceGrounded"/>: zero on a tick that ends grounded,
        /// one more <paramref name="deltaTime"/> on a tick that does not.
        /// </summary>
        /// <remarks>
        /// Runs after <see cref="SnapToGround"/>, so a snapped mover counts as grounded.
        /// </remarks>
        void AccumulateAirTime(float deltaTime)
        {
            m_State.TimeSinceGrounded = m_State.IsGrounded
                ? 0f
                : m_State.TimeSinceGrounded + deltaTime;
        }

        /// <summary>
        /// Whether a face with this normal is one the mover may stand on.
        /// </summary>
        /// <remarks>
        /// Inclusive: a face exactly at <see cref="SlopeLimit"/> is standable. Every stage asks this
        /// one method, so the slope limit has a single definition.
        /// </remarks>
        bool IsStandable(Vector3 normal)
            => Vector3.Dot(normal, Vector3.up) >= Mathf.Cos(m_SlopeLimit * Mathf.Deg2Rad);

        /// <summary>
        /// Slows grounded downhill motion by the same <see cref="SlopeFactor(Vector3, Vector3)"/> as
        /// the climb, so up and down match, and lays it along the ground so the mover follows the
        /// slope down. Motion only; velocity is unchanged.
        /// </summary>
        Vector3 SlowedDownhill(Vector3 motion)
        {
            if (!m_State.IsGrounded || motion.y > 0f)
                return motion;

            Vector3 normal = m_State.GroundNormal;
            var across = new Vector3(motion.x, 0f, motion.z);
            float magnitude = across.magnitude;
            if (magnitude <= k_MinMoveDistance || normal.y <= 0f
                || Vector3.Dot(across, new Vector3(normal.x, 0f, normal.z)) <= 0f)
                return motion;

            float factor = m_SpeedAtSlopeLimit < 1f ? SlopeFactor(normal, across / magnitude) : 1f;
            float x = motion.x * factor;
            float z = motion.z * factor;

            // Follow the ground down. Flat motion floats off the slope and only the snap brings it
            // back, every other tick on a gentle walk, which reads as a stutter.
            float descent = -(normal.x * x + normal.z * z) / normal.y;
            return new Vector3(x, motion.y + descent, z);
        }

        /// <summary>
        /// The share of horizontal speed kept moving in horizontal <paramref name="direction"/> on a
        /// surface with <paramref name="normal"/>. Walking across a slope costs nothing.
        /// </summary>
        float SlopeFactor(Vector3 normal, Vector3 direction)
        {
            float along = direction.x * normal.x + direction.z * normal.z;
            return SlopeFactor(along * along);
        }

        /// <summary>
        /// 1 at <paramref name="sinSquared"/> 0, <see cref="SpeedAtSlopeLimit"/> at the slope limit's
        /// <c>sin²</c>, linear between, clamped to 0 to 1.
        /// </summary>
        float SlopeFactor(float sinSquared)
        {
            float limit = Mathf.Sin(m_SlopeLimit * Mathf.Deg2Rad);
            limit *= limit;
            if (limit <= 0f)
                return 1f;

            return Mathf.Clamp01(1f - (1f - m_SpeedAtSlopeLimit) * sinSquared / limit);
        }

        static float Horizontal(Vector3 vector) => new Vector2(vector.x, vector.z).magnitude;

        /// <summary>
        /// Removes the component of <paramref name="vector"/> that moves into the plane, and nothing else.
        /// </summary>
        static void ClipAgainstPlane(ref Vector3 vector, Vector3 normal)
        {
            float into = Vector3.Dot(vector, normal);
            if (into < 0f)
                vector -= normal * into;
        }

        /// <summary>
        /// Caps the vertical component of <paramref name="vector"/> so a face the mover may not
        /// stand on cannot lift it: the value may never rise above what it was before that
        /// face's clip, and may never become positive when it was not already.
        /// </summary>
        /// <remarks>
        /// Without this, sliding along a steep face converts horizontal speed into climb. A cap
        /// rather than flattening, so jumps along walls and slides down steep faces are kept. It
        /// only ever removes upward motion, so it cannot push through a ceiling.
        /// </remarks>
        /// <param name="vector">Motion or velocity, modified in place.</param>
        /// <param name="verticalBefore">The vector's Y component before this plane's clip.</param>
        static void LimitClimb(ref Vector3 vector, float verticalBefore)
        {
            float cap = Mathf.Max(verticalBefore, 0f);
            if (vector.y > cap)
                vector.y = cap;
        }

        /// <summary>
        /// The capsule at the current simulated position.
        /// </summary>
        /// <remarks>
        /// Always upright along world up; only upright capsules are supported.
        /// </remarks>
        CapsuleShape CurrentShape()
            => CapsuleShape.Upright(m_State.Position, Vector3.up, m_Height, m_Radius);

        /// <summary>
        /// Returns the backend, building the default one on first use so a mover without
        /// <c>Awake</c> still collides.
        /// </summary>
        /// <remarks>
        /// Asks for the <see cref="CapsuleCollider"/> specifically, so a second collider (such as a
        /// trigger) is never handed to the backend.
        /// </remarks>
        ICollisionBackend ResolveBackend()
        {
            // Built once and reused, so Simulate allocates nothing after the first tick.
            return m_Backend ??= new CapsuleSweepBackend(
                GetComponent<CapsuleCollider>(), m_CollisionMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Writes the sim capsule's dimensions onto the <see cref="CapsuleCollider"/>.
        /// </summary>
        /// <remarks>
        /// The collider always mirrors <see cref="Height"/> and <see cref="Radius"/>; edits to its own
        /// fields are overwritten on the next validate or <c>Awake</c>.
        /// </remarks>
        void SyncCollider()
        {
            if (!TryGetComponent<CapsuleCollider>(out var capsule))
                return;

            capsule.direction = 1; // Y axis: the capsule is always upright.
            capsule.height = m_Height;
            capsule.radius = m_Radius;

            // Feet at the transform origin, so the centre sits half a height up.
            capsule.center = new Vector3(0f, m_Height * 0.5f, 0f);
        }

        /// <summary>
        /// Forces the <see cref="Rigidbody"/> into the only configuration the sim can coexist
        /// with: kinematic, no gravity, no interpolation.
        /// </summary>
        /// <remarks>
        /// The body is only for presence and trigger events; the solver owns the transform, gravity
        /// comes from processors, and <see cref="MoveInterpolator"/> handles smoothing.
        /// </remarks>
        void ConfigureBody()
        {
            if (!TryGetComponent<Rigidbody>(out var body))
                return;

            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
        }

        void Awake()
        {
            m_State.Position = transform.position;
            m_State.Rotation = transform.rotation;
            m_State.GroundNormal = Vector3.up;
            m_State.MovementMode = MoveState.ModeAirborne;

            SyncCollider();
            ConfigureBody();

            // Build the backend now so the first tick does not pay for it.
            ResolveBackend();
        }

        void OnValidate()
        {
            // Keeps the collider matching the capsule fields while tuning in the Inspector.
            SyncCollider();
            ConfigureBody();
        }
    }
}
