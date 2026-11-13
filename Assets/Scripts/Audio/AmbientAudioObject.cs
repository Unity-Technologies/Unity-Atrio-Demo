using System;
using UnityEngine;
using UnityEngine.Audio;

public class AmbientAudioObject : MonoBehaviour
{
    [Serializable] 
    public class AudioSourceInfo
    {
        public AudioSource ASource;
        public AudioMixerGroup MixerGroup;
    }
    public AudioSourceInfo[] AudioSourceInfoArray;

    /// <summary>Animation clip sampled each frame to drive scene lighting based on time of day.</summary>
    [SerializeField] private AnimationClip animationClip;



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }


}
