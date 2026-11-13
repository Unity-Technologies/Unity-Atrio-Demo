using UnityEngine;
using Unity.BuildingBlocks.Gameplay;

/// <summary>
/// Links an Animator parameter (such as "Blend") to the player camera's vertical pitch.
/// When the player looks down, the value is -1; when looking straight, 0; and when looking up, 1.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
[DefaultExecutionOrder(105)] // Runs right after FirstPersonCameraRig (100)
public class CameraPitchAnimatorDriver : MonoBehaviour
{
    [SerializeField]
    [Tooltip("The name of the float parameter on the Animator to drive.")]
    string m_ParameterName = "Blend";

    [SerializeField]
    [Tooltip("Optional reference to the FirstPersonCameraRig or ILookPitch. If null, searched automatically.")]
    FirstPersonCameraRig m_CameraRig;

    [SerializeField]
    [Tooltip("Optional Camera transform to read vertical look direction from. If null, uses the rig's PitchTransform or Camera.main.")]
    Transform m_CameraTransform;

    [SerializeField, Range(10f, 90f)]
    [Tooltip("Max pitch angle in degrees that corresponds to a blend value of 1 or -1.")]
    float m_MaxPitchAngle = 89f;

    [SerializeField, Min(0f)]
    [Tooltip("Smoothing time in seconds. 0 is instant.")]
    float m_Smoothing = 0.05f;

    [SerializeField]
    [Tooltip("If true, only updates the parameter while this GameObject is active and the Animator has a controller.")]
    bool m_OnlyWhenActive = true;

    Animator m_Animator;
    int m_ParameterHash;
    bool m_HasParameter;
    float m_CurrentBlend;
    float m_Velocity;

    /// <summary>The name of the parameter being driven.</summary>
    public string ParameterName
    {
        get => m_ParameterName;
        set
        {
            m_ParameterName = value;
            m_ParameterHash = Animator.StringToHash(m_ParameterName);
            CheckParameter();
        }
    }

    /// <summary>The CameraRig driving this animator.</summary>
    public FirstPersonCameraRig CameraRig
    {
        get => m_CameraRig;
        set => m_CameraRig = value;
    }

    void Awake()
    {
        EnsureInit();
    }

    void OnEnable()
    {
        EnsureInit();
        ResolveReferences();
        CheckParameter();
        UpdateBlend(true);
    }

    void Update()
    {
        UpdateBlend(false);
    }

    public void EnsureInit()
    {
        if (m_Animator == null)
            m_Animator = GetComponent<Animator>();

        if (m_ParameterHash == 0 && !string.IsNullOrEmpty(m_ParameterName))
            m_ParameterHash = Animator.StringToHash(m_ParameterName);
    }

    public void ResolveReferences()
    {
        EnsureInit();

        if (m_CameraRig == null)
            m_CameraRig = FindAnyObjectByType<FirstPersonCameraRig>();

        if (m_CameraTransform == null)
        {
            if (m_CameraRig != null && m_CameraRig.PitchTransform != null)
                m_CameraTransform = m_CameraRig.PitchTransform;
            else if (Camera.main != null)
                m_CameraTransform = Camera.main.transform;
        }
    }

    public void CheckParameter()
    {
        EnsureInit();

        if (m_Animator == null || m_Animator.runtimeAnimatorController == null || string.IsNullOrEmpty(m_ParameterName))
        {
            m_HasParameter = false;
            return;
        }

        m_HasParameter = false;
        foreach (AnimatorControllerParameter param in m_Animator.parameters)
        {
            if (param.nameHash == m_ParameterHash && param.type == AnimatorControllerParameterType.Float)
            {
                m_HasParameter = true;
                break;
            }
        }
    }

    public void UpdateBlend(bool instant = false)
    {
        EnsureInit();

        if (m_Animator == null)
            return;

        if (m_OnlyWhenActive && (!isActiveAndEnabled || m_Animator.runtimeAnimatorController == null))
            return;

        // If controller changed at runtime, recheck parameter
        if (!m_HasParameter)
        {
            CheckParameter();
            if (!m_HasParameter)
                return;
        }

        float targetBlend = CalculateTargetBlend();

        if (instant || m_Smoothing <= 0f)
        {
            m_CurrentBlend = targetBlend;
            m_Velocity = 0f;
        }
        else
        {
            m_CurrentBlend = Mathf.SmoothDamp(m_CurrentBlend, targetBlend, ref m_Velocity, m_Smoothing);
        }

        m_Animator.SetFloat(m_ParameterHash, m_CurrentBlend);
    }

    public float CalculateTargetBlend()
    {
        if (m_CameraRig != null)
        {
            // In FirstPersonCameraRig / ILookPitch:
            // Pitch is positive when looking down, negative when looking up.
            // Target: looking down = -1, looking straight = 0, looking up = 1.
            float pitch = m_CameraRig.Pitch;
            float maxAngle = m_MaxPitchAngle > 0f ? m_MaxPitchAngle : 89f;
            return Mathf.Clamp(-pitch / maxAngle, -1f, 1f);
        }

        if (m_CameraTransform != null)
        {
            // Camera.forward.y: +1 when looking straight up, 0 when looking straight, -1 when looking straight down.
            return Mathf.Clamp(m_CameraTransform.forward.y, -1f, 1f);
        }

        ResolveReferences();
        return 0f;
    }
}
