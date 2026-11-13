using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// The view's pitch and eye position, so an interactable can aim the camera without knowing
    /// which camera it is.
    /// </summary>
    /// <remarks>
    /// Yaw is not here: facing is <see cref="MoveState.Rotation"/>, set through
    /// <see cref="KinematicMoverExtensions.Teleport"/>. The shipped implementation is
    /// <c>FirstPersonCameraRig</c>.
    /// </remarks>
    public interface ILookPitch
    {
        /// <summary>
        /// Current pitch in degrees. Positive looks down, negative looks up, the sign the camera's
        /// local X rotation uses.
        /// </summary>
        float Pitch { get; }

        /// <summary>Where the eye is, in world space. An interactable aims from here.</summary>
        Vector3 EyePosition { get; }

        /// <summary>Points the view up or down. The implementation clamps to its own range.</summary>
        void SetPitch(float pitch);
    }
}
