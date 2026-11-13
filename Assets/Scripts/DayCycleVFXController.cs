using System;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Lighting/Day Cycle VFX Controller")]
public class DayCycleVFXController : MonoBehaviour
{
    [Header("Day Cycle Configuration")]
    [SerializeField] private List<ParticleSystem> m_DaytimeVFX = new List<ParticleSystem>();
    [SerializeField] private List<ParticleSystem> m_NighttimeVFX = new List<ParticleSystem>();
    [field:SerializeField] public bool IsDayTime { get; private set; } = true;

    private void Start()
    {
        ToggleOnOff();
    }

    public void ToggleVFX()
    {
        IsDayTime = !IsDayTime;
        ToggleOnOff();
    }

    public void SetDayCycleVFX(bool isDayTime)
    {
        if(isDayTime == IsDayTime)
            return;
        
        IsDayTime = isDayTime;
        ToggleOnOff();
    }

    private void ToggleOnOff()
    {
        if (IsDayTime)
        {
            StopVFX(m_NighttimeVFX);
            StartVFX(m_DaytimeVFX);
        }
        else
        {
            StopVFX(m_DaytimeVFX);
            StartVFX(m_NighttimeVFX);
        }
    }

    private static void StartVFX(List<ParticleSystem> vfxList)
    {
       if(vfxList == null || vfxList.Count == 0)
           return;
        
       foreach (var vfx in vfxList)
       {
           vfx.Play();
       }
    }
    private static void StopVFX(List<ParticleSystem> vfxList)
    {
        if(vfxList == null || vfxList.Count == 0)
            return;
        
        foreach (var vfx in vfxList)
        {
            vfx.Stop();
        }
    }
}
