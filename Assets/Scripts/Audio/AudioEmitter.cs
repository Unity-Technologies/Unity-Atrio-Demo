using System;
using UnityEngine;
using UnityEngine.Audio;
using Random = UnityEngine.Random;

public class AudioEmitter : MonoBehaviour
{
    /// <summary>The AudioSource used for playback.</summary>
    [SerializeField] public AudioSource EmitterAudioSource;
    /// <summary>Mixer group this emitter routes audio through.</summary>
    [SerializeField] public AudioMixerGroup AudioMixerGroup;
    /// <summary>Audio clip or random container to play.</summary>
    [SerializeField] public AudioResource AudioContent;
    /// <summary>When true, loops the clip if AudioContent is an AudioClip.</summary>
    [SerializeField] public bool LoopAudioClip;

    [Tooltip("Max number of samples to play before doing a full reset (eg. 4 dog barks)")]
    /// <summary>Random range for how many samples to play per burst.</summary>
    [SerializeField] public Vector2 TriggerSampleCountMinMax;

    [Tooltip("Delay after TriggerSampleCount before performing full reset (eg. Delay before next dog position)")]
    /// <summary>Random range for the pause between bursts.</summary>
    [SerializeField] public Vector2 MinMaxDelayBetweenEmitterReset;

    [Tooltip("Delay between each audioClip trigger (eg. dog bark variant)")]
    /// <summary>Random range for the delay between each sample within a burst.</summary>
    [SerializeField] public Vector2 MinMaxDelayBetweenSampleTrigger;
    /// <summary>Random range for playback pitch applied to each sample.</summary>
    [SerializeField] public Vector2 MinMaxPitch;

    [Tooltip("Master volume")]
    /// <summary>Base volume multiplier for this emitter.</summary>
    [SerializeField] public float Volume;
    [Tooltip("Volume scale (eg. Set by Spline volume curve)")]
    /// <summary>Secondary volume multiplier, typically driven by a spline volume curve.</summary>
    public float VolumeScale;

    private Action m_CompleteCallback;
    private uint  m_TriggerCount;
    private float m_ResetTimer;
    private float m_SampleTimer;
    private float m_SampleInterval;
    private float m_Pitch;

    private void Awake()
    {
        VolumeScale = 1;
    }

    private void Start()
    {
        InitAudioParameters();
    }

    /// <summary>
    /// Each frame: applies volume, counts down the inter-sample delay to trigger the next clip,
    /// or counts down the reset timer when the burst is exhausted and calls InitAudioParameters.
    /// </summary>
    private void Update()
    {
        EmitterAudioSource.volume = Volume * VolumeScale;

        if (m_TriggerCount == 0)
        {
            m_ResetTimer -= Time.deltaTime;
            if (m_ResetTimer <= 0)
            {
                m_ResetTimer += Random.Range(MinMaxDelayBetweenEmitterReset.x, MinMaxDelayBetweenEmitterReset.y);
                InitAudioParameters();
            }
        }
        else
        {
            m_SampleTimer -= Time.deltaTime;
            if (m_SampleTimer <= 0)
            {
                m_SampleTimer = m_SampleInterval;
                m_TriggerCount--;

                if (AudioContent != null && EmitterAudioSource != null)
                {
                    EmitterAudioSource.outputAudioMixerGroup = AudioMixerGroup;
                    EmitterAudioSource.resource = AudioContent;
                    EmitterAudioSource.pitch = m_Pitch;
                    EmitterAudioSource.loop = AudioContent is AudioClip && LoopAudioClip;
                    EmitterAudioSource.Play();
                }
            }
        }
    }

    /// <summary>Registers a callback that fires when a burst sequence completes and InitAudioParameters is called.</summary>
    public void SetCompetedCallback(Action callback)
    {
        m_CompleteCallback = callback;
    }

    /// <summary>Randomises burst count, pitch, and inter-sample interval, then fires the complete callback.</summary>
    private void InitAudioParameters()
    {
        m_TriggerCount   = (uint)Random.Range((int)TriggerSampleCountMinMax.x, (int)TriggerSampleCountMinMax.y);
        m_Pitch          = Random.Range(MinMaxPitch.x, MinMaxPitch.y);
        m_SampleInterval = Random.Range(MinMaxDelayBetweenSampleTrigger.x, MinMaxDelayBetweenSampleTrigger.y);
        m_SampleTimer    = 0;
        m_CompleteCallback?.Invoke();
    }
}
