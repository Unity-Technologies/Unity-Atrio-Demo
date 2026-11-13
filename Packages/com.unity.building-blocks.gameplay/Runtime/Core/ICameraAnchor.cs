using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// One of the six local axes of a transform. Says which way an animated camera node looks and
    /// which way is up, so a camera exported with another convention works without re-exporting.
    /// </summary>
    public enum AnchorAxis
    {
        /// <summary>The node's local +X.</summary>
        PositiveX,

        /// <summary>The node's local -X.</summary>
        NegativeX,

        /// <summary>The node's local +Y. Unity's up.</summary>
        PositiveY,

        /// <summary>The node's local -Y.</summary>
        NegativeY,

        /// <summary>The node's local +Z. Unity's forward, where a Unity camera looks.</summary>
        PositiveZ,

        /// <summary>The node's local -Z. Where a camera looks in most DCCs.</summary>
        NegativeZ,
    }

    /// <summary>Helpers for <see cref="AnchorAxis"/>.</summary>
    public static class AnchorAxes
    {
        /// <summary>The unit vector for an axis.</summary>
        public static Vector3 ToVector(AnchorAxis axis)
        {
            switch (axis)
            {
                case AnchorAxis.PositiveX: return Vector3.right;
                case AnchorAxis.NegativeX: return Vector3.left;
                case AnchorAxis.PositiveY: return Vector3.up;
                case AnchorAxis.NegativeY: return Vector3.down;
                case AnchorAxis.NegativeZ: return Vector3.back;
                default: return Vector3.forward;
            }
        }

        /// <summary>
        /// The rotation that turns a node whose <paramref name="forward"/> and <paramref name="up"/>
        /// axes are the given ones into a Unity camera looking down +Z with +Y up. Identity for
        /// +Z / +Y. Apply it after the node's rotation: <c>node.rotation * correction</c>.
        /// </summary>
        /// <returns>
        /// <c>false</c>, with an identity correction, when the two axes are the same line and cannot
        /// define an orientation.
        /// </returns>
        public static bool TryCorrection(AnchorAxis forward, AnchorAxis up, out Quaternion correction)
        {
            Vector3 f = ToVector(forward);
            Vector3 u = ToVector(up);

            if (Mathf.Abs(Vector3.Dot(f, u)) > 0.5f)
            {
                correction = Quaternion.identity;
                return false;
            }

            correction = Quaternion.LookRotation(f, u);
            return true;
        }
    }

    /// <summary>
    /// A view that can ride another transform for a while, such as an animated camera node during
    /// a clip, then return to its normal place.
    /// </summary>
    /// <remarks>
    /// While anchored the view copies the anchor's world pose every frame and ignores look input.
    /// <see cref="Release"/> returns control facing where the ride ended; while the character is
    /// locked, <c>FirstPersonCameraRig</c> (the shipped implementation) faces the body instead.
    /// </remarks>
    public interface ICameraAnchor
    {
        /// <summary>Whether the view is riding an anchor right now. False while easing back.</summary>
        bool IsAnchored { get; }

        /// <summary>
        /// Rides <paramref name="anchor"/> until <see cref="Release"/>. Calling it while already riding
        /// switches anchors with a blend.
        /// </summary>
        /// <param name="anchor">The transform to copy. A camera node in an animated object.</param>
        /// <param name="correction">
        /// Applied after the anchor's rotation, to point a node exported with another convention the
        /// way a Unity camera looks. <see cref="AnchorAxes.TryCorrection"/> builds it; identity for a
        /// node that already looks down its +Z.
        /// </param>
        /// <param name="copyFieldOfView">
        /// Take the field of view from a <see cref="Camera"/> on the anchor, when there is one, and put
        /// the view's own back on release.
        /// </param>
        /// <param name="blendSeconds">Seconds to ease from the current view onto the anchor. 0 snaps.</param>
        void AttachTo(Transform anchor, Quaternion correction, bool copyFieldOfView, float blendSeconds);

        /// <summary>Eases the view back to where it normally sits. Does nothing when not riding.</summary>
        /// <param name="blendSeconds">Seconds to ease. 0 snaps.</param>
        void Release(float blendSeconds);
    }
}
