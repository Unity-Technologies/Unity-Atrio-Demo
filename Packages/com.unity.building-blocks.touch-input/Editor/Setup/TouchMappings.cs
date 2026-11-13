#if BB_TOUCH_HAS_INPUTSYSTEM
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// One input action the loaded scenes read: either an action in an asset, or an inline action serialized
    /// on a component.
    /// </summary>
    sealed class TouchInputSource
    {
        /// <summary>A key that survives reloads: <c>asset:&lt;asset guid&gt;:&lt;action id&gt;</c> or <c>inline:&lt;owner global id&gt;:&lt;field&gt;</c>.</summary>
        public string Key { get; internal set; }

        /// <summary>Whether the action is an inline <c>InputAction</c> on a component rather than in an asset.</summary>
        public bool IsInline { get; internal set; }

        /// <summary>The action's name ("Move"); an unnamed inline action shows its field's name.</summary>
        public string Name { get; internal set; }

        /// <summary>Where the action lives, such as "PlayerControls · Player" or "Door · Interact".</summary>
        public string Where { get; internal set; }

        /// <summary>Asset source: the asset. <c>null</c> for an inline source.</summary>
        public InputActionAsset Asset { get; internal set; }

        /// <summary>Asset source: the action's id in <see cref="Asset"/>.</summary>
        public Guid ActionId { get; internal set; }

        /// <summary>Inline source: the component the action is serialized on.</summary>
        public Component Owner { get; internal set; }

        /// <summary>Inline source: the <see cref="SerializedProperty.propertyPath"/> of the serialized <c>InputAction</c>.</summary>
        public string ActionPath { get; internal set; }

        /// <summary>Every component in the scene that reads this action, in hierarchy order.</summary>
        public List<Component> Readers { get; } = new List<Component>();

        /// <summary>Whether a joystick can drive it (a Vector2 action).</summary>
        public bool TakesStick { get; internal set; }

        /// <summary>Whether a button can drive it (a press).</summary>
        public bool TakesButton { get; internal set; }

        /// <summary>The control names in its <c>&lt;TouchRig&gt;/&lt;name&gt;</c> bindings, one per mapping.</summary>
        public List<string> ControlNames { get; } = new List<string>();

        /// <summary>Whether the asset is read-only because it is inside an immutable package.</summary>
        public string ReadOnlyReason { get; internal set; }

        /// <summary>Whether a control of <paramref name="control"/>'s kind can drive this action.</summary>
        public bool Takes(TouchControl control) => control is TouchJoystick ? TakesStick : TakesButton;

        /// <summary>Whether <paramref name="control"/> is already mapped to it.</summary>
        public bool IsMappedTo(TouchControl control)
        {
            string name = TouchBindingAuthoring.ControlName(control);
            foreach (string mapped in ControlNames)
            {
                if (string.Equals(mapped, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Which scene action each touch control drives. A mapping is a <c>&lt;TouchRig&gt;/&lt;name&gt;</c> binding
    /// on the action, so there is no separate record to drift.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asset bindings are written and re-imported, and Undo does not reverse them. Inline bindings are undoable.
    /// Only <c>&lt;TouchRig&gt;/</c> bindings are touched, and nothing is written in Play.
    /// </para>
    /// <para>
    /// The scan walks every serialized field, so run it on change events, not every frame. Actions waiting for
    /// a control have no binding yet, so they are kept per rig in <see cref="SessionState"/>.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    static class TouchMappings
    {
        static TouchMappings()
        {
            // Undo deserializes in place, so a live inline action keeps its old binding cache.
            Undo.undoRedoPerformed += () => ForgetAllCachedBindings(mappedOnly: false);
        }

        static void ForgetAllCachedBindings(bool mappedOnly)
        {
            if (Application.isPlaying)
                return;

            foreach (TouchInputSource source in Scan())
            {
                if (source.IsInline && (!mappedOnly || source.ControlNames.Count > 0))
                    ForgetCachedBindings(LiveAction(source.Owner, source.ActionPath));
            }
        }

        const string k_InlineBindings = "m_SingletonActionBindings";
        const string k_PendingKey = "BuildingBlocks.TouchInput.PendingMappings.";

        /// <summary>Every action the loaded scenes read, in hierarchy order of each one's first reader.</summary>
        public static List<TouchInputSource> Scan()
        {
            var sources = new List<TouchInputSource>();
            var byKey = new Dictionary<string, TouchInputSource>();
            var behaviours = new List<MonoBehaviour>();

            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    root.GetComponentsInChildren(true, behaviours);
                    foreach (MonoBehaviour behaviour in behaviours)
                    {
                        if (behaviour == null || behaviour is BaseInputModule || behaviour is TouchInputBridge)
                            continue;

                        Collect(behaviour, sources, byKey);
                    }
                }
            }

            return sources;
        }

        static void Collect(MonoBehaviour behaviour, List<TouchInputSource> into, Dictionary<string, TouchInputSource> byKey)
        {
            using var serialized = new SerializedObject(behaviour);
            SerializedProperty property = serialized.GetIterator();
            bool enter = true;
            while (property.Next(enter))
            {
                enter = false;
                if (property.propertyType == SerializedPropertyType.ObjectReference)
                {
                    switch (property.objectReferenceValue)
                    {
                        case InputActionReference reference:
                            AddAssetAction(behaviour, reference.action, into, byKey);
                            break;
                        case InputActionAsset asset:
                            foreach (InputActionMap map in asset.actionMaps)
                            {
                                foreach (InputAction action in map.actions)
                                    AddAssetAction(behaviour, action, into, byKey);
                            }

                            break;
                    }

                    continue;
                }

                if (property.propertyType != SerializedPropertyType.Generic)
                    continue;

                if (property.type == "InputActionProperty")
                {
                    if (property.FindPropertyRelative("m_UseReference").boolValue)
                    {
                        if (property.FindPropertyRelative("m_Reference").objectReferenceValue is InputActionReference byRef)
                            AddAssetAction(behaviour, byRef.action, into, byKey);
                    }
                    else
                    {
                        AddInline(behaviour, property.FindPropertyRelative("m_Action"), property.displayName, into, byKey);
                    }

                    continue;
                }

                if (property.type == "InputAction")
                {
                    AddInline(behaviour, property, property.displayName, into, byKey);
                    continue;
                }

                // Only structs and arrays can hold an action further down.
                enter = true;
            }
        }

        static void AddAssetAction(Component reader, InputAction action, List<TouchInputSource> into, Dictionary<string, TouchInputSource> byKey)
        {
            InputActionAsset asset = action?.actionMap?.asset;
            if (asset == null)
                return;

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
            string key = "asset:" + (string.IsNullOrEmpty(guid) ? asset.GetInstanceIDSafe().ToString() : guid) + ":" + action.id;
            if (byKey.TryGetValue(key, out TouchInputSource existing))
            {
                if (!existing.Readers.Contains(reader))
                    existing.Readers.Add(reader);
                return;
            }

            var source = new TouchInputSource
            {
                Key = key,
                IsInline = false,
                Name = action.name,
                Where = asset.name + " · " + action.actionMap.name,
                Asset = asset,
                ActionId = action.id,
                TakesStick = TouchBindingAuthoring.IsCompatible(action.type, action.expectedControlType, true),
                TakesButton = TouchBindingAuthoring.IsCompatible(action.type, action.expectedControlType, false),
            };
            source.Readers.Add(reader);
            if (!TouchBindingAuthoring.IsWritable(asset, out string reason))
                source.ReadOnlyReason = reason;

            foreach (InputBinding binding in action.bindings)
                AddControlName(source, binding.path);

            byKey.Add(key, source);
            into.Add(source);
        }

        static void AddInline(Component owner, SerializedProperty actionProperty, string fieldName, List<TouchInputSource> into,
            Dictionary<string, TouchInputSource> byKey)
        {
            if (actionProperty == null)
                return;

            SerializedProperty bindings = actionProperty.FindPropertyRelative(k_InlineBindings);
            string name = actionProperty.FindPropertyRelative("m_Name")?.stringValue;
            int count = bindings != null ? bindings.arraySize : 0;

            // An untouched field (no name, no binding) reads nothing.
            if (string.IsNullOrEmpty(name) && count == 0)
                return;

            string key = "inline:" + GlobalObjectId.GetGlobalObjectIdSlow(owner) + ":" + actionProperty.propertyPath;
            if (byKey.ContainsKey(key))
                return;

            var type = (InputActionType)actionProperty.FindPropertyRelative("m_Type").enumValueIndex;
            string expected = actionProperty.FindPropertyRelative("m_ExpectedControlType")?.stringValue;
            var source = new TouchInputSource
            {
                Key = key,
                IsInline = true,
                Name = string.IsNullOrEmpty(name) ? fieldName : name,
                Where = owner.gameObject.name + " · " + ObjectNames.NicifyVariableName(owner.GetType().Name),
                Owner = owner,
                ActionPath = actionProperty.propertyPath,
                TakesStick = TouchBindingAuthoring.IsCompatible(type, expected, true),
                TakesButton = TouchBindingAuthoring.IsCompatible(type, expected, false),
            };
            source.Readers.Add(owner);
            for (int i = 0; i < count; i++)
                AddControlName(source, bindings.GetArrayElementAtIndex(i).FindPropertyRelative("m_Path").stringValue);

            byKey.Add(key, source);
            into.Add(source);
        }

        static void AddControlName(TouchInputSource source, string path)
        {
            if (!TouchBindingAuthoring.IsTouchPath(path))
                return;

            string name = path.Substring(TouchBindingAuthoring.PathPrefix.Length);
            // Sub-control bindings (<TouchRig>/move/x) are hand-written. Show them as the control; never edit them here.
            int slash = name.IndexOf('/');
            if (slash >= 0)
                name = name.Substring(0, slash);
            if (name.Length > 0 && !source.ControlNames.Contains(name))
                source.ControlNames.Add(name);
        }

        /// <summary>Makes <paramref name="control"/> drive <paramref name="source"/>'s action, keeping its other mappings.</summary>
        public static AuthoringResult Map(TouchInputSource source, TouchControl control)
        {
            if (source == null || control == null)
                return AuthoringResult.Refused("No action or no control.");
            if (Application.isPlaying)
                return AuthoringResult.Refused("Exit Play mode to change mappings.");
            if (!source.Takes(control))
                return AuthoringResult.Refused($"{source.Name} does not take a {(control is TouchJoystick ? "Vector2" : "button")} value.");

            string name = TouchBindingAuthoring.ControlName(control);
            if (name.Length == 0)
                return AuthoringResult.Refused("Give the control an id first: its binding path is made from it.");

            AuthoringResult result;
            if (!source.IsInline)
            {
                result = TouchBindingAuthoring.AddTouchBinding(source.Asset, source.ActionId, name);
            }
            else
            {
                result = EditInline(source, "Map " + control.name + " to " + source.Name, action =>
                {
                    foreach (InputBinding b in action.bindings)
                    {
                        if (TouchBindingAuthoring.IsExactly(b.path, name))
                            return;
                    }

                    action.AddBinding(TouchBindingAuthoring.PathFor(name));
                });
            }

            if (result.Ok)
                SetPending(control.GetComponentInParent<TouchRig>(true), source.Key, false);
            return result;
        }

        /// <summary>Removes only the mapping from <paramref name="controlName"/> to <paramref name="source"/>'s action.</summary>
        public static AuthoringResult Unmap(TouchInputSource source, string controlName)
        {
            if (source == null || string.IsNullOrEmpty(controlName))
                return AuthoringResult.Done();
            if (Application.isPlaying)
                return AuthoringResult.Refused("Exit Play mode to change mappings.");

            if (!source.IsInline)
                return TouchBindingAuthoring.RemoveTouchBinding(source.Asset, source.ActionId, controlName);

            return EditInline(source, "Remove Touch Mapping", action =>
            {
                for (int i = action.bindings.Count - 1; i >= 0; i--)
                {
                    if (TouchBindingAuthoring.IsExactly(action.bindings[i].path, controlName))
                        action.ChangeBinding(i).Erase();
                }
            });
        }

        /// <summary>Moves a mapping from control <paramref name="fromName"/> to <paramref name="to"/>.</summary>
        public static AuthoringResult Move(TouchInputSource source, string fromName, TouchControl to)
        {
            if (to != null && string.Equals(fromName, TouchBindingAuthoring.ControlName(to), StringComparison.OrdinalIgnoreCase))
                return AuthoringResult.Done();

            AuthoringResult mapped = Map(source, to);
            if (!mapped.Ok)
                return mapped;

            // Map re-imported the asset, so remove through a fresh scan of the same action.
            TouchInputSource fresh = Find(Scan(), source.Key) ?? source;
            AuthoringResult removed = Unmap(fresh, fromName);
            return removed.Ok ? mapped : removed;
        }

        /// <summary>
        /// Renames <paramref name="control"/>'s id and GameObject, and moves all its mappings with it.
        /// Refuses an empty or taken id.
        /// </summary>
        public static AuthoringResult Rename(TouchControl control, string newId)
        {
            if (control == null)
                return AuthoringResult.Refused("No control.");

            string trimmed = (newId ?? string.Empty).Trim();
            if (trimmed == control.ActionId)
                return AuthoringResult.Done();
            if (TouchControlIds.Key(trimmed).Length == 0)
                return AuthoringResult.Refused("An id needs at least one letter or digit.");

            TouchRig rig = control.GetComponentInParent<TouchRig>(true);
            if (rig != null && TouchControlIds.IsTaken(rig, trimmed, control))
                return AuthoringResult.Refused($"\"{trimmed}\" is taken in this rig (ids ignore case and punctuation).");
            if (Application.isPlaying)
                return AuthoringResult.Refused("Exit Play mode to rename a control.");

            string oldName = TouchBindingAuthoring.ControlName(control);
            string newName = TouchRigDevice.ControlNameFor(trimmed);
            if (oldName.Length > 0 && newName != oldName)
            {
                var assets = new HashSet<InputActionAsset>();
                foreach (TouchInputSource source in Scan())
                {
                    if (!source.ControlNames.Exists(n => string.Equals(n, oldName, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    if (!source.IsInline)
                    {
                        if (assets.Add(source.Asset))
                        {
                            AuthoringResult moved = TouchBindingAuthoring.RenameBinding(source.Asset, oldName, newName);
                            if (!moved.Ok)
                                return moved;
                        }

                        continue;
                    }

                    string oldPrefix = TouchBindingAuthoring.PathFor(oldName);
                    EditInline(source, "Rename Touch Control", action =>
                    {
                        for (int i = 0; i < action.bindings.Count; i++)
                        {
                            string path = action.bindings[i].path;
                            if (TouchBindingAuthoring.IsExactly(path, oldName) ||
                                path.StartsWith(oldPrefix + "/", StringComparison.OrdinalIgnoreCase))
                                action.ChangeBinding(i).WithPath(TouchBindingAuthoring.PathFor(newName) + path.Substring(oldPrefix.Length));
                        }
                    });
                }
            }

            TouchControlIds.Apply(control, trimmed);
            return AuthoringResult.Done();
        }

        /// <summary>
        /// Edits an inline action through the live <see cref="InputAction"/> on the component.
        /// </summary>
        /// <remarks>
        /// Editing only the serialized bindings does not stick. The live action caches its bindings and writes them
        /// back over the field on the next save or entering Play.
        /// </remarks>
        static AuthoringResult EditInline(TouchInputSource source, string undoName, Action<InputAction> edit)
        {
            if (source.Owner == null)
                return AuthoringResult.Refused("The component that reads this action is gone.");

            InputAction action = LiveAction(source.Owner, source.ActionPath);
            if (action == null)
                return AuthoringResult.Refused($"Can't find {source.Name} on {source.Owner.name} any more.");

            Undo.RecordObject(source.Owner, undoName);
            edit(action);
            ForgetCachedBindings(action);
            EditorUtility.SetDirty(source.Owner);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source.Owner);
            return AuthoringResult.Done();
        }

        static readonly System.Reflection.FieldInfo s_ActionMapField =
            typeof(InputAction).GetField("m_ActionMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        /// <summary>
        /// Drops a disabled standalone action's hidden action map, so the next read rebuilds it from the field.
        /// </summary>
        /// <remarks>
        /// The action builds that map once and keeps it. Undo and Play without a domain reload replace the bindings
        /// but not the map, so the action would keep using the old bindings.
        /// </remarks>
        internal static void ForgetCachedBindings(InputAction action)
        {
            if (action == null || action.enabled || action.actionMap != null || s_ActionMapField == null)
                return;
            s_ActionMapField.SetValue(action, null);
        }

        /// <summary>Finds the live object at a serialized path such as <c>actions.Array.data[2]</c>, by reflection.</summary>
        static InputAction LiveAction(Component owner, string propertyPath)
        {
            object current = owner;
            string[] parts = propertyPath.Replace(".Array.data[", ".[").Split('.');
            foreach (string part in parts)
            {
                if (current == null)
                    return null;

                if (part.StartsWith("[", StringComparison.Ordinal))
                {
                    int index = int.Parse(part.Substring(1, part.Length - 2));
                    if (!(current is System.Collections.IList list) || index >= list.Count)
                        return null;
                    current = list[index];
                    continue;
                }

                System.Reflection.FieldInfo field = null;
                for (Type type = current.GetType(); type != null && field == null; type = type.BaseType)
                    field = type.GetField(part, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                                               System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (field == null)
                    return null;
                current = field.GetValue(current);
            }

            return current as InputAction;
        }

        /// <summary>The source with <paramref name="key"/> in <paramref name="sources"/>, or <c>null</c>.</summary>
        public static TouchInputSource Find(List<TouchInputSource> sources, string key) =>
            sources.Find(s => s.Key == key);

        static string PendingKey(TouchRig rig) => k_PendingKey + (rig != null ? GlobalObjectId.GetGlobalObjectIdSlow(rig).ToString() : "none");

        /// <summary>The source keys chosen on <paramref name="rig"/> that have no control yet, oldest first.</summary>
        public static List<string> Pending(TouchRig rig)
        {
            string raw = SessionState.GetString(PendingKey(rig), string.Empty);
            return raw.Length == 0 ? new List<string>() : new List<string>(raw.Split('\n'));
        }

        /// <summary>Adds or removes <paramref name="sourceKey"/> from <paramref name="rig"/>'s pending list.</summary>
        public static void SetPending(TouchRig rig, string sourceKey, bool pending)
        {
            List<string> keys = Pending(rig);
            keys.Remove(sourceKey);
            if (pending)
                keys.Add(sourceKey);
            SessionState.SetString(PendingKey(rig), string.Join("\n", keys));
        }

        /// <summary>A Unity object to ping for a source: its asset, or the component reading it.</summary>
        public static Object PingTarget(TouchInputSource source) =>
            source == null ? null : source.IsInline ? source.Owner : (Object)source.Asset;
    }
}
#endif
