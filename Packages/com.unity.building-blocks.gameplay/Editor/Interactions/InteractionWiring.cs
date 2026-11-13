using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Connects a <see cref="TriggerVolume"/> and an <see cref="Interactable"/>: the interactable names
    /// the volume in its <c>Zone</c> field; nothing is written on the volume.
    /// </summary>
    public static class InteractionWiring
    {
        /// <summary>An Input Prompt in the loaded scenes that hides with <paramref name="interactable"/>, the way the Add Input Prompt menu wires it, or <c>null</c>.</summary>
        internal static InputPrompt PromptReading(Interactable interactable)
        {
            if (interactable == null)
                return null;

            foreach (InputPrompt prompt in Object.FindObjectsByType<InputPrompt>(FindObjectsInactive.Include))
            {
                if (prompt.ShowWhileEnabled == interactable)
                    return prompt;
            }

            return null;
        }

        /// <summary>Whether <paramref name="prompt"/>'s On Pressed calls <c>Play</c> on <paramref name="interactable"/>.</summary>
        internal static bool PromptPlays(InputPrompt prompt, Interactable interactable)
        {
            if (prompt == null || interactable == null)
                return false;

            var pressed = prompt.OnPressed;
            for (int i = 0; i < pressed.GetPersistentEventCount(); i++)
            {
                if (pressed.GetPersistentTarget(i) == interactable && pressed.GetPersistentMethodName(i) == nameof(Interactable.Play))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sets <paramref name="interactable"/>'s Zone to <paramref name="volume"/>, its Starts to "on press"
        /// unless it already reads the zone, and enables it. One Undo step.
        /// </summary>
        /// <remarks>
        /// Starts becomes "on enter" instead when the volume does not detect the player (see
        /// <see cref="DetectsPlayer"/>). Returns <c>false</c> when either is <c>null</c> or already connected.
        /// </remarks>
        public static bool Connect(TriggerVolume volume, Interactable interactable)
        {
            if (volume == null || interactable == null || IsConnected(volume, interactable))
                return false;

            // Serialized object, not the properties: the setters do play-mode wiring.
            var so = new SerializedObject(interactable);
            so.FindProperty("m_Zone").objectReferenceValue = volume;
            SerializedProperty playOn = so.FindProperty("m_PlayOn");
            var mode = (InteractablePlayOn)playOn.intValue;
            if (mode != InteractablePlayOn.Interact && mode != InteractablePlayOn.EnterZone)
                playOn.intValue = (int)(DetectsPlayer(volume) ? InteractablePlayOn.Interact : InteractablePlayOn.EnterZone);
            so.FindProperty("m_Enabled").boolValue = true;
            so.ApplyModifiedProperties();
            return true;
        }

        /// <summary>
        /// Whether any <see cref="KinematicMover"/> in the loaded scenes passes <paramref name="volume"/>'s
        /// Detects (needed for On press). True when no character is loaded.
        /// </summary>
        public static bool DetectsPlayer(TriggerVolume volume)
        {
            if (volume == null) return false;

            bool anyPlayer = false;
            foreach (KinematicMover mover in Object.FindObjectsByType<KinematicMover>(FindObjectsInactive.Include))
            {
                anyPlayer = true;
                foreach (Collider collider in mover.GetComponentsInChildren<Collider>(true))
                    if (volume.Accepts(collider)) return true;
            }

            return !anyPlayer;
        }

        /// <summary>Whether <paramref name="interactable"/>'s Zone is <paramref name="volume"/>.</summary>
        public static bool IsConnected(TriggerVolume volume, Interactable interactable) =>
            volume != null && interactable != null && interactable.Zone == volume;
    }
}
