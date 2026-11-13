using UnityEngine;

public class CatAudio : MonoBehaviour
{
    public GameObject CatPurrGameObject;
    public GameObject CatMeowGameObject; 
    private AudioSource CatPurrAudioSource;
    private AudioSource CatMeowAudioSource;
    public float PurrVolume = 0.5f; // Normal purr level. Kept below 1 so there's headroom to purr louder when fussed
    public float FussedPurrVolume = 1f; // Purr level while being petted, between the CatFussedAudioStart/Stop animation events
    public float FussedFadeTime = 0.3f; // Seconds to fade between the normal and fussed purr levels
    public float PurrMasterVolume = 1f; // Scales both PurrVolume and FussedPurrVolume
    private float m_CatPurrVolume;
    private float m_CatPurrVolumeScale = 0;
    private bool m_IsFussed;

    private const float k_Silence = 0;
    private const float k_FullVolume = 1;
   

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

        CatPurrAudioSource = CatPurrGameObject.GetComponent<AudioSource>();
        m_CatPurrVolume = PurrVolume;
        CatMeowAudioSource = CatMeowGameObject.GetComponent<AudioSource>();
    }


    // Called by CatAnimationAudio when this cat's PetCat reaches the CatFussedAudioStart/Stop events
    public void SetFussed(bool isFussed)
    {
        m_IsFussed = isFussed;
    }

    // Update is called once per frame
    void Update()
    {
        if (CatPurrAudioSource == null || CatMeowAudioSource == null)
        {
            return;
        }

        if (CatMeowAudioSource.isPlaying == true)
        {
            m_CatPurrVolumeScale = k_Silence;
        }
        else
        {
            m_CatPurrVolumeScale = k_FullVolume;
        }

        float targetPurrVolume = m_IsFussed ? FussedPurrVolume : PurrVolume;
        float fadeSpeed = FussedFadeTime > 0 ? Mathf.Abs(FussedPurrVolume - PurrVolume) / FussedFadeTime : float.MaxValue;
        m_CatPurrVolume = Mathf.MoveTowards(m_CatPurrVolume, targetPurrVolume, fadeSpeed * Time.deltaTime);

        CatPurrAudioSource.volume = m_CatPurrVolumeScale * m_CatPurrVolume * PurrMasterVolume; // Stop purring when meowing. We could lerp the purrs to fade them back in, but it doesn't seem necessary

    }
}
