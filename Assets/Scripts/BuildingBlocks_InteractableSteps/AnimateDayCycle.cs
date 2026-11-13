using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using Unity.BuildingBlocks.Gameplay;

// The name shown in the + Add step menu.
[System.Serializable]
[InteractionStep("Animate Day Cycle")]
public class AnimateDayCycle : InteractionStep, ISerializationCallbackReceiver
{
    [Header("Day Cycle Control")]
    [SceneObject] public Transform DayCycleControl;
    [SceneObject] public Transform LightsControl;
    [SerializeField, SceneObject]
    [Tooltip("Animator for the Orrery Model. If unassigned, automatically found from the Orrery interaction hierarchy.")]
    public Animator Orrery;

    public Animator OrreryAnimator
    {
        get => Orrery;
        set => Orrery = value;
    }
    
    [Header("Day to Night")]
    [SerializeField]
    [Tooltip("Initial value (X) and end value (Y) of the time of day parameter when playing from Day to Night.")]
    Vector2 m_TimeOfDayDayToNight = new Vector2(12f, 24f);

    [SerializeField, Min(0f)]
    [Tooltip("Seconds to animate the parameter over time. 0 snaps immediately.")]
    float m_DurationDay = 35f;

    [SerializeField]
    [Tooltip("Easing curve: input is normalized time (0 to 1), output is interpolated progress between Initial Value and End Value.")]
    AnimationCurve m_CurveDay = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [SerializeField, Min(0f)]
    [Tooltip("Delay in seconds before turning lights on.")]
    float m_LightsOnDelay = 17f;
    
    [SerializeField, Min(0f)]
    [Tooltip("Period of time over which all light elements are turned on at different moments.")]
    float m_LightsOnDuration = 1f;
    
    [Header("Night to Day")]
    [SerializeField]
    [Tooltip("Initial value (X) and end value (Y) of the time of day parameter when playing from Night to Day.")]
    Vector2 m_TimeOfDayNightToDay = new Vector2(0f, 12f);

    [SerializeField, Min(0f)]
    [Tooltip("Seconds to animate the parameter over time. 0 snaps immediately.")]
    float m_DurationNight = 35f;

    [SerializeField]
    [Tooltip("Easing curve: input is normalized time (0 to 1), output is interpolated progress between Initial Value and End Value.")]
    AnimationCurve m_CurveNight = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    
    [SerializeField, Min(0f)]
    [Tooltip("Delay in seconds before turning lights off.")]
    float m_LightsOffDelay = 18f;
    
    [SerializeField, Min(0f)]
    [Tooltip("Period of time over which all light elements are turned off at different moments.")]
    float m_LightsOffDuration = 1f;

    // Legacy fields for backward compatibility / migration
    [SerializeField, HideInInspector] float m_InitialValueDay = float.NaN;
    [SerializeField, HideInInspector] float m_EndValueDay = float.NaN;
    [SerializeField, HideInInspector] float m_InitialValueNight = float.NaN;
    [SerializeField, HideInInspector] float m_EndValueNight = float.NaN;

    public Vector2 TimeOfDayDay
    {
        get => m_TimeOfDayDayToNight;
        set => m_TimeOfDayDayToNight = value;
    }

    public Vector2 TimeOfDayNight
    {
        get => m_TimeOfDayNightToDay;
        set => m_TimeOfDayNightToDay = value;
    }

    public float DurationDay
    {
        get => m_DurationDay;
        set => m_DurationDay = Mathf.Max(0f, value);
    }

    public float DurationNight
    {
        get => m_DurationNight;
        set => m_DurationNight = Mathf.Max(0f, value);
    }

    public float LightsDelayDay
    {
        get => m_LightsOnDelay;
        set => m_LightsOnDelay = Mathf.Max(0f, value);
    }

    public float LightsDelayNight
    {
        get => m_LightsOffDelay;
        set => m_LightsOffDelay = Mathf.Max(0f, value);
    }

