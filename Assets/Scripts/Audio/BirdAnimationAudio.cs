using UnityEngine;
using UnityEngine.Audio;

// Sits on the Boid root in Boid.prefab (next to the Animator) to receive animation events from the bird clips
// in Assets/Assets/Character/Bird/Animation. Every bird in the swarm has one, so keep the sounds short and quiet.
public class BirdAnimationAudio : MonoBehaviour
{
    public AudioSource BirdAudioSource; // Defaults to the AudioSource on this GameObject if left empty
    [SerializeField] public AudioResource BirdWingFlapARC; // Overrides the AudioSource's resource if set
    [SerializeField] public Vector2 MinMaxPitch = new Vector2(0.9f, 1.1f);

    void Awake()
    {
        if (BirdAudioSource == null)
        {
            BirdAudioSource = GetComponent<AudioSource>();
        }
    }

    // Called by the BirdFlapAudio animation event on Flapping.anim
    public void BirdFlapAudio()
    {
        if (BirdAudioSource == null)
        {
            return;
        }
        if (BirdWingFlapARC != null)
        {
            BirdAudioSource.resource = BirdWingFlapARC;
        }
        BirdAudioSource.pitch = Random.Range(MinMaxPitch.x, MinMaxPitch.y);
        BirdAudioSource.Play();
    }
}
