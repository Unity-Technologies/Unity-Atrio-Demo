using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// The root of a set of touch controls. Gameplay finds a control here by its id, for example
    /// <c>rig.Get&lt;TouchJoystick&gt;("Move")</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A control registers with the nearest rig above it while enabled. A disabled control is not registered,
    /// so <see cref="Get{T}"/> returns <c>null</c> for it. If two controls share an id, you get a warning and
    /// the first one registered wins.
    /// </para>
    /// <para>
    /// <see cref="ShowOn"/> and <see cref="ShowWhen"/> fade the views out and stop them taking touches. Hiding
    /// releases a held control, so its value resets; it stays registered and readable from C#.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Rig")]
    [DisallowMultipleComponent]
    public sealed class TouchRig : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Platforms that show these controls; untick ones played with a gamepad or keyboard.")]
        TouchPlatforms m_ShowOn = TouchPlatform.All;

        [SerializeField]
        [Tooltip("When the controls show: always, with a touchscreen, or after the last input was a touch.")]
        TouchShowWhen m_ShowWhen = TouchShowWhen.Always;

        readonly List<TouchControl> m_Controls = new List<TouchControl>();
        readonly HashSet<string> m_ReportedDuplicates = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The platforms that show this rig's controls. Default <see cref="TouchPlatform.All"/>.</summary>
        public TouchPlatforms ShowOn
        {
            get => m_ShowOn;
            set => m_ShowOn = value;
        }

        /// <summary>When, on a platform in <see cref="ShowOn"/>, the controls show. Default <see cref="TouchShowWhen.Always"/>.</summary>
        public TouchShowWhen ShowWhen
        {
            get => m_ShowWhen;
            set => m_ShowWhen = value;
        }

        /// <summary>
        /// Whether this rig's controls show right now: the current platform is in <see cref="ShowOn"/> and
        /// <see cref="ShowWhen"/> holds (<see cref="TouchInputHints"/>).
        /// </summary>
        public bool IsShown => ShouldShow(m_ShowOn, m_ShowWhen, TouchPlatform.Current,
            TouchInputHints.TouchscreenPresent, TouchInputHints.LastInputWasTouch);

        /// <summary>The rule behind <see cref="IsShown"/>, with every input given.</summary>
        public static bool ShouldShow(TouchPlatforms showOn, TouchShowWhen when, TouchPlatforms current,
            bool touchscreenPresent, bool lastInputWasTouch)
        {
            if ((showOn & current) == 0)
                return false;

            switch (when)
            {
                case TouchShowWhen.TouchscreenPresent: return touchscreenPresent;
                case TouchShowWhen.LastInputWasTouch: return lastInputWasTouch;
                default: return true;
            }
        }

        /// <summary>Every control currently registered, in registration order.</summary>
        public IReadOnlyList<TouchControl> Controls => m_Controls;

        /// <summary>
        /// Raised when a control registers, leaves, or changes its <see cref="TouchControl.ActionId"/>. The
        /// argument is this rig.
        /// </summary>
        /// <remarks>
        /// One change can raise it more than once, so treat it as "look again". Do expensive work once later,
        /// not on every call.
        /// </remarks>
        public event Action<TouchRig> ControlsChanged;

        /// <summary>
        /// The control with this <see cref="TouchControl.ActionId"/> that is a <typeparamref name="T"/>, or
        /// <c>null</c> when there is none. Never throws.
        /// </summary>
        /// <example><code>
        /// var move = rig.Get&lt;TouchJoystick&gt;("Move");
        /// if (move != null) direction = move.Value;
        /// </code></example>
        public T Get<T>(string actionId) where T : TouchControl
        {
            if (string.IsNullOrEmpty(actionId))
                return null;

            for (int i = 0; i < m_Controls.Count; i++)
            {
                if (m_Controls[i] is T control && string.Equals(control.ActionId, actionId, StringComparison.Ordinal))
                    return control;
            }

            return null;
        }

        /// <summary>
        /// Like <see cref="Get{T}"/> without the type, for code that cannot call a generic method, such as
        /// hot-reloaded code. Cast the result: <c>rig.Find("Move") as TouchJoystick</c>.
        /// </summary>
        public TouchControl Find(string actionId)
        {
            if (string.IsNullOrEmpty(actionId))
                return null;

            for (int i = 0; i < m_Controls.Count; i++)
            {
                TouchControl control = m_Controls[i];
                if (control != null && string.Equals(control.ActionId, actionId, StringComparison.Ordinal))
                    return control;
            }

            return null;
        }

        /// <summary>Calls <see cref="TouchControl.Cancel"/> on every registered control, for example on pause.</summary>
        public void CancelAll()
        {
            for (int i = 0; i < m_Controls.Count; i++)
            {
                if (m_Controls[i] != null)
                    m_Controls[i].Cancel();
            }
        }

        internal void Register(TouchControl control)
        {
            if (control == null || m_Controls.Contains(control))
                return;

            string id = control.ActionId;
            if (!string.IsNullOrEmpty(id) && !m_ReportedDuplicates.Contains(id))
            {
                for (int i = 0; i < m_Controls.Count; i++)
                {
                    TouchControl other = m_Controls[i];
                    if (other == null || !string.Equals(other.ActionId, id, StringComparison.Ordinal))
                        continue;

                    m_ReportedDuplicates.Add(id);
                    Debug.LogWarning(
                        $"TouchRig '{name}': '{other.name}' and '{control.name}' both use ActionId \"{id}\". " +
                        $"Get<T>(\"{id}\") returns '{other.name}'; give the other a different id.", this);
                    break;
                }
            }

            m_Controls.Add(control);
            NotifyControlsChanged();
        }

        internal void Unregister(TouchControl control)
        {
            if (m_Controls.Remove(control))
                NotifyControlsChanged();
        }

        /// <summary>Raises <see cref="ControlsChanged"/>. Called by the registry and by a registered control whose id changed.</summary>
        internal void NotifyControlsChanged() => ControlsChanged?.Invoke(this);

        void OnDisable() => CancelAll();
    }
}
