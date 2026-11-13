using UnityEngine;

/// <summary>
/// Marks the colliders on this GameObject and its children as a footstep surface.
/// Colliders without one (on themselves or a parent) use FootstepAudio.DefaultSurfaceType.
/// </summary>
public class FootstepSurface : MonoBehaviour
{
    public FootstepSurfaceType SurfaceType;
}
