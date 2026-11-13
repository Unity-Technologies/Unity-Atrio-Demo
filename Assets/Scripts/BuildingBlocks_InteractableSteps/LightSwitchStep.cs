using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[Serializable]
[Unity.BuildingBlocks.Gameplay.InteractionStep("Light Switch")]
public sealed class LightSwitchStep : Unity.BuildingBlocks.Gameplay.InteractionStep
{
    [Header("Light Switch")]
    [SerializeField, Unity.BuildingBlocks.Gameplay.SceneObject]
    [Tooltip("The animated toggle mesh. Its signed local X rotation is positive when off and negative when on.")]
    private MeshFilter m_SwitchMesh;
    [SerializeField, Unity.BuildingBlocks.Gameplay.SceneObject]
    private DayCycleLightController m_LightController;
    [SerializeField] private AnimationClip m_SwitchOn;
    [SerializeField] private AnimationClip m_SwitchOff;
    [SerializeField, Min(0f)]
    [Tooltip("Seconds after the animation starts to change the lights and fire the matching event.")]
    private float m_SwitchDelay = 1.6f;

    [Header("Animation")]
    [SerializeField, Unity.BuildingBlocks.Gameplay.SceneObject] private Animator[] m_Animators = Array.Empty<Animator>();
    [SerializeField, Min(0.01f)] private float m_Speed = 1f;
    [SerializeField, Min(0f)] private float m_BlendIn;
    [SerializeField, Min(0f)] private float m_BlendOut;

    [Header("Camera")]
    [SerializeField, Unity.BuildingBlocks.Gameplay.SceneObject] private GameObject m_PointOfView;
    [SerializeField] private Unity.BuildingBlocks.Gameplay.AnchorAxis m_CameraForward = Unity.BuildingBlocks.Gameplay.AnchorAxis.PositiveZ;
    [SerializeField] private Unity.BuildingBlocks.Gameplay.AnchorAxis m_CameraUp = Unity.BuildingBlocks.Gameplay.AnchorAxis.PositiveY;
    [SerializeField, Min(0f)] private float m_CameraBlendIn = 0.25f;
    [SerializeField, Min(0f)] private float m_CameraBlendOut = 0.25f;
    [SerializeField] private bool m_CopyFieldOfView = true;

    [Header("Switch Effects")]
    [SerializeField] private UnityEvent m_OnSwitchOn = new UnityEvent();
    [SerializeField] private UnityEvent m_OnSwitchOff = new UnityEvent();

    private Unity.BuildingBlocks.Gameplay.PlayAnimationStep m_Playback;

    public DayCycleLightController LightController { get => m_LightController; set => m_LightController = value; }
    public AnimationClip SwitchOn { get => m_SwitchOn; set => m_SwitchOn = value; }
    public AnimationClip SwitchOff { get => m_SwitchOff; set => m_SwitchOff = value; }
    public UnityEvent OnSwitchOn => m_OnSwitchOn;
    public UnityEvent OnSwitchOff => m_OnSwitchOff;

    public bool TryGetLightState(out bool active)
    {
        active = false;
        if (m_LightController == null || m_LightController.Lights == null)
            return false;

        // The controller changes child Light GameObjects, not the referenced lamp root.
        // Read the first light's local active state so an inactive ancestor does not
        // make an already switched-on lamp appear switched off.
        foreach (GameObject target in m_LightController.Lights)
        {
            if (target == null) continue;
            Light light = target.GetComponentInChildren<Light>(true);
            if (light == null) continue;
            active = light.gameObject.activeSelf;
            return true;
        }
        return false;
    }

    public override IEnumerator Run(Unity.BuildingBlocks.Gameplay.Interaction interaction)
    {
        if (m_SwitchMesh == null)
        {
            Debug.LogWarning("Light Switch: assign the Switch Mesh toggle MeshFilter.", interaction?.Interactable);
            yield break;
        }

        // The existing clips move local X from +25 degrees (off) to -25 degrees (on).
        // Normalize Euler wraparound so 335 degrees is interpreted as -25 degrees.
        bool turnOn = Mathf.DeltaAngle(0f, m_SwitchMesh.transform.localEulerAngles.x) >= 0f;
        AnimationClip clip = turnOn ? m_SwitchOn : m_SwitchOff;
        if (clip == null)
        {
            Debug.LogWarning("Light Switch: assign both Switch On and Switch Off animation clips.", interaction?.Interactable);
            yield break;
        }

        m_Playback = new Unity.BuildingBlocks.Gameplay.PlayAnimationStep
        {
            Clip = clip, Animators = m_Animators, Speed = m_Speed,
            BlendIn = m_BlendIn, BlendOut = m_BlendOut,
            PointOfView = m_PointOfView, CameraForward = m_CameraForward, CameraUp = m_CameraUp,
            CameraBlendIn = m_CameraBlendIn, CameraBlendOut = m_CameraBlendOut,
            CopyFieldOfView = m_CopyFieldOfView
        };

        IEnumerator animation = m_Playback.Run(interaction);
        float startedAt = interaction.Elapsed;
        bool switched = false;
        bool playing = true;
        try
        {
            while (playing || !switched)
            {
                if (playing) playing = animation.MoveNext();
                if (!switched && interaction.Elapsed - startedAt >= m_SwitchDelay)
                {
                    switched = true;
                    if (TryGetLightState(out bool lightActive))
                    {
                        bool turnLightsOn = !lightActive;
                        m_LightController.TurnOnOff(turnLightsOn);
                        if (turnLightsOn) m_OnSwitchOn.Invoke();
                        else m_OnSwitchOff.Invoke();
                    }
                }
                if (playing || !switched) yield return null;
            }
        }
        finally
        {
            (animation as IDisposable)?.Dispose();
            m_Playback.Stop(interaction);
            m_Playback = null;
        }
    }

    public override void Stop(Unity.BuildingBlocks.Gameplay.Interaction interaction)
    {
        m_Playback?.Stop(interaction);
    }
}
