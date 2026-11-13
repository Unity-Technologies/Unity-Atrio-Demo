using UnityEngine;
using UnityEngine.Audio;

public class LightSwitchAudio : MonoBehaviour
{
    private AudioSource m_LightSwitchAudioSource;
    private AudioSource m_LightBulbAudioSource;

    [SerializeField] public AudioResource LightSwitchOnOffAudioRandomContainer;
    [SerializeField] public float LightSwitchVolume;
    [SerializeField] public float LightBulbVolume;

    public GameObject LightGameObject;
    public float LightBulbDelay = 0.1f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        m_LightSwitchAudioSource = GetComponent<AudioSource>();
        if (LightGameObject != null)
        {
            m_LightBulbAudioSource = LightGameObject.GetComponent<AudioSource>();
        }
    }


    public void LightSwitchOn()
    {
        if (m_LightSwitchAudioSource != null)
        {
            TriggerLight(LightSwitchOnOffAudioRandomContainer);
        }
        if (m_LightBulbAudioSource != null)
        {
            m_LightBulbAudioSource.volume = LightBulbVolume;
            m_LightBulbAudioSource.PlayDelayed(LightBulbDelay);
        }
    }

    public void LightSwitchOff()
    {
        if (m_LightSwitchAudioSource != null)
        {
            TriggerLight(LightSwitchOnOffAudioRandomContainer);
        }
        if (m_LightBulbAudioSource != null)
        {
            m_LightBulbAudioSource.Stop();
        }
    }

    private void TriggerLight(AudioResource audioRandomContainer)
    {
        if (m_LightSwitchAudioSource != null)
        {
            m_LightSwitchAudioSource.resource= audioRandomContainer;
            m_LightSwitchAudioSource.volume = LightSwitchVolume;
            m_LightSwitchAudioSource.Play();
        }
    }
}


