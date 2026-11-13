using System;
using System.Collections;
using UnityEngine;
using Unity.BuildingBlocks.Gameplay;

/// <summary>
/// An interaction step that triggers an <see cref="Interactable"/> on a target <see cref="GameObject"/>
/// if that GameObject is active in the scene.
/// </summary>
[Serializable]
[InteractionStep("Trigger Interactable")]
public class TriggerInteractableStep : InteractionStep
{
    [SceneObject]
    [Tooltip("The GameObject in the scene that has an Interactable component.")]
    public GameObject Target;

    [Min(0f)]
    [Tooltip("Seconds to wait after the target interactable starts. Skipped if it cannot be triggered.")]
    public float DelayAfterTrigger;

    public override IEnumerator Run(Interaction interaction)
    {
        if (Target == null || !Target.activeInHierarchy)
            yield break;

        if (!Target.TryGetComponent<Interactable>(out var interactable))
        {
            interactable = Target.GetComponentInChildren<Interactable>();
        }

        if (interactable == null || !interactable.CanPlay)
            yield break;

        // An instant run can finish (or stop) inside Play, so IsPlaying alone
        // cannot tell whether the trigger was accepted.
        bool endedDuringPlay = false;
        Action<Interactable> onEnded = _ => endedDuringPlay = true;
        interactable.Finished += onEnded;
        interactable.Interrupted += onEnded;
        bool triggered;
        try
        {
            interactable.Play(interaction != null ? interaction.Player : null);
            triggered = endedDuringPlay || (interactable != null && interactable.IsPlaying);
        }
        finally
        {
            interactable.Finished -= onEnded;
            interactable.Interrupted -= onEnded;
        }

        if (triggered && DelayAfterTrigger > 0f)
        {
            if (interaction != null)
                yield return interaction.Wait(DelayAfterTrigger);
            else
                yield return new WaitForSeconds(DelayAfterTrigger);
        }
    }

    public override void Stop(Interaction interaction)
    {
    }
}
