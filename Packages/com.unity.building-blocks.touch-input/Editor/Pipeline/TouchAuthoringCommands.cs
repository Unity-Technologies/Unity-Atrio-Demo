using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if BB_TOUCH_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Editor Pipeline commands to build a touch layout from a terminal or an agent: read the scene, add, place,
    /// rename and remove controls, map actions, and run the Rig check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// They use the same code as the menu and the Touch Rig inspector, so an agent's control matches a designer's.
    /// Each command is one Undo step, but a mapping written to an action asset is not undone.
    /// </para>
    /// <para>
    /// Edit mode only; in Play they refuse and name <c>touch_map</c> or <c>touch_tune</c> instead. A bad request
    /// throws <see cref="ArgumentException"/> and changes nothing. Placement is in canvas units from an anchor.
    /// </para>
    /// </remarks>
    static class TouchAuthoringCommands
    {
        const string k_Tag = "touch-input";

        static readonly string[] k_AnchorNames =
            { "bottom_left", "bottom", "bottom_right", "left", "center", "right", "top_left", "top", "top_right" };

        static readonly Vector2[] k_Anchors =
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(1f, 1f),
        };

        [CliCommand("touch_scene_get",
            "Read what a touch layout needs: the input actions the open scenes read (kind stick|button, who reads them, " +
            "which touch controls drive them), every Touch Rig's controls with placement (anchor, x, y, size in canvas " +
            "units), the canvas size, and the Rig check findings. Start here before adding or moving controls.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchSceneInfo GetScene(
            [CliArg("rig", "Only this rig's controls and findings. Omit for every rig.")] string rig = null)
        {
            List<TouchRig> rigs = string.IsNullOrEmpty(rig) ? AllRigs() : new List<TouchRig> { RigNamed(rig) };

            var info = new TouchSceneInfo { playing = Application.isPlaying };
            info.rigs = new TouchSceneRigInfo[rigs.Count];
            var checks = new List<TouchCheckInfo>();

#if BB_TOUCH_HAS_INPUTSYSTEM
            List<TouchInputSource> sources = TouchMappings.Scan();
            info.actions = new TouchActionInfo[sources.Count];
            for (int i = 0; i < sources.Count; i++)
                info.actions[i] = Describe(sources[i]);
#else
            List<object> sources = null;
            info.actions = Array.Empty<TouchActionInfo>();
#endif

            for (int i = 0; i < rigs.Count; i++)
            {
                List<TouchControl> controls = TouchControlIds.ControlsOf(rigs[i]);
                var rigInfo = new TouchSceneRigInfo
                {
                    name = rigs[i].name,
                    scene = rigs[i].gameObject.scene.name,
                    path = PathOf(rigs[i].transform),
                    controls = new TouchPlacedControlInfo[controls.Count],
                };
                for (int c = 0; c < controls.Count; c++)
                    rigInfo.controls[c] = Describe(controls[c], sources);
                info.rigs[i] = rigInfo;
                checks.AddRange(Checks(rigs[i]));
                info.canvas ??= DescribeCanvas(rigs[i]);
            }

            info.checks = SortChecks(checks);
            return info;
        }

        [CliCommand("touch_add",
            "Add a touch control (joystick | button) to a Touch Rig, creating the Canvas, EventSystem and rig when the " +
            "scene has none. With action, the control drives it (written into the action asset or inline action, as " +
            "the Mappings card does) and, without id, is named after it. Placement: anchor + x/y/size in canvas units " +
            "(see touch_scene_get); omitted, it goes where the menu puts it. Edit mode only. One Undo step for the scene.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchAuthoredInfo Add(
            [CliArg("kind", "joystick (a Vector2: move, look, aim) or button (a press: jump, fire, interact).")] string kind = null,
            [CliArg("id", "Control id, e.g. Move. Omit to take the action's name, or an auto id.")] string id = null,
            [CliArg("action", "Action to drive, from touch_scene_get: Asset:Map/Action, inline:GameObject/Action, or a unique name.")] string action = null,
            [CliArg("anchor", "bottom_left | bottom | bottom_right | left | center | right | top_left | top | top_right.")] string anchor = null,
            [CliArg("x", "Distance from the anchor to the centre, into the screen (+ right on a centred axis). Canvas units.")] string x = null,
            [CliArg("y", "Distance from the anchor to the centre, into the screen (+ up on a centred axis). Canvas units.")] string y = null,
            [CliArg("size", "Width and height in canvas units. Default 220 for a joystick, 150 for a button.")] string size = null,
            [CliArg("rig", "Rig GameObject name. Omit when the scene has one rig (or none: one is created).")] string rig = null)
        {
            RefuseInPlay("touch_add");
            bool joystick = ParseKind(kind);

            TouchRig target = OptionalRig(rig);
            Placement placement = ParsePlacement(anchor, x, y, size, null);

            if (!string.IsNullOrEmpty(id))
            {
                if (TouchControlIds.Key(id).Length == 0)
                    throw new ArgumentException($"\"{id}\" is not an id: use at least one letter or digit.");
                if (target != null && TouchControlIds.IsTaken(target, id))
                    throw new ArgumentException($"\"{id}\" is taken in rig '{target.name}' (ids ignore case and punctuation). Pick another, or touch_place the existing control.");
            }

#if BB_TOUCH_HAS_INPUTSYSTEM
            TouchInputSource source = null;
            if (!string.IsNullOrEmpty(action))
            {
                source = ResolveAction(action);
                RefuseUnmappable(source, joystick);
            }
#else
            if (!string.IsNullOrEmpty(action))
                throw new ArgumentException("Mapping needs the Input System package. Add the control without --action and read it from C#.");
#endif

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Touch Control");

            GameObject created = joystick
                ? TouchRigSpawner.CreateJoystick(target != null ? target.gameObject : null)
                : TouchRigSpawner.CreateButton(target != null ? target.gameObject : null);
            var control = created.GetComponent<TouchControl>();
            TouchRig rigOfControl = control.GetComponentInParent<TouchRig>(true);

            string finalId = id;
#if BB_TOUCH_HAS_INPUTSYSTEM
            if (string.IsNullOrEmpty(finalId) && source != null)
                finalId = TouchControlIds.Unique(rigOfControl, TouchControlIds.FromActionName(source.Name), control);
#endif
            if (!string.IsNullOrEmpty(finalId))
                TouchControlIds.Apply(control, finalId.Trim(), "Add Touch Control");

            if (placement.Any)
                Place((RectTransform)control.transform, placement);

            var warnings = new List<string>();
#if BB_TOUCH_HAS_INPUTSYSTEM
            if (source != null)
            {
                AuthoringResult mapped = TouchMappings.Map(source, control);
                if (!mapped.Ok)
                {
                    Undo.CollapseUndoOperations(group);
                    Undo.RevertAllDownToGroup(group);
                    throw new ArgumentException($"Not added: {mapped.Message}");
                }
            }
#endif

            Show(control);
            Undo.CollapseUndoOperations(group);
            MarkDirty(control);

            warnings.AddRange(PlacementWarnings(control));
            return new TouchAuthoredInfo { control = Describe(control, Sources()), warnings = warnings.ToArray() };
        }

        [CliCommand("touch_place",
            "Move or resize a touch control: anchor + x/y/size in canvas units (see touch_scene_get). Omitted values are " +
            "kept; a new anchor without x/y keeps the control's current x/y from that anchor. Edit mode only; one Undo step.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchAuthoredInfo PlaceControl(
            [CliArg("control", "Control id, e.g. Jump.")] string control = null,
            [CliArg("anchor", "bottom_left | bottom | bottom_right | left | center | right | top_left | top | top_right.")] string anchor = null,
            [CliArg("x", "Distance from the anchor to the centre, into the screen (+ right on a centred axis). Canvas units.")] string x = null,
            [CliArg("y", "Distance from the anchor to the centre, into the screen (+ up on a centred axis). Canvas units.")] string y = null,
            [CliArg("size", "Width and height in canvas units.")] string size = null,
            [CliArg("rig", "Rig GameObject name. Omit when only one rig has the control.")] string rig = null)
        {
            RefuseInPlay("touch_place");
            TouchControl target = ResolveControl(control, rig);
            var rect = (RectTransform)target.transform;
            Placement placement = ParsePlacement(anchor, x, y, size, rect);
            if (!placement.Any)
                throw new ArgumentException("Nothing to change: give --anchor, --x, --y or --size.");

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Place Touch Control");
            Place(rect, placement);
            Show(target);
            Undo.CollapseUndoOperations(group);

            MarkDirty(target);
            return new TouchAuthoredInfo { control = Describe(target, Sources()), warnings = PlacementWarnings(target).ToArray() };
        }

        [CliCommand("touch_rename",
            "Rename a touch control's id (and GameObject). Every mapping it has moves with it, as on the Mappings card. " +
            "Edit mode only.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchAuthoredInfo Rename(
            [CliArg("control", "Current control id.")] string control = null,
            [CliArg("id", "New id. Must be free in the rig (ids ignore case and punctuation).")] string id = null,
            [CliArg("rig", "Rig GameObject name. Omit when only one rig has the control.")] string rig = null)
        {
            RefuseInPlay("touch_rename");
            TouchControl target = ResolveControl(control, rig);
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException($"Give --id, the new id for '{target.ActionId}'.");

            int group = BeginUndo("Rename Touch Control");
#if BB_TOUCH_HAS_INPUTSYSTEM
            AuthoringResult result = TouchMappings.Rename(target, id);
            Undo.CollapseUndoOperations(group);
            if (!result.Ok)
                throw new ArgumentException(result.Message);
#else
            TouchRig owner = target.GetComponentInParent<TouchRig>(true);
            if (TouchControlIds.Key(id).Length == 0)
                throw new ArgumentException("An id needs at least one letter or digit.");
            if (owner != null && TouchControlIds.IsTaken(owner, id, target))
                throw new ArgumentException($"\"{id}\" is taken in this rig (ids ignore case and punctuation).");
            TouchControlIds.Apply(target, id.Trim());
            Undo.CollapseUndoOperations(group);
#endif

            MarkDirty(target);
            return new TouchAuthoredInfo { control = Describe(target, Sources()), warnings = Array.Empty<string>() };
        }

        [CliCommand("touch_remove",
            "Delete a touch control. Its mappings are removed too (an action asset's binding is not restored by Ctrl+Z; " +
            "the GameObject is), unless keep_mappings. Edit mode only.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchRemovedInfo Remove(
            [CliArg("control", "Control id to delete.")] string control = null,
            [CliArg("rig", "Rig GameObject name. Omit when only one rig has the control.")] string rig = null,
            [CliArg("keep_mappings", "Leave the actions' <TouchRig>/id bindings in place (the Rig check will flag them).")] bool keepMappings = false)
        {
            RefuseInPlay("touch_remove");
            TouchControl target = ResolveControl(control, rig);
            TouchRig owner = target.GetComponentInParent<TouchRig>(true);
            var info = new TouchRemovedInfo { id = target.ActionId, rig = owner != null ? owner.name : null };
            var unmapped = new List<string>();
            int group = BeginUndo("Remove Touch Control");

#if BB_TOUCH_HAS_INPUTSYSTEM
            if (!keepMappings)
            {
                // All or nothing. Each unmap re-imports its asset, so look each action up again by key.
                // If one fails, map the removed ones back before reporting.
                string name = TouchBindingAuthoring.ControlName(target);
                var keys = new List<string>();
                foreach (TouchInputSource source in TouchMappings.Scan())
                {
                    if (source.IsMappedTo(target))
                        keys.Add(source.Key);
                }

                var done = new List<string>();
                foreach (string key in keys)
                {
                    TouchInputSource source = TouchMappings.Find(TouchMappings.Scan(), key);
                    if (source == null)
                        continue;

                    AuthoringResult result = TouchMappings.Unmap(source, name);
                    if (!result.Ok)
                    {
                        foreach (string undone in done)
                        {
                            TouchInputSource again = TouchMappings.Find(TouchMappings.Scan(), undone);
                            if (again != null)
                                TouchMappings.Map(again, target);
                        }

                        Undo.CollapseUndoOperations(group);
                        throw new ArgumentException($"Not removed: could not unmap {Handle(source)}: {result.Message}");
                    }

                    done.Add(key);
                    unmapped.Add(Handle(source));
                }
            }
#endif

            Scene scene = target.gameObject.scene;
            Undo.DestroyObjectImmediate(target.gameObject);
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            info.unmapped = unmapped.ToArray();
            return info;
        }

#if BB_TOUCH_HAS_INPUTSYSTEM

        [CliCommand("touch_assign",
            "Make a touch control drive an action, kept in the action asset or inline action (the Mappings card's " +
            "write). With from, the mapping moves from that control instead of being added. A joystick drives stick " +
            "actions, a button drives button actions. Edit mode only; for a change while playing use touch_map.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchAssignedInfo Assign(
            [CliArg("action", "Action from touch_scene_get: Asset:Map/Action, inline:GameObject/Action, or a unique name.")] string action = null,
            [CliArg("control", "Control id that should drive it.")] string control = null,
            [CliArg("from", "Control id that drives it now: the mapping moves from it to control.")] string from = null,
            [CliArg("rig", "Rig GameObject name. Omit when only one rig has the control.")] string rig = null)
        {
            RefuseInPlay("touch_assign", "touch_map");
            TouchInputSource source = ResolveAction(action);
            TouchControl target = ResolveControl(control, rig);
            RefuseUnmappable(source, target is TouchJoystick);

            int group = BeginUndo("Assign Touch Mapping");
            AuthoringResult result;
            if (string.IsNullOrEmpty(from))
            {
                result = TouchMappings.Map(source, target);
            }
            else
            {
                string fromName = MappedName(source, from, rig);
                result = TouchMappings.Move(source, fromName, target);
            }

            Undo.CollapseUndoOperations(group);
            if (!result.Ok)
                throw new ArgumentException(result.Message);

            MarkDirty(target);
            return Assigned(source);
        }

        [CliCommand("touch_unassign",
            "Stop a touch control driving an action (removes its <TouchRig>/id binding from the asset or inline action). " +
            "Edit mode only; for a change while playing use touch_unmap.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchAssignedInfo Unassign(
            [CliArg("action", "Action from touch_scene_get.")] string action = null,
            [CliArg("control", "Control id that drives it.")] string control = null,
            [CliArg("rig", "Rig GameObject name. Omit when only one rig has the control.")] string rig = null)
        {
            RefuseInPlay("touch_unassign", "touch_unmap");
            TouchInputSource source = ResolveAction(action);
            string name = MappedName(source, control, rig);

            int group = BeginUndo("Remove Touch Mapping");
            AuthoringResult result = TouchMappings.Unmap(source, name);
            Undo.CollapseUndoOperations(group);
            if (!result.Ok)
                throw new ArgumentException(result.Message);

            return Assigned(source);
        }
#endif

        /// <summary>Starts this command's own Undo group, so it never merges into the previous command's step.</summary>
        static int BeginUndo(string name)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
            return group;
        }

        [CliCommand("touch_check",
            "Run the Rig check (what the Touch Rig inspector shows). With fix, run every fix that needs no choice, as " +
            "Fix all does, and return what is left. Edit mode only for fix.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchCheckListInfo Check(
            [CliArg("rig", "Rig GameObject name. Omit for every rig.")] string rig = null,
            [CliArg("fix", "Run the fixes that need no choice.")] bool fix = false)
        {
            List<TouchRig> rigs = string.IsNullOrEmpty(rig) ? AllRigs() : new List<TouchRig> { RigNamed(rig) };
            var fixedBy = new List<string>();

            if (fix)
            {
                RefuseInPlay("touch_check --fix");
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Fix Touch Rig");

                foreach (TouchRig r in rigs)
                {
                    // Each pass re-inspects: one fix (a bridge, a Touch scheme) can change the next finding.
                    for (int pass = 0; pass < 8; pass++)
                    {
                        bool applied = false;
                        foreach (TouchSetupCheck check in TouchRigDoctor.Inspect(r))
                        {
                            if (!Fixable(check))
                                continue;
                            fixedBy.Add($"{r.name}: {check.FixLabel}");
                            check.Fix();
                            applied = true;
                            break;
                        }

                        if (!applied)
                            break;
                    }

                    EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
                }

                Undo.CollapseUndoOperations(group);
            }

            var checks = new List<TouchCheckInfo>();
            foreach (TouchRig r in rigs)
                checks.AddRange(Checks(r));
            return new TouchCheckListInfo { checks = SortChecks(checks), fixedBy = fixedBy.ToArray() };
        }

        struct Placement
        {
            public bool HasAnchor;
            public Vector2 Anchor;
            public bool HasX, HasY, HasSize;
            public float X, Y, Size;

            public bool Any => HasAnchor || HasX || HasY || HasSize;
        }

        static Placement ParsePlacement(string anchor, string x, string y, string size, RectTransform current)
        {
            var p = new Placement();
            if (!string.IsNullOrEmpty(anchor))
            {
                int i = Array.FindIndex(k_AnchorNames, n => string.Equals(n, anchor.Trim().Replace('-', '_').Replace(' ', '_'), StringComparison.OrdinalIgnoreCase));
                if (i < 0)
                    throw new ArgumentException($"Unknown anchor '{anchor}'. Use one of {string.Join(", ", k_AnchorNames)}.");
                p.HasAnchor = true;
                p.Anchor = k_Anchors[i];
            }

            p.HasX = ParseNumber(x, "x", out p.X);
            p.HasY = ParseNumber(y, "y", out p.Y);
            p.HasSize = ParseNumber(size, "size", out p.Size);
            if (p.HasSize && p.Size < 40f)
                throw new ArgumentException($"--size {size} is too small to touch; use 40 or more (a fingertip is about 100 canvas units on a 1080-wide canvas).");

            // A new anchor without x/y keeps the control's offsets measured from the new anchor.
            if (current != null && p.HasAnchor)
            {
                if (!p.HasX) { p.HasX = true; p.X = Offsets(current).x; }
                if (!p.HasY) { p.HasY = true; p.Y = Offsets(current).y; }
            }

            return p;
        }

        static bool ParseNumber(string text, string name, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!float.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException($"--{name} '{text}' is not a number.");
            return true;
        }

        static void Place(RectTransform rect, Placement p)
        {
            Undo.RecordObject(rect, "Place Touch Control");
            Vector2 offsets = Offsets(rect);
            if (p.HasAnchor)
                rect.anchorMin = rect.anchorMax = p.Anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);

            float x = p.HasX ? p.X : offsets.x;
            float y = p.HasY ? p.Y : offsets.y;
            rect.anchoredPosition = new Vector2(Inward(rect.anchorMin.x) * x, Inward(rect.anchorMin.y) * y);

            if (p.HasSize)
            {
                float old = Mathf.Max(1f, rect.sizeDelta.x);
                float scale = p.Size / old;
                rect.sizeDelta = new Vector2(p.Size, p.Size);

                // A joystick's ring and knob are fixed-size children, so scale them with the control.
                if (rect.GetComponent<TouchJoystick>() != null)
                {
                    foreach (RectTransform child in rect)
                    {
                        if (child.anchorMin != child.anchorMax)
                            continue;
                        Undo.RecordObject(child, "Place Touch Control");
                        child.sizeDelta *= scale;
                        child.anchoredPosition *= scale;
                    }
                }
            }
        }

        // +1 from a 0 or 0.5 anchor, -1 from a 1 anchor, so positive offsets point into the screen.
        static float Inward(float anchor) => anchor > 0.75f ? -1f : 1f;

        static Vector2 Offsets(RectTransform rect) =>
            new Vector2(Inward(rect.anchorMin.x) * rect.anchoredPosition.x, Inward(rect.anchorMin.y) * rect.anchoredPosition.y);

        static string AnchorName(RectTransform rect)
        {
            if (rect.anchorMin != rect.anchorMax)
                return "custom";
            for (int i = 0; i < k_Anchors.Length; i++)
            {
                if (Vector2.Distance(k_Anchors[i], rect.anchorMin) < 0.001f)
                    return k_AnchorNames[i];
            }

            return "custom";
        }

        static List<string> PlacementWarnings(TouchControl control)
        {
            var warnings = new List<string>();
            var rect = (RectTransform)control.transform;
            Rect mine = WorldRect(rect);
            TouchRig rig = control.GetComponentInParent<TouchRig>(true);
            foreach (TouchControl other in TouchControlIds.ControlsOf(rig))
            {
                if (other == control || !other.gameObject.activeInHierarchy)
                    continue;
                if (mine.Overlaps(WorldRect((RectTransform)other.transform)))
                    warnings.Add($"Overlaps '{other.ActionId}': a finger on the overlap reaches only one of them.");
            }

            if (TouchRigDoctor.OutsideSafeArea(control, out string edge, out float pixels))
                warnings.Add($"Sticks out of the safe area at the {edge} by {pixels:0} px: part of it is under a notch or the screen edge.");
            return warnings;
        }

        static Rect WorldRect(RectTransform rect)
        {
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

#if BB_TOUCH_HAS_INPUTSYSTEM
        static List<TouchInputSource> Sources() => TouchMappings.Scan();

        static TouchPlacedControlInfo Describe(TouchControl control, List<TouchInputSource> sources)
#else
        static List<object> Sources() => null;

        static TouchPlacedControlInfo Describe(TouchControl control, List<object> sources)
#endif
        {
            var rect = (RectTransform)control.transform;
            Vector2 offsets = Offsets(rect);
            TouchRig rig = control.GetComponentInParent<TouchRig>(true);
            var drives = new List<string>();
#if BB_TOUCH_HAS_INPUTSYSTEM
            if (sources != null)
            {
                foreach (TouchInputSource source in sources)
                {
                    if (source.IsMappedTo(control))
                        drives.Add(Handle(source));
                }
            }
#endif
            return new TouchPlacedControlInfo
            {
                id = control.ActionId,
                type = control is TouchJoystick ? "Joystick" : "Button",
                rig = rig != null ? rig.name : null,
                shown = control.enabled && control.gameObject.activeInHierarchy,
                anchor = AnchorName(rect),
                x = Round(offsets.x),
                y = Round(offsets.y),
                size = Round(rect.sizeDelta.x),
                drives = drives.ToArray(),
            };
        }

        static float Round(float value) => Mathf.Round(value * 10f) / 10f;

        static TouchCanvasInfo DescribeCanvas(TouchRig rig)
        {
            Canvas canvas = rig.GetComponentInParent<Canvas>(true);
            if (canvas == null)
                return null;
            canvas = canvas.rootCanvas;
            var scaler = canvas.GetComponent<CanvasScaler>();
            var rect = (RectTransform)canvas.transform;
            return new TouchCanvasInfo
            {
                name = canvas.name,
                referenceResolution = scaler != null ? new[] { scaler.referenceResolution.x, scaler.referenceResolution.y } : null,
                match = scaler != null ? scaler.matchWidthOrHeight : 0f,
                size = new[] { Round(rect.rect.width), Round(rect.rect.height) },
            };
        }

        static List<TouchCheckInfo> Checks(TouchRig rig)
        {
            var list = new List<TouchCheckInfo>();
            foreach (TouchSetupCheck check in TouchRigDoctor.Inspect(rig))
            {
                list.Add(new TouchCheckInfo
                {
                    rig = rig.name,
                    severity = check.Severity.ToString(),
                    id = check.Id,
                    title = check.Title,
                    detail = check.Detail,
                    control = check.Subject != null ? check.Subject.ActionId : null,
                    fix = Fixable(check) ? check.FixLabel : null,
                });
            }

            return list;
        }

        // The inspector's Fix all rule: one fix, and not a choice of action.
        static bool Fixable(TouchSetupCheck check) => check.Fixes.Count == 1 && !check.OffersActionPicker;

        static TouchCheckInfo[] SortChecks(List<TouchCheckInfo> checks)
        {
            int Rank(string severity) => severity == "Error" ? 0 : severity == "Warning" ? 1 : 2;
            var sorted = new List<TouchCheckInfo>(checks);
            // Stable: findings keep the doctor's order within a severity.
            var ordered = new List<TouchCheckInfo>();
            for (int r = 0; r < 3; r++)
                ordered.AddRange(sorted.FindAll(c => Rank(c.severity) == r));
            return ordered.ToArray();
        }

#if BB_TOUCH_HAS_INPUTSYSTEM
        static TouchActionInfo Describe(TouchInputSource source)
        {
            var readers = new List<string>();
            foreach (Component reader in source.Readers)
            {
                if (reader == null)
                    continue;
                string entry = $"{reader.gameObject.name} ({reader.GetType().Name})";
                if (!readers.Contains(entry))
                    readers.Add(entry);
            }

            return new TouchActionInfo
            {
                action = Handle(source),
                name = source.Name,
                kind = source.TakesStick ? "stick" : source.TakesButton ? "button" : "other",
                readers = readers.ToArray(),
                controls = ControlIds(source),
                readOnly = source.ReadOnlyReason,
            };
        }

        static string Handle(TouchInputSource source)
        {
            if (source.IsInline)
                return $"inline:{(source.Owner != null ? source.Owner.gameObject.name : "?")}/{source.Name}";

            InputAction action = source.Asset != null ? source.Asset.FindAction(source.ActionId) : null;
            string map = action != null && action.actionMap != null ? action.actionMap.name : "?";
            return $"{(source.Asset != null ? source.Asset.name : "?")}:{map}/{source.Name}";
        }

        // The controls whose binding name the action carries. A name no control has is kept as is.
        static string[] ControlIds(TouchInputSource source)
        {
            var ids = new List<string>();
            List<TouchRig> rigs = AllRigs();
            foreach (string name in source.ControlNames)
            {
                string id = name;
                foreach (TouchRig rig in rigs)
                {
                    foreach (TouchControl control in TouchControlIds.ControlsOf(rig))
                    {
                        if (string.Equals(TouchBindingAuthoring.ControlName(control), name, StringComparison.OrdinalIgnoreCase))
                            id = control.ActionId;
                    }
                }

                ids.Add(id);
            }

            return ids.ToArray();
        }

        static TouchAssignedInfo Assigned(TouchInputSource source)
        {
            TouchInputSource fresh = TouchMappings.Find(TouchMappings.Scan(), source.Key) ?? source;
            return new TouchAssignedInfo
            {
                action = Handle(fresh),
                controls = ControlIds(fresh),
                writtenTo = fresh.IsInline ? "inline" : "asset",
            };
        }

        /// <summary>
        /// Finds the action a handle names: <c>Asset:Map/Action</c> or <c>inline:GameObject/Action</c>, or a shorter
        /// name when only one action in the scene matches.
        /// </summary>
        static TouchInputSource ResolveAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                throw new ArgumentException("Give --action: a handle from touch_scene_get (Asset:Map/Action), or the action's name.");

            string wanted = action.Trim();
            List<TouchInputSource> sources = TouchMappings.Scan();
            if (sources.Count == 0)
                throw new ArgumentException("The open scenes read no input actions: nothing to map. Give a component an InputActionReference first.");

            var matches = sources.FindAll(s => string.Equals(Handle(s), wanted, StringComparison.OrdinalIgnoreCase));
            if (matches.Count == 0)
                matches = sources.FindAll(s => string.Equals(MapAndName(s), wanted, StringComparison.OrdinalIgnoreCase));
            if (matches.Count == 0)
                matches = sources.FindAll(s => string.Equals(s.Name, wanted, StringComparison.OrdinalIgnoreCase));

            if (matches.Count == 1)
                return matches[0];

            var handles = new List<string>();
            foreach (TouchInputSource s in matches.Count > 1 ? matches : sources)
                handles.Add(Handle(s));
            throw new ArgumentException(matches.Count > 1
                ? $"'{action}' names {matches.Count} actions: {string.Join(", ", handles)}. Use one of those handles."
                : $"No action '{action}' in the open scenes. They read: {string.Join(", ", handles)}.");
        }

        static string MapAndName(TouchInputSource source)
        {
            string handle = Handle(source);
            int colon = handle.IndexOf(':');
            return colon >= 0 ? handle.Substring(colon + 1) : handle;
        }

        static void RefuseUnmappable(TouchInputSource source, bool joystick)
        {
            if (!string.IsNullOrEmpty(source.ReadOnlyReason))
                throw new ArgumentException($"{Handle(source)} cannot be mapped: {source.ReadOnlyReason}");
            if (joystick && !source.TakesStick)
                throw new ArgumentException($"{Handle(source)} takes {(source.TakesButton ? "a button press" : "neither a stick nor a button")}, not a joystick's Vector2. Use a button.");
            if (!joystick && !source.TakesButton)
                throw new ArgumentException($"{Handle(source)} takes {(source.TakesStick ? "a Vector2" : "neither a stick nor a button")}, not a button press. Use a joystick.");
        }

        // A live control's binding name, else the id itself, so a deleted control's mapping can still be removed.
        static string MappedName(TouchInputSource source, string controlId, string rig)
        {
            if (string.IsNullOrWhiteSpace(controlId))
                throw new ArgumentException("Which control? Give its id.");

            TouchControl live = TryResolveControl(controlId, rig, out _);
            string name = live != null ? TouchBindingAuthoring.ControlName(live) : TouchRigDevice.ControlNameFor(controlId.Trim());
            if (!source.ControlNames.Exists(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException($"'{controlId}' does not drive {Handle(source)}. It is driven by: {(source.ControlNames.Count > 0 ? string.Join(", ", ControlIds(source)) : "no touch control")}.");
            return name;
        }
#endif

        static List<TouchRig> AllRigs()
        {
            var rigs = new List<TouchRig>();
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    rigs.AddRange(root.GetComponentsInChildren<TouchRig>(true));
            }

            return rigs;
        }

        static TouchRig RigNamed(string name)
        {
            List<TouchRig> rigs = AllRigs();
            List<TouchRig> found = rigs.FindAll(r => r.name == name);
            if (found.Count == 0)
                found = rigs.FindAll(r => string.Equals(r.name, name, StringComparison.OrdinalIgnoreCase));
            if (found.Count == 1)
                return found[0];

            var names = new List<string>();
            foreach (TouchRig r in rigs) names.Add(r.name);
            throw new ArgumentException(found.Count > 1
                ? $"{found.Count} rigs are named '{name}'. Rename one."
                : $"No Touch Rig named '{name}'. Rigs: {(names.Count > 0 ? string.Join(", ", names) : "none")}.");
        }

        // The named rig, the only rig, or null when the scene has none (the spawner then makes one).
        static TouchRig OptionalRig(string name)
        {
            if (!string.IsNullOrEmpty(name))
                return RigNamed(name);
            List<TouchRig> rigs = AllRigs();
            if (rigs.Count <= 1)
                return rigs.Count == 1 ? rigs[0] : null;

            var names = new List<string>();
            foreach (TouchRig r in rigs) names.Add(r.name);
            throw new ArgumentException($"The scene has {rigs.Count} rigs ({string.Join(", ", names)}). Give --rig.");
        }

        static TouchControl ResolveControl(string id, string rig)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Which control? Give --control with its id (touch_scene_get lists them).");
            TouchControl control = TryResolveControl(id, rig, out string problem);
            if (control == null)
                throw new ArgumentException(problem);
            return control;
        }

        static TouchControl TryResolveControl(string id, string rig, out string problem)
        {
            problem = null;
            List<TouchRig> rigs = string.IsNullOrEmpty(rig) ? AllRigs() : new List<TouchRig> { RigNamed(rig) };
            string wanted = id.Trim();
            var all = new List<TouchControl>();
            foreach (TouchRig r in rigs)
                all.AddRange(TouchControlIds.ControlsOf(r));

            List<TouchControl> found = all.FindAll(c => c.ActionId == wanted);
            if (found.Count == 0)
                found = all.FindAll(c => string.Equals(c.ActionId, wanted, StringComparison.OrdinalIgnoreCase));
            if (found.Count == 1)
                return found[0];

            var ids = new List<string>();
            foreach (TouchControl c in (found.Count > 1 ? found : all))
                ids.Add($"{c.ActionId} ({c.GetComponentInParent<TouchRig>(true)?.name})");
            problem = found.Count > 1
                ? $"'{id}' is in several rigs: {string.Join(", ", ids)}. Give --rig."
                : $"No control '{id}'. Controls: {(ids.Count > 0 ? string.Join(", ", ids) : "none")}.";
            return null;
        }

        static bool ParseKind(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
                throw new ArgumentException("Give --kind: joystick (move, look, aim) or button (jump, fire, interact).");
            switch (kind.Trim().ToLowerInvariant())
            {
                case "joystick":
                case "stick":
                    return true;
                case "button":
                    return false;
                default:
                    throw new ArgumentException($"Unknown kind '{kind}'. Use joystick or button.");
            }
        }

        static void RefuseInPlay(string command, string runtimeCommand = null)
        {
            if (!Application.isPlaying)
                return;
            throw new ArgumentException(runtimeCommand != null
                ? $"{command} edits the scene and its action asset; exit Play first. While playing, use {runtimeCommand}."
                : $"{command} edits the scene; exit Play first. While playing, tune with touch_tune and map with touch_map.");
        }

        static void MarkDirty(TouchControl control)
        {
            EditorUtility.SetDirty(control);
            EditorSceneManager.MarkSceneDirty(control.gameObject.scene);
        }

        // Select it so whoever watches the scene sees the change. Selection is recorded in Undo, so do it inside
        // the command's group, or Ctrl+Z undoes only the selection.
        static void Show(TouchControl control)
        {
            Selection.activeGameObject = control.gameObject;
            EditorGUIUtility.PingObject(control.gameObject);
        }

        static string PathOf(Transform t)
        {
            string path = "/" + t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = "/" + p.name + path;
            return path;
        }
    }
}
