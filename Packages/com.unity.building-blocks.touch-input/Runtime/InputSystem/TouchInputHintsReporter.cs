using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Fills <see cref="TouchInputHints"/> from the Input System while the game runs. It installs itself before
    /// the first scene loads.
    /// </summary>
    /// <remarks>
    /// In the Editor a mouse click counts as a touch. In a player the mouse is ignored, because some phone
    /// browsers send a click after every tap.
    /// </remarks>
    static class TouchInputHintsReporter
    {
        const float k_PressMagnitude = 0.5f;

        static Action<InputEventPtr, InputDevice> s_OnEvent;
        static Action<InputDevice, InputDeviceChange> s_OnDeviceChange;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InstallOnLoad() => Install();

        /// <summary>Starts reporting, from a clean slate. Safe to call twice.</summary>
        internal static void Install()
        {
            Uninstall();
            TouchInputHints.Reset();
            s_OnEvent = OnEvent;
            s_OnDeviceChange = (_, __) => UpdateTouchscreen();
            InputSystem.onEvent += s_OnEvent;
            InputSystem.onDeviceChange += s_OnDeviceChange;
            UpdateTouchscreen();
        }

        /// <summary>Stops reporting and restores the fallbacks.</summary>
        internal static void Uninstall()
        {
            if (s_OnEvent != null)
                InputSystem.onEvent -= s_OnEvent;
            if (s_OnDeviceChange != null)
                InputSystem.onDeviceChange -= s_OnDeviceChange;
            s_OnEvent = null;
            s_OnDeviceChange = null;
            TouchInputHints.Reset();
        }

        static void UpdateTouchscreen() => TouchInputHints.TouchscreenPresent = Touchscreen.current != null;

        static void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device == null || device is TouchRigDevice)
                return;
            if (!eventPtr.IsA<StateEvent>() && !eventPtr.IsA<DeltaStateEvent>())
                return;

            switch (device)
            {
                case Touchscreen _:
                    // A touchscreen only sends events while a finger is on it.
                    TouchInputHints.LastInputWasTouch = true;
                    break;

                case Mouse _:
                    if (Application.isEditor && eventPtr.HasButtonPress())
                        TouchInputHints.LastInputWasTouch = true;
                    break;

                case Gamepad _:
                case Joystick _:
                case Keyboard _:
                    if (eventPtr.HasButtonPress(k_PressMagnitude, buttonControlsOnly: false))
                        TouchInputHints.LastInputWasTouch = false;
                    break;
            }
        }
    }
}
