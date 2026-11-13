using UnityEditor;
using UnityEditor.Events;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>Adds an Input Prompt that starts an Interactable, taking over its zone and button.</summary>
    static class InputPromptMenu
    {
        const string k_MenuPath = "CONTEXT/Interactable/Add Input Prompt (Experimental)";

        [MenuItem(k_MenuPath)]
        static void AddPrompt(MenuCommand command) => AddPrompt((Interactable)command.context);

        [MenuItem(k_MenuPath, true)]
        static bool CanAddPrompt(MenuCommand command) =>
            command.context is Interactable interactable && interactable.GetComponent<InputPrompt>() == null &&
            CanTakeOver(interactable);

        /// <summary>
        /// Only "press inside a zone" maps onto a prompt: without a zone the prompt never shows in full,
        /// and the other modes are not started by a button.
        /// </summary>
        internal static bool CanTakeOver(Interactable interactable) =>
            interactable.PlayOn == InteractablePlayOn.Interact && interactable.Zone != null;

        /// <summary>
        /// Adds a prompt on the interactable's object with its zone and a copy of its button, sets the
        /// interactable to Manual, wires the prompt's On Pressed to its Play, and hides the prompt while
        /// the interactable is off. Returns the prompt, or <c>null</c> when <see cref="CanTakeOver"/> is false.
        /// </summary>
        internal static InputPrompt AddPrompt(Interactable interactable)
        {
            InputPrompt prompt = interactable.GetComponent<InputPrompt>();
            if (prompt != null)
                return prompt;
            if (!CanTakeOver(interactable))
                return null;

            InputActionProperty button = interactable.Button;
            TriggerVolume zone = interactable.Zone;

            // Manual first: in Play mode it unwires and disables the interactable's own inline action.
            Undo.RecordObject(interactable, "Add Input Prompt");
            interactable.PlayOn = InteractablePlayOn.Manual;
            EditorUtility.SetDirty(interactable);

            prompt = Undo.AddComponent<InputPrompt>(interactable.gameObject);
            prompt.Zone = zone;
            // A copy, so neither component can disable the other's inline action.
            prompt.Button = button.reference != null
                ? new InputActionProperty(button.reference)
                : new InputActionProperty(button.action?.Clone());
            prompt.ShowWhileEnabled = interactable;
            UnityEventTools.AddVoidPersistentListener(prompt.OnPressed, new UnityAction(interactable.Play));
            EditorUtility.SetDirty(prompt);
            return prompt;
        }
    }
}
