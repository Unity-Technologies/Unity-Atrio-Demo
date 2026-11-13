using UnityEngine.Scripting;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>What <see cref="TouchCommands.GetRigs"/> (<c>touch_rig_get</c>) returns: every rig it looked at.</summary>
    [Preserve]
    public sealed class TouchRigListInfo
    {
        /// <summary>Whether the game is running (Play in the Editor, or a Player).</summary>
        [Preserve] public bool playing;

        /// <summary>The rigs in the loaded scenes, in scene and hierarchy order (one when a rig name was given).</summary>
        [Preserve] public TouchRigInfo[] rigs;
    }

    /// <summary>One <see cref="TouchRig"/> and its controls.</summary>
    [Preserve]
    public sealed class TouchRigInfo
    {
        /// <summary>The rig's GameObject name: what the commands' <c>rig</c> argument matches.</summary>
        [Preserve] public string name;

        /// <summary>Name of the scene the rig is in.</summary>
        [Preserve] public string scene;

        /// <summary>Hierarchy path of the rig in its scene, <c>/Canvas/Touch Rig</c>.</summary>
        [Preserve] public string path;

        /// <summary>Whether the rig carries a <c>TouchInputBridge</c> (its controls are also a <c>&lt;TouchRig&gt;</c> device).</summary>
        [Preserve] public bool bridge;

        /// <summary>
        /// Every control whose nearest rig is this one, enabled or not, in hierarchy order: the same set
        /// <see cref="TouchTuning"/> covers.
        /// </summary>
        [Preserve] public TouchControlInfo[] controls;
    }

    /// <summary>One control as the commands see it: its id, state, value and Feel settings.</summary>
    [Preserve]
    public sealed class TouchControlInfo
    {
        /// <summary>The control's <see cref="TouchControl.ActionId"/>.</summary>
        [Preserve] public string id;

        /// <summary><c>Joystick</c> or <c>Button</c> (the tuning JSON's <c>type</c>).</summary>
        [Preserve] public string type;

        /// <summary>The control's GameObject name.</summary>
        [Preserve] public string gameObject;

        /// <summary>Whether the component is enabled on an active GameObject. A hidden control is still tuned and is still on the device, at rest.</summary>
        [Preserve] public bool enabled;

        /// <summary>Whether the rig's registry returns it (<see cref="TouchRig.Find"/>). Only while the game runs, and only when shown.</summary>
        [Preserve] public bool registered;

        /// <summary>The Input System binding path that reads it, <c>&lt;TouchRig&gt;/move</c>; <c>null</c> without the Input System.</summary>
        [Preserve] public string binding;

        /// <summary>
        /// Actions (<c>Map/Action</c>) this control drives through a mapping added while the game runs (<c>touch_map</c>,
        /// <c>TouchRebinding.Map</c>), not through the action asset. Saved by <c>touch_save</c>; <c>null</c> without the Input System.
        /// </summary>
        [Preserve] public string[] mappedAtRuntime;

        /// <summary>Whether a finger is on it now.</summary>
        [Preserve] public bool pressed;

        /// <summary>A joystick's <see cref="TouchJoystick.Value"/> as <c>[x, y]</c>; <c>null</c> for a button.</summary>
        [Preserve] public float[] value;

        /// <summary>A button's <see cref="TouchButton.HoldSeconds"/>; 0 for a joystick.</summary>
        [Preserve] public float holdSeconds;

        /// <summary>A joystick's Feel settings, as the tuning JSON names them; <c>null</c> for a button (it has none yet).</summary>
        [Preserve] public TouchJoystickTuningInfo tuning;
    }

    /// <summary>A joystick's Feel settings under the names <c>touch_tune</c> and the tuning JSON use.</summary>
    [Preserve]
    public sealed class TouchJoystickTuningInfo
    {
        /// <summary><c>Fixed</c>, <c>Floating</c> or <c>FloatingWithLeash</c>.</summary>
        [Preserve] public string originMode;

        /// <summary><see cref="TouchJoystick.DeadZone"/>, 0..1.</summary>
        [Preserve] public float deadZone;

        /// <summary><see cref="TouchJoystick.Saturation"/>, 0..1.</summary>
        [Preserve] public float saturation;

        /// <summary><see cref="TouchJoystick.RecenterOnRelease"/>.</summary>
        [Preserve] public bool recenterOnRelease;
    }

    /// <summary>What <see cref="TouchCommands.Tune"/> (<c>touch_tune</c>) returns: the <see cref="TouchTuningResult"/>, and the control after.</summary>
    [Preserve]
    public sealed class TouchTuneInfo
    {
        /// <summary>The rig that was tuned.</summary>
        [Preserve] public string rig;

        /// <summary><see cref="TouchTuningResult.Parsed"/>: always <c>true</c> here (unreadable JSON is an error).</summary>
        [Preserve] public bool parsed;

        /// <summary><see cref="TouchTuningResult.ControlsApplied"/>, summed over the JSON and the single setting.</summary>
        [Preserve] public int controlsApplied;

        /// <summary><see cref="TouchTuningResult.Problems"/> of the JSON: what was skipped. A single setting that cannot be applied is an error instead.</summary>
        [Preserve] public string[] problems;

        /// <summary><see cref="TouchTuningResult.Clean"/>: nothing skipped.</summary>
        [Preserve] public bool clean;

        /// <summary>The control the single setting went to, read back after; <c>null</c> when only JSON was applied.</summary>
        [Preserve] public TouchControlInfo control;
    }

    /// <summary>What <see cref="TouchCommands.Rebind"/> (<c>touch_rebind</c>), <c>touch_map</c> and <c>touch_unmap</c> return.</summary>
    [Preserve]
    public sealed class TouchRebindInfo
    {
        /// <summary>The action, <c>Map/Action</c>.</summary>
        [Preserve] public string action;

        /// <summary>How many live copies of that action were changed (an asset and a <c>PlayerInput</c>'s copy of it count twice).</summary>
        [Preserve] public int actions;

        /// <summary><see cref="TouchRebindResult.BindingsChanged"/>, summed.</summary>
        [Preserve] public int bindingsChanged;

        /// <summary><see cref="TouchRebindResult.Problems"/>: warnings such as "the device has no control 'pet' yet".</summary>
        [Preserve] public string[] problems;

        /// <summary>The action's binding paths after the change, with overrides applied (first copy).</summary>
        [Preserve] public string[] bindings;

        /// <summary>The binding paths on the action that were added while the game runs (<c>touch_map</c>), first copy.</summary>
        [Preserve] public string[] mappedAtRuntime;
    }

    /// <summary>What <see cref="TouchCommands.Save"/> (<c>touch_save</c>) returns: every file written and what is in it.</summary>
    [Preserve]
    public sealed class TouchSaveInfo
    {
        /// <summary>One entry per rig saved with <see cref="TouchTuning.Save(TouchRig)"/>.</summary>
        [Preserve] public TouchSavedFile[] tuning;

        /// <summary>One entry per action asset whose binding overrides and runtime mappings were saved with <c>TouchRebinding.Save</c>; empty without the Input System.</summary>
        [Preserve] public TouchSavedFile[] rebinds;
    }

    /// <summary>One saved file and its text, so it can be pulled off a phone and applied elsewhere.</summary>
    [Preserve]
    public sealed class TouchSavedFile
    {
        /// <summary>The rig name (tuning) or the action asset name (rebinds).</summary>
        [Preserve] public string name;

        /// <summary>Where the file was written on the machine running the game.</summary>
        [Preserve] public string path;

        /// <summary>The text written: tuning JSON for <c>touch_tune --tuning</c>, or the Input System's override JSON.</summary>
        [Preserve] public string json;
    }
}
