using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// A <see cref="TouchRig"/> as an Input System device, so an action can bind to an on-screen control, such
    /// as <c>&lt;TouchRig&gt;/move</c>. A <see cref="TouchInputBridge"/> creates and feeds it; do not create one yourself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Control names come from the ids: <c>Move</c> becomes <c>move</c> and <c>Look Camera</c> becomes
    /// <c>lookCamera</c> (see <see cref="ControlNameFor"/>). It is its own device, not a gamepad, so a "Touch"
    /// control scheme lists <c>&lt;Touchscreen&gt;</c> and <c>&lt;TouchRig&gt;</c> together.
    /// </para>
    /// <para>
    /// Actions see a value one frame later than <c>rig.Get&lt;T&gt;(id)</c>, because uGUI delivers pointer
    /// events after the Input System updates.
    /// </para>
    /// </remarks>
    public sealed class TouchRigDevice : InputDevice
    {
        /// <summary>The <see cref="TouchRigDevice"/> that last received input, or <c>null</c>.</summary>
        public static TouchRigDevice current { get; private set; }

        /// <summary>The device control name for <paramref name="actionId"/>, or empty if no valid characters remain.</summary>
        public static string ControlNameFor(string actionId) => TouchRigLayout.ControlName(actionId);

        /// <summary>The binding path of the control with <paramref name="actionId"/>: <c>"&lt;TouchRig&gt;/move"</c> for <c>"Move"</c>.</summary>
        public static string BindingPathFor(string actionId) => TouchRigLayout.PathPrefix + ControlNameFor(actionId);

        /// <summary>The stick for the joystick with this ActionId, or <c>null</c>.</summary>
        public StickControl Stick(string actionId) => Find<StickControl>(actionId);

        /// <summary>The button for the button with this ActionId, or <c>null</c>.</summary>
        public ButtonControl Button(string actionId) => Find<ButtonControl>(actionId);

        /// <inheritdoc/>
        public override void MakeCurrent()
        {
            base.MakeCurrent();
            current = this;
        }

        /// <inheritdoc/>
        protected override void OnRemoved()
        {
            base.OnRemoved();
            if (current == this)
                current = null;
        }

        T Find<T>(string actionId) where T : InputControl
        {
            string name = ControlNameFor(actionId);
            return name.Length == 0 ? null : TryGetChildControl<T>(name);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => current = null;
    }
}
