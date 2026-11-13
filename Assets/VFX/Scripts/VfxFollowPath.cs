using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace Courtyard.VFX
{
    /// <summary>
    /// Moves the particles of every listed ParticleSystem along a shared path:
    /// spawn position -> waypoints (in order) -> one random destination.
    ///
    /// Paths are baked once (Burst parallel job) per ParticleSystem x destination,
    /// using the emitter centre as the start point. Each particle then uses:
    ///     position = baked(s) + W0(s) * (spawn - emitterCentre)
    /// where W0 is the start point's influence on the curve. Both curve types are
    /// linear in their control points, so this path shape is exact per particle.
    /// Only the arc-length spacing comes from the reference path (slight speed
    /// difference near the start for particles spawned far from the centre).
    ///
    /// Requirements / notes:
    /// - The Custom Data module must be DISABLED on each system. Custom1 stores
    ///   the spawn position (xyz, this component's local space) and a flag (w).
    /// - Start Speed is best set to 0 (spawn is back-solved from velocity * age).
    /// - Waypoints/destinations are in this GameObject's local space.
    /// - Built-in Noise / Velocity / Force modules get overwritten; disable them.
    /// - Builds bake once on enable. In the editor, edits and emitter moves re-bake.
    ///
    /// Followers (Play mode only): listed Transforms stay idle until
    /// StartFollowers() is called. Each then moves along the same path shape
    /// from its current position to one random destination, over its own
    /// Duration. Speed/noise curves use elapsed / Duration as "life".
    /// They stop at the destination. The start is stored on the first
    /// StartFollowers() call; later calls snap back to it and run again.
    /// </summary>
    [ExecuteAlways]
    public class VfxFollowPath : MonoBehaviour
    {
        public enum PathMode
        {
            CatmullRom = 0, // passes through every waypoint
            Bezier     = 1  // single bezier, waypoints act as control points
        }

        [System.Serializable]
        public class PathEmitter
        {
            public ParticleSystem System;

            [Tooltip("Scales the path noise for this system's particles. 0 = no noise, 1 = full noise.")]
            [Range(0f, 1f)] public float NoiseMultiplier = 1f;
        }
        
        [System.Serializable]
        public class PathFollower
        {
            public Transform Target;

            [Tooltip("Seconds to travel from the start position to the destination.")]
            [Min(0.01f)] public float Duration = 5f;

            [Tooltip("Scales the path noise for this object. 0 = no noise, 1 = same as particles.")]
            [Range(0f, 1f)] public float NoiseMultiplier = 1f;

            [Tooltip("Seconds between position updates. 0 = every frame. Raise it for objects that are expensive to move, " +
                     "such as Surface Cache GI emitters: every move rebuilds the GI ray tracing structure, which allocates on the compute backend.")]
            [Min(0f)] public float UpdateInterval = 0f;
        }
        
        const int k_CurveSamples = 64;    // speed / noise curve lookup size
        const int k_BakeSamples  = 256;   // baked samples per path, evenly spaced by length
        const int k_RawSamples   = 1024;  // raw samples used to measure path length
        
        [Tooltip("Particle systems whose particles follow the path.")]
        [SerializeField] List<PathEmitter> m_Emitters = new List<PathEmitter>();

        [SerializeField] PathMode m_PathMode = PathMode.CatmullRom;

        [Tooltip("Path points in order, local to this GameObject.")]
        [SerializeField] List<Vector3> m_Waypoints = new List<Vector3> { new Vector3(0f, 1f, 2f), new Vector3(0f, 2f, 4f) };

        [Tooltip("Each particle ends at one random destination. Empty = end at last waypoint.")]
        [SerializeField] List<Vector3> m_Destinations = new List<Vector3>();

        [Tooltip("X = normalized particle life, Y = normalized distance along the path.")]
        [SerializeField] AnimationCurve m_SpeedCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("X = normalized particle life, Y = noise offset in this GameObject's local units.")]
        [SerializeField] AnimationCurve m_NoiseAmplitude = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.5f, 0.25f), new Keyframe(1f, 0f));

        [Tooltip("X = normalized particle life, Y = noise frequency in cycles per second.")]
        [SerializeField] AnimationCurve m_NoiseFrequency = AnimationCurve.Constant(0f, 1f, 1f);

        [Tooltip("Particles per worker-thread batch.")]
        [Range(64, 256)]
        [SerializeField] int m_BatchSize = 128;

        [Tooltip("GameObjects moved along the path in Play mode, after StartFollowers() is called. Start = position at the first call.")]
        [SerializeField] List<PathFollower> m_Followers = new List<PathFollower>();

        [SerializeField] float m_GizmoSize = 0.1f;

        public PathMode Mode => m_PathMode;
        public List<PathEmitter> Emitters => m_Emitters;

        // Null-safe: an empty list slot or an entry without a system returns null.
        ParticleSystem GetSystem(int i) => m_Emitters[i] != null ? m_Emitters[i].System : null;
        public List<Vector3> Waypoints => m_Waypoints;
        public List<Vector3> Destinations => m_Destinations;
        public List<PathFollower> Followers => m_Followers;

        // Baked data (read-only for particle jobs).
        NativeArray<float3> m_WaypointsNative;
        NativeArray<float3> m_DestinationsNative;
        NativeArray<float3> m_EmitterCentres;     // per system, local space
        NativeArray<float4> m_BakedPaths;         // xyz = position, w = W0
        NativeArray<float>  m_SpeedLut;
        NativeArray<float>  m_AmplitudeLut;
        NativeArray<float>  m_FrequencyIntegralLut;

        JobHandle   m_BakeHandle;
        bool        m_BakeCompleted;
        JobHandle[] m_ParticleHandles = new JobHandle[0];
        int         m_DestinationSlots;
        bool        m_Valid;
        bool        m_Dirty;

        readonly List<VfxFollowPathRelay> m_Relays = new List<VfxFollowPathRelay>();
        readonly List<VfxFollowPathRelay> m_RelayScratch = new List<VfxFollowPathRelay>();
        readonly List<Vector3> m_BakedCentres = new List<Vector3>();   // managed copy, editor change check
        readonly List<Vector4> m_CustomDataScratch = new List<Vector4>();

        // Follower runtime state (Play mode only, never serialized).
        // Kept across re-bakes so inspector edits in Play mode don't restart objects.
        struct FollowerState
        {
            public Transform Target;
            public float3    StartLocal;   // this GameObject's local space
            public uint      Seed;         // picks destination + noise pattern
            public float     Elapsed;
            public float     SinceUpdate;  // seconds since the Transform was last written
            public bool      Finished;
        }

        readonly List<FollowerState> m_FollowerStates = new List<FollowerState>();
        NativeArray<float3> m_FollowerStarts;
        NativeArray<float4> m_FollowerPaths;      // same layout as m_BakedPaths
        JobHandle           m_FollowerBakeHandle;
        bool                m_FollowerBakeCompleted;
        bool                m_FollowersStarted;   // set by StartFollowers()
        BakePathsJob        m_BakeJobTemplate;     // shared bake inputs from the last Rebuild

        // ---------------------------------------------------------------------
        //  Lifecycle
        // ---------------------------------------------------------------------

        void OnEnable()
        {
            Rebuild();
        }

        void OnDisable()
        {
            CompleteAllJobs();
            ReleaseRelays();
            DisposeNative();
            m_Valid = false;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            m_Dirty = true; // rebuilt in Update; OnValidate is not a safe place to allocate
        }

        bool EmittersMoved()
        {
            if (m_BakedCentres.Count != m_Emitters.Count)
                return true;
            for (int i = 0; i < m_Emitters.Count; i++)
            {
                var ps = GetSystem(i);
                if (ps == null) continue;
                if ((transform.InverseTransformPoint(ps.transform.position) - m_BakedCentres[i]).sqrMagnitude > 1e-8f)
                    return true;
            }
            return false;
        }
