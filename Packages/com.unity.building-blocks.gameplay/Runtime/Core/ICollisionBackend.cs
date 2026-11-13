using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Geometry queries the mover uses to meet the world: sweep, overlap and depenetrate a capsule.
    /// </summary>
    /// <remarks>
    /// The production implementation is <see cref="CapsuleSweepBackend"/>. Implementations hold no
    /// state between calls, never allocate (callers own buffers), and answer geometry only;
    /// grounding, slopes, steps and movement modes belong to the mover.
    /// </remarks>
    internal interface ICollisionBackend
    {
        /// <summary>
        /// Finds the nearest blocking hit along <paramref name="direction"/> within
        /// <paramref name="distance"/>.
        /// </summary>
        /// <remarks>
        /// Must ignore zero-distance hits (start overlaps); <see cref="Depenetrate"/> resolves those.
        /// The mover's own collider is excluded by the backend's configuration, not the caller.
        /// </remarks>
        /// <param name="shape">The capsule at the start of the sweep, world space.</param>
        /// <param name="direction">Sweep direction. Expected to be unit length.</param>
        /// <param name="distance">Maximum sweep distance in meters.</param>
        /// <param name="hit">The nearest blocking hit, or <c>default</c> when none was found.</param>
        /// <returns><c>true</c> when something blocks the sweep within the distance.</returns>
        bool Sweep(in CapsuleShape shape, Vector3 direction, float distance, out RaycastHit hit);

        /// <summary>
        /// Collects the colliders currently overlapping <paramref name="shape"/> into a
        /// caller-owned buffer.
        /// </summary>
        /// <remarks>
        /// Never allocates: results beyond <c>results.Length</c> are dropped.
        /// </remarks>
        /// <param name="shape">The capsule to test, world space.</param>
        /// <param name="results">Caller-owned buffer that receives the overlapping colliders.</param>
        /// <returns>How many entries of <paramref name="results"/> were written.</returns>
        int Overlap(in CapsuleShape shape, Collider[] results);

        /// <summary>
        /// Computes the minimum translation that separates <paramref name="shape"/> from
        /// <paramref name="other"/>.
        /// </summary>
        /// <param name="shape">The capsule to separate, world space.</param>
        /// <param name="rotation">Orientation of the capsule, needed by the underlying query.</param>
        /// <param name="other">The collider to separate from.</param>
        /// <param name="direction">Direction to move the capsule along. Unit length on success.</param>
        /// <param name="distance">How far to move along <paramref name="direction"/>, in meters.</param>
        /// <returns>
        /// <c>true</c> when the shapes overlap and a separation was produced; <c>false</c>
        /// when they do not overlap, in which case the out parameters are meaningless.
        /// </returns>
        bool Depenetrate(in CapsuleShape shape, Quaternion rotation, Collider other,
                         out Vector3 direction, out float distance);
    }
}
