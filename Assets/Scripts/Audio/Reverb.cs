using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Drives two Audio Mixer parameters based on the camera's Y position within a BoxCollider zone.
/// - AtrioReverbSendLevel: fades from 0 dB at Y=0 to reverbMaxSend using a correct dB curve.
/// - AtrioAmbienceLowPassFilter: sweeps linearly from lowPassMinFreq at Y=0 to lowPassMaxFreq at the top.
/// Both parameters are set to their maximum/silent values when the camera leaves the collider.
/// </summary>
public class Reverb : MonoBehaviour
{
    public AudioMixer Mixer;
    /// <summary>Tag of the GameObject whose Y position is tracked (usually the main camera).</summary>
    public string ListenerTag = "MainCamera";

    /// <summary>Reverb send level (dB) at Y=0 — full send.</summary>
    public float ReverbMinSend = 0f;
    /// <summary>Reverb send level (dB) at the collider's top or outside — effectively silent.</summary>
    public float ReverbMaxSend = -80f;
    /// <summary>Low-pass cutoff frequency (Hz) at Y=0.</summary>
    public float LowPassMinFreq = 500f;
    /// <summary>Low-pass cutoff frequency (Hz) at the collider's top or outside.</summary>
    public float LowPassMaxFreq = 5000f;
    /// <summary>How quickly the low-pass cutoff moves towards its target. Higher is faster.</summary>
    public float LowPassSmoothing = 4f;

    public float HighPassMinFreq = 100;
    public float HighPassMaxFreq = 300;

    private BoxCollider m_Box;
    private Transform m_Listener;
    private float m_DestLowPassCutoff;
    private float m_DestHighPassCutoff;
    private float m_DestInsideAtrioAmbienceLFP;
    private float m_CurrentLowPassCutoff;
    private float m_CurrentHighPassCutoff;
    private float m_CurrentInsideAtrioAmbienceLPF;


    void Awake()
    {
        m_Box = GetComponent<BoxCollider>();
        m_CurrentLowPassCutoff = LowPassMaxFreq;
        m_CurrentHighPassCutoff = HighPassMinFreq;
        m_CurrentInsideAtrioAmbienceLPF = 22000;
    }

    void Start()
    {
        GameObject go = GameObject.FindWithTag(ListenerTag);
        if (go != null) m_Listener = go.transform;
    }

    void Update()
    {
        if (m_Listener == null || m_Box == null) return;

        Bounds bounds = m_Box.bounds;
        bool nowInside = bounds.Contains(m_Listener.position);

        if (nowInside)
        {
            float maxY = bounds.max.y;
            // t=0 at world Y=0, t=1 at the collider's top
            float t = Mathf.InverseLerp(0f, maxY, m_Listener.position.y);

            // Convert linear gain to dB so -6 dB == 0.5 amplitude
            float linearGain = Mathf.Lerp(1f, 0f, t);
            float reverbSend = linearGain > 0.0001f ? 20f * Mathf.Log10(linearGain) : ReverbMaxSend;

            t *= t;
            t *= t;
            m_DestLowPassCutoff = Mathf.Lerp(LowPassMinFreq, LowPassMaxFreq, t);
            m_DestHighPassCutoff = Mathf.Lerp(HighPassMaxFreq, HighPassMinFreq, t);
            m_DestInsideAtrioAmbienceLFP = Mathf.Lerp(300, 22000, t);
            float dogsFilterLowPass = Mathf.Lerp(0, 22000, t);

            Mixer.SetFloat("AtrioReverbSendLevel", reverbSend);
            Mixer.SetFloat("Dogs_LowPassCutoff", dogsFilterLowPass);
        }
        else
        {
            Mixer.SetFloat("AtrioReverbSendLevel", ReverbMaxSend);
            m_DestLowPassCutoff = LowPassMaxFreq;
            m_DestHighPassCutoff = HighPassMinFreq;
            m_DestInsideAtrioAmbienceLFP = 22000;
            Mixer.SetFloat("Dogs_LowPassCutoff", 22000);
        }

        // Frame-rate independent ease towards the target cutoff
        float blend = 1f - Mathf.Exp(-LowPassSmoothing * Time.deltaTime);
        m_CurrentLowPassCutoff = Mathf.Lerp(m_CurrentLowPassCutoff, m_DestLowPassCutoff, blend);
        Mixer.SetFloat("AtrioAmbienceLowPassFilter", m_CurrentLowPassCutoff);

        m_CurrentHighPassCutoff = Mathf.Lerp(m_CurrentHighPassCutoff, m_DestHighPassCutoff, blend);
        Mixer.SetFloat("AtrioAmbienceHighPassFilter", m_CurrentHighPassCutoff);

        // This controls the overall ambience level when inside the atrio
        // Still allow for a little of the ambience to bleed through, so that the other SFX sit well with it
        // Otherwise, complete silece makes the other SFX feel more unnatural. 
        m_CurrentInsideAtrioAmbienceLPF = Mathf.Lerp(m_CurrentInsideAtrioAmbienceLPF, m_DestInsideAtrioAmbienceLFP, blend);
        Mixer.SetFloat("InsideAtrio_DayAmbienceLPF", m_CurrentInsideAtrioAmbienceLPF);
    }
}
