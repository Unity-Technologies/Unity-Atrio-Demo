using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The production <see cref="ICollisionBackend"/>: capsule casts and depenetration over
    /// Unity's physics module.
    /// </summary>
    /// <remarks>
    /// All configuration is supplied at construction; nothing carries over between calls.
    /// <see cref="Depenetrate"/> needs an enabled <see cref="CapsuleCollider"/> on the mover;
    /// without one it reports no overlap, so the mover still collides but cannot recover from an overlap.
    /// </remarks>
    internal sealed class CapsuleSweepBackend : ICollisionBackend
    {
        /// <summary>Default capacity of the self-exclusion scratch buffer.</summary>
        internal const int DefaultSweepBufferCapacity = 16;

        readonly CapsuleCollider m_Self;
        readonly int m_LayerMask;
        readonly QueryTriggerInteraction m_TriggerInteraction;

        // Scratch for self-exclusion: CapsuleCast cannot skip a collider. Sized once; the sim must not allocate.
        readonly RaycastHit[] m_SweepBuffer;

        /// <summary>Creates a backend bound to one mover's query configuration.</summary>
        /// <param name="self">
        /// The mover's own capsule collider, excluded from every query and used for depenetration.
        /// May be <c>null</c>, which disables <see cref="Depenetrate"/>.
        /// </param>
        /// <param name="layerMask">Layers the mover collides with.</param>
        /// <param name="triggerInteraction">Whether trigger colliders block the mover.</param>
        /// <param name="sweepBufferCapacity">
        /// Size of the self-exclusion buffer. A sweep touching more colliders may miss the nearest one.
        /// </param>
        public CapsuleSweepBackend(CapsuleCollider self, int layerMask,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore,
            int sweepBufferCapacity = DefaultSweepBufferCapacity)
        {
            m_Self = self;
            m_LayerMask = layerMask;
            m_TriggerInteraction = triggerInteraction;
            m_SweepBuffer = new RaycastHit[Mathf.Max(1, sweepBufferCapacity)];
        }

        /// <inheritdoc/>
        public bool Sweep(in CapsuleShape shape, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;

            if (distance <= 0f || direction.sqrMagnitude <= 0f)
                return false;

            int count = Physics.CapsuleCastNonAlloc(shape.Bottom, shape.Top, shape.Radius,
                direction, m_SweepBuffer, distance, m_LayerMask, m_TriggerInteraction);

            bool found = false;
            float nearest = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                var candidate = m_SweepBuffer[i];

                if (candidate.collider == m_Self)
                    continue;

                // Start overlap: the normal is unreliable, and Depenetrate handles it.
                if (candidate.distance <= 0f)
                    continue;

                // Strict less-than: on a tie the first reported hit wins.
                if (candidate.distance < nearest)
                {
                    nearest = candidate.distance;
                    hit = candidate;
                    found = true;
                }
            }

            return found;
        }

        /// <inheritdoc/>
        public int Overlap(in CapsuleShape shape, Collider[] results)
        {
            if (results == null || results.Length == 0)
                return 0;

            int count = Physics.OverlapCapsuleNonAlloc(shape.Bottom, shape.Top, shape.Radius,
                results, m_LayerMask, m_TriggerInteraction);

            if (m_Self == null)
                return count;

            // Remove the mover's own collider in place.
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (results[i] == m_Self)
                    continue;

                results[kept++] = results[i];
            }

            for (int i = kept; i < count; i++)
                results[i] = null;

            return kept;
        }

        /// <inheritdoc/>
        public bool Depenetrate(in CapsuleShape shape, Quaternion rotation, Collider other,
                                out Vector3 direction, out float distance)
        {
            direction = Vector3.zero;
            distance = 0f;

            // No capsule collider on the mover means no shape to hand ComputePenetration.
            if (m_Self == null || other == null || other == m_Self)
                return false;

            // ComputePenetration adds the collider's centre offset to the pose, so subtract it here.
            // Assumes uniform scale; a non-uniformly scaled mover is not supported.
            Vector3 centre = (shape.Bottom + shape.Top) * 0.5f;
            Vector3 scaledOffset = Vector3.Scale(m_Self.center, m_Self.transform.lossyScale);
            Vector3 position = centre - rotation * scaledOffset;

            return Physics.ComputePenetration(
                m_Self, position, rotation,
                other, other.transform.position, other.transform.rotation,
                out direction, out distance);
        }
    }
}
