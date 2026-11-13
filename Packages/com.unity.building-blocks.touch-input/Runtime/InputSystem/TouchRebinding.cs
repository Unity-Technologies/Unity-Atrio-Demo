using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Changes which on-screen control drives an action while the game runs. Changes live in memory; call
    /// <see cref="Save(InputActionAsset)"/> to keep them and <see cref="Load(InputActionAsset)"/> next session
    /// to restore them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Rebind(InputAction, string)"/> overrides only the action's <c>&lt;TouchRig&gt;/…</c> binding; the
    /// asset and other bindings are untouched. <see cref="Map"/> adds a new touch binding in memory, which
    /// <see cref="Save(InputActionAsset)"/> keeps beside the overrides. Neither ever writes the asset file.
    /// </para>
    /// <para>
    /// Problems come back in a <see cref="TouchRebindResult"/> and are logged once; expected failures never throw.
    /// The API is safe to call from hot-reloaded code.
    /// </para>
    /// <code>
    /// InputAction interact = playerInput.actions.FindAction("Interact");
    /// TouchRebinding.Rebind(interact, "Pet");             // &lt;TouchRig&gt;/jump  →  &lt;TouchRig&gt;/pet
    /// TouchRebinding.Map(interact, "Jump");               // Interact now also reads &lt;TouchRig&gt;/jump
    /// TouchRebinding.Save(playerInput.actions);           // &lt;persistentDataPath&gt;/TouchRebinding/&lt;asset&gt;.json
    /// // next session, before the player reads input:
    /// TouchRebinding.Load(playerInput.actions);
    /// </code>
    /// </remarks>
    public static class TouchRebinding
    {
        /// <summary>Folder under <see cref="Application.persistentDataPath"/> that <see cref="DefaultPath"/> uses.</summary>
        public const string FolderName = "TouchRebinding";

        /// <summary>
        /// Points the action's one <c>&lt;TouchRig&gt;/…</c> binding at the control <paramref name="controlId"/>.
        /// Fails, changing nothing, if the action has no touch binding or several; then use
        /// <see cref="Rebind(InputAction, string, string)"/>.
        /// </summary>
        /// <param name="action">The action to rebind. It may be enabled; it picks up the new control at once.</param>
        /// <param name="controlId">The id of the control that should drive it, such as <c>"Pet"</c>.</param>
        public static TouchRebindResult Rebind(InputAction action, string controlId) =>
            RebindWhere(action, null, controlId);

        /// <summary>
        /// Moves the action's touch binding from <paramref name="fromControlId"/> to <paramref name="toControlId"/>.
        /// For an action driven by several touch controls.
        /// </summary>
        public static TouchRebindResult Rebind(InputAction action, string fromControlId, string toControlId)
        {
            if (string.IsNullOrEmpty(TouchRigLayout.ControlName(fromControlId)))
                return Finish(action, false, 0, Problems($"\"{fromControlId}\" is not a control id."));

            return RebindWhere(action, fromControlId, toControlId);
        }

        /// <summary>
        /// Removes the overrides on the action's touch bindings, so it reads the asset's controls again. Other
        /// bindings keep their overrides.
        /// </summary>
        public static TouchRebindResult Reset(InputAction action)
        {
            if (action == null)
                return Finish(null, false, 0, Problems("No action to reset."));

            int changed = 0;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                InputBinding binding = bindings[i];
                if (!binding.hasOverrides || (!IsTouchPath(binding.path) && !IsTouchPath(binding.overridePath)))
                    continue;

                action.RemoveBindingOverride(i);
                changed++;
            }

            return Finish(action, true, changed, null);
        }

        /// <summary>
        /// Every binding override on <paramref name="asset"/> in the Input System's JSON format, plus any
        /// <see cref="Map"/> bindings in a <c>"touchMappings"</c> list. Returns <c>""</c> when there is nothing to keep.
        /// </summary>
        /// <remarks>This includes overrides made elsewhere, such as a keyboard rebind.</remarks>
        public static string Export(InputActionAsset asset)
        {
            if (asset == null)
                return "";

            string overrides = asset.SaveBindingOverridesAsJson();
            List<MappingEntry> mappings = MappingEntriesOf(asset);
            if (mappings.Count == 0)
                return overrides;

            OverrideList file = string.IsNullOrEmpty(overrides) ? null : JsonUtility.FromJson<OverrideList>(overrides);
            file ??= new OverrideList();
            file.bindings ??= new List<OverrideEntry>();
            file.touchMappings = mappings;
            return JsonUtility.ToJson(file);
        }

        /// <summary>
        /// Replaces the overrides and added mappings on <paramref name="asset"/> with those in <paramref name="json"/>.
        /// Empty text removes them all; text that is not override JSON fails and changes nothing, without throwing.
        /// </summary>
        /// <remarks>Entries for bindings or actions the asset does not have are skipped and reported.</remarks>
        public static TouchRebindResult Apply(InputActionAsset asset, string json)
        {
            if (asset == null)
                return Finish(null, false, 0, Problems("No action asset to apply binding overrides to."));

            if (string.IsNullOrWhiteSpace(json))
            {
                asset.RemoveAllBindingOverrides();
                return Finish(null, true, ReplayMappings(asset, null, null), null);
            }

            OverrideList parsed;
            try
            {
                parsed = JsonUtility.FromJson<OverrideList>(json);
            }
            catch (Exception)
            {
                parsed = null;
            }

            // JsonUtility reads "{}" as an empty list; only text that names the list counts as overrides.
            if (parsed == null || parsed.bindings == null || json.IndexOf("\"bindings\"", StringComparison.Ordinal) < 0)
                return Finish(null, false, 0, Problems(
                    $"The text is not binding-override JSON (the shape SaveBindingOverridesAsJson writes); '{asset.name}' was not changed."));

            // Mappings first, so the overrides saved on them find their bindings.
            List<string> problems = null;
            int mappingsChanged = ReplayMappings(asset, parsed.touchMappings, msg => (problems ??= new List<string>()).Add(msg));

            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InputBinding binding in asset.bindings)
                known.Add(binding.id.ToString());

            var unknown = new StringBuilder();
            var usable = new OverrideList { bindings = new List<OverrideEntry>(parsed.bindings.Count) };
            foreach (OverrideEntry entry in parsed.bindings)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.id) && known.Contains(entry.id))
                {
                    usable.bindings.Add(entry);
                    continue;
                }

                string label = entry == null ? "(empty)" : string.IsNullOrEmpty(entry.action) ? entry.id : entry.action;
                unknown.Append(unknown.Length == 0 ? label : ", " + label);
            }

            if (unknown.Length > 0)
                (problems ??= new List<string>()).Add($"'{asset.name}' has no binding for the saved override(s) of {unknown}; skipped.");

            try
            {
                // Only overrides whose binding exists go through, so the Input System has nothing to warn about.
                asset.LoadBindingOverridesFromJson(usable.bindings.Count > 0 ? JsonUtility.ToJson(usable) : "", true);
                // (usable carries no mappings: JsonUtility writes an empty list, which the Input System ignores.)
            }
            catch (Exception e)
            {
                return Finish(null, false, 0, Problems($"The Input System refused the overrides: {e.Message}"));
            }

            return Finish(null, true, usable.bindings.Count + mappingsChanged, problems);
        }

        /// <summary>
        /// Where <see cref="Save(InputActionAsset)"/> and <see cref="Load(InputActionAsset)"/> keep the asset's
        /// overrides: <c>&lt;persistentDataPath&gt;/TouchRebinding/&lt;asset name&gt;.json</c>.
        /// </summary>
        public static string DefaultPath(InputActionAsset asset)
        {
            string name = asset != null && !string.IsNullOrEmpty(asset.name) ? asset.name : "Actions";
            return Path.Combine(Application.persistentDataPath, FolderName, SafeFileName(name) + ".json");
        }

        /// <summary>Saves the asset's overrides to <see cref="DefaultPath"/>. See <see cref="Save(InputActionAsset, string)"/>.</summary>
        public static bool Save(InputActionAsset asset) => asset != null && Save(asset, DefaultPath(asset));

        /// <summary>
        /// Writes <see cref="Export"/> to <paramref name="path"/>; with nothing to keep, the file is empty and loading
        /// it clears overrides. Never throws; returns <c>false</c> and logs a warning if it cannot write.
        /// </summary>
        public static bool Save(InputActionAsset asset, string path)
        {
            if (asset == null || string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("TouchRebinding.Save: needs an action asset and a file path.");
                return false;
            }

            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(path, Export(asset));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TouchRebinding.Save: could not write '{path}': {e.Message}", asset);
                return false;
            }

            return true;
        }

        /// <summary>Loads the overrides saved at <see cref="DefaultPath"/>. See <see cref="Load(InputActionAsset, string)"/>.</summary>
        public static TouchRebindResult Load(InputActionAsset asset) => Load(asset, asset != null ? DefaultPath(asset) : null);

        /// <summary>
        /// Reads <paramref name="path"/> and applies it, never throwing. A missing or unreadable file fails and
        /// changes nothing.
        /// </summary>
        public static TouchRebindResult Load(InputActionAsset asset, string path)
        {
            if (asset == null)
                return Finish(null, false, 0, Problems("No action asset to load binding overrides into."));

            string json;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return Finish(null, false, 0, Problems($"No saved binding overrides at '{path}'."));

                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return Finish(null, false, 0, Problems($"Could not read '{path}': {e.Message}"));
            }

            return Apply(asset, json);
        }

        /// <summary>
        /// Makes the control <paramref name="controlId"/> also drive <paramref name="action"/> while the game runs, by
        /// adding a touch binding in memory. Other bindings are left alone.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Fails, changing nothing, if the control's value does not fit the action: a joystick gives a Vector2, a
        /// button gives a press. If the rig's device lacks the control, the binding is added unchecked and reported.
        /// </para>
        /// <para>
        /// In the Editor the binding is removed when Play stops. Use the Touch Rig's Mappings card to make it permanent.
        /// </para>
        /// </remarks>
        /// <param name="action">The action the control should drive, <c>playerInput.actions.FindAction("Interact")</c>.</param>
        /// <param name="controlId">The control's id, such as <c>"Jump"</c>.</param>
        public static TouchRebindResult Map(InputAction action, string controlId)
        {
            if (action == null)
                return Finish(null, false, 0, Problems("No action to map."));

            string name = TouchRigLayout.ControlName(controlId);
            if (name.Length == 0)
                return Finish(action, false, 0, Problems($"\"{controlId}\" is not a control id."));

            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (!bindings[i].isComposite && string.Equals(ControlNameIn(bindings[i].effectivePath), name, StringComparison.OrdinalIgnoreCase))
                    return Finish(action, true, 0, null);
            }

            List<string> problems = null;
            if (TryKindOnDevice(name, out TouchRigLayout.Kind kind))
            {
                bool stick = kind == TouchRigLayout.Kind.Stick;
                if (!TouchRigLayout.Accepts(action.type, action.expectedControlType, stick))
                {
                    string expects = string.IsNullOrEmpty(action.expectedControlType) ? action.type.ToString() : action.expectedControlType;
                    return Finish(action, false, 0, Problems(stick
                        ? $"'{controlId}' is a joystick (a Vector2) and '{action.name}' expects {expects}; map a button to it, or the joystick to a Vector2 action."
                        : $"'{controlId}' is a button (a press) and '{action.name}' expects {expects}; map a joystick to it, or the button to a Button action."));
                }
            }
            else
            {
                problems = Problems(
                    $"The Touch Rig device has no control '{name}' yet, so its value type was not checked; '{action.name}' will read it once a control with that id is under the rig.");
            }

            var binding = new InputBinding { path = TouchRigLayout.PathPrefix + name, groups = TouchGroups(action) };
            Guid id = Guid.NewGuid();
            binding.id = id;
            try
            {
                action.AddBinding(binding);
            }
            catch (Exception e)
            {
                return Finish(action, false, 0, Problems($"The Input System refused the binding: {e.Message}"));
            }

            s_Mappings.Add(new RuntimeMapping { Action = action, BindingId = id, Path = binding.path });
            return Finish(action, true, 1, problems);
        }

        /// <summary>
        /// Removes the binding <see cref="Map"/> added for <paramref name="controlId"/>, even if it was rebound since.
        /// Fails for a binding the asset itself has; rebind that one instead.
        /// </summary>
        public static TouchRebindResult Unmap(InputAction action, string controlId)
        {
            if (action == null)
                return Finish(null, false, 0, Problems("No action to unmap."));

            string name = TouchRigLayout.ControlName(controlId);
            if (name.Length == 0)
                return Finish(action, false, 0, Problems($"\"{controlId}\" is not a control id."));

            Prune();
            int removed = 0;
            bool authored = false;
            for (int i = action.bindings.Count - 1; i >= 0; i--)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite)
                    continue;

                bool reads = string.Equals(ControlNameIn(binding.path), name, StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(ControlNameIn(binding.effectivePath), name, StringComparison.OrdinalIgnoreCase);
                if (!reads)
                    continue;

                int entry = IndexOfMapping(action, binding.id);
                if (entry < 0)
                {
                    authored = true;
                    continue;
                }

                action.ChangeBinding(i).Erase();
                s_Mappings.RemoveAt(entry);
                removed++;
            }

            if (removed > 0)
                return Finish(action, true, removed, null);
            if (authored)
                return Finish(action, false, 0, Problems(
                    $"'{action.name}' reads {TouchRigLayout.PathPrefix}{name} from its asset, not from a runtime mapping; Unmap removes only what Map added. Rebind it, or remove it on the Touch Rig's Mappings card outside Play."));
            return Finish(action, false, 0, Problems($"'{action.name}' has no mapping to {TouchRigLayout.PathPrefix}{name}."));
        }

        /// <summary>
        /// The binding paths <see cref="Map"/> added to <paramref name="action"/> that are still on it, in the order
        /// added. Empty for none or a <c>null</c> action.
        /// </summary>
        public static string[] RuntimeMappings(InputAction action)
        {
            var paths = new List<string>();
            if (action != null)
            {
                Prune();
                foreach (RuntimeMapping mapping in s_Mappings)
                {
                    if (mapping.Action == action)
                        paths.Add(mapping.Path);
                }
            }

            return paths.ToArray();
        }

        /// <summary>Every live runtime mapping: the action and the path <see cref="Map"/> added, in the order added.</summary>
        internal static void CollectRuntimeMappings(List<InputAction> actions, List<string> paths)
        {
            actions.Clear();
            paths.Clear();
            Prune();
            foreach (RuntimeMapping mapping in s_Mappings)
            {
                actions.Add(mapping.Action);
                paths.Add(mapping.Path);
            }
        }

        /// <summary>
        /// Removes every binding <see cref="Map"/> added and returns how many. The Editor calls it when Play stops.
        /// </summary>
        internal static int UnmapAll()
        {
            int removed = 0;
            for (int i = s_Mappings.Count - 1; i >= 0; i--)
            {
                RuntimeMapping mapping = s_Mappings[i];
                s_Mappings.RemoveAt(i);
                InputActionAsset asset = mapping.Action.actionMap != null ? mapping.Action.actionMap.asset : null;
                if (!ReferenceEquals(asset, null) && asset == null)
                    continue;

                int index = IndexOfBinding(mapping.Action, mapping.BindingId);
                if (index < 0)
                    continue;

                try
                {
                    mapping.Action.ChangeBinding(index).Erase();
                    removed++;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[TouchRebinding] Could not remove the runtime mapping {mapping.Path} from '{mapping.Action.name}': {e.Message}");
                }
            }

            return removed;
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void HookPlayModeExit()
        {
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
                UnmapAll();
        }
#endif

        sealed class RuntimeMapping
        {
            public InputAction Action;
            public Guid BindingId;
            public string Path;
        }

        // Package code is not hot-reloaded, so a static registry is fine here.
        static readonly List<RuntimeMapping> s_Mappings = new List<RuntimeMapping>();

        /// <summary>Drops entries whose binding is gone (erased elsewhere, the asset re-imported or destroyed).</summary>
        static void Prune()
        {
            for (int i = s_Mappings.Count - 1; i >= 0; i--)
            {
                RuntimeMapping mapping = s_Mappings[i];
                InputActionAsset asset = mapping.Action.actionMap != null ? mapping.Action.actionMap.asset : null;
                bool destroyed = !ReferenceEquals(asset, null) && asset == null;
                if (destroyed || IndexOfBinding(mapping.Action, mapping.BindingId) < 0)
                    s_Mappings.RemoveAt(i);
            }
        }

        static int IndexOfMapping(InputAction action, Guid bindingId)
        {
            for (int i = 0; i < s_Mappings.Count; i++)
            {
                if (s_Mappings[i].Action == action && s_Mappings[i].BindingId == bindingId)
                    return i;
            }

            return -1;
        }

        static int IndexOfBinding(InputAction action, Guid bindingId)
        {
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].id == bindingId)
                    return i;
            }

            return -1;
        }

        /// <summary>The asset's runtime mappings as save-file entries, in the order added.</summary>
        static List<MappingEntry> MappingEntriesOf(InputActionAsset asset)
        {
            var entries = new List<MappingEntry>();
            Prune();
            foreach (RuntimeMapping mapping in s_Mappings)
            {
                InputActionMap map = mapping.Action.actionMap;
                if (map == null || map.asset != asset)
                    continue;

                int index = IndexOfBinding(mapping.Action, mapping.BindingId);
                entries.Add(new MappingEntry
                {
                    action = map.name + "/" + mapping.Action.name,
                    id = mapping.BindingId.ToString(),
                    path = mapping.Path,
                    groups = mapping.Action.bindings[index].groups ?? "",
                });
            }

            return entries;
        }

        /// <summary>
        /// Makes the asset's runtime mappings match <paramref name="wanted"/>, keeping binding ids. Returns how many
        /// bindings were added or removed.
        /// </summary>
        static int ReplayMappings(InputActionAsset asset, List<MappingEntry> wanted, Action<string> report)
        {
            int changed = 0;
            var keep = new HashSet<Guid>();
            if (wanted != null)
            {
                foreach (MappingEntry entry in wanted)
                {
                    if (entry != null && Guid.TryParse(entry.id, out Guid id))
                        keep.Add(id);
                }
            }

            Prune();
            for (int i = s_Mappings.Count - 1; i >= 0; i--)
            {
                RuntimeMapping mapping = s_Mappings[i];
                if (mapping.Action.actionMap == null || mapping.Action.actionMap.asset != asset || keep.Contains(mapping.BindingId))
                    continue;

                mapping.Action.ChangeBinding(IndexOfBinding(mapping.Action, mapping.BindingId)).Erase();
                s_Mappings.RemoveAt(i);
                changed++;
            }

            if (wanted == null)
                return changed;

            foreach (MappingEntry entry in wanted)
            {
                string controlName = entry != null ? ControlNameIn(entry.path) : null;
                if (string.IsNullOrEmpty(controlName))
                {
                    report?.Invoke($"A saved mapping of '{asset.name}' is not a {TouchRigLayout.PathPrefix}… binding; skipped.");
                    continue;
                }

                InputAction action = string.IsNullOrEmpty(entry.action) ? null : asset.FindAction(entry.action);
                if (action == null)
                {
                    report?.Invoke($"'{asset.name}' has no action '{entry.action}' for the saved mapping to {entry.path}; skipped.");
                    continue;
                }

                if (!Guid.TryParse(entry.id, out Guid id))
                    id = Guid.NewGuid();

                int existing = IndexOfBinding(action, id);
                if (existing >= 0)
                {
                    // Still there (loaded twice in one session): keep it registered, add nothing.
                    if (IndexOfMapping(action, id) < 0)
                        s_Mappings.Add(new RuntimeMapping { Action = action, BindingId = id, Path = action.bindings[existing].path });
                    continue;
                }

                bool alreadyReads = false;
                foreach (InputBinding binding in action.bindings)
                {
                    if (!binding.isComposite && string.Equals(ControlNameIn(binding.path), controlName, StringComparison.OrdinalIgnoreCase))
                        alreadyReads = true;
                }

                if (alreadyReads)
                    continue;   // the asset has it now (made permanent on the Mappings card): nothing to add

                string path = TouchRigLayout.PathPrefix + controlName;
                var added = new InputBinding { path = path, groups = string.IsNullOrEmpty(entry.groups) ? null : entry.groups };
                added.id = id;
                try
                {
                    action.AddBinding(added);
                }
                catch (Exception e)
                {
                    report?.Invoke($"The Input System refused the saved mapping {entry.action} → {path}: {e.Message}");
                    continue;
                }

                s_Mappings.Add(new RuntimeMapping { Action = action, BindingId = id, Path = path });
                changed++;
            }

            return changed;
        }

        /// <summary>The kind of the rig device's control <paramref name="name"/>, when the active bridge has one.</summary>
        static bool TryKindOnDevice(string name, out TouchRigLayout.Kind kind)
        {
            kind = TouchRigLayout.Kind.Button;
            TouchInputBridge bridge = TouchInputBridge.Active;
            if (bridge == null || bridge.Device == null || bridge.Entries == null)
                return false;

            foreach (TouchRigLayout.Entry entry in bridge.Entries)
            {
                if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    kind = entry.Kind;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The control-scheme groups for a new touch binding: those of the asset's first grouped touch binding, else
        /// a scheme listing <c>&lt;TouchRig&gt;</c>, else none.
        /// </summary>
        static string TouchGroups(InputAction action)
        {
            InputActionAsset asset = action.actionMap != null ? action.actionMap.asset : null;
            if (asset == null)
                return null;

            foreach (InputBinding binding in asset.bindings)
            {
                if (IsTouchPath(binding.path) && !string.IsNullOrEmpty(binding.groups))
                    return binding.groups;
            }

            foreach (InputControlScheme scheme in asset.controlSchemes)
            {
                foreach (InputControlScheme.DeviceRequirement requirement in scheme.deviceRequirements)
                {
                    if (!string.IsNullOrEmpty(requirement.controlPath) &&
                        requirement.controlPath.StartsWith("<" + TouchRigLayout.LayoutName + ">", StringComparison.OrdinalIgnoreCase))
                        return scheme.bindingGroup;
                }
            }

            return null;
        }

        static TouchRebindResult RebindWhere(InputAction action, string fromControlId, string toControlId)
        {
            if (action == null)
                return Finish(null, false, 0, Problems("No action to rebind."));

            string toName = TouchRigLayout.ControlName(toControlId);
            if (toName.Length == 0)
                return Finish(action, false, 0, Problems($"\"{toControlId}\" is not a control id."));

            string fromName = fromControlId != null ? TouchRigLayout.ControlName(fromControlId) : null;

            int index = -1, touchBindings = 0;
            var bindings = action.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].isComposite)
                    continue;

                string current = ControlNameIn(bindings[i].effectivePath);
                if (current == null)
                    continue;

                touchBindings++;
                if (fromName == null || string.Equals(current, fromName, StringComparison.OrdinalIgnoreCase))
                {
                    if (index < 0)
                        index = i;
                }
            }

            if (touchBindings == 0)
                return Finish(action, false, 0, Problems(
                    $"'{action.name}' has no {TouchRigLayout.PathPrefix}… binding to change; map it first (TouchRebinding.Map, or the Touch Rig's Mappings card)."));
            if (index < 0)
                return Finish(action, false, 0, Problems(
                    $"'{action.name}' has no binding to {TouchRigLayout.PathPrefix}{fromName}."));
            if (fromName == null && touchBindings > 1)
                return Finish(action, false, 0, Problems(
                    $"'{action.name}' has {touchBindings} {TouchRigLayout.PathPrefix}… bindings; name the control to move away from."));

            List<string> problems = null;
            TouchInputBridge bridge = TouchInputBridge.Active;
            if (bridge != null && bridge.Device != null && bridge.Device.TryGetChildControl(toName) == null)
                problems = Problems(
                    $"The Touch Rig device has no control '{toName}' yet; '{action.name}' will read it once a control with that id is under the rig.");

            string newPath = TouchRigLayout.PathPrefix + toName;
            InputBinding target = bindings[index];
            if (string.Equals(target.path, newPath, StringComparison.OrdinalIgnoreCase))
                action.RemoveBindingOverride(index);
            else
                action.ApplyBindingOverride(index, newPath);

            return Finish(action, true, 1, problems);
        }

        /// <summary>The control name in a <c>&lt;TouchRig&gt;/name…</c> path, or <c>null</c> for any other path.</summary>
        static string ControlNameIn(string path)
        {
            if (!IsTouchPath(path))
                return null;

            int start = TouchRigLayout.PathPrefix.Length;
            int end = path.IndexOf('/', start);
            return end < 0 ? path.Substring(start) : path.Substring(start, end - start);
        }

        static bool IsTouchPath(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith(TouchRigLayout.PathPrefix, StringComparison.OrdinalIgnoreCase);

        static TouchRebindResult Finish(InputAction action, bool succeeded, int changed, List<string> problems)
        {
            var result = new TouchRebindResult(succeeded, changed, problems);
            if (result.Problems.Length > 0)
                Debug.LogWarning($"TouchRebinding{(action != null ? $" on '{action.name}'" : "")}: {result}");
            return result;
        }

        static List<string> Problems(string problem) => new List<string> { problem };

        static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        // Mirrors the Input System's override JSON so text can be checked before anything is removed. The extra
        // mappings list is ignored by the Input System's own loader.
        [Serializable]
        sealed class OverrideList
        {
            public List<OverrideEntry> bindings;
            public List<MappingEntry> touchMappings;
        }

        [Serializable]
        sealed class MappingEntry
        {
            public string action;   // "Map/Action"
            public string id;       // the added binding's id, so overrides saved on it find it again
            public string path;     // "<TouchRig>/jump"
            public string groups;
        }

        [Serializable]
        sealed class OverrideEntry
        {
            public string action;
            public string id;
            public string path;
            public string interactions;
            public string processors;
        }
    }
}