    public float LightsDurationDay
    {
        get => m_LightsOnDuration;
        set => m_LightsOnDuration = Mathf.Max(0f, value);
    }

    public float LightsDurationNight
    {
        get => m_LightsOffDuration;
        set => m_LightsOffDuration = Mathf.Max(0f, value);
    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {
        if (!float.IsNaN(m_InitialValueDay) && !float.IsNaN(m_EndValueDay))
        {
            m_TimeOfDayDayToNight = new Vector2(m_InitialValueDay, m_EndValueDay);
            m_InitialValueDay = float.NaN;
            m_EndValueDay = float.NaN;
        }
        if (!float.IsNaN(m_InitialValueNight) && !float.IsNaN(m_EndValueNight))
        {
            m_TimeOfDayNightToDay = new Vector2(m_InitialValueNight, m_EndValueNight);
            m_InitialValueNight = float.NaN;
            m_EndValueNight = float.NaN;
        }
    }

    void ISerializationCallbackReceiver.OnBeforeSerialize() { }

    const string k_TransitionDayToNightState = "Anim_Orrery_Transition_DayToNight";
    const string k_TransitionNightToDayState = "Anim_Orrery_Transition_NightToDay";
    const string k_IdleDayState = "Anim_Orrery_Idle";
    const string k_IdleNightState = "Anim_Orrery_Idle_Night";
    // The transition clips hold a one-frame wrap key (a full turn of the minute hand) at their
    // first or last frame, and they loop, so the very end wraps to frame 0. Skip that frame at each end.
    const float k_TransitionEdgeSeconds = 1f / 60f;
    // The idles keep the globe and rings wobbling, and the end of Day to Night is ~5 degrees off
    // Idle_Night's first frame, so blend between idle and transition instead of cutting.
    const float k_BlendSeconds = 0.5f;

    struct ScheduledLight
    {
        public GameObject element;
        public float triggerTime;
        public bool applied;
    }

    bool isNight = false;
    bool m_IsRunning;
    float m_OriginalValue;

    public override IEnumerator Run(Interaction interaction)
    {
        KinematicMover actor = interaction != null ? interaction.Player : null;

        m_OriginalValue = isNight ? m_TimeOfDayDayToNight.y : m_TimeOfDayDayToNight.x;
        m_IsRunning = true;

        float from = isNight ? m_TimeOfDayNightToDay.x : m_TimeOfDayDayToNight.x;
        float to = isNight ? m_TimeOfDayNightToDay.y : m_TimeOfDayDayToNight.y;
        float duration = isNight ? m_DurationNight : m_DurationDay;
        AnimationCurve curve = isNight ? m_CurveNight : m_CurveDay;

        float delay = isNight ? m_LightsOffDelay : m_LightsOnDelay;
        float lightsDuration = isNight ? m_LightsOffDuration : m_LightsOnDuration;
        bool turnOnLights = !isNight;

        DayCycleLightController lightsController;
        List<GameObject> lightElements = GetLightElements(out lightsController);
        ScheduledLight[] lightSchedule = BuildLightSchedule(lightElements, delay, lightsDuration);

        Animator orreryAnim = GetOrreryAnimator(interaction);
        string transitionState = isNight ? k_TransitionNightToDayState : k_TransitionDayToNightState;
        string endIdle = isNight ? k_IdleDayState : k_IdleNightState;

        if (duration <= 0f)
        {
            SetValue(to);
            ApplyAllLights(turnOnLights, lightElements, lightsController);
            if (orreryAnim != null)
            {
                orreryAnim.speed = 1f;
                orreryAnim.Play(endIdle, 0, 0f);
                orreryAnim.Update(0f);
            }
            isNight = !isNight;
            m_IsRunning = false;
            yield break;
        }

        SetValue(from);
        int startStateHash = 0;
        float startStateTime = 0f;
        if (orreryAnim != null)
        {
            var startState = orreryAnim.GetCurrentAnimatorStateInfo(0);
            startStateHash = startState.fullPathHash;
            startStateTime = startState.normalizedTime;
            orreryAnim.speed = 0f;
            orreryAnim.Play(transitionState, 0, 0f);
            orreryAnim.Update(0f);
        }

        float minProgress = 0f;
        float maxProgress = 1f;
        float transitionLength = 0f;
        if (orreryAnim != null)
        {
            var clips = orreryAnim.GetCurrentAnimatorClipInfo(0);
            if (clips.Length > 0 && clips[0].clip != null && clips[0].clip.length > 0f)
            {
                transitionLength = clips[0].clip.length;
                minProgress = k_TransitionEdgeSeconds / transitionLength;
                maxProgress = 1f - minProgress;
            }
            // Hold the idle's current pose; the loop below blends out of it.
            orreryAnim.Play(startStateHash, 0, startStateTime);
            orreryAnim.Update(0f);
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            float dt = interaction != null && interaction.DeltaTime > 0f ? interaction.DeltaTime : Time.deltaTime;
            elapsed += dt;

            float t = Mathf.Clamp01(elapsed / duration);
            float along = curve != null ? curve.Evaluate(t) : t;
            float current = Mathf.LerpUnclamped(from, to, along);

            SetValue(current);

            if (orreryAnim != null)
            {
                float animProgress = Mathf.Lerp(minProgress, maxProgress, along);
                if (elapsed < k_BlendSeconds)
                {
                    // Animator speed is 0, so drive the blend weight by hand each frame.
                    float weight = Mathf.SmoothStep(0f, 1f, elapsed / k_BlendSeconds);
                    orreryAnim.Play(startStateHash, 0, startStateTime);
                    orreryAnim.Update(0f);
                    orreryAnim.CrossFadeInFixedTime(transitionState, k_BlendSeconds, 0, animProgress * transitionLength, weight);
                }
                else
                {
                    orreryAnim.Play(transitionState, 0, animProgress);
                }
                orreryAnim.Update(0f);
            }

            for (int i = 0; i < lightSchedule.Length; i++)
            {
                if (!lightSchedule[i].applied && elapsed >= lightSchedule[i].triggerTime)
                {
                    lightSchedule[i].applied = true;
                    SetElementState(lightSchedule[i].element, turnOnLights, lightsController);
                }
            }

            yield return null;
        }

        // Apply exact destination value
        float finalAlong = curve != null ? curve.Evaluate(1f) : 1f;
        SetValue(Mathf.LerpUnclamped(from, to, finalAlong));

        for (int i = 0; i < lightSchedule.Length; i++)
        {
            if (!lightSchedule[i].applied)
            {
                lightSchedule[i].applied = true;
                SetElementState(lightSchedule[i].element, turnOnLights, lightsController);
            }
        }

        if (lightsController != null)
        {
            lightsController._isDayTime = !turnOnLights;
        }

        if (orreryAnim != null)
        {
            float blendElapsed = 0f;
            while (blendElapsed < k_BlendSeconds)
            {
                float weight = Mathf.SmoothStep(0f, 1f, blendElapsed / k_BlendSeconds);
                orreryAnim.Play(transitionState, 0, maxProgress);
                orreryAnim.Update(0f);
                orreryAnim.CrossFadeInFixedTime(endIdle, k_BlendSeconds, 0, blendElapsed, weight);
                orreryAnim.Update(0f);
                yield return null;
                blendElapsed += interaction != null && interaction.DeltaTime > 0f ? interaction.DeltaTime : Time.deltaTime;
            }

            orreryAnim.CrossFadeInFixedTime(endIdle, k_BlendSeconds, 0, blendElapsed, 1f);
            orreryAnim.Update(0f);
            orreryAnim.speed = 1f;
        }

        // Once the effect has happened, playReverse changes to its opposite state
        isNight = !isNight;
        m_IsRunning = false;
    }

    public override void Stop(Interaction interaction)
    {
        if (m_IsRunning)
        {
            SetValue(m_OriginalValue);
            DayCycleLightController lightsController;
            var lightElements = GetLightElements(out lightsController);
            ApplyAllLights(isNight, lightElements, lightsController);

            var orreryAnim = GetOrreryAnimator(interaction);
            if (orreryAnim != null)
            {
                orreryAnim.speed = 1f;
                orreryAnim.Play(isNight ? k_IdleNightState : k_IdleDayState, 0, 0f);
                orreryAnim.Update(0f);
            }
            m_IsRunning = false;
        }
    }

    ScheduledLight[] BuildLightSchedule(List<GameObject> elements, float startDelay, float period)
    {
        if (elements == null || elements.Count == 0)
            return System.Array.Empty<ScheduledLight>();

        int count = elements.Count;
        ScheduledLight[] schedule = new ScheduledLight[count];

        List<GameObject> shuffled = new List<GameObject>(elements);
        var rng = new System.Random();
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var temp = shuffled[i];
            shuffled[i] = shuffled[j];
            shuffled[j] = temp;
        }

        float staggerPeriod = Mathf.Max(0f, period);
        for (int i = 0; i < count; i++)
        {
            float fraction = count > 1 ? (float)i / (count - 1) : 0f;
            schedule[i] = new ScheduledLight
            {
                element = shuffled[i],
                triggerTime = startDelay + fraction * staggerPeriod,
                applied = false
            };
        }

        return schedule;
    }

