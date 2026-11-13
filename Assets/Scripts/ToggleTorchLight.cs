using UnityEngine;

/// <summary>
/// Controls toggling the torch light on and off, switching the mesh material between
/// day and night materials via DayCycleMaterialSwap and adjusting the Spot Light intensity.
/// </summary>
[AddComponentMenu("Lighting/Toggle Torch Light")]
public class ToggleTorchLight : MonoBehaviour
{
    [Header("References")]
    [Tooltip("DayCycleMaterialSwap script attached to this GameObject.")]
    [SerializeField] private DayCycleMaterialSwap dayCycleMaterialSwap;

    [Tooltip("Spot Light located in the hierarchy.")]
    [SerializeField] private Light spotLight;

    [Header("Light Settings")]
    [Tooltip("Light intensity when the torch is turned on.")]
    [SerializeField] private float lightIntensityOn = 2f;

    [Tooltip("Light intensity when the torch is turned off.")]
    [SerializeField] private float lightIntensityOff = 0f;

    [Tooltip("Whether the light is on at start.")]
    [SerializeField] private bool lightOnAtStart = false;

    private bool isLightOn;

    public DayCycleMaterialSwap DayCycleMaterialSwap
    {
        get => dayCycleMaterialSwap;
        set => dayCycleMaterialSwap = value;
    }

    public Light SpotLight
    {
        get => spotLight;
        set => spotLight = value;
    }

    public float LightIntensityOn
    {
        get => lightIntensityOn;
        set => lightIntensityOn = value;
    }

    public float LightIntensityOff
    {
        get => lightIntensityOff;
        set => lightIntensityOff = value;
    }

    public bool LightOnAtStart
    {
        get => lightOnAtStart;
        set => lightOnAtStart = value;
    }

    public bool IsLightOn => isLightOn;

    /// <summary>
    /// Raised when the light switches on (true) or off (false). Not raised when the state is set to what it already was.
    /// </summary>
    public event System.Action<bool> LightStateChanged;

    private void Awake()
    {
        FindReferences();
        isLightOn = lightOnAtStart;
    }

    private void Start()
    {
        SetLightState(lightOnAtStart);
    }

    private void Reset()
    {
        FindReferences();
    }

    /// <summary>
    /// Finds and caches references to DayCycleMaterialSwap on this object and Spot Light in the hierarchy.
    /// </summary>
    public void FindReferences()
    {
        if (dayCycleMaterialSwap == null)
        {
            dayCycleMaterialSwap = GetComponent<DayCycleMaterialSwap>();
        }

        if (spotLight == null)
        {
            Light[] lights = GetComponentsInChildren<Light>(true);
            foreach (var l in lights)
            {
                if (l != null && l.name == "Spot Light")
                {
                    spotLight = l;
                    break;
                }
            }

            if (spotLight == null)
            {
                foreach (var l in lights)
                {
                    if (l != null && l.type == LightType.Spot)
                    {
                        spotLight = l;
                        break;
                    }
                }
            }

            if (spotLight == null && lights.Length > 0)
            {
                spotLight = lights[0];
            }
        }
    }

    /// <summary>
    /// Toggles the light state.
    /// If currently on, assigns day material to the target renderer and changes spot light intensity to lightIntensityOff.
    /// If currently off, assigns night material to the target renderer and changes spot light intensity to lightIntensityOn.
    /// </summary>
    [ContextMenu("Toggle Light")]
    public void ToggleLight()
    {
        if (isLightOn)
        {
            SetLightState(false);
        }
        else
        {
            SetLightState(true);
        }
    }

    /// <summary>
    /// Sets the light state explicitly.
    /// </summary>
    /// <param name="on">True to turn on, false to turn off.</param>
    public void SetLightState(bool on)
    {
        FindReferences();

        bool changed = isLightOn != on;
        isLightOn = on;

        if (dayCycleMaterialSwap != null)
        {
            MeshRenderer targetRenderer = dayCycleMaterialSwap.TargetRenderer;
            Material materialToAssign = on ? dayCycleMaterialSwap.NightMaterial : dayCycleMaterialSwap.DayMaterial;

            if (targetRenderer != null && materialToAssign != null)
            {
                targetRenderer.sharedMaterial = materialToAssign;
            }
        }

        if (spotLight != null)
        {
            spotLight.intensity = on ? lightIntensityOn : lightIntensityOff;
        }

        if (changed)
        {
            LightStateChanged?.Invoke(on);
        }
    }
}
