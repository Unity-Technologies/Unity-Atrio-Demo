namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>What <c>touch_scene_get</c> returns: everything an agent needs to decide which controls a scene wants.</summary>
    public sealed class TouchSceneInfo
    {
        /// <summary>Whether the Editor is in Play. The authoring commands refuse to run in Play.</summary>
        public bool playing;

        /// <summary>The canvas the rigs sit on, or <c>null</c> when the scene has no rig yet.</summary>
        public TouchCanvasInfo canvas;

        /// <summary>Every Touch Rig in the loaded scenes and its controls, hidden ones included.</summary>
        public TouchSceneRigInfo[] rigs;

        /// <summary>Every input action the loaded scenes read, and which touch controls drive it. Empty without the Input System.</summary>
        public TouchActionInfo[] actions;

        /// <summary>The Rig check's findings for every rig (what the rig inspector shows), worst first.</summary>
        public TouchCheckInfo[] checks;
    }

    /// <summary>The canvas a rig is drawn on. Placement numbers are in its units.</summary>
    public sealed class TouchCanvasInfo
    {
        /// <summary>The canvas GameObject's name.</summary>
        public string name;

        /// <summary>The Canvas Scaler's reference resolution <c>[width, height]</c>, or <c>null</c> without a scaler.</summary>
        public float[] referenceResolution;

        /// <summary>The Canvas Scaler's width/height match (0 width, 1 height).</summary>
        public float match;

        /// <summary>The canvas's size right now <c>[width, height]</c>, in canvas units (depends on the Game view).</summary>
        public float[] size;
    }

    /// <summary>One rig and its controls with their placement.</summary>
    public sealed class TouchSceneRigInfo
    {
        /// <summary>The rig's GameObject name: what the commands' <c>rig</c> argument matches.</summary>
        public string name;

        /// <summary>Name of the scene the rig is in.</summary>
        public string scene;

        /// <summary>Hierarchy path of the rig, <c>/Touch Canvas/Safe Area/Touch Rig</c>.</summary>
        public string path;

        /// <summary>The rig's controls in hierarchy order.</summary>
        public TouchPlacedControlInfo[] controls;
    }

    /// <summary>One control: what it is, where it sits and what it drives.</summary>
    public sealed class TouchPlacedControlInfo
    {
        /// <summary>The control's id (its <c>&lt;TouchRig&gt;/id</c> binding name is made from it).</summary>
        public string id;

        /// <summary><c>Joystick</c> or <c>Button</c>.</summary>
        public string type;

        /// <summary>The rig's GameObject name.</summary>
        public string rig;

        /// <summary>Whether it is shown (component enabled on an active GameObject).</summary>
        public bool shown;

        /// <summary>
        /// <c>bottom_left</c>, <c>bottom</c>, <c>bottom_right</c>, <c>left</c>, <c>center</c>, <c>right</c>, <c>top_left</c>,
        /// <c>top</c>, <c>top_right</c>, or <c>custom</c> when the anchors are not one of those.
        /// </summary>
        public string anchor;

        /// <summary>Distance from the anchor to the control's centre, measured into the screen; signed (+ right) on a centred axis.</summary>
        public float x;

        /// <summary>Distance from the anchor to the control's centre, measured into the screen; signed (+ up) on a centred axis.</summary>
        public float y;

        /// <summary>The control's width (controls are square), in canvas units.</summary>
        public float size;

        /// <summary>The actions it drives, as <c>touch_scene_get</c>'s action handles. Empty when it drives nothing.</summary>
        public string[] drives;
    }

    /// <summary>One input action the loaded scenes read.</summary>
    public sealed class TouchActionInfo
    {
        /// <summary>
        /// The handle the commands' <c>action</c> argument takes: <c>Asset:Map/Action</c> for an asset's action,
        /// <c>inline:GameObject/Action</c> for an action serialized on a component. The short name works when unambiguous.
        /// </summary>
        public string action;

        /// <summary>The action's name, <c>Move</c>.</summary>
        public string name;

        /// <summary><c>stick</c> (a Vector2: map a joystick), <c>button</c> (a press: map a button), or <c>other</c>.</summary>
        public string kind;

        /// <summary>What reads it: <c>Player (TouchTestMover)</c>. Says what the action is for.</summary>
        public string[] readers;

        /// <summary>Ids of the touch controls that drive it now.</summary>
        public string[] controls;

        /// <summary>Why it cannot be mapped (an asset inside an immutable package), or <c>null</c>.</summary>
        public string readOnly;
    }

    /// <summary>One Rig check finding.</summary>
    public sealed class TouchCheckInfo
    {
        /// <summary>The rig's GameObject name.</summary>
        public string rig;

        /// <summary><c>Info</c>, <c>Warning</c> or <c>Error</c>.</summary>
        public string severity;

        /// <summary>Stable key of the kind of finding.</summary>
        public string id;

        /// <summary>One line naming what was found.</summary>
        public string title;

        /// <summary>Why it matters.</summary>
        public string detail;

        /// <summary>The control it is about, or <c>null</c> for the rig or scene.</summary>
        public string control;

        /// <summary>What <c>touch_check --fix</c> would do about it, or <c>null</c> when a person has to decide.</summary>
        public string fix;
    }

    /// <summary>What <c>touch_add</c>, <c>touch_place</c> and <c>touch_rename</c> return: the control as it is now.</summary>
    public sealed class TouchAuthoredInfo
    {
        /// <summary>The control after the change.</summary>
        public TouchPlacedControlInfo control;

        /// <summary>Things worth a look: overlapping another control, outside the safe area, a mapping that was not made.</summary>
        public string[] warnings;
    }

    /// <summary>What <c>touch_remove</c> returns.</summary>
    public sealed class TouchRemovedInfo
    {
        /// <summary>The removed control's id.</summary>
        public string id;

        /// <summary>The rig it was in.</summary>
        public string rig;

        /// <summary>The actions whose binding to it was removed. Empty with <c>keep_mappings</c>.</summary>
        public string[] unmapped;
    }

    /// <summary>What <c>touch_assign</c> and <c>touch_unassign</c> return: the action's touch controls after the change.</summary>
    public sealed class TouchAssignedInfo
    {
        /// <summary>The action's handle.</summary>
        public string action;

        /// <summary>Ids of the touch controls that drive it now.</summary>
        public string[] controls;

        /// <summary>Where the mapping was written: <c>asset</c> (re-imported; not undone by Ctrl+Z) or <c>inline</c> (undoable).</summary>
        public string writtenTo;
    }

    /// <summary>What <c>touch_check</c> returns.</summary>
    public sealed class TouchCheckListInfo
    {
        /// <summary>The findings left after any fixes, worst first.</summary>
        public TouchCheckInfo[] checks;

        /// <summary>The fixes run, by label. Empty without <c>fix</c>.</summary>
        public string[] fixedBy;
    }
}
