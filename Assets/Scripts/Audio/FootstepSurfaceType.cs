using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Sounds and settings for one kind of floor (stone, wood, ...). Referenced by FootstepSurface
/// components on colliders, and by FootstepAudio as the default when a collider has none.
/// </summary>
[CreateAssetMenu(fileName = "FootstepSurface", menuName = "Audio/Footstep Surface Type")]
public class FootstepSurfaceType : ScriptableObject
{
    [SerializeField] public AudioResource StepAudioRandomContainer;
    [SerializeField] public AudioResource LandAudioRandomContainer;

    [Range(0f, 1f)] public float Volume = 1f;
    /// <summary>Scales FootstepAudio.StrideLength on this surface, e.g. shorter strides on stairs.</summary>
    public float StrideMultiplier = 1f;
}
