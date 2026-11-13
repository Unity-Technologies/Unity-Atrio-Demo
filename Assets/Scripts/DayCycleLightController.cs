using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Controls a list of light GameObjects in the scene for day/night transitions.
/// Toggles child active states and switches materials on children containing a DayCycleMaterialSwap component.
/// </summary>
[AddComponentMenu("Lighting/Day Cycle Light Controller")]
public class DayCycleLightController : MonoBehaviour
{
    [Header("Day Cycle Configuration")]
    [SerializeField] private List<GameObject> lights = new List<GameObject>();
    public bool _isDayTime = true;

    public List<GameObject> Lights => lights;

    public void ApplyDayCycle()
    {
        TurnOnOff(_isDayTime);
        _isDayTime = !_isDayTime;
    }

    public void SetElement(GameObject go, bool isNight)
    {
        if (go == null) return;

        foreach (Light light in go.GetComponentsInChildren<Light>(true))
        {
            light.gameObject.SetActive(isNight);
        }

        foreach (DayCycleMaterialSwap swap in go.GetComponentsInChildren<DayCycleMaterialSwap>(true))
        {
            if (swap == null) continue;

            MeshRenderer meshRenderer = swap.TargetRenderer;
            Material targetMaterial = isNight ? swap.NightMaterial : swap.DayMaterial;

            if (meshRenderer != null && targetMaterial != null)
            {
                meshRenderer.sharedMaterial = targetMaterial;
            }
        }
    }

    public void TurnOnOff(bool isDayTime)
    {
        if (lights == null) return;

        foreach (GameObject go in lights)
        {
            SetElement(go, isDayTime);
        }
    }
}
