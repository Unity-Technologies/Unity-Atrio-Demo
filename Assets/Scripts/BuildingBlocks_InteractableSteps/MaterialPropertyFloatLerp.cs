using System.Collections;
using UnityEngine;
using Unity.BuildingBlocks.Gameplay;

[System.Serializable]
[InteractionStep("MaterialPropertyFloatLerp")]
public class MaterialPropertyFloatLerp : InteractionStep
{
    [SceneObject] public MeshRenderer Target;
    [SerializeField] private string m_PropertyName;
    [SerializeField] private float m_StartValue = 0;
    [SerializeField] private float m_EndValue = 1;
    [SerializeField, Min(0f)] private float m_Duration = 1;

    private int m_MaterialPropertyId;

    // Renderer.material gives the renderer its own instance (made once, then reused), so the lerp never
    // writes to the material asset. Steps only run in play mode, where that instance goes with the scene.
    
    public override IEnumerator Run(Interaction interaction)
    {
        if (Target == null || Target.sharedMaterial == null || string.IsNullOrWhiteSpace(m_PropertyName))
            yield break;
        
        m_MaterialPropertyId = Shader.PropertyToID(m_PropertyName);
        Target.material.SetFloat(m_MaterialPropertyId, m_StartValue);

        float elapsed = 0f;
        while (elapsed <= m_Duration)
        {
            float dt = interaction != null && interaction.DeltaTime > 0f ? interaction.DeltaTime : Time.deltaTime;
            elapsed += dt;
            
            float t = Mathf.Clamp01(elapsed / m_Duration);
            float val = Mathf.Lerp(m_StartValue, m_EndValue, t);
            Target.material.SetFloat(m_MaterialPropertyId, val);
            yield return null;
        }
    }

    public override void Stop(Interaction interaction)
    {
        if (Target == null || Target.sharedMaterial == null || string.IsNullOrWhiteSpace(m_PropertyName))
            return;
        
        m_MaterialPropertyId = Shader.PropertyToID(m_PropertyName);
        Target.material.SetFloat(m_MaterialPropertyId, m_StartValue);
    }
}
