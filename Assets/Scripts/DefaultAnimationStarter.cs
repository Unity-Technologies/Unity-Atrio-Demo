using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Allows selecting an AnimationClip in the Editor to act as the default animation state
/// that automatically plays when the game starts.
/// </summary>
public class DefaultAnimationStarter : MonoBehaviour
{
    [Tooltip("The animation clip that will be the default state playing on game start.")]
    [SerializeField] private AnimationClip defaultClip;
    
    [Tooltip("Randomize the starting time offset. Useful when placing multiple instances so they don't animate in sync.")]
    [SerializeField] private bool randomizeStartTime = false;

    //The Animator component to animate. If left empty, will automatically search this GameObject and its children.
    private Animator animator;
    
    // Runtime state
    private PlayableGraph _playableGraph;
    private AnimatorOverrideController _runtimeOverrideController;

    public Animator Animator
    {
        get
        {
            if (animator == null) EnsureAnimator();
            return animator;
        }
        set => animator = value;
    }

    public AnimationClip DefaultClip
    {
        get => defaultClip;
        set => defaultClip = value;
    }

    public bool RandomizeStartTime
    {
        get => randomizeStartTime;
        set => randomizeStartTime = value;
    }

    private void Reset()
    {
        EnsureAnimator();
    }

    private void Start()
    {
        PlayDefaultAnimation();
    }

    private void OnDestroy()
    {
        CleanupPlayableGraph();
    }

    /// <summary>
    /// Ensures an Animator reference is cached.
    /// </summary>
    public void EnsureAnimator()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
    }

    /// <summary>
    /// Plays the chosen default animation clip according to configuration.
    /// </summary>
    public void PlayDefaultAnimation()
    {
        if (defaultClip == null)
        {
            return;
        }

        EnsureAnimator();

        if (animator != null)
        {
            PlayOnAnimator();
            return;
        }
        
        Debug.LogWarning($"[{nameof(DefaultAnimationStarter)}] No Animator component found on '{gameObject.name}'.", this);
    }

    private void PlayOnAnimator()
    {
        if (animator == null || defaultClip == null) return;

        float startNormalizedTime = randomizeStartTime ? Random.value : 0f;

        // Case 1: Animator has a RuntimeAnimatorController assigned
        if (animator.runtimeAnimatorController != null)
        {
            ApplyClipOverride(startNormalizedTime);
        }
        else
        {
            // Case 2: No RuntimeAnimatorController - play directly via Playables API
            PlayViaPlayables(startNormalizedTime);
        }
    }

    private void ApplyClipOverride(float startNormalizedTime)
    {
        RuntimeAnimatorController baseController = animator.runtimeAnimatorController;
        if (baseController is AnimatorOverrideController aoc)
        {
            baseController = aoc.runtimeAnimatorController;
        }

        if (baseController == null) return;

        // Create a per-instance override controller so multiple instances in the scene
        // can have different default clips without mutating project assets
        _runtimeOverrideController = new AnimatorOverrideController(baseController);

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(_runtimeOverrideController.overridesCount);
        _runtimeOverrideController.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, defaultClip);
        }

        _runtimeOverrideController.ApplyOverrides(overrides);
        animator.runtimeAnimatorController = _runtimeOverrideController;

        if (animator.layerCount > 0)
        {
            animator.Play(0, 0, startNormalizedTime);
        }
    }

    private void PlayViaPlayables(float startNormalizedTime)
    {
        CleanupPlayableGraph();

        _playableGraph = PlayableGraph.Create($"{gameObject.name}_DefaultAnimation");
        var output = AnimationPlayableOutput.Create(_playableGraph, "Animation", animator);
        var clipPlayable = AnimationClipPlayable.Create(_playableGraph, defaultClip);

        output.SetSourcePlayable(clipPlayable);

        if (defaultClip.length > 0f && startNormalizedTime > 0f)
        {
            clipPlayable.SetTime(startNormalizedTime * defaultClip.length);
        }

        _playableGraph.Play();
    }

    private void CleanupPlayableGraph()
    {
        if (_playableGraph.IsValid())
        {
            _playableGraph.Destroy();
        }
    }
}
