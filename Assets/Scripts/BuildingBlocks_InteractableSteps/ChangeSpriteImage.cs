using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using Unity.BuildingBlocks.Gameplay;

/// <summary>
/// Changes the sprite (SourceImage) of a uGUI Image component.
/// Supports toggling between the original sprite and the specified sprite.
/// </summary>
[Serializable]
[InteractionStep("Change Sprite Image")]
public class ChangeSpriteImage : InteractionStep
{
    // Note: Delay is inherited from InteractionStep (public float Delay) and appears at the top in the inspector.

    [SceneObject]
    [Tooltip("The Image component (or GameObject with an Image component) whose sprite will be changed.")]
    public Image Target;

    [Tooltip("The sprite to display on the Image component.")]
    public Sprite Sprite;

    [Tooltip("If true, toggles between the original sprite and the specified Sprite on each execution.")]
    public bool Toggle;

    // Stores the initial sprite captured before any swap occurred
    Sprite m_OriginalSprite;
    bool m_HasCapturedOriginal;

    public override IEnumerator Run(Interaction interaction)
    {
        if (Target == null)
            yield break;

        if (!m_HasCapturedOriginal)
        {
            m_OriginalSprite = Target.sprite;
            m_HasCapturedOriginal = true;
        }

        if (Toggle)
        {
            // Toggle between the original sprite and the exposed Sprite
            if (Target.sprite == Sprite)
            {
                Target.sprite = m_OriginalSprite;
            }
            else
            {
                Target.sprite = Sprite;
            }
        }
        else
        {
            Target.sprite = Sprite;
        }

        yield break;
    }

    public override void Stop(Interaction interaction)
    {
        // Put back anything Run changed, if the interactable is stopped early.
    }
}
