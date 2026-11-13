using System;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// The platforms a <see cref="TouchRig"/> shows its controls on. Flags, so any mix can be ticked, like a layer mask.
    /// </summary>
    [Flags]
    public enum TouchPlatforms
    {
        /// <summary>iPhone and iPad builds.</summary>
        [InspectorName("iOS")] IOS = 1 << 0,

        /// <summary>Android builds, phones and tablets.</summary>
        Android = 1 << 1,

        /// <summary>A Web build in a phone or tablet browser.</summary>
        [InspectorName("Web (mobile browser)")] WebMobile = 1 << 2,

        /// <summary>A Web build in a desktop browser.</summary>
        [InspectorName("Web (desktop browser)")] WebDesktop = 1 << 3,

        /// <summary>Windows, macOS and Linux players, and anything not listed here.</summary>
        Desktop = 1 << 4,

        /// <summary>PlayStation, Xbox, Switch and Apple TV.</summary>
        Console = 1 << 5,

        /// <summary>The Editor's Game view. In the Device Simulator, the simulated phone's platform counts instead.</summary>
        Editor = 1 << 6,
    }

    /// <summary>Which <see cref="TouchPlatforms"/> value the game runs on.</summary>
    public static class TouchPlatform
    {
        /// <summary>Every platform. The default for <see cref="TouchRig.ShowOn"/>.</summary>
        public const TouchPlatforms All = (TouchPlatforms)~0;

        static RuntimePlatform s_LastPlatform = (RuntimePlatform)(-1);
        static bool s_LastMobile;
        static TouchPlatforms s_Last;

        /// <summary>
        /// The platform the game runs on now. In the Device Simulator this is the simulated phone, not the Editor.
        /// </summary>
        public static TouchPlatforms Current
        {
            get
            {
                RuntimePlatform platform = UnityEngine.Device.Application.platform;
                bool mobile = UnityEngine.Device.Application.isMobilePlatform;
                if (platform != s_LastPlatform || mobile != s_LastMobile)
                {
                    s_LastPlatform = platform;
                    s_LastMobile = mobile;
                    s_Last = From(platform, mobile);
                }

                return s_Last;
            }
        }

        /// <summary>
        /// The <see cref="TouchPlatforms"/> group <paramref name="platform"/> belongs to.
        /// <paramref name="isMobilePlatform"/> only matters for Web.
        /// </summary>
        public static TouchPlatforms From(RuntimePlatform platform, bool isMobilePlatform)
        {
            switch (platform)
            {
                case RuntimePlatform.OSXEditor:
                case RuntimePlatform.WindowsEditor:
                case RuntimePlatform.LinuxEditor:
                    return TouchPlatforms.Editor;
                case RuntimePlatform.IPhonePlayer:
                    return TouchPlatforms.IOS;
                case RuntimePlatform.Android:
                    return TouchPlatforms.Android;
                case RuntimePlatform.WebGLPlayer:
                    return isMobilePlatform ? TouchPlatforms.WebMobile : TouchPlatforms.WebDesktop;
                case RuntimePlatform.tvOS:
                    return TouchPlatforms.Console;
            }

            // Console platforms change between editor versions, so match them by name.
            string name = platform.ToString();
            if (name.StartsWith("PS", StringComparison.Ordinal) || name.Contains("Xbox") || name.StartsWith("Switch", StringComparison.Ordinal))
                return TouchPlatforms.Console;

            return TouchPlatforms.Desktop;
        }
    }

    /// <summary>When, on a platform <see cref="TouchRig.ShowOn"/> allows, the rig's controls are shown.</summary>
    public enum TouchShowWhen
    {
        /// <summary>Always.</summary>
        Always,

        /// <summary>Only while the device has a touchscreen.</summary>
        [InspectorName("Touchscreen present")] TouchscreenPresent,

        /// <summary>
        /// Only while the last input was a touch. A gamepad or keyboard press hides the controls until the next touch.
        /// </summary>
        [InspectorName("Last input was touch")] LastInputWasTouch,
    }

    /// <summary>
    /// What the input hardware reports, for <see cref="TouchShowWhen"/>. The Input System sets these values;
    /// without that package, the fallbacks apply.
    /// </summary>
    public static class TouchInputHints
    {
        static bool? s_TouchscreenPresent;
        static bool s_LastInputWasTouch = true;

        /// <summary>
        /// Whether the device has a touchscreen. Without the Input System, whether the platform is mobile.
        /// </summary>
        public static bool TouchscreenPresent
        {
            get => s_TouchscreenPresent ?? UnityEngine.Device.Application.isMobilePlatform;
            internal set => s_TouchscreenPresent = value;
        }

        /// <summary>
        /// Whether the last input was a touch, or a mouse click in the Editor. True until a gamepad, joystick or
        /// keyboard press, and always true without the Input System.
        /// </summary>
        public static bool LastInputWasTouch
        {
            get => s_LastInputWasTouch;
            internal set => s_LastInputWasTouch = value;
        }

        /// <summary>Clears reported values so the fallbacks apply. For tests and a fresh Play.</summary>
        internal static void Reset()
        {
            s_TouchscreenPresent = null;
            s_LastInputWasTouch = true;
        }
    }
}
