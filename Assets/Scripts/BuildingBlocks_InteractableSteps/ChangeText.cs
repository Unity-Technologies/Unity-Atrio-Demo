using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Changes the text of a TextMeshProUGUI component.
/// Supports toggling between the original text and the specified text.
/// </summary>
[Serializable]
[Unity.BuildingBlocks.Gameplay.InteractionStep("Change Text")]
public class ChangeText : Unity.BuildingBlocks.Gameplay.InteractionStep
{
    // Delay is inherited from InteractionStep and appears at the top in the inspector.

    [Unity.BuildingBlocks.Gameplay.SceneObject]
    [Tooltip("The TextMeshProUGUI component whose text will be changed.")]
    public TMPro.TextMeshProUGUI Target;

    [TextArea]
    [Tooltip("The text to display on the TextMeshProUGUI component.")]
    public string Text;

    [Tooltip("If true, toggles between the original text and the specified Text on each execution.")]
    public bool Toggle;

    string m_OriginalText;
    bool m_HasCapturedOriginal;

    public override IEnumerator Run(Unity.BuildingBlocks.Gameplay.Interaction interaction)
    {
        if (Target == null)
            yield break;

        if (!m_HasCapturedOriginal)
        {
            m_OriginalText = Target.text;
            m_HasCapturedOriginal = true;
        }

        // TMP normalizes null text to an empty string.
        string newText = Text ?? string.Empty;
        if (Toggle && Target.text == newText)
        {
            Target.text = m_OriginalText;
        }
        else
        {
            Target.text = newText;
        }

        yield break;
    }
}
