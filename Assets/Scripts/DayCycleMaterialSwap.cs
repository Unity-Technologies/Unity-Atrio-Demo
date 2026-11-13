using UnityEngine;

/// <summary>
/// Marks a child GameObject of a light for day/night material swapping.
/// Can optionally provide custom day/night materials; otherwise falls back to the controller's night material and original material.
/// </summary>
[AddComponentMenu("Lighting/Day Cycle Material Swap")]
public class DayCycleMaterialSwap : MonoBehaviour
{
    [Tooltip("Target MeshRenderer to swap materials on. If not assigned, will automatically look on this GameObject.")]
    [SerializeField] private MeshRenderer targetRenderer;

    [Tooltip("Optional custom material to apply during night time. If left empty, the controller's night material will be used.")]
    [SerializeField] private Material nightMaterial;

    [Tooltip("Optional custom material to apply during day time. If left empty, the original cached material is restored.")]
    [SerializeField] private Material dayMaterial;

    public MeshRenderer TargetRenderer
    {
        get
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<MeshRenderer>();
            }
            return targetRenderer;
        }
        set => targetRenderer = value;
    }

    public Material NightMaterial
    {
        get => nightMaterial;
        set => nightMaterial = value;
    }

    public Material DayMaterial
    {
        get => dayMaterial;
        set => dayMaterial = value;
    }
}
