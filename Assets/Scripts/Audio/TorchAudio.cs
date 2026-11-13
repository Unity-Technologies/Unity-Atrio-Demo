using UnityEngine;

// Sits on the Torch prefab root with an AudioSource. Plays TorchOnOff when the torch light comes on or goes out.
// ToggleTorchLight is added to the torch in Gameplay.prefab and is switched by the Interactable_equip_torch,
// Interactable_reset_torch and Interactable_turn_on_off sequences.
public class TorchAudio : MonoBehaviour
{
    public AudioSource TorchAudioSource; // Defaults to the AudioSource on this GameObject if left empty
    public ToggleTorchLight TorchLight; // Defaults to the ToggleTorchLight under this GameObject if left empty

    void Awake()
    {
        if (TorchAudioSource == null)
        {
            TorchAudioSource = GetComponent<AudioSource>();
        }
        if (TorchLight == null)
        {
            TorchLight = GetComponentInChildren<ToggleTorchLight>(true);
        }
    }

    void OnEnable()
    {
        if (TorchLight != null)
        {
            TorchLight.LightStateChanged += OnLightStateChanged;
        }
    }

    void OnDisable()
    {
        if (TorchLight != null)
        {
            TorchLight.LightStateChanged -= OnLightStateChanged;
        }
    }

    void OnLightStateChanged(bool on)
    {
        if (TorchAudioSource != null && TorchAudioSource.isActiveAndEnabled)
        {
            TorchAudioSource.Play();
        }
    }
}