    void SetValue(float value)
    {
        if (DayCycleControl == null) return;

        var skyLight = DayCycleControl.GetComponent<StylizedSky.SkyLightController>()
            ?? DayCycleControl.GetComponentInChildren<StylizedSky.SkyLightController>()
            ?? DayCycleControl.GetComponentInParent<StylizedSky.SkyLightController>();

        if (skyLight != null)
        {
            skyLight.TimeOfDay = value;
            return;
        }

        var timeOfDay = DayCycleControl.GetComponent<TimeOfDay>()
            ?? DayCycleControl.GetComponentInChildren<TimeOfDay>()
            ?? DayCycleControl.GetComponentInParent<TimeOfDay>();

        if (timeOfDay != null)
        {
            timeOfDay.TimeOfDay24Hr = value;
            return;
        }

        DayCycleControl.SendMessage("set_TimeOfDay", value, SendMessageOptions.DontRequireReceiver);
    }

    List<GameObject> GetLightElements(out DayCycleLightController controller)
    {
        controller = null;
        if (LightsControl == null) return new List<GameObject>();

        controller = LightsControl.GetComponent<DayCycleLightController>()
            ?? LightsControl.GetComponentInChildren<DayCycleLightController>()
            ?? LightsControl.GetComponentInParent<DayCycleLightController>();

        List<GameObject> elements = new List<GameObject>();

        if (controller != null)
        {
            var roots = controller.Lights;
            if (roots != null)
            {
                foreach (var root in roots)
                {
                    if (root == null) continue;

                    var swaps = root.GetComponentsInChildren<DayCycleMaterialSwap>(true);
                    var lights = root.GetComponentsInChildren<Light>(true);

                    if (swaps.Length == 0 && lights.Length == 0)
                    {
                        if (!elements.Contains(root))
                            elements.Add(root);
                        continue;
                    }

                    List<GameObject> fixtures = new List<GameObject>();
                    foreach (var s in swaps)
                    {
                        if (s != null && !fixtures.Contains(s.gameObject))
                            fixtures.Add(s.gameObject);
                    }
                    foreach (var l in lights)
                    {
                        if (l == null) continue;
                        var swapInParent = l.GetComponentInParent<DayCycleMaterialSwap>();
                        var target = swapInParent != null ? swapInParent.gameObject : l.gameObject;
                        if (!fixtures.Contains(target))
                            fixtures.Add(target);
                    }

                    foreach (var f in fixtures)
                    {
                        if (!elements.Contains(f))
                            elements.Add(f);
                    }
                }
            }
        }
        else
        {
            var swaps = LightsControl.GetComponentsInChildren<DayCycleMaterialSwap>(true);
            var lights = LightsControl.GetComponentsInChildren<Light>(true);
            foreach (var s in swaps)
            {
                if (s != null && !elements.Contains(s.gameObject))
                    elements.Add(s.gameObject);
            }
            foreach (var l in lights)
            {
                if (l == null) continue;
                var swapInParent = l.GetComponentInParent<DayCycleMaterialSwap>();
                var target = swapInParent != null ? swapInParent.gameObject : l.gameObject;
                if (!elements.Contains(target))
                    elements.Add(target);
            }
            if (elements.Count == 0)
            {
                elements.Add(LightsControl.gameObject);
            }
        }

        return elements;
    }

