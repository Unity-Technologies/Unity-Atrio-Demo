using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>Remembers which kind of device the player pressed last, so prompts can show the matching glyph.</summary>
    /// <remarks>
    /// Only button presses and touches count, so a resting stick or a mouse nudge does not flip the glyphs.
    /// Devices that are not a keyboard, mouse, gamepad or touchscreen, such as on-screen controls, are ignored.
    /// </remarks>
    public static class InputDeviceTracker
    {
        const float k_PressMagnitude = 0.5f;

        static Action<InputEventPtr, InputDevice> s_OnEvent;
        static InputDeviceKind s_Current;

        /// <summary>
        /// The kind of device pressed last. Starts as touch on a phone, A-Right gamepad on an A-Right platform,
        /// else keyboard and mouse. Any gamepad counts as A-Right on an A-Right platform, or in the Editor while
        /// the active build target is one.
        /// </summary>
        public static InputDeviceKind Current => s_Current;

        /// <summary>Fires when <see cref="Current"/> changes.</summary>
        public static event Action<InputDeviceKind> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void InstallOnLoad()
        {
            if (s_OnEvent != null)
                InputSystem.onEvent -= s_OnEvent;
            Changed = null;
            s_Current = UsesARightLayout() ? InputDeviceKind.ARightGamepad
                : Application.isMobilePlatform && Touchscreen.current != null ? InputDeviceKind.Touch
                : InputDeviceKind.KeyboardMouse;
            s_OnEvent = OnEvent;
            InputSystem.onEvent += s_OnEvent;
        }

        static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device == null || (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>()))
                return;

            switch (device)
            {
                case Touchscreen _:
                    // A touchscreen only sends events while a finger is on it.
                    Set(InputDeviceKind.Touch);
                    break;
                case Gamepad _:
                    if (eventPtr.HasButtonPress(k_PressMagnitude, buttonControlsOnly: false))
                        Set(UsesARightLayout() ? InputDeviceKind.ARightGamepad : InputDeviceKind.Gamepad);
                    break;
                case Keyboard _:
                case Mouse _:
                    if (eventPtr.HasButtonPress())
                        Set(InputDeviceKind.KeyboardMouse);
                    break;
            }
        }

        // A-Right platforms are matched by platform name prefix, so this builds without their platform modules
        // installed and covers newer models. In the Editor the active build target counts too, so Play Mode while
        // targeting one shows the A-Right look.
        const string k_ARightPlatformPrefix = "Switch";

        static bool UsesARightLayout()
        {
#if UNITY_EDITOR
            if (UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString().StartsWith(k_ARightPlatformPrefix, StringComparison.Ordinal))
                return true;
#endif
            return Application.platform.ToString().StartsWith(k_ARightPlatformPrefix, StringComparison.Ordinal);
        }

        static void Set(InputDeviceKind kind)
        {
            if (s_Current == kind)
                return;
            s_Current = kind;
            Changed?.Invoke(kind);
        }
    }
}
