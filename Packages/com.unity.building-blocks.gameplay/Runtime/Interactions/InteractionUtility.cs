using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>Helpers the interaction components share, so none of them has to reference another.</summary>
    static class InteractionUtility
    {
        /// <summary>The first mover under the object tagged Player, or <c>null</c>.</summary>
        internal static KinematicMover FindPlayer()
        {
            GameObject tagged = GameObject.FindWithTag(TriggerVolume.k_DefaultTag);
            return tagged != null ? tagged.GetComponentInParent<KinematicMover>() : null;
        }

        // Tests point this at their own eye; it may return null to mean "no camera".
        internal static Func<Transform> s_EyeOverride;

        /// <summary>The camera the player looks through: the main camera, unless a test overrides it.</summary>
        internal static Transform Eye()
        {
            if (s_EyeOverride != null)
                return s_EyeOverride();

            Camera camera = Camera.main;
            return camera != null ? camera.transform : null;
        }

        /// <summary>A copy of an inline action's serialized fields, without the binding cache the original holds.</summary>
        /// <remarks>
        /// Unity deserializes into a field initializer's action and keeps its binding cache when the saved
        /// binding count differs, so saved bindings (a rebind, touch) would be ignored. A copy starts uncached.
        /// </remarks>
        internal static InputAction Uncached(InputAction action)
        {
            var copy = new InputAction();
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(action), copy);
            return copy;
        }
    }
}