#endif

        void Update()
        {
#if UNITY_EDITOR
            // Editor only: a build never re-bakes after OnEnable.
            if (!m_Dirty && EmittersMoved())
                m_Dirty = true;
            if (m_Dirty && isActiveAndEnabled)
                Rebuild();
#endif
            if (Application.isPlaying)
                UpdateFollowers();
        }

        /// Request a re-bake (editor tools call this after moving points).
        public void MarkDirty() => m_Dirty = true;

        /// Start moving the followers to a new random destination. Play mode only.
        /// First call: each follower's current position is stored as its start.
        /// Later calls: followers snap back to that stored start and run again.
        public void StartFollowers()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[VfxFollowPath] StartFollowers() only works in Play mode.", this);
                return;
            }

            if (m_FollowersStarted && m_FollowerStates.Count > 0)
            {
                ReplayFromStoredStart();    // paths were baked from these starts, no re-bake needed
                return;
            }

            m_FollowerStates.Clear();       // capture starts (current positions) and new seeds
            m_FollowersStarted = true;
            BakeFollowers();                // if disabled, the next OnEnable -> Rebuild bakes instead
        }

        // Snap every follower back to its stored start and pick a new random destination.
        void ReplayFromStoredStart()
        {
            for (int i = 0; i < m_FollowerStates.Count; i++)
            {
                var state = m_FollowerStates[i];
                if (state.Target != null)
                    state.Target.position = transform.TransformPoint(state.StartLocal);
                state.Seed        = NewSeed();
                state.Elapsed     = 0f;
                state.SinceUpdate = 0f;
                state.Finished    = false;
                m_FollowerStates[i] = state;
            }
        }

        // ---------------------------------------------------------------------
        //  Build / bake
        // ---------------------------------------------------------------------

        void Rebuild()
        {
            m_Dirty = false;
            CompleteAllJobs();
            ReleaseRelays();
            DisposeNative();
            m_Valid = false;

            int systemCount      = m_Emitters.Count;
            int waypointCount    = m_Waypoints.Count;
            int destinationCount = m_Destinations.Count;
            bool hasFollowers    = Application.isPlaying && m_Followers.Count > 0;

            m_BakedCentres.Clear();
            for (int i = 0; i < systemCount; i++)
            {
                var ps = GetSystem(i);
                m_BakedCentres.Add(ps != null ? transform.InverseTransformPoint(ps.transform.position) : Vector3.zero);
            }

            if ((systemCount == 0 && !hasFollowers) || (waypointCount == 0 && destinationCount == 0))
                return;

            m_DestinationSlots = math.max(destinationCount, 1);
            int pathCount = systemCount * m_DestinationSlots;

            m_WaypointsNative      = new NativeArray<float3>(math.max(waypointCount, 1), Allocator.Persistent);
            m_DestinationsNative   = new NativeArray<float3>(math.max(destinationCount, 1), Allocator.Persistent);
            m_EmitterCentres       = new NativeArray<float3>(math.max(systemCount, 1), Allocator.Persistent);
            m_BakedPaths           = new NativeArray<float4>(math.max(pathCount, 1) * k_BakeSamples, Allocator.Persistent);
            m_SpeedLut             = new NativeArray<float>(k_CurveSamples, Allocator.Persistent);
            m_AmplitudeLut         = new NativeArray<float>(k_CurveSamples, Allocator.Persistent);
            m_FrequencyIntegralLut = new NativeArray<float>(k_CurveSamples, Allocator.Persistent);

            for (int i = 0; i < waypointCount; i++)    m_WaypointsNative[i]    = m_Waypoints[i];
            for (int i = 0; i < destinationCount; i++) m_DestinationsNative[i] = m_Destinations[i];
            for (int i = 0; i < systemCount; i++)      m_EmitterCentres[i]     = m_BakedCentres[i];

            // AnimationCurve is managed, so curves are sampled here (3 x 64 evaluations).
            BakeCurve(m_SpeedCurve,     m_SpeedLut);
            BakeCurve(m_NoiseAmplitude, m_AmplitudeLut);
            BakeIntegral(m_NoiseFrequency, m_FrequencyIntegralLut);

            var bakeJob = new BakePathsJob
            {
                waypoints        = m_WaypointsNative,
                waypointCount    = waypointCount,
                destinations     = m_DestinationsNative,
                hasDestination   = destinationCount > 0,
                destinationSlots = m_DestinationSlots,
                emitterCentres   = m_EmitterCentres,
                pathMode         = (int)m_PathMode,
                baked            = m_BakedPaths
            };
            if (pathCount > 0)
            {
                m_BakeHandle    = bakeJob.Schedule(pathCount, 1);
                m_BakeCompleted = false;
            }
            else
            {
                m_BakeHandle    = default;
                m_BakeCompleted = true;
            }

            m_BakeJobTemplate = bakeJob;

            m_ParticleHandles = new JobHandle[systemCount];
            CreateRelays();
            m_Valid = true;

            if (m_FollowersStarted && hasFollowers)
                BakeFollowers();
        }

        // Bakes follower paths only (particle paths are untouched). Uses the
        // same bake job with each follower's start in place of an emitter centre.
        void BakeFollowers()
        {
            m_FollowerBakeHandle.Complete();
            if (m_FollowerStarts.IsCreated) m_FollowerStarts.Dispose();
            if (m_FollowerPaths.IsCreated)  m_FollowerPaths.Dispose();

            int followerCount = m_Followers.Count;
            if (!m_Valid || followerCount == 0)
                return;

            SyncFollowerStates();
            m_FollowerStarts = new NativeArray<float3>(followerCount, Allocator.Persistent);
            for (int i = 0; i < followerCount; i++) m_FollowerStarts[i] = m_FollowerStates[i].StartLocal;
            m_FollowerPaths = new NativeArray<float4>(followerCount * m_DestinationSlots * k_BakeSamples, Allocator.Persistent);

            var followerBakeJob = m_BakeJobTemplate;
            followerBakeJob.emitterCentres = m_FollowerStarts;
            followerBakeJob.baked          = m_FollowerPaths;
            m_FollowerBakeHandle    = followerBakeJob.Schedule(followerCount * m_DestinationSlots, 1);
            m_FollowerBakeCompleted = false;
        }

        void CreateRelays()
        {
            for (int i = 0; i < m_Emitters.Count; i++)
            {
                var ps = GetSystem(i);
                if (ps == null) continue;

                if (ps.customData.enabled)
                    Debug.LogWarning($"[VfxFollowPath] '{ps.name}': disable the Custom Data module. " +
                                     "It overwrites Custom1, which stores spawn positions.", ps);

                // Touch the Custom1 stream so it is in use for the job (no data change).
                ps.GetCustomParticleData(m_CustomDataScratch, ParticleSystemCustomData.Custom1);
                ps.SetCustomParticleData(m_CustomDataScratch, ParticleSystemCustomData.Custom1);

                var relay = FindFreeRelay(ps.gameObject);
                if (relay == null)
                {
                    relay = ps.gameObject.AddComponent<VfxFollowPathRelay>();
                    relay.hideFlags = HideFlags.HideAndDontSave;
                }
                relay.Owner = this;
                relay.Index = i;
                m_Relays.Add(relay);
            }
        }

        // Reuse an idle relay (no owner, e.g. after a disable or script reload)
        // instead of adding another one. A relay claimed earlier in this
        // rebuild already has Owner == this, so a system listed twice gets two.
        VfxFollowPathRelay FindFreeRelay(GameObject go)
        {
            go.GetComponents(m_RelayScratch);
            foreach (var relay in m_RelayScratch)
                if (relay.Owner == null)
                    return relay;
            return null;
        }

        // Relays are never destroyed here: Unity forbids destroying components
        // while GameObjects are being (de)activated, e.g. during a prefab save.
        // An idle relay does nothing, is never saved (HideAndDontSave) and is
        // reused by the next Rebuild.
        void ReleaseRelays()
        {
            foreach (var relay in m_Relays)
                if (relay != null && relay.Owner == this)
                    relay.Owner = null;
            m_Relays.Clear();
        }

        void CompleteAllJobs()
        {
            m_BakeHandle.Complete();
            m_FollowerBakeHandle.Complete();
            for (int i = 0; i < m_ParticleHandles.Length; i++)
                m_ParticleHandles[i].Complete();
        }

        void DisposeNative()
        {
            if (m_WaypointsNative.IsCreated)      m_WaypointsNative.Dispose();
            if (m_DestinationsNative.IsCreated)   m_DestinationsNative.Dispose();
            if (m_EmitterCentres.IsCreated)       m_EmitterCentres.Dispose();
            if (m_BakedPaths.IsCreated)           m_BakedPaths.Dispose();
            if (m_SpeedLut.IsCreated)             m_SpeedLut.Dispose();
            if (m_AmplitudeLut.IsCreated)         m_AmplitudeLut.Dispose();
            if (m_FrequencyIntegralLut.IsCreated) m_FrequencyIntegralLut.Dispose();
            if (m_FollowerStarts.IsCreated)       m_FollowerStarts.Dispose();
            if (m_FollowerPaths.IsCreated)        m_FollowerPaths.Dispose();
        }

        static void BakeCurve(AnimationCurve curve, NativeArray<float> lut)
        {
            float step = 1f / (lut.Length - 1);
            for (int i = 0; i < lut.Length; i++)
                lut[i] = curve != null ? curve.Evaluate(i * step) : 0f;
        }

        // Running integral of the curve over normalized life (trapezoid rule).
        // Multiplied by lifetime in the job this gives a continuous noise phase.
        static void BakeIntegral(AnimationCurve curve, NativeArray<float> lut)
        {
            float step = 1f / (lut.Length - 1);
            float prev = curve != null ? curve.Evaluate(0f) : 0f;
            lut[0] = 0f;
            for (int i = 1; i < lut.Length; i++)
            {
                float cur = curve != null ? curve.Evaluate(i * step) : 0f;
                lut[i] = lut[i - 1] + (prev + cur) * 0.5f * step;
                prev = cur;
            }
        }

        // ---------------------------------------------------------------------
        //  Followers (GameObjects, Play mode only)
        // ---------------------------------------------------------------------

        // Match runtime state to the list. An entry whose Target is unchanged
        // keeps its start, seed and progress; anything else starts fresh.
        void SyncFollowerStates()
        {
            for (int i = 0; i < m_Followers.Count; i++)
            {
                Transform target = m_Followers[i] != null ? m_Followers[i].Target : null;
                if (i < m_FollowerStates.Count && m_FollowerStates[i].Target == target)
                    continue;

                var state = new FollowerState
                {
                    Target     = target,
                    StartLocal = target != null ? (float3)transform.InverseTransformPoint(target.position) : float3.zero,
                    Seed       = NewSeed(),
                    Elapsed    = 0f,
                    Finished   = false
                };
                if (i < m_FollowerStates.Count) m_FollowerStates[i] = state;
                else                            m_FollowerStates.Add(state);
            }
            if (m_FollowerStates.Count > m_Followers.Count)
                m_FollowerStates.RemoveRange(m_Followers.Count, m_FollowerStates.Count - m_Followers.Count);
        }

        static uint NewSeed() => (uint)UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        void UpdateFollowers()
        {
            if (!m_Valid || !m_FollowerPaths.IsCreated)
                return;

            if (!m_FollowerBakeCompleted)
            {
                m_FollowerBakeHandle.Complete();
                m_FollowerBakeCompleted = true;
            }

            float dt = Time.deltaTime;
            int count = math.min(math.min(m_Followers.Count, m_FollowerStates.Count), m_FollowerStarts.Length);
            for (int i = 0; i < count; i++)
            {
                var settings = m_Followers[i];
                var state    = m_FollowerStates[i];
                if (settings == null || state.Target == null || state.Finished)
                    continue;

                float duration = math.max(settings.Duration, 0.01f);
                state.Elapsed  = math.min(state.Elapsed + dt, duration);
                state.SinceUpdate += dt;
                state.Finished = state.Elapsed >= duration;

                // Progress always advances; the Transform is only written on the interval,
                // and once more on the last frame so the follower lands on its destination.
                if (state.SinceUpdate >= settings.UpdateInterval || state.Finished)
                {
                    float t = state.Elapsed / duration;
                    int pathBase = (i * m_DestinationSlots + (int)(state.Seed % (uint)m_DestinationSlots)) * k_BakeSamples;
                    float3 local = PathSampler.PositionAt(m_FollowerPaths, pathBase, float3.zero, t, duration, state.Seed,
                                                          settings.NoiseMultiplier, m_SpeedLut, m_AmplitudeLut, m_FrequencyIntegralLut);
                    state.Target.position = transform.TransformPoint(local);
                    state.SinceUpdate = 0f;
                }

                m_FollowerStates[i] = state;
            }
        }

        // ---------------------------------------------------------------------
        //  Per-frame scheduling (called by each system's relay)
        // ---------------------------------------------------------------------

        internal void ScheduleFor(int index)
        {
            if (!m_Valid || index < 0 || index >= m_Emitters.Count)
                return;
            var ps = GetSystem(index);
            if (ps == null)
                return;

            if (!m_BakeCompleted)
            {
                // Scheduled in OnEnable; finished long before the first particle update.
                m_BakeHandle.Complete();
                m_BakeCompleted = true;
            }

            Matrix4x4 localToSim = GetLocalToSimulationMatrix(ps);
            var job = new FollowPathJob
            {
                baked                = m_BakedPaths,
                systemBase           = index * m_DestinationSlots * k_BakeSamples,
                destinationSlots     = (uint)m_DestinationSlots,
                emitterCentre        = m_EmitterCentres[index],
                localToSim           = ToFloat4x4(localToSim),
                simToLocal           = ToFloat4x4(localToSim.inverse),
                speedLut             = m_SpeedLut,
                amplitudeLut         = m_AmplitudeLut,
                frequencyIntegralLut = m_FrequencyIntegralLut,
                noiseMultiplier      = m_Emitters[index].NoiseMultiplier   // entry is non-null: GetSystem returned a system
            };
            m_ParticleHandles[index] = job.ScheduleBatch(ps, m_BatchSize);
        }

        // Maps a point in this GameObject's local space into the system's
        // simulation space (the space job positions are in).
        Matrix4x4 GetLocalToSimulationMatrix(ParticleSystem ps)
        {
            var main = ps.main;
            switch (main.simulationSpace)
            {
                case ParticleSystemSimulationSpace.World:
                    return transform.localToWorldMatrix;
                case ParticleSystemSimulationSpace.Custom:
                    return main.customSimulationSpace != null
                        ? main.customSimulationSpace.worldToLocalMatrix * transform.localToWorldMatrix
                        : transform.localToWorldMatrix;
                default: // Local: relative to the particle system's own transform
                    return ps.transform.worldToLocalMatrix * transform.localToWorldMatrix;
            }
        }

        static float4x4 ToFloat4x4(Matrix4x4 m)
        {
            Vector4 c0 = m.GetColumn(0), c1 = m.GetColumn(1), c2 = m.GetColumn(2), c3 = m.GetColumn(3);
            return new float4x4(
                new float4(c0.x, c0.y, c0.z, c0.w),
                new float4(c1.x, c1.y, c1.z, c1.w),
                new float4(c2.x, c2.y, c2.z, c2.w),
                new float4(c3.x, c3.y, c3.z, c3.w));
        }

        // ---------------------------------------------------------------------
        //  Gizmos
        // ---------------------------------------------------------------------

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f);
            foreach (var p in m_Waypoints)
                Gizmos.DrawSphere(transform.TransformPoint(p), m_GizmoSize);

            Gizmos.color = new Color(1f, 0.45f, 0.1f);
            foreach (var p in m_Destinations)
                Gizmos.DrawSphere(transform.TransformPoint(p), m_GizmoSize);

            int waypointCount = m_Waypoints.Count;
            if (waypointCount == 0 && m_Destinations.Count == 0)
                return;

            using (var worldWaypoints = new NativeArray<float3>(math.max(waypointCount, 1), Allocator.Temp))
            {
                var wps = worldWaypoints;
                for (int i = 0; i < waypointCount; i++)
                    wps[i] = transform.TransformPoint(m_Waypoints[i]);

                Gizmos.color = Color.yellow;
                bool anySystem = false;
                for (int i = 0; i < m_Emitters.Count; i++)
                {
                    var ps = GetSystem(i);
                    if (ps == null) continue;
                    anySystem = true;
                    DrawPathsFrom(ps.transform.position, wps, waypointCount);
                }
                if (!anySystem)
                    DrawPathsFrom(transform.position, wps, waypointCount);

                Gizmos.color = new Color(0.3f, 1f, 0.4f);
                foreach (var follower in m_Followers)
                    if (follower != null && follower.Target != null)
                        DrawPathsFrom(follower.Target.position, wps, waypointCount);
            }
        }

        void DrawPathsFrom(Vector3 start, NativeArray<float3> worldWaypoints, int waypointCount)
        {
            if (m_Destinations.Count == 0)
            {
                DrawPath(new PathDef(start, worldWaypoints, waypointCount, float3.zero, false, (int)m_PathMode));
                return;
            }
            foreach (var d in m_Destinations)
                DrawPath(new PathDef(start, worldWaypoints, waypointCount,
                                     transform.TransformPoint(d), true, (int)m_PathMode));
        }

        static void DrawPath(PathDef path)
        {
            const int steps = 64;
            Vector3 prev = path.Evaluate(0f);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 cur = path.Evaluate(i / (float)steps);
                Gizmos.DrawLine(prev, cur);
                prev = cur;
            }
        }

        // ---------------------------------------------------------------------
        //  Path math (used by the bake job and gizmos)
        // ---------------------------------------------------------------------

        /// Control points: [0] = start, [1..W] = waypoints, [W+1] = destination (optional).
        struct PathDef
        {
            readonly float3 m_Start;
            readonly NativeArray<float3> m_Waypoints;
            readonly int m_WaypointCount;
            readonly float3 m_Destination;
            readonly int m_Mode;
            readonly int m_Count;

            public PathDef(float3 start, NativeArray<float3> waypoints, int waypointCount,
                           float3 destination, bool hasDestination, int mode)
            {
                m_Start         = start;
                m_Waypoints     = waypoints;
                m_WaypointCount = waypointCount;
                m_Destination   = destination;
                m_Mode          = mode;
                m_Count         = 1 + waypointCount + (hasDestination ? 1 : 0);
            }

            float3 Point(int k)
            {
                if (k <= 0) return m_Start;
                if (k <= m_WaypointCount) return m_Waypoints[k - 1];
                return m_Destination;
            }

            // Catmull-Rom needs one extra point before the start and after the end.
            float3 PointMirrored(int k)
            {
                if (k < 0)        return 2f * Point(0) - Point(1);
                if (k >= m_Count) return 2f * Point(m_Count - 1) - Point(m_Count - 2);
                return Point(k);
            }

            // How much control point 0 contributes to slot k. Written out without
            // recursion (m_Count >= 2 here):
            //   slot -1      = 2*P0 - P1           -> weight 2
            //   slot m_Count = 2*Plast - Plast-1   -> weight -1 only when Plast-1 is P0
            float StartWeightOfSlot(int k)
            {
                if (k < 0)        return 2f;
                if (k >= m_Count) return m_Count == 2 ? -1f : 0f;
                return k == 0 ? 1f : 0f;
            }

            static float4 CatmullRomCoefficients(float f)
            {
                float f2 = f * f;
                float f3 = f2 * f;
                return 0.5f * new float4(
                    -f + 2f * f2 - f3,
                    2f - 5f * f2 + 3f * f3,
                    f + 4f * f2 - 3f * f3,
                    -f2 + f3);
            }

            void CatmullRomSegment(float u, out int i, out float f)
            {
                int segments = m_Count - 1;
                float x = u * segments;
                i = math.min((int)math.floor(x), segments - 1);
                f = x - i;
            }

            /// Curve parameter u in [0,1] (not evenly spaced by length).
            public float3 Evaluate(float u)
            {
                if (m_Count < 2) return m_Start;
                u = math.saturate(u);
                if (m_Mode == (int)PathMode.Bezier)
                    return EvaluateBezier(u);

                CatmullRomSegment(u, out int i, out float f);
                float4 c = CatmullRomCoefficients(f);
                return c.x * PointMirrored(i - 1) + c.y * Point(i) + c.z * Point(i + 1) + c.w * PointMirrored(i + 2);
            }

            /// Influence of the start point on the curve at u (W0).
            public float StartWeight(float u)
            {
                if (m_Count < 2) return 1f;
                u = math.saturate(u);
                if (m_Mode == (int)PathMode.Bezier)
                {
                    float b = 1f - u, w = 1f;
                    for (int k = 0; k < m_Count - 1; k++) w *= b; // (1-u)^n
                    return w;
                }

                CatmullRomSegment(u, out int i, out float f);
                float4 c = CatmullRomCoefficients(f);
                return c.x * StartWeightOfSlot(i - 1) + c.y * StartWeightOfSlot(i)
                     + c.z * StartWeightOfSlot(i + 1) + c.w * StartWeightOfSlot(i + 2);
            }

            // Bernstein form, O(n). Evaluated from the nearer end (a <= 0.5)
            // so the starting coefficient b^n never underflows.
            float3 EvaluateBezier(float u)
            {
                int n = m_Count - 1;
                bool flip = u > 0.5f;
                float a = flip ? 1f - u : u;
                float b = 1f - a;

                float coeff = 1f;
                for (int k = 0; k < n; k++) coeff *= b;
                float ratio = a / b;

                float3 sum = float3.zero;
                for (int k = 0; k <= n; k++)
                {
                    sum += coeff * Point(flip ? n - k : k);
                    coeff *= (n - k) / (float)(k + 1) * ratio;
                }
                return sum;
            }
        }

        // ---------------------------------------------------------------------
        //  Jobs
        // ---------------------------------------------------------------------

        /// One index per (particle system x destination) path. Writes k_BakeSamples
        /// entries evenly spaced by length: xyz = position, w = start weight.
        [BurstCompile]
        struct BakePathsJob : IJobParallelFor
        {
            [ReadOnly] public NativeArray<float3> waypoints;
            public int waypointCount;
            [ReadOnly] public NativeArray<float3> destinations;
            public bool hasDestination;
            public int destinationSlots;
            [ReadOnly] public NativeArray<float3> emitterCentres;
            public int pathMode;

            // Each index writes its own block of k_BakeSamples entries.
            [NativeDisableParallelForRestriction] public NativeArray<float4> baked;

            public void Execute(int pathIndex)
            {
                int system      = pathIndex / destinationSlots;
                int destination = pathIndex % destinationSlots;
                float3 dest = hasDestination ? destinations[destination] : float3.zero;
                var path = new PathDef(emitterCentres[system], waypoints, waypointCount, dest, hasDestination, pathMode);

                // Cumulative length at k_RawSamples + 1 evenly spaced u values.
                var cumulative = new NativeArray<float>(k_RawSamples + 1, Allocator.Temp);
                float3 prev = path.Evaluate(0f);
                cumulative[0] = 0f;
                for (int j = 1; j <= k_RawSamples; j++)
                {
                    float3 cur = path.Evaluate(j / (float)k_RawSamples);
                    cumulative[j] = cumulative[j - 1] + math.distance(prev, cur);
                    prev = cur;
                }
                float total = cumulative[k_RawSamples];

                int outBase = pathIndex * k_BakeSamples;
                int seg = 0;
                for (int k = 0; k < k_BakeSamples; k++)
                {
                    float s = k / (float)(k_BakeSamples - 1);
                    float u;
                    if (total <= 1e-6f)
                    {
                        u = s;
                    }
                    else
                    {
                        float target = s * total;
                        while (seg < k_RawSamples - 1 && cumulative[seg + 1] < target)
                            seg++;
                        float segLength = cumulative[seg + 1] - cumulative[seg];
                        float local = segLength > 1e-9f ? math.saturate((target - cumulative[seg]) / segLength) : 0f;
                        u = (seg + local) / k_RawSamples;
                    }
                    baked[outBase + k] = new float4(path.Evaluate(u), path.StartWeight(u));
                }
                cumulative.Dispose();
            }
        }

        [BurstCompile]
        struct FollowPathJob : IJobParticleSystemParallelForBatch
        {
            [ReadOnly] public NativeArray<float4> baked;
            public int systemBase;
            public uint destinationSlots;
            public float3 emitterCentre;
            public float4x4 localToSim;
            public float4x4 simToLocal;
            [ReadOnly] public NativeArray<float> speedLut;
            [ReadOnly] public NativeArray<float> amplitudeLut;
            [ReadOnly] public NativeArray<float> frequencyIntegralLut;
            public float noiseMultiplier;   // per system, scales the noise amplitude

            const float k_VelocityEpsilon = 0.002f; // normalized life step for velocity

            public void Execute(ParticleSystemJobData particles, int startIndex, int count)
            {
                var posX = particles.positions.x;
                var posY = particles.positions.y;
                var posZ = particles.positions.z;
                var velX = particles.velocities.x;
                var velY = particles.velocities.y;
                var velZ = particles.velocities.z;
                var c1X  = particles.customData1.x;
                var c1Y  = particles.customData1.y;
                var c1Z  = particles.customData1.z;
                var c1W  = particles.customData1.w;
                var alivePercent = particles.aliveTimePercent;
                var invLifetimes = particles.inverseStartLifetimes;
                var seeds        = particles.randomSeeds;

                int end = startIndex + count;
                for (int i = startIndex; i < end; i++)
                {
                    float lifetime = 1f / math.max(invLifetimes[i], 1e-6f);
                    float t = math.saturate(alivePercent[i] * 0.01f);

                    // First time we see this particle: store its spawn (local space).
                    // New particles start with custom data = 0, so w == 0 means "new".
                    float3 spawnLocal;
                    if (c1W[i] == 0f)
                    {
                        float3 pos = new float3(posX[i], posY[i], posZ[i]);
                        float3 vel = new float3(velX[i], velY[i], velZ[i]);
                        float3 spawnSim = pos - vel * (t * lifetime); // undo built-in movement since birth
                        spawnLocal = math.mul(simToLocal, new float4(spawnSim, 1f)).xyz;
                        c1X[i] = spawnLocal.x;
                        c1Y[i] = spawnLocal.y;
                        c1Z[i] = spawnLocal.z;
                        c1W[i] = 1f;
                    }
                    else
                    {
                        spawnLocal = new float3(c1X[i], c1Y[i], c1Z[i]);
                    }

                    uint seed = seeds[i];
                    int pathBase = systemBase + (int)(seed % destinationSlots) * k_BakeSamples;
                    float3 offset = spawnLocal - emitterCentre;

                    // Velocity by finite difference over normalized life.
                    float tA = t - k_VelocityEpsilon;
                    float tB = t;
                    if (tA < 0f) { tA = t; tB = t + k_VelocityEpsilon; }

                    float3 pA = math.mul(localToSim, new float4(PositionAt(pathBase, offset, tA, lifetime, seed), 1f)).xyz;
                    float3 pB = math.mul(localToSim, new float4(PositionAt(pathBase, offset, tB, lifetime, seed), 1f)).xyz;
                    float3 p  = (tB == t) ? pB : pA;
                    float3 v  = (pB - pA) / (k_VelocityEpsilon * lifetime);

                    posX[i] = p.x; posY[i] = p.y; posZ[i] = p.z;
                    velX[i] = v.x; velY[i] = v.y; velZ[i] = v.z;
                }
            }

            // Local-space position at normalized life t, noise scaled per system.
            float3 PositionAt(int pathBase, float3 offset, float t, float lifetime, uint seed)
            {
                return PathSampler.PositionAt(baked, pathBase, offset, t, lifetime, seed, noiseMultiplier,
                                              speedLut, amplitudeLut, frequencyIntegralLut);
            }
        }

        /// Path sampling shared by the particle job (Burst) and followers (main thread).
        static class PathSampler
        {
            // Local-space position at normalized life t on one baked path.
            public static float3 PositionAt(NativeArray<float4> baked, int pathBase, float3 offset, float t,
                                            float lifetime, uint seed, float noiseMultiplier,
                                            NativeArray<float> speedLut, NativeArray<float> amplitudeLut,
                                            NativeArray<float> frequencyIntegralLut)
            {
                float s = math.saturate(SampleLut(speedLut, t));
                float f = s * (k_BakeSamples - 1);
                int a = (int)f;
                int b = math.min(a + 1, k_BakeSamples - 1);
                float4 sample = math.lerp(baked[pathBase + a], baked[pathBase + b], f - a);
                float3 p = sample.xyz + sample.w * offset;

                float amplitude = SampleLut(amplitudeLut, t) * noiseMultiplier;
                if (amplitude != 0f)
                {
                    float phase = SampleLut(frequencyIntegralLut, t) * lifetime;
                    p += amplitude * new float3(
                        ValueNoise(phase, seed, 0u),
                        ValueNoise(phase, seed, 1u),
                        ValueNoise(phase, seed, 2u));
                }
                return p;
            }

            public static float SampleLut(NativeArray<float> lut, float x)
            {
                float f = math.saturate(x) * (lut.Length - 1);
                int i = (int)f;
                int j = math.min(i + 1, lut.Length - 1);
                return math.lerp(lut[i], lut[j], f - i);
            }

            // Smooth 1D value noise in [-1, 1]. Each particle/axis gets its own
            // random offset so particles do not wobble in sync.
            static float ValueNoise(float x, uint seed, uint axis)
            {
                x += (Hash(seed ^ Hash(axis + 0x9E3779B9u)) & 1023u) * 0.618f;
                float cell = math.floor(x);
                float f = x - cell;
                uint c = (uint)(int)cell;
                float a = Hash01(c,      seed, axis);
                float b = Hash01(c + 1u, seed, axis);
                float u = f * f * (3f - 2f * f);
                return math.lerp(a, b, u) * 2f - 1f;
            }

            static float Hash01(uint cell, uint seed, uint axis)
            {
                uint h = Hash(cell ^ Hash(seed ^ Hash(axis + 0x9E3779B9u)));
                return (h & 0x00FFFFFFu) * (1f / 0x00FFFFFF);
            }

            // Wang integer hash.
            static uint Hash(uint x)
            {
                x = (x ^ 61u) ^ (x >> 16);
                x *= 9u;
                x ^= x >> 4;
                x *= 0x27d4eb2du;
                x ^= x >> 15;
                return x;
            }
        }
    }
}
