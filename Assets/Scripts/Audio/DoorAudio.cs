using UnityEngine;
using UnityEngine.Audio;

public class DoorAudio : MonoBehaviour
{
    public AudioSource DoorAudioSource; // Defaults to the AudioSource on this door if left empty
    [SerializeField] public AudioResource DoorOpenAudioARC;

    void Awake()
    {
        if (DoorAudioSource == null)
        {
            DoorAudioSource = GetComponent<AudioSource>();
        }
    }

    // Called by the DoorOpenAudioEvent animation event on the DoorOpen clip in EntranceDoor.fbx
    public void DoorOpenAudioEvent()
    {
        if (DoorAudioSource != null)
        {
            if (DoorOpenAudioARC != null)
            {
                DoorAudioSource.resource = DoorOpenAudioARC;
            }
            DoorAudioSource.Play();
        }
    }
}
