using StylizedSky;
using UnityEngine;
using UnityEngine.Audio;

public class TimeOfDay : MonoBehaviour
{
    public const float k_HoursInADay = 24;

    /// <summary>Animation clip sampled each frame to drive scene lighting based on time of day.</summary>
    [SerializeField] private AnimationClip animationClip;

    /// <summary>Normalised night volume fed into the audio mixer each frame.</summary>
    public float Night;
    /// <summary>Normalised day volume fed into the audio mixer each frame.</summary>
    public float Day;
    /// <summary>Normalised dusk volume fed into the audio mixer each frame.</summary>
    public float Dusk;

    [SerializeField] public float DayVolumeScaler;
    [SerializeField] public float NightVolumeScaler;
    [SerializeField] public float DawnDuskVolumeScaler;

    [SerializeField] SkyLightController SkylightController;

    /// <summary>Audio mixer containing the exposed Dusk_Volume, Day_Volume, and Night_Volume parameters.</summary>
    public AudioMixer CurrentAudioMixer;
    /// <summary>Current time expressed as a 24-hour value; driven by DebugTimeOfDaySpeed each frame.</summary>
    public float TimeOfDay24Hr;
    /// <summary>Units of simulated hours added to TimeOfDay24Hr each frame for debug purposes.</summary>
    public float DebugTimeOfDaySpeed;

    private void Start() { }

    /// <summary>
    /// Advances the time-of-day clock, samples the animation clip to drive scene lighting,
    /// then pushes the three ambience volumes (scaled by their bank's VolumeScaler) into the audio mixer.
    /// </summary>
    private void Update()
    {
        float timeOfDay = TimeOfDay24Hr;
        if (SkylightController != null)
        {
            timeOfDay = SkylightController.TimeOfDay;
        }
        float d = Mathf.Abs(timeOfDay) % k_HoursInADay;
        d = d / k_HoursInADay * animationClip.length;
        animationClip.SampleAnimation(gameObject, d);

        // Day, Night & Dusk parameters are set from the animationClip timeline
        if (CurrentAudioMixer != null)
        {
            SetGroupVolume("Dusk_Volume", Dusk * DawnDuskVolumeScaler);
            SetGroupVolume("Day_Volume", Day * DayVolumeScaler);
            SetGroupVolume("Night_Volume", Night * NightVolumeScaler);
        }
    }

    /// <summary>Converts a normalised 0–1 volume to decibels and pushes it to the named mixer exposed parameter.</summary>
    private void SetGroupVolume(string groupName, float normalizedVolume)
    {
        float v = Mathf.Max(normalizedVolume, 0.0001f);
        CurrentAudioMixer.SetFloat(groupName, Mathf.Log10(v) * 20f);
    }
}
