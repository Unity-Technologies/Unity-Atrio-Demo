using UnityEngine;
using UnityEngine.Audio;

// Sits on the Cat prefab root (next to the Animator) to receive animation events from Cat_StandingIdle and PetCat.
// Events only reach the Animator they play on, so they only affect this cat's audio
public class CatAnimationAudio : MonoBehaviour
{
    public AudioSource CatMeowAudioSource; // Defaults to the AudioSource on CatAudio > CatMeow if left empty
    [SerializeField] public AudioResource CatMeowAudioARC; // Overrides the AudioSource's resource if set
    [SerializeField] public Vector2 MinMaxPitch = new Vector2(0.8f, 1.2f);

    private CatAudio m_CatAudio;

    void Awake()
    {
        m_CatAudio = GetComponentInChildren<CatAudio>();
        if (CatMeowAudioSource == null)
        {
            if (m_CatAudio != null && m_CatAudio.CatMeowGameObject != null)
            {
                CatMeowAudioSource = m_CatAudio.CatMeowGameObject.GetComponent<AudioSource>();
            }
        }

        if (CatMeowAudioSource == null)
        {
            Debug.LogWarning($"{name}: CatAnimationAudio has no meow AudioSource", this);
        }
    }

    // Called by the CatMeowAudio animation event on the clip in Cat_StandingIdle.fbx
    public void CatMeowAudio()
    {
        if (CatMeowAudioSource == null)
        {
            return;
        }
        if (CatMeowAudioARC != null)
        {
            CatMeowAudioSource.resource = CatMeowAudioARC;
        }
        CatMeowAudioSource.pitch = Random.Range(MinMaxPitch.x, MinMaxPitch.y);
        CatMeowAudioSource.Play();
    }

    // Called by the CatFussedAudioStart animation event on PetCat.fbx: purr louder while being petted
    public void CatFussedAudioStart()
    {
        if (m_CatAudio != null)
        {
            m_CatAudio.SetFussed(true);
        }
    }

    // Called by the CatFussedAudioStop animation event on PetCat.fbx
    public void CatFussedAudioStop()
    {
        if (m_CatAudio != null)
        {
            m_CatAudio.SetFussed(false);
        }
    }
}
