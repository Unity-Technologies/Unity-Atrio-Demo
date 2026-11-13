using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Holds the MasterVolume mixer bus silent from Awake, then fades it up to the level its exposed
/// parameter is set to in the mixer once the streamed ambience source has actually started producing audio.
/// </summary>
[DefaultExecutionOrder(-1000)]
public class MasterVolumeFade : MonoBehaviour
{
    private const float k_SilentDb = -80f;
    private const float k_MinLinear = 0.0001f;

    /// <summary>Audio mixer containing the exposed MasterVolume parameter.</summary>
    [SerializeField] private AudioMixer audioMixer;
    /// <summary>Name of the exposed volume parameter on the MasterVolume bus.</summary>
    [SerializeField] private string exposedParameter = "MasterVolume";
    /// <summary>Streamed ambience source; the fade begins once it is playing.</summary>
    [SerializeField] private AudioSource streamedAmbience;
    /// <summary>Time in seconds to fade from silence to the mixer's level.</summary>
    [SerializeField] private float fadeDuration = 3f;

    private float fadeElapsed;
    private float targetDb;
    private bool fading;
    private bool finished;

    private void Awake()
    {
        // Fade towards the level the exposed parameter is set to in the mixer's snapshot.
        // ClearFloat drops any earlier script override so GetFloat returns the snapshot value.
        targetDb = 0f;
        if (audioMixer != null)
        {
            audioMixer.ClearFloat(exposedParameter);
            if (!audioMixer.GetFloat(exposedParameter, out targetDb))
            {
                targetDb = 0f;
            }
        }
        SetVolumeDb(k_SilentDb);
    }

    /// <summary>
    /// Keeps the bus silent until the ambience starts (SetFloat isn't always honoured in Awake),
    /// then ramps linear gain from silence up to the mixer's level over fadeDuration.
    /// </summary>
    private void Update()
    {
        if (finished || audioMixer == null)
        {
            return;
        }

        if (!fading)
        {
            SetVolumeDb(k_SilentDb);
            // A streamed clip can report isPlaying before it has buffered, so wait for the playhead to move.
            if (streamedAmbience == null || (streamedAmbience.isPlaying && streamedAmbience.timeSamples > 0))
            {
                fading = true;
            }
            return;
        }

        fadeElapsed += Time.unscaledDeltaTime;
        float t = fadeDuration > 0f ? Mathf.Clamp01(fadeElapsed / fadeDuration) : 1f;
        SetVolumeDb(Mathf.Log10(Mathf.Max(t, k_MinLinear)) * 20f + targetDb);

        if (t >= 1f)
        {
            finished = true;
        }
    }

    private void SetVolumeDb(float db)
    {
        if (audioMixer != null)
        {
            audioMixer.SetFloat(exposedParameter, db);
        }
    }
}
