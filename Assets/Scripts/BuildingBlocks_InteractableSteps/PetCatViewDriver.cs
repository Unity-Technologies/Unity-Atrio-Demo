using System;
using UnityEngine;

/// <summary>
/// Helper for <see cref="PetCatStep"/>. Runs the step's view code once per frame, after the player's
/// body has been placed for the frame and before the camera rig reads it. Added and removed by the
/// step at runtime; never add it by hand.
/// </summary>
[AddComponentMenu("")]
[DefaultExecutionOrder(50)] // After MoveInterpolator (0) writes the body, before FirstPersonCameraRig (100).
public sealed class PetCatViewDriver : MonoBehaviour
{
    /// <summary>Called every LateUpdate with the frame's delta time.</summary>
    public Action<float> Tick;

    void LateUpdate() => Tick?.Invoke(Time.deltaTime);
}