    void SetElementState(GameObject element, bool turnOn, DayCycleLightController controller)
    {
        if (element == null) return;

        if (controller != null)
        {
            controller.SetElement(element, turnOn);
            return;
        }

        var lights = element.GetComponentsInChildren<Light>(true);
        var swaps = element.GetComponentsInChildren<DayCycleMaterialSwap>(true);

        foreach (var light in lights)
        {
            light.gameObject.SetActive(turnOn);
        }

        foreach (var swap in swaps)
        {
            if (swap == null) continue;
            var r = swap.TargetRenderer;
            var mat = turnOn ? swap.NightMaterial : swap.DayMaterial;
            if (r != null && mat != null)
                r.sharedMaterial = mat;
        }

        if (lights.Length == 0 && swaps.Length == 0)
        {
            element.SendMessage("ApplyDayCycle", SendMessageOptions.DontRequireReceiver);
        }
    }

    void ApplyAllLights(bool turnOn, List<GameObject> elements, DayCycleLightController controller)
    {
        if (elements != null)
        {
            foreach (var el in elements)
            {
                SetElementState(el, turnOn, controller);
            }
        }

        if (controller != null)
        {
            controller._isDayTime = !turnOn;
        }
        else if (LightsControl != null && (elements == null || elements.Count == 0))
        {
            LightsControl.SendMessage("ApplyDayCycle", SendMessageOptions.DontRequireReceiver);
        }
    }

