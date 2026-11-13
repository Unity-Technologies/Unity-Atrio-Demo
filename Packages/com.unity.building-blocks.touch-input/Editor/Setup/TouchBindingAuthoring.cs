#if BB_TOUCH_HAS_INPUTSYSTEM
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;
using PackageSource = UnityEditor.PackageManager.PackageSource;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>Where <see cref="TouchBindingAuthoring.ResolveAsset"/> found the action asset.</summary>
    enum ActionAssetSource
    {
        /// <summary>Nothing found: no bridge field, nothing in the scene, no project-wide actions.</summary>
        None,

        /// <summary>The rig's <see cref="TouchInputBridge.Actions"/> field.</summary>
        BridgeField,

        /// <summary>The one asset the loaded scenes' components reference.</summary>
        Scene,

        /// <summary><c>InputSystem.actions</c>.</summary>
        ProjectWide,
    }

    /// <summary>The asset a rig's controls author into, and how it was chosen.</summary>
    readonly struct ActionAssetResolution
    {
        public readonly InputActionAsset Asset;
        public readonly ActionAssetSource Source;

        /// <summary>Every asset the scene references, or empty if the scene was not scanned.</summary>
        public readonly IReadOnlyList<InputActionAsset> SceneAssets;

        public ActionAssetResolution(InputActionAsset asset, ActionAssetSource source, IReadOnlyList<InputActionAsset> sceneAssets)
        {
            Asset = asset;
            Source = source;
            SceneAssets = sceneAssets ?? Array.Empty<InputActionAsset>();
        }

        /// <summary>Whether the scene references several assets and the bridge names none, so the designer has to choose.</summary>
        public bool IsAmbiguous => Source != ActionAssetSource.BridgeField && SceneAssets.Count > 1;

        /// <summary>Where the asset came from and its name, such as "Scene · FirstPersonControls".</summary>
        public string Describe()
        {
            string name = Asset != null ? Asset.name : "none";
            switch (Source)
            {
                case ActionAssetSource.BridgeField: return "Set on the rig · " + name;
                case ActionAssetSource.Scene: return "From the scene · " + name;
                case ActionAssetSource.ProjectWide: return "Project-wide · " + name;
                default: return "No action asset";
            }
        }
    }

    /// <summary>A binding of a touch control: the action it sits on and its index in <see cref="InputAction.bindings"/>.</summary>
    readonly struct TouchBinding
    {
        public readonly InputAction Action;
        public readonly int Index;

        public TouchBinding(InputAction action, int index)
        {
            Action = action;
            Index = index;
        }

        public InputBinding Binding => Action.bindings[Index];

        /// <summary>"Player / Move".</summary>
        public string ActionLabel => TouchBindingAuthoring.Label(Action);
    }

    // Parked: no UI uses this now. It may come back on the mapping row.
    /// <summary>What a button's binding does with a press (written as an interaction on it).</summary>
    enum TouchButtonInteraction
    {
        /// <summary>No interaction: the action performs when the finger lands.</summary>
        Press,

        /// <summary><c>hold</c>: performs after the finger has stayed down (the Input System's default hold time).</summary>
        Hold,

        /// <summary><c>tap</c>: performs when the finger lifts quickly.</summary>
        Tap,
    }

    /// <summary>
    /// Reads and writes touch bindings (<c>&lt;TouchRig&gt;/&lt;controlName&gt;</c>) in the user's action asset.
    /// The binding is the mapping; the control stores nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It only changes bindings that start with <c>&lt;TouchRig&gt;/</c>, plus a <c>Touch</c> control scheme when
    /// the asset has schemes but no Touch one. The <c>Fix</c> methods are the exception: each does exactly what its
    /// button says, and only when clicked.
    /// </para>
    /// <para>
    /// An asset file is edited as a working copy, written back with <c>ToJson()</c> and re-imported, as the Input
    /// System's own editor does. Read-only assets are refused, and nothing is written in Play.
    /// </para>
    /// </remarks>
    static class TouchBindingAuthoring
    {
        public const string PathPrefix = "<TouchRig>/";
        public const string TouchSchemeName = "Touch";
        public const string ScaleProcessor = "scaleVector2";
        public const string InvertProcessor = "invertVector2";

        // The scene scan walks every serialized field, so it reruns only after a change event,
        // plus a slow timer for changes no event reports.
        const double k_SceneScanBackstopSeconds = 10.0;
        static double s_NextSceneScan;
        static bool s_SceneDirty = true;
        static bool s_Subscribed;
        static List<InputActionAsset> s_SceneAssets = new List<InputActionAsset>();

        /// <summary>Set by tests so the scene scan returns this instead of looking.</summary>
        internal static List<InputActionAsset> SceneAssetsOverride;

        /// <summary>Set by tests to stand in for <c>InputSystem.actions</c>. Use <see cref="NoProjectWide"/> for none.</summary>
        internal static InputActionAsset ProjectWideOverride;
        internal static bool NoProjectWide;

        /// <summary>
        /// The asset <paramref name="rig"/>'s controls write into: the bridge's Actions field, else the one asset the
        /// scenes reference, else project-wide actions.
        /// </summary>
        /// <remarks>
        /// If the scenes reference several assets and the field is empty, it returns the project-wide asset (if any)
        /// and sets <see cref="ActionAssetResolution.IsAmbiguous"/>.
        /// </remarks>
        public static ActionAssetResolution ResolveAsset(TouchRig rig)
        {
            TouchInputBridge bridge = rig != null ? rig.GetComponent<TouchInputBridge>() : null;
            if (bridge != null && bridge.Actions != null)
                return new ActionAssetResolution(bridge.Actions, ActionAssetSource.BridgeField, null);

            List<InputActionAsset> scene = SceneAssets();
            if (scene.Count == 1)
                return new ActionAssetResolution(scene[0], ActionAssetSource.Scene, scene);

            InputActionAsset projectWide = ProjectWide();
            if (projectWide != null)
                return new ActionAssetResolution(projectWide, ActionAssetSource.ProjectWide, scene);

            return new ActionAssetResolution(null, ActionAssetSource.None, scene);
        }

        static InputActionAsset ProjectWide()
        {
            if (NoProjectWide)
                return null;

            return ProjectWideOverride != null ? ProjectWideOverride : InputSystem.actions;
        }

        /// <summary>
        /// Every <see cref="InputActionAsset"/> the loaded scenes' components point at, directly or through a reference.
        /// </summary>
        /// <remarks>
        /// EventSystem input modules and the rig bridge's own field are skipped. The result is cached until the scene
        /// changes, because inspectors ask twice a second.
        /// </remarks>
        public static List<InputActionAsset> SceneAssets()
        {
            if (SceneAssetsOverride != null)
                return SceneAssetsOverride;

            if (!s_Subscribed)
            {
                s_Subscribed = true;
                EditorApplication.hierarchyChanged += InvalidateSceneScan;
                ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => InvalidateSceneScan();
                UnityEditor.SceneManagement.EditorSceneManager.sceneOpened += (_, __) => InvalidateSceneScan();
                UnityEditor.SceneManagement.EditorSceneManager.sceneClosed += _ => InvalidateSceneScan();
            }

            double now = EditorApplication.timeSinceStartup;
            if (!s_SceneDirty && now < s_NextSceneScan)
                return s_SceneAssets;

            s_SceneDirty = false;
            s_NextSceneScan = now + k_SceneScanBackstopSeconds;
            var found = new List<InputActionAsset>();
            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (behaviour == null || behaviour is BaseInputModule || behaviour is TouchInputBridge || EditorUtility.IsPersistent(behaviour))
                    continue;

                using var serialized = new SerializedObject(behaviour);
                SerializedProperty property = serialized.GetIterator();
                bool enter = true;
                while (property.Next(enter))
                {
                    // Only structs and arrays can hold a reference further down.
                    enter = property.propertyType == SerializedPropertyType.Generic;
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    InputActionAsset asset = property.objectReferenceValue switch
                    {
                        InputActionAsset direct => direct,
                        InputActionReference reference => reference.asset,
                        _ => null,
                    };

                    if (asset != null && !found.Contains(asset))
                        found.Add(asset);
                }
            }

            s_SceneAssets = found;
            return found;
        }

        /// <summary>Forgets the cached scene scan, so the next resolve looks again.</summary>
        public static void InvalidateSceneScan() => s_SceneDirty = true;

        /// <summary>
        /// Whether <paramref name="asset"/> can be written. It must be an <c>.inputactions</c> file outside any
        /// immutable package.
        /// </summary>
        public static bool IsWritable(InputActionAsset asset, out string reason)
        {
            reason = null;
            if (asset == null)
            {
                reason = "No action asset to write into.";
                return false;
            }

            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
                return true;

            if (!path.EndsWith(".inputactions", StringComparison.OrdinalIgnoreCase))
            {
                reason = $"'{asset.name}' can't be edited here. It's inside another asset.";
                return false;
            }

            if (path.StartsWith("Packages/", StringComparison.Ordinal))
            {
                PackageInfo package = PackageInfo.FindForAssetPath(path);
                if (package != null && package.source != PackageSource.Embedded && package.source != PackageSource.Local)
                {
                    reason = $"'{asset.name}' is read-only. It's inside {package.displayName}.";
                    return false;
                }
            }

            return true;
        }

        /// <summary>The device control name of <paramref name="control"/>, such as <c>move</c> for id <c>Move</c>. Empty for an empty id.</summary>
        public static string ControlName(TouchControl control) =>
            control != null ? TouchRigDevice.ControlNameFor(control.ActionId) : string.Empty;

        /// <summary><c>&lt;TouchRig&gt;/move</c>.</summary>
        public static string PathFor(string controlName) => PathPrefix + controlName;

        /// <summary>Whether <paramref name="path"/> addresses the rig device at all.</summary>
        public static bool IsTouchPath(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase);

        internal static bool IsExactly(string path, string controlName) =>
            controlName.Length > 0 && string.Equals(path, PathFor(controlName), StringComparison.OrdinalIgnoreCase);

        static bool IsUnder(string path, string controlName) =>
            controlName.Length > 0 && path != null &&
            path.StartsWith(PathFor(controlName) + "/", StringComparison.OrdinalIgnoreCase);

        /// <summary>The first binding in asset order whose path is exactly <c>&lt;TouchRig&gt;/&lt;controlName&gt;</c>.</summary>
        public static bool FindBinding(InputActionAsset asset, string controlName, out TouchBinding binding)
        {
            binding = default;
            if (asset == null || string.IsNullOrEmpty(controlName))
                return false;

            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        if (IsExactly(action.bindings[i].path, controlName))
                        {
                            binding = new TouchBinding(action, i);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Every binding that reads the control: its own and any on a sub-control (<c>&lt;TouchRig&gt;/move/x</c>).</summary>
        public static void FindBindings(InputActionAsset asset, string controlName, List<TouchBinding> into)
        {
            into.Clear();
            if (asset == null || string.IsNullOrEmpty(controlName))
                return;

            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        string path = action.bindings[i].path;
                        if (IsExactly(path, controlName) || IsUnder(path, controlName))
                            into.Add(new TouchBinding(action, i));
                    }
                }
            }
        }

        /// <summary>Whether <paramref name="control"/> drives any action of <paramref name="asset"/>.</summary>
        public static bool DrivesAnything(InputActionAsset asset, TouchControl control)
        {
            var bindings = new List<TouchBinding>();
            FindBindings(asset, ControlName(control), bindings);
            return bindings.Count > 0;
        }

        /// <summary>"Player / Move".</summary>
        public static string Label(InputAction action) =>
            action == null ? "None" : (action.actionMap != null ? action.actionMap.name + " / " : string.Empty) + action.name;

        /// <summary>
        /// Whether <paramref name="action"/> can take <paramref name="control"/>'s value.
        /// A joystick needs a Vector2 action; any other control needs a press.
        /// </summary>
        public static bool IsCompatible(InputAction action, TouchControl control)
        {
            if (action == null || control == null)
                return false;

            return IsCompatible(action.type, action.expectedControlType, GivesVector2(control));
        }

        /// <summary>
        /// <see cref="IsCompatible(InputAction, TouchControl)"/> by kind: whether an action of <paramref name="type"/>
        /// expecting <paramref name="expectedControlType"/> takes a Vector2 or a press.
        /// </summary>
        public static bool IsCompatible(InputActionType type, string expectedControlType, bool givesVector2) =>
            TouchRigLayout.Accepts(type, expectedControlType, givesVector2);

        /// <summary>Joysticks give a Vector2; everything else gives a press.</summary>
        public static bool GivesVector2(TouchControl control) => control is TouchJoystick;

        // Parked: no UI uses this now. It may come back on the mapping row.
        /// <summary>
        /// The actions <paramref name="control"/> may drive, in asset order; <paramref name="hidden"/> counts the
        /// rest ("8 button actions hidden").
        /// </summary>
        public static List<InputAction> CompatibleActions(InputActionAsset asset, TouchControl control, out int hidden)
        {
            var actions = new List<InputAction>();
            hidden = 0;
            if (asset == null)
                return actions;

            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    if (IsCompatible(action, control))
                        actions.Add(action);
                    else
                        hidden++;
                }
            }

            return actions;
        }

        /// <summary>The asset's Touch scheme (named Touch, any case), or <c>null</c>.</summary>
        public static InputControlScheme? TouchScheme(InputActionAsset asset)
        {
            if (asset == null)
                return null;

            foreach (InputControlScheme scheme in asset.controlSchemes)
            {
                if (string.Equals(scheme.name, TouchSchemeName, StringComparison.OrdinalIgnoreCase))
                    return scheme;
            }

            return null;
        }

        /// <summary>Whether <paramref name="groups"/> (<c>";Keyboard&amp;Mouse;Touch"</c>) contains <paramref name="group"/>.</summary>
        public static bool InGroup(string groups, string group)
        {
            if (string.IsNullOrEmpty(groups) || string.IsNullOrEmpty(group))
                return false;

            foreach (string part in groups.Split(InputBinding.Separator))
            {
                if (string.Equals(part.Trim(), group, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Reads a stick or area binding's sensitivity (<c>scaleVector2</c> x, 1 when absent) and invert Y.</summary>
        public static void GetResponse(InputBinding binding, out float sensitivity, out bool invertY)
        {
            sensitivity = 1f;
            invertY = false;
            foreach (string processor in Split(binding.processors))
            {
                string name = NameOf(processor);
                if (string.Equals(name, ScaleProcessor, StringComparison.OrdinalIgnoreCase))
                {
                    if (float.TryParse(Parameter(processor, "x"), NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
                        sensitivity = x;
                }
                else if (string.Equals(name, InvertProcessor, StringComparison.OrdinalIgnoreCase))
                {
                    string y = Parameter(processor, "invertY");
                    invertY = y == null || string.Equals(y, "true", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        /// <summary>
        /// The processor string for a sensitivity and invert Y, keeping any other processor already on
        /// <paramref name="existing"/>. Sensitivity 1 and no invert write nothing.
        /// </summary>
        public static string ResponseProcessors(string existing, float sensitivity, bool invertY)
        {
            var kept = new List<string>();
            foreach (string processor in Split(existing))
            {
                string name = NameOf(processor);
                if (!string.Equals(name, ScaleProcessor, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, InvertProcessor, StringComparison.OrdinalIgnoreCase))
                    kept.Add(processor);
            }

            if (Mathf.Abs(sensitivity - 1f) > 0.0001f)
            {
                string s = sensitivity.ToString("0.###", CultureInfo.InvariantCulture);
                kept.Add($"{ScaleProcessor}(x={s},y={s})");
            }

            if (invertY)
                kept.Add($"{InvertProcessor}(invertX=false,invertY=true)");

            return string.Join(InputBinding.Separator.ToString(), kept);
        }

        /// <summary>Reads a button binding's interaction: <c>hold</c>, <c>tap</c>, else Press.</summary>
        public static TouchButtonInteraction GetInteraction(InputBinding binding)
        {
            foreach (string interaction in Split(binding.interactions))
            {
                string name = NameOf(interaction);
                if (string.Equals(name, "hold", StringComparison.OrdinalIgnoreCase))
                    return TouchButtonInteraction.Hold;
                if (string.Equals(name, "tap", StringComparison.OrdinalIgnoreCase))
                    return TouchButtonInteraction.Tap;
            }

            return TouchButtonInteraction.Press;
        }

        /// <summary>The interaction string for <paramref name="kind"/>, keeping any interaction other than press, hold and tap.</summary>
        public static string InteractionString(string existing, TouchButtonInteraction kind)
        {
            var kept = new List<string>();
            foreach (string interaction in Split(existing))
            {
                string name = NameOf(interaction);
                if (!string.Equals(name, "hold", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "tap", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "press", StringComparison.OrdinalIgnoreCase))
                    kept.Add(interaction);
            }

            if (kind == TouchButtonInteraction.Hold)
                kept.Add("hold");
            else if (kind == TouchButtonInteraction.Tap)
                kept.Add("tap");

            return string.Join(InputBinding.Separator.ToString(), kept);
        }

        /// <summary>Splits a list like "scaleVector2(x=1,y=1);invertVector2" into its entries, ignoring separators inside parentheses.</summary>
        static List<string> Split(string list)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(list))
                return parts;

            int depth = 0;
            var current = new StringBuilder();
            foreach (char c in list)
            {
                if (c == '(') depth++;
                else if (c == ')') depth--;

                if (c == InputBinding.Separator && depth == 0)
                {
                    if (current.ToString().Trim().Length > 0)
                        parts.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            if (current.ToString().Trim().Length > 0)
                parts.Add(current.ToString().Trim());

            return parts;
        }

        static string NameOf(string entry)
        {
            int paren = entry.IndexOf('(');
            return (paren < 0 ? entry : entry.Substring(0, paren)).Trim();
        }

        static string Parameter(string entry, string parameter)
        {
            int open = entry.IndexOf('(');
            int close = entry.LastIndexOf(')');
            if (open < 0 || close <= open)
                return null;

            foreach (string pair in entry.Substring(open + 1, close - open - 1).Split(','))
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && string.Equals(kv[0].Trim(), parameter, StringComparison.OrdinalIgnoreCase))
                    return kv[1].Trim();
            }

            return null;
        }

        // Parked: no UI uses this now. It may come back on the mapping row.
        /// <summary>
        /// Makes <paramref name="control"/> drive <paramref name="action"/>, moving its binding there if it was on another action.
        /// </summary>
        /// <remarks>
        /// An auto id is renamed to the action's name first, with Undo. A Touch scheme is added if the asset has
        /// schemes but no Touch one.
        /// </remarks>
        public static AuthoringResult Assign(TouchControl control, InputActionAsset asset, InputAction action)
        {
            if (control == null)
                return AuthoringResult.Refused("No control.");
            if (action == null || asset == null || asset.FindAction(action.id) == null)
                return AuthoringResult.Refused("That action is not in the asset this rig authors into.");
            if (!IsCompatible(action, control))
                return AuthoringResult.Refused($"{Label(action)} does not take a {(GivesVector2(control) ? "Vector2" : "button")} value.");

            string oldName = ControlName(control);
            string newId = TouchControlIds.IdAfterPick(control, action.name);
            string newName = TouchRigDevice.ControlNameFor(newId);
            if (newName.Length == 0)
                return AuthoringResult.Refused("Give the control an id first: its binding path is made from it.");

            bool addedScheme = false;
            string schemeName = null;
            string assetName = asset.name;   // the imported object is replaced by the re-import
            AuthoringResult written = Write(asset, doc =>
            {
                InputAction target = doc.FindAction(action.id);

                // Find the control's current binding so its Response moves with it.
                string processors = null;
                string interactions = null;
                bool found = false;
                ForEachBinding(doc, (a, i, b) =>
                {
                    if (!found && IsExactly(b.path, oldName))
                    {
                        processors = b.processors;
                        interactions = b.interactions;
                        found = true;
                    }
                });

                RemoveBindings(doc, oldName);
                if (newName != oldName)
                {
                    RemoveBindings(doc, newName);
                    RenameIn(doc, oldName, newName);
                }

                string groups = TouchGroup(doc, out addedScheme, out schemeName);
                target.AddBinding(PathFor(newName), interactions, processors, groups);
            });

            if (!written.Ok)
                return written;

            if (newId != control.ActionId)
                TouchControlIds.Apply(control, newId, "Pick Action");

            return AuthoringResult.Done(addedScheme ? $"Added a {schemeName} scheme to {assetName}." : null, addedScheme);
        }

        /// <summary>
        /// Adds a <c>&lt;TouchRig&gt;/&lt;controlName&gt;</c> binding to action <paramref name="actionId"/>, in the Touch
        /// scheme. Bindings on other actions stay. Does nothing if the binding is already there.
        /// </summary>
        public static AuthoringResult AddTouchBinding(InputActionAsset asset, Guid actionId, string controlName)
        {
            if (asset == null || asset.FindAction(actionId) == null)
                return AuthoringResult.Refused("That action is not in its asset any more.");
            if (string.IsNullOrEmpty(controlName))
                return AuthoringResult.Refused("Give the control an id first: its binding path is made from it.");

            InputAction current = asset.FindAction(actionId);
            foreach (InputBinding b in current.bindings)
            {
                if (IsExactly(b.path, controlName))
                    return AuthoringResult.Done();
            }

            bool addedScheme = false;
            string schemeName = null;
            string assetName = asset.name;
            AuthoringResult written = Write(asset, doc =>
            {
                string groups = TouchGroup(doc, out addedScheme, out schemeName);
                doc.FindAction(actionId).AddBinding(PathFor(controlName), groups: groups);
            });

            if (!written.Ok)
                return written;
            return AuthoringResult.Done(addedScheme ? $"Added a {schemeName} scheme to {assetName}." : null, addedScheme);
        }

        /// <summary>Erases the <c>&lt;TouchRig&gt;/&lt;controlName&gt;</c> bindings of one action only.</summary>
        public static AuthoringResult RemoveTouchBinding(InputActionAsset asset, Guid actionId, string controlName)
        {
            InputAction current = asset != null ? asset.FindAction(actionId) : null;
            if (current == null || string.IsNullOrEmpty(controlName))
                return AuthoringResult.Done();

            bool any = false;
            foreach (InputBinding b in current.bindings)
                any |= IsExactly(b.path, controlName);
            if (!any)
                return AuthoringResult.Done();

            return Write(asset, doc =>
            {
                InputAction action = doc.FindAction(actionId);
                for (int i = action.bindings.Count - 1; i >= 0; i--)
                {
                    if (IsExactly(action.bindings[i].path, controlName))
                    {
                        AssertTouch(action.bindings[i]);
                        action.ChangeBinding(i).Erase();
                    }
                }
            });
        }

        // Parked: no UI uses this now. It may come back on the mapping row.
        /// <summary>Removes the control's own binding. Its id stays; bindings on its sub-controls are left alone.</summary>
        public static AuthoringResult Unassign(TouchControl control, InputActionAsset asset)
        {
            string name = ControlName(control);
            if (!FindBinding(asset, name, out _))
                return AuthoringResult.Done();

            return Write(asset, doc => RemoveBindings(doc, name));
        }

        // Parked: no UI uses this now. It may come back on the mapping row.
        /// <summary>Writes sensitivity and invert Y as processors on the control's own binding, and nowhere else.</summary>
        public static AuthoringResult SetResponse(TouchControl control, InputActionAsset asset, float sensitivity, bool invertY)
        {
            string name = ControlName(control);
            if (!FindBinding(asset, name, out TouchBinding current))
                return AuthoringResult.Refused("The control drives no action yet: pick one first.");

            string processors = ResponseProcessors(current.Binding.processors, sensitivity, invertY);
            if (processors == (current.Binding.processors ?? string.Empty))
                return AuthoringResult.Done();

            return Write(asset, doc => ChangeOwn(doc, name, b => b.processors = processors));
        }

        // Parked: no UI uses this now. It may come back on the mapping row.
        /// <summary>Writes a button's interaction on the control's own binding, and nowhere else.</summary>
        public static AuthoringResult SetInteraction(TouchControl control, InputActionAsset asset, TouchButtonInteraction kind)
        {
            string name = ControlName(control);
            if (!FindBinding(asset, name, out TouchBinding current))
                return AuthoringResult.Refused("The control drives no action yet: pick one first.");

            string interactions = InteractionString(current.Binding.interactions, kind);
            if (interactions == (current.Binding.interactions ?? string.Empty))
                return AuthoringResult.Done();

            return Write(asset, doc => ChangeOwn(doc, name, b => b.interactions = interactions));
        }

        /// <summary>
        /// Moves every binding of control <paramref name="oldName"/>, sub-controls included, to <paramref name="newName"/>.
        /// </summary>
        public static AuthoringResult RenameBinding(InputActionAsset asset, string oldName, string newName)
        {
            if (asset == null || string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName)
                return AuthoringResult.Done();

            var existing = new List<TouchBinding>();
            FindBindings(asset, oldName, existing);
            if (existing.Count == 0)
                return AuthoringResult.Done();

            return Write(asset, doc => RenameIn(doc, oldName, newName));
        }

        /// <summary>Renames a control's id and GameObject (Undo-recorded) and moves its bindings in the rig's asset with it.</summary>
        public static AuthoringResult RenameControl(TouchControl control, string newId)
        {
            if (control == null || newId == control.ActionId)
                return AuthoringResult.Done();

            string oldName = ControlName(control);
            string newName = TouchRigDevice.ControlNameFor(newId);
            ActionAssetResolution resolved = ResolveAsset(control.GetComponentInParent<TouchRig>(true));

            AuthoringResult moved = AuthoringResult.Done();
            if (resolved.Asset != null && oldName.Length > 0 && newName.Length > 0 && !Application.isPlaying)
            {
                var existing = new List<TouchBinding>();
                FindBindings(resolved.Asset, oldName, existing);
                if (existing.Count > 0)
                    moved = RenameBinding(resolved.Asset, oldName, newName);
            }

            TouchControlIds.Apply(control, newId);
            return moved;
        }

        /// <summary>Fix: removes group <c>Touch</c> from one non-touch binding, such as <c>&lt;Pointer&gt;/delta</c> on Look.</summary>
        public static AuthoringResult RemoveFromTouchSchemeFix(InputActionAsset asset, Guid bindingId)
        {
            InputControlScheme? scheme = TouchScheme(asset);
            if (scheme == null)
                return AuthoringResult.Refused("The asset has no Touch scheme.");

            string group = scheme.Value.bindingGroup;
            return Write(asset, doc => ChangeById(doc, bindingId, b =>
            {
                var kept = new List<string>();
                foreach (string part in (b.groups ?? string.Empty).Split(InputBinding.Separator))
                {
                    if (part.Length > 0 && !string.Equals(part, group, StringComparison.OrdinalIgnoreCase))
                        kept.Add(part);
                }

                b.groups = string.Join(InputBinding.Separator.ToString(), kept);
            }));
        }

        /// <summary>Fix: puts the control's own binding into the Touch scheme's group.</summary>
        public static AuthoringResult MoveIntoTouchSchemeFix(TouchControl control, InputActionAsset asset)
        {
            InputControlScheme? scheme = TouchScheme(asset);
            string name = ControlName(control);
            if (scheme == null || !FindBinding(asset, name, out _))
                return AuthoringResult.Refused("Nothing to move.");

            string group = scheme.Value.bindingGroup;
            return Write(asset, doc => ChangeOwn(doc, name, b =>
            {
                b.groups = string.IsNullOrEmpty(b.groups) ? group : b.groups + InputBinding.Separator + group;
            }));
        }

        /// <summary>Fix: adds <c>&lt;TouchRig&gt;</c> to the existing Touch scheme as an optional device, so <c>PlayerInput</c> pairs the rig.</summary>
        public static AuthoringResult AddRigToTouchSchemeFix(InputActionAsset asset)
        {
            if (TouchScheme(asset) == null)
                return AuthoringResult.Refused("The asset has no Touch scheme.");

            return Write(asset, doc =>
            {
                // Remove and re-add in order, so only the Touch scheme's entry changes in the file.
                var schemes = new List<InputControlScheme>(doc.controlSchemes);
                foreach (InputControlScheme scheme in schemes)
                    doc.RemoveControlScheme(scheme.name);

                foreach (InputControlScheme scheme in schemes)
                {
                    bool touch = string.Equals(scheme.name, TouchSchemeName, StringComparison.OrdinalIgnoreCase);
                    doc.AddControlScheme(touch ? scheme.WithOptionalDevice("<" + TouchRigLayout.LayoutName + ">") : scheme);
                }
            });
        }

        /// <summary>Whether the asset's Touch scheme lists the rig device (always true when it has none).</summary>
        public static bool TouchSchemeListsRig(InputActionAsset asset)
        {
            InputControlScheme? scheme = TouchScheme(asset);
            if (scheme == null)
                return true;

            foreach (InputControlScheme.DeviceRequirement requirement in scheme.Value.deviceRequirements)
            {
                if (string.Equals(requirement.controlPath, "<" + TouchRigLayout.LayoutName + ">", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Fix for a read-only asset: copies it into <c>Assets/</c> and returns the copy (imported).</summary>
        public static InputActionAsset DuplicateIntoAssets(InputActionAsset asset)
        {
            string source = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(source))
                return null;

            string target = AssetDatabase.GenerateUniqueAssetPath("Assets/" + Path.GetFileName(source));
            File.WriteAllText(target, File.ReadAllText(Path.GetFullPath(source)));
            AssetDatabase.ImportAsset(target, ImportAssetOptions.ForceSynchronousImport);
            InvalidateSceneScan();
            return AssetDatabase.LoadAssetAtPath<InputActionAsset>(target);
        }

        /// <summary>
        /// Applies <paramref name="edit"/> to <paramref name="asset"/>: in place when it has no file, else to a working
        /// copy that is written back and re-imported.
        /// </summary>
        static AuthoringResult Write(InputActionAsset asset, Action<InputActionAsset> edit)
        {
            if (Application.isPlaying)
                return AuthoringResult.Refused("Exit Play mode to change bindings.");
            if (!IsWritable(asset, out string reason))
                return AuthoringResult.Refused(reason);

            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path))
            {
                edit(asset);
                return AuthoringResult.Done();
            }

            string fullPath = Path.GetFullPath(path);
            InputActionAsset doc = InputActionAsset.FromJson(File.ReadAllText(fullPath));
            try
            {
                edit(doc);
                File.WriteAllText(fullPath, doc.ToJson());
            }
            finally
            {
                Object.DestroyImmediate(doc);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return AuthoringResult.Done();
        }

        /// <summary>The group for a new touch binding: the Touch scheme's, added if needed, else none.</summary>
        static string TouchGroup(InputActionAsset doc, out bool added, out string schemeName)
        {
            added = false;
            schemeName = TouchSchemeName;
            if (doc.controlSchemes.Count == 0)
                return null;

            InputControlScheme? existing = TouchScheme(doc);
            if (existing != null)
            {
                schemeName = existing.Value.name;
                return existing.Value.bindingGroup;
            }

            doc.AddControlScheme(TouchSchemeName)
                .WithOptionalDevice("<Touchscreen>")
                .WithOptionalDevice("<" + TouchRigLayout.LayoutName + ">");
            added = true;
            return TouchSchemeName;
        }

        static void ForEachBinding(InputActionAsset doc, Action<InputAction, int, InputBinding> visit)
        {
            foreach (InputActionMap map in doc.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                        visit(action, i, action.bindings[i]);
                }
            }
        }

        /// <summary>Erases every binding whose path is exactly the control's.</summary>
        static void RemoveBindings(InputActionAsset doc, string controlName)
        {
            if (string.IsNullOrEmpty(controlName))
                return;

            foreach (InputActionMap map in doc.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = action.bindings.Count - 1; i >= 0; i--)
                    {
                        if (IsExactly(action.bindings[i].path, controlName))
                        {
                            AssertTouch(action.bindings[i]);
                            action.ChangeBinding(i).Erase();
                        }
                    }
                }
            }
        }

        static void RenameIn(InputActionAsset doc, string oldName, string newName)
        {
            if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
                return;

            string oldPrefix = PathFor(oldName);
            foreach (InputActionMap map in doc.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        InputBinding binding = action.bindings[i];
                        if (!IsExactly(binding.path, oldName) && !IsUnder(binding.path, oldName))
                            continue;

                        AssertTouch(binding);
                        binding.path = PathFor(newName) + binding.path.Substring(oldPrefix.Length);
                        action.ChangeBinding(i).To(binding);
                    }
                }
            }
        }

        static void ChangeOwn(InputActionAsset doc, string controlName, Action<InputBindingBox> change)
        {
            foreach (InputActionMap map in doc.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        if (!IsExactly(action.bindings[i].path, controlName))
                            continue;

                        AssertTouch(action.bindings[i]);
                        var box = new InputBindingBox(action.bindings[i]);
                        change(box);
                        action.ChangeBinding(i).To(box.Binding);
                        return;
                    }
                }
            }
        }

        static void ChangeById(InputActionAsset doc, Guid bindingId, Action<InputBindingBox> change)
        {
            foreach (InputActionMap map in doc.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        if (action.bindings[i].id != bindingId)
                            continue;

                        var box = new InputBindingBox(action.bindings[i]);
                        change(box);
                        action.ChangeBinding(i).To(box.Binding);
                        return;
                    }
                }
            }
        }

        static void AssertTouch(InputBinding binding)
        {
            if (!IsTouchPath(binding.path))
                throw new InvalidOperationException($"Touch authoring refused to change '{binding.path}': it only writes <TouchRig>/ bindings.");
        }

        /// <summary>A mutable holder, so a lambda can edit an <see cref="InputBinding"/> struct.</summary>
        sealed class InputBindingBox
        {
            public InputBinding Binding;

            public InputBindingBox(InputBinding binding) => Binding = binding;

            public string processors { get => Binding.processors; set => Binding.processors = value; }
            public string interactions { get => Binding.interactions; set => Binding.interactions = value; }
            public string groups { get => Binding.groups; set => Binding.groups = value; }
        }
    }
}
#endif
