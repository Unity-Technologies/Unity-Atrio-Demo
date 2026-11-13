using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// A world-space capsule, as handed to an <see cref="ICollisionBackend"/> query.
    /// </summary>
    /// <remarks>
    /// Blittable and immutable. <see cref="Bottom"/> and <see cref="Top"/> are hemisphere centres,
    /// not tips, matching <c>Physics.CapsuleCast</c>.
    /// </remarks>
    internal readonly struct CapsuleShape
    {
        /// <summary>Centre of the lower hemisphere, world space.</summary>
        public readonly Vector3 Bottom;

        /// <summary>Centre of the upper hemisphere, world space.</summary>
        public readonly Vector3 Top;

        /// <summary>Capsule radius in meters.</summary>
        public readonly float Radius;

        /// <summary>Creates a capsule from its two hemisphere centres and radius.</summary>
        /// <param name="bottom">Centre of the lower hemisphere, world space.</param>
        /// <param name="top">Centre of the upper hemisphere, world space.</param>
        /// <param name="radius">Capsule radius in meters.</param>
        public CapsuleShape(Vector3 bottom, Vector3 top, float radius)
        {
            Bottom = bottom;
            Top = top;
            Radius = radius;
        }

        /// <summary>
        /// Builds an upright capsule standing with its feet at <paramref name="feet"/>.
        /// </summary>
        /// <remarks>
        /// Becomes a sphere when <paramref name="height"/> is at most twice <paramref name="radius"/>.
        /// </remarks>
        /// <param name="feet">World position of the capsule's lowest point.</param>
        /// <param name="up">Up axis of the capsule. Expected to be unit length.</param>
        /// <param name="height">Total capsule height, tip to tip, in meters.</param>
        /// <param name="radius">Capsule radius in meters.</param>
        public static CapsuleShape Upright(Vector3 feet, Vector3 up, float height, float radius)
        {
            float half = Mathf.Max(0f, height * 0.5f - radius);
            Vector3 centre = feet + up * (height * 0.5f);
            return new CapsuleShape(centre - up * half, centre + up * half, radius);
        }

        /// <summary>Returns this capsule translated by <paramref name="offset"/>.</summary>
        public CapsuleShape Translated(Vector3 offset)
            => new CapsuleShape(Bottom + offset, Top + offset, Radius);
    }
}