    void ApplyLights(KinematicMover actor)
    {
        DayCycleLightController lightsController;
        var lightElements = GetLightElements(out lightsController);
        ApplyAllLights(!isNight, lightElements, lightsController);
    }

    Animator GetOrreryAnimator(Interaction interaction)
    {
        if (Orrery != null)
        {
            if (!Orrery.enabled)
                Orrery.enabled = true;
            return Orrery;
        }

        if (interaction != null && interaction.Interactable != null)
        {
            var current = interaction.Interactable.transform;
            while (current != null)
            {
                var model = current.Find("Model");
                if (model != null && model.TryGetComponent(out Animator anim))
                {
                    if (!anim.enabled)
                        anim.enabled = true;
                    return anim;
                }
                current = current.parent;
            }
        }

        Transform[] roots = { LightsControl, DayCycleControl };
        foreach (var r in roots)
        {
            var current = r;
            while (current != null)
            {
                var model = current.Find("Model");
                if (model != null && model.TryGetComponent(out Animator anim))
                {
                    if (!anim.enabled)
                        anim.enabled = true;
                    return anim;
                }
                current = current.parent;
            }
        }

        var fallback = GameObject.Find("Gameplay/Interactions/Orrery/Model");
        if (fallback != null && fallback.TryGetComponent(out Animator fallbackAnim))
        {
            if (!fallbackAnim.enabled)
                fallbackAnim.enabled = true;
            return fallbackAnim;
        }

        return null;
    }
}
