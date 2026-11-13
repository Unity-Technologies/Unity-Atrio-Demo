using UnityEngine;

public class OrreryPressButtonAudio : MonoBehaviour
{
    public AudioSource audioSource;
    public AudioClip clip;
    public void AudioOrreryPressButton()
    {
        if (audioSource != null)
        {
            audioSource.clip = clip;
            audioSource.Play();
        }
    }
}
