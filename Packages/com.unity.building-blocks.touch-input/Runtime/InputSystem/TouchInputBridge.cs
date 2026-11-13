using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Publishes a <see cref="TouchRig"/> to the Input System as a <see cref="TouchRigDevice"/>, so actions can
    /// bind to on-screen controls, such as <c>&lt;TouchRig&gt;/move</c>. Add it to the rig's GameObject.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disabled controls stay on the device at rest, so a binding works as soon as they show. When enabled
    /// controls are added, removed or renamed, the device is rebuilt at the next update; a disabled control's
    /// new id takes effect when it is enabled. A rebuild cancels held buttons; held joysticks keep working.
    /// </para>
    /// <para>
    /// Actions see values one frame after <c>rig.Get&lt;T&gt;(id)</c>. Only one bridge can be enabled at a time;
    /// a second one warns and disables itself.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Input Bridge")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TouchRig))]
    public sealed class TouchInputBridge : MonoBehaviour
    {
        static TouchInputBridge s_Active;

        [SerializeField]
        [Tooltip("Action asset the inspector writes this rig's bindings into, or empty for the scene's asset or project-wide actions.")]
        InputActionAsset m_Actions;

        readonly List<TouchControl> m_Found = new List<TouchControl>();
        readonly List<string> m_Problems = new List<string>();

        TouchRig m_Rig;
        TouchRigDevice m_Device;
        TouchRigLayout.Entry[] m_Entries = Array.Empty<TouchRigLayout.Entry>();
        Slot[] m_Slots = Array.Empty<Slot>();
        NativeArray<byte> m_EventBuffer;
        InputEventPtr m_Event;
        Action m_OnBeforeUpdate;
        Action<TouchRig> m_OnControlsChanged;
        TouchInputBridge m_BlockedBy;
        bool m_Live;
        bool m_Dirty;
        int m_RebuildCount;

        /// <summary>The enabled bridge, or <c>null</c>.</summary>
        public static TouchInputBridge Active => s_Active;

        /// <summary>The device this bridge added, or <c>null</c> while it is disabled.</summary>
        public TouchRigDevice Device => m_Device;

        /// <summary>
        /// The action asset the Editor inspectors write this rig's bindings into; not read in Play. <c>null</c> uses
        /// the asset the scene references, then project-wide actions.
        /// </summary>
        public InputActionAsset Actions
        {
            get => m_Actions;
            set => m_Actions = value;
        }

        /// <summary>The rig this bridge publishes.</summary>
        public TouchRig Rig => m_Rig != null ? m_Rig : GetComponent<TouchRig>();

        /// <summary>The bridge that was already enabled when this one tried to enable, or <c>null</c>.</summary>
        internal TouchInputBridge BlockedBy => m_BlockedBy;

        /// <summary>What the device carries right now, in state order.</summary>
        internal IReadOnlyList<TouchRigLayout.Entry> Entries => m_Entries;

        /// <summary>How many times the device was replaced since this bridge was created. For tests.</summary>
        internal int RebuildCount => m_RebuildCount;

        /// <summary>Whether a change to the rig is waiting for the next update to be looked at. For tests.</summary>
        internal bool HasPendingChanges => m_Dirty;

        /// <summary>
        /// Rebuilds the device now, or does nothing while disabled; the bridge already rebuilds when controls change.
        /// Held buttons are canceled; held joysticks stay held.
        /// </summary>
        public void Rebuild()
        {
            if (!m_Live)
                return;

            CollectControls(m_Rig, m_Found);
            RebuildFrom(m_Found);
        }

        /// <summary>
        /// Queues one state event with every control's value, allocating nothing unless the rig changed. Runs once
        /// per Input System update; call it only in tests.
        /// </summary>
        public void Publish()
        {
            if (m_Dirty && m_Live)
                ApplyPendingChanges();

            if (m_Device == null || !m_EventBuffer.IsCreated)
                return;

            for (int i = 0; i < m_Slots.Length; i++)
                m_Slots[i].Write(m_Event);

            m_Event.time = InputState.currentTime;
            InputSystem.QueueEvent(m_Event);
        }

        void OnEnable() => Activate();

        void OnDisable() => Deactivate();

        /// <summary>
        /// The body of <c>OnEnable</c>: claims the one bridge slot, adds the device, starts publishing. Internal
        /// because EditMode tests never run a new component's <c>OnEnable</c>.
        /// </summary>
        internal void Activate()
        {
            if (m_Live)
                return;

            if (s_Active != null && s_Active != this)
            {
                m_BlockedBy = s_Active;
                Debug.LogWarning(
                    $"TouchInputBridge '{name}': '{s_Active.name}' already publishes a rig as <{TouchRigLayout.LayoutName}>, " +
                    "and there is one bridge at a time. This one is disabled; remove it or disable the other first.", this);
                enabled = false;
                return;
            }

#if ENABLE_INPUT_SYSTEM
            m_Live = true;
            m_BlockedBy = null;
            s_Active = this;
            m_Rig = GetComponent<TouchRig>();
            m_OnBeforeUpdate ??= Publish;
            m_OnControlsChanged ??= OnControlsChanged;

            CollectControls(m_Rig, m_Found);
            CreateDevice(m_Found);
            InputSystem.onBeforeUpdate += m_OnBeforeUpdate;
            if (m_Rig != null)
                m_Rig.ControlsChanged += m_OnControlsChanged;
#else
            Debug.LogWarning(
                $"TouchInputBridge '{name}': Active Input Handling does not include the Input System package, so there " +
                "is no device to publish to. Set Project Settings ▸ Player ▸ Active Input Handling to Input System or Both.", this);
            enabled = false;
#endif
        }

        /// <summary>The body of <c>OnDisable</c>: stops publishing and removes the device. See <see cref="Activate"/>.</summary>
        internal void Deactivate()
        {
            if (!m_Live)
                return;

            m_Live = false;
            m_Dirty = false;
            InputSystem.onBeforeUpdate -= m_OnBeforeUpdate;
            if (m_Rig != null)
                m_Rig.ControlsChanged -= m_OnControlsChanged;
            DestroyDevice();

            if (s_Active == this)
                s_Active = null;
        }

        void OnControlsChanged(TouchRig rig) => m_Dirty = true;

        /// <summary>
        /// Checks the rig once after any number of changes and rebuilds only if the device's controls differ.
        /// Runs at the start of <see cref="Publish"/>.
        /// </summary>
        internal void ApplyPendingChanges()
        {
            m_Dirty = false;
            if (!m_Live)
                return;

            CollectControls(m_Rig, m_Found);
            if (SameControls(m_Found))
                return;

            RebuildFrom(m_Found);
        }

        bool SameControls(List<TouchControl> controls)
        {
            m_Problems.Clear();
            TouchRigLayout.Entry[] planned = TouchRigLayout.Plan(controls, m_Problems);
            if (planned.Length != m_Entries.Length)
                return false;

            for (int i = 0; i < planned.Length; i++)
            {
                if (planned[i].Control != m_Entries[i].Control ||
                    planned[i].Kind != m_Entries[i].Kind ||
                    !string.Equals(planned[i].Name, m_Entries[i].Name, StringComparison.Ordinal) ||
                    !string.Equals(planned[i].DisplayName, m_Entries[i].DisplayName, StringComparison.Ordinal))
                    return false;
            }

            return true;
        }

        void RebuildFrom(List<TouchControl> controls)
        {
            // A held press would be canceled on the old device and performed again on the new one, so a held Jump
            // would jump twice. Reset presses instead. A joystick reports a level, not an edge, so it stays held.
            for (int i = 0; i < controls.Count; i++)
            {
                TouchControl control = controls[i];
                if (control != null && !(control is TouchJoystick))
                    control.Cancel();
            }

            DestroyDevice();
            CreateDevice(controls);
            m_RebuildCount++;
        }

        void CreateDevice(List<TouchControl> controls)
        {
            m_Dirty = false;
            m_Problems.Clear();
            TouchRigLayout.Entry[] entries = TouchRigLayout.Plan(controls, m_Problems);
            foreach (string problem in m_Problems)
                Debug.LogWarning($"TouchInputBridge '{name}': {problem}", this);

            // The builder may be called whenever the Input System reloads layouts; it only ever sees this plan.
            m_Entries = entries;
            InputSystem.RegisterLayoutBuilder(() => TouchRigLayout.Build(entries), TouchRigLayout.LayoutName);

            m_Device = (TouchRigDevice)InputSystem.AddDevice(TouchRigLayout.LayoutName);
            m_Slots = new Slot[entries.Length];
            for (int i = 0; i < entries.Length; i++)
                m_Slots[i] = new Slot(entries[i], m_Device);

            m_EventBuffer = StateEvent.From(m_Device, out m_Event, Allocator.Persistent);
        }

        void DestroyDevice()
        {
            for (int i = 0; i < m_Slots.Length; i++)
                m_Slots[i].Detach();

            m_Slots = Array.Empty<Slot>();
            m_Entries = Array.Empty<TouchRigLayout.Entry>();

            if (m_Device != null && m_Device.added)
                InputSystem.RemoveDevice(m_Device);

            m_Device = null;

            if (m_EventBuffer.IsCreated)
                m_EventBuffer.Dispose();

            m_Event = default;
        }

        /// <summary>
        /// The controls a bridge on <paramref name="rig"/> would publish: every control whose nearest rig is this
        /// one, enabled or not, in hierarchy order. Hiding a control does not change the layout.
        /// </summary>
        internal static void CollectControls(TouchRig rig, List<TouchControl> into)
        {
            into.Clear();
            if (rig == null)
                return;

            rig.GetComponentsInChildren(true, into);
            for (int i = into.Count - 1; i >= 0; i--)
            {
                if (into[i].GetComponentInParent<TouchRig>(true) != rig)
                    into.RemoveAt(i);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_Active = null;

        /// <summary>
        /// One rig control and its device control. It remembers a press between two publishes, so a quick tap is not lost.
        /// </summary>
        sealed class Slot
        {
            readonly TouchRigLayout.Kind m_Kind;
            readonly TouchControl m_Source;
            readonly TouchJoystick m_Joystick;
            readonly TouchButton m_Button;

            readonly StickControl m_Stick;
            readonly ButtonControl m_Pressed;

            readonly Action m_OnPressed;

            bool m_PressedSincePublish;

            public Slot(in TouchRigLayout.Entry entry, TouchRigDevice device)
            {
                m_Kind = entry.Kind;
                m_Source = entry.Control;

                switch (m_Kind)
                {
                    case TouchRigLayout.Kind.Stick:
                        m_Joystick = (TouchJoystick)entry.Control;
                        m_Stick = device.TryGetChildControl<StickControl>(entry.Name);
                        break;

                    case TouchRigLayout.Kind.Button:
                        m_Pressed = device.TryGetChildControl<ButtonControl>(entry.Name);
                        m_Button = entry.Control as TouchButton;
                        if (m_Button != null)
                        {
                            m_OnPressed = () => m_PressedSincePublish = true;
                            m_Button.Pressed += m_OnPressed;
                        }
                        break;
                }
            }

            public void Write(InputEventPtr stateEvent)
            {
                switch (m_Kind)
                {
                    case TouchRigLayout.Kind.Stick:
                        m_Stick.WriteValueIntoEvent(m_Joystick != null ? m_Joystick.Value : Vector2.zero, stateEvent);
                        break;

                    case TouchRigLayout.Kind.Button:
                        bool down = m_PressedSincePublish || (m_Source != null && m_Source.IsPressed);
                        m_PressedSincePublish = false;
                        m_Pressed.WriteValueIntoEvent(down ? 1f : 0f, stateEvent);
                        break;
                }
            }

            public void Detach()
            {
                if (m_Button != null)
                    m_Button.Pressed -= m_OnPressed;
            }
        }
    }
}
