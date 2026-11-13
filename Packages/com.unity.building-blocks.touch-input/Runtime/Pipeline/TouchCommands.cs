using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Unity.Pipeline.Commands;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;
#if BB_TOUCH_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
#endif

// Nothing in a build references this assembly; the commands are found by reflection. Make the linker keep it.
[assembly: AlwaysLinkAssembly]

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Pipeline commands that drive a Touch Rig from a terminal or an agent: list rigs, tune, map, rebind and save.
    /// They work in the Editor and in a development Player, so a phone's joystick can be tuned from a laptop.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each command is a thin call into <see cref="TouchTuning"/> or <c>TouchRebinding</c>. A rig is named by its
    /// GameObject name and can be left out when only one rig fits. Control ids match exactly first, then ignoring case.
    /// </para>
    /// <para>
    /// A request that cannot be done throws <see cref="ArgumentException"/> saying what to change. Main thread only.
    /// Callers may pass arguments by position, so do not reorder parameters.
    /// </para>
    /// </remarks>
    [Preserve]
    public static class TouchCommands
    {
        const string k_Tag = "touch-input";

        static readonly string[] k_Settings = { "originMode", "deadZone", "saturation", "recenterOnRelease" };

        /// <summary>
        /// <c>touch_rig_get</c>: every Touch Rig in the loaded scenes and its controls (id, type, enabled, current value,
        /// Feel settings).
        /// </summary>
        /// <param name="rig">Only the rig with this GameObject name. <c>null</c> or empty: every rig.</param>
        /// <exception cref="ArgumentException">A rig name was given and no loaded rig has it.</exception>
        [Preserve]
        [CliCommand("touch_rig_get", "List Touch Rigs in the loaded scenes and their controls: id, type, enabled, value, tuning.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchRigListInfo GetRigs(
            [CliArg("rig", "Only the rig with this GameObject name. Omit for every rig.")] string rig = null)
        {
            List<TouchRig> rigs = string.IsNullOrEmpty(rig) ? AllRigs() : new List<TouchRig> { RigNamed(rig) };

            var infos = new TouchRigInfo[rigs.Count];
            for (int i = 0; i < rigs.Count; i++)
                infos[i] = Describe(rigs[i]);

            return new TouchRigListInfo { playing = Application.isPlaying, rigs = infos };
        }

        /// <summary>
        /// <c>touch_tune</c>: sets one feel setting on one control, applies tuning JSON, or both (JSON first).
        /// </summary>
        /// <remarks>The JSON argument is <c>tuning</c>, because the <c>unity</c> CLI already uses <c>--json</c>.</remarks>
        /// <param name="control">The control's id, <c>Move</c>. Needed with <paramref name="setting"/>.</param>
        /// <param name="setting"><c>originMode</c>, <c>deadZone</c>, <c>saturation</c> or <c>recenterOnRelease</c>.</param>
        /// <param name="value">A number from 0 to 1 for <c>deadZone</c> or <c>saturation</c>, an origin mode name, or
        /// <c>true</c> / <c>false</c>.</param>
        /// <param name="tuning">Tuning JSON as <see cref="TouchTuning.Export"/> writes it. Parts it cannot use are listed in the result.</param>
        /// <param name="rig">The rig's GameObject name; may be left out when it is unambiguous.</param>
        /// <exception cref="ArgumentException">Nothing to do, no such rig or control, an unknown setting, a value of the
        /// wrong type, or JSON that cannot be read.</exception>
        [Preserve]
        [CliCommand("touch_tune", "Set one Feel setting on a touch control (control, setting, value) and/or apply tuning JSON. Returns what was applied.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchTuneInfo Tune(
            [CliArg("control", "Control id, e.g. Move.")] string control = null,
            [CliArg("setting", "originMode | deadZone | saturation | recenterOnRelease.")] string setting = null,
            [CliArg("value", "Number for deadZone/saturation, Fixed|Floating|FloatingWithLeash, or true|false.")] string value = null,
            [CliArg("tuning", "Tuning JSON (as touch_save returns) to apply first. Forgiving: skipped parts are listed.")] string tuning = null,
            [CliArg("rig", "Rig GameObject name. Omit when only one rig has the control.")] string rig = null)
        {
            bool hasSetting = !string.IsNullOrEmpty(setting);
            bool hasJson = !string.IsNullOrWhiteSpace(tuning);

            if (!hasSetting && !string.IsNullOrEmpty(control))
                throw new ArgumentException($"Give --setting and --value for control '{control}' (one of {string.Join(", ", k_Settings)}).");
            if (!hasSetting && !hasJson)
                throw new ArgumentException("Nothing to tune: give --control, --setting and --value, or --tuning with tuning JSON.");
            if (hasSetting && string.IsNullOrEmpty(control))
                throw new ArgumentException($"Which control? Give --control with --setting {setting}.");
            if (hasSetting && value == null)
                throw new ArgumentException($"Give --value for --setting {setting}.");

            TouchRig target = ResolveRig(rig, hasSetting ? control : null);
            var info = new TouchTuneInfo { rig = target.name, parsed = true, problems = Array.Empty<string>() };

            // Resolve everything the single setting needs before anything changes.
            TouchControl tuned = null;
            string entry = null;
            if (hasSetting)
            {
                tuned = ControlOn(target, control);
                entry = SettingJson(tuned, setting, value);
            }

            if (hasJson)
            {
                TouchTuningResult result = TouchTuning.ApplyQuietly(target, tuning);
                if (!result.Parsed)
                    throw new ArgumentException(string.Join(" ", result.Problems));

                info.controlsApplied = result.ControlsApplied;
                info.problems = result.Problems;
                if (result.Problems.Length > 0)
                    Debug.LogWarning($"touch_tune on '{target.name}': {result}", target);
            }

            if (hasSetting)
            {
                TouchTuningResult result = TouchTuning.ApplyQuietly(target, entry);
                if (!result.Clean)
                    throw new ArgumentException(string.Join(" ", result.Problems));

                info.controlsApplied += result.ControlsApplied;
                info.control = Describe(tuned, target);
            }

            MarkDirty(target);
            info.clean = info.problems.Length == 0;
            return info;
        }

        /// <summary>
        /// <c>touch_save</c>: saves tuning and, with the Input System, binding overrides and mappings. Returns each
        /// file's text, so values tuned on a phone can be applied in the Editor with <c>touch_tune --tuning</c>.
        /// </summary>
        /// <remarks>
        /// In the Editor during Play, saved tuning is also kept in the scene when Play stops. Rebinds are saved for each
        /// action asset with overrides, mappings or an earlier save, so a reset or unmap is kept too.
        /// </remarks>
        /// <param name="rig">Only this rig's tuning. <c>null</c> or empty: every rig in the loaded scenes.</param>
        /// <param name="rebinds">Also save binding overrides. Default <c>true</c>.</param>
        /// <exception cref="ArgumentException">No such rig, or no rig at all.</exception>
        /// <exception cref="InvalidOperationException">A file could not be written (the warning in the log says why).</exception>
        [Preserve]
        [CliCommand("touch_save", "Save Touch Rig tuning (and binding overrides) to persistent data; returns the JSON written.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchSaveInfo Save(
            [CliArg("rig", "Rig GameObject name. Omit to save every rig.")] string rig = null,
            [CliArg("rebinds", "Also save binding overrides (Input System). Default true.")] bool rebinds = true)
        {
            List<TouchRig> rigs = string.IsNullOrEmpty(rig) ? AllRigs() : new List<TouchRig> { RigNamed(rig) };
            if (rigs.Count == 0)
                throw new ArgumentException("No Touch Rig in the loaded scenes; nothing to save.");

            var tuning = new List<TouchSavedFile>();
            foreach (TouchRig each in rigs)
            {
                string path = TouchTuning.DefaultPath(each);
                if (!TouchTuning.Save(each, path))
                    throw new InvalidOperationException($"Could not save the tuning of '{each.name}' to '{path}'.");
                tuning.Add(new TouchSavedFile { name = each.name, path = path, json = TouchTuning.Export(each) });
            }

            var saved = new List<TouchSavedFile>();
#if BB_TOUCH_HAS_INPUTSYSTEM
            if (rebinds)
            {
                foreach (InputActionAsset asset in ActionAssets())
                {
                    string path = TouchRebinding.DefaultPath(asset);
                    string json = TouchRebinding.Export(asset);
                    if (json.Length == 0 && !System.IO.File.Exists(path))
                        continue;
                    if (!TouchRebinding.Save(asset, path))
                        throw new InvalidOperationException($"Could not save the binding overrides of '{asset.name}' to '{path}'.");
                    saved.Add(new TouchSavedFile { name = asset.name, path = path, json = json });
                }
            }
#endif

            return new TouchSaveInfo { tuning = tuning.ToArray(), rebinds = saved.ToArray() };
        }

#if BB_TOUCH_HAS_INPUTSYSTEM

        /// <summary>
        /// <c>touch_rebind</c>: points an action's touch binding at another control, or removes its touch overrides.
        /// Applies to every live copy of the action.
        /// </summary>
        /// <param name="action">The action, <c>Player/Jump</c>, or <c>Jump</c> when only one map has it;
        /// <c>TouchTest:Player/Jump</c> when two assets have it.</param>
        /// <param name="control">The id of the control that should drive it, <c>Pet</c>. Not needed with <paramref name="reset"/>.</param>
        /// <param name="from">For an action with several touch bindings: the control id to move away from.</param>
        /// <param name="reset">Remove the action's touch overrides instead, so it reads the controls its asset names.</param>
        /// <exception cref="ArgumentException">No such action, an ambiguous name, a missing control id, or the action has no
        /// touch binding to change.</exception>
        [Preserve]
        [CliCommand("touch_rebind", "Rebind an action's <TouchRig> binding to another control id (an Input System override), or reset it.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchRebindInfo Rebind(
            [CliArg("action", "Action: Map/Action, or Action when only one map has it; Asset:Map/Action when two assets have it.")] string action = null,
            [CliArg("control", "Control id that should drive it, e.g. Pet.")] string control = null,
            [CliArg("from", "With several touch bindings: the control id to move away from.")] string from = null,
            [CliArg("reset", "Remove the action's touch overrides instead.")] bool reset = false)
        {
            if (string.IsNullOrEmpty(action))
                throw new ArgumentException("Which action? Give --action, e.g. Player/Jump.");
            if (!reset && string.IsNullOrEmpty(control))
                throw new ArgumentException($"Give --control (the control id that should drive '{action}'), or --reset true.");

            List<InputAction> matches = FindActions(action);
            var problems = new List<string>();
            int changed = 0;

            foreach (InputAction each in matches)
            {
                TouchRebindResult result = reset ? TouchRebinding.Reset(each)
                    : string.IsNullOrEmpty(from) ? TouchRebinding.Rebind(each, control)
                    : TouchRebinding.Rebind(each, from, control);

                if (!result.Succeeded)
                    throw new ArgumentException(string.Join(" ", result.Problems));

                changed += result.BindingsChanged;
                foreach (string problem in result.Problems)
                {
                    if (!problems.Contains(problem))
                        problems.Add(problem);
                }
            }

            return Report(matches, changed, problems);
        }

        /// <summary>
        /// <c>touch_map</c>: makes a control drive an action, in every live copy, by adding a touch binding in memory.
        /// <c>touch_save</c> keeps it.
        /// </summary>
        /// <remarks>
        /// The asset file is not changed. Use the Touch Rig's Mappings card to make a mapping permanent.
        /// </remarks>
        /// <param name="action">The action, <c>Player/Interact</c>, or <c>Interact</c> when only one map has it;
        /// <c>TouchTest:Player/Interact</c> when two assets have it.</param>
        /// <param name="control">The id of the control that should drive it, <c>Jump</c>.</param>
        /// <exception cref="ArgumentException">No such action, an ambiguous name, a missing or invalid control id, or a
        /// control whose value the action does not take (a joystick on a Button action).</exception>
        [Preserve]
        [CliCommand("touch_map", "Map a touch control to an action at runtime: adds a <TouchRig> binding in memory; touch_save keeps it.",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchRebindInfo Map(
            [CliArg("action", "Action: Map/Action, or Action when only one map has it; Asset:Map/Action when two assets have it.")] string action = null,
            [CliArg("control", "Control id that should drive it, e.g. Jump.")] string control = null)
        {
            RequireActionAndControl(action, control, "map");
            return Apply(FindActions(action), each => TouchRebinding.Map(each, control), true);
        }

        /// <summary>
        /// <c>touch_unmap</c>: removes a mapping that <c>touch_map</c> added. Bindings in the asset itself are never removed.
        /// </summary>
        /// <param name="action">The action, <c>Player/Interact</c>, or <c>Interact</c> when only one map has it;
        /// <c>TouchTest:Player/Interact</c> when two assets have it.</param>
        /// <param name="control">The id of the control whose runtime mapping to remove, <c>Jump</c>.</param>
        /// <exception cref="ArgumentException">No such action, an ambiguous name, a missing control id, or no runtime
        /// mapping from that control on the action.</exception>
        [Preserve]
        [CliCommand("touch_unmap", "Remove a mapping touch_map added (never a binding the asset has).",
            MainThreadRequired = true, Tags = new[] { k_Tag })]
        public static TouchRebindInfo Unmap(
            [CliArg("action", "Action: Map/Action, or Action when only one map has it; Asset:Map/Action when two assets have it.")] string action = null,
            [CliArg("control", "Control id whose runtime mapping to remove, e.g. Jump.")] string control = null)
        {
            RequireActionAndControl(action, control, "unmap");
            return Apply(FindActions(action), each => TouchRebinding.Unmap(each, control), false);
        }

        static void RequireActionAndControl(string action, string control, string verb)
        {
            if (string.IsNullOrEmpty(action))
                throw new ArgumentException("Which action? Give --action, e.g. Player/Interact.");
            if (string.IsNullOrEmpty(control))
                throw new ArgumentException($"Give --control (the control id to {verb} '{action}' {(verb == "map" ? "to" : "from")}).");
        }

        /// <summary>Runs <paramref name="change"/> on every copy; the first failure throws, before later copies change.</summary>
        static TouchRebindInfo Apply(List<InputAction> matches, Func<InputAction, TouchRebindResult> change, bool noteEditMode)
        {
            var problems = new List<string>();
            int changed = 0;
            foreach (InputAction each in matches)
            {
                TouchRebindResult result = change(each);
                if (!result.Succeeded)
                    throw new ArgumentException(string.Join(" ", result.Problems));

                changed += result.BindingsChanged;
                foreach (string problem in result.Problems)
                {
                    if (!problems.Contains(problem))
                        problems.Add(problem);
                }
            }

            if (noteEditMode && changed > 0 && !Application.isPlaying)
                problems.Add("Not in Play: the binding is on the asset's in-memory copy only and is dropped when the asset is re-imported; use the Touch Rig's Mappings card to keep it in the asset.");

            return Report(matches, changed, problems);
        }

        static TouchRebindInfo Report(List<InputAction> matches, int changed, List<string> problems)
        {
            InputAction first = matches[0];
            var paths = new string[first.bindings.Count];
            for (int i = 0; i < paths.Length; i++)
                paths[i] = first.bindings[i].effectivePath;

            return new TouchRebindInfo
            {
                action = FullName(first),
                actions = matches.Count,
                bindingsChanged = changed,
                problems = problems.ToArray(),
                bindings = paths,
                mappedAtRuntime = TouchRebinding.RuntimeMappings(first),
            };
        }

        /// <summary>Actions (<c>Map/Action</c>) that read <paramref name="controlName"/> through a runtime mapping.</summary>
        static string[] RuntimeMappedActions(string controlName)
        {
            var actions = new List<InputAction>();
            var paths = new List<string>();
            TouchRebinding.CollectRuntimeMappings(actions, paths);

            var names = new List<string>();
            string path = TouchRigLayout.PathPrefix + controlName;
            for (int i = 0; i < actions.Count; i++)
            {
                string full = FullName(actions[i]);
                if (string.Equals(paths[i], path, StringComparison.OrdinalIgnoreCase) && !names.Contains(full))
                    names.Add(full);
            }

            return names.ToArray();
        }

        /// <summary>
        /// Every live copy of the named action: enabled actions first, then project-wide actions and the assets of
        /// <c>PlayerInput</c> components and bridges in the loaded scenes.
        /// </summary>
        /// <remarks>
        /// Copies share an action id, such as an asset and a <c>PlayerInput</c>'s clone. Two assets that both have
        /// <c>Player/Jump</c> hold two different actions, so the name needs the asset: <c>TouchTest:Player/Jump</c>.
        /// </remarks>
        internal static List<InputAction> FindActions(string name)
        {
            string assetName = null, actionName = name;
            int colon = name.IndexOf(':');
            if (colon >= 0)
            {
                assetName = name.Substring(0, colon);
                actionName = name.Substring(colon + 1);
            }

            bool qualified = actionName.IndexOf('/') >= 0;
            var found = new List<InputAction>();
            var fullNames = new List<string>();
            var ids = new List<Guid>();

            void Consider(InputAction candidate)
            {
                if (candidate == null || found.Contains(candidate))
                    return;

                string full = FullName(candidate);
                bool match = qualified
                    ? string.Equals(full, actionName, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(candidate.name, actionName, StringComparison.OrdinalIgnoreCase);
                if (match && assetName != null)
                    match = string.Equals(AssetName(candidate), assetName, StringComparison.OrdinalIgnoreCase);
                if (!match)
                    return;

                found.Add(candidate);
                if (!fullNames.Exists(n => string.Equals(n, full, StringComparison.OrdinalIgnoreCase)))
                    fullNames.Add(full);
                if (!ids.Contains(candidate.id))
                    ids.Add(candidate.id);
            }

            foreach (InputAction enabled in InputSystem.ListEnabledActions())
                Consider(enabled);

            foreach (InputActionAsset asset in ActionAssets())
            {
                foreach (InputActionMap map in asset.actionMaps)
                {
                    foreach (InputAction candidate in map.actions)
                        Consider(candidate);
                }
            }

            if (found.Count == 0)
                throw new ArgumentException(
                    $"No action '{name}' in the enabled actions, project-wide actions, or the PlayerInput and bridge assets of the loaded scenes.");
            if (fullNames.Count > 1)
                throw new ArgumentException(
                    $"'{name}' names {fullNames.Count} actions ({string.Join(", ", fullNames)}); give it as Map/Action.");
            if (ids.Count > 1)
            {
                var assets = new List<string>();
                foreach (InputAction each in found)
                {
                    string label = AssetName(each) + ":" + FullName(each);
                    if (!assets.Contains(label))
                        assets.Add(label);
                }

                throw new ArgumentException(
                    $"'{name}' names actions in {assets.Count} assets ({string.Join(", ", assets)}); give it as Asset:Map/Action.");
            }

            return found;
        }

        /// <summary>The action's asset name without a clone's <c>(Clone)</c>; empty for an action with no asset.</summary>
        static string AssetName(InputAction action)
        {
            InputActionAsset asset = action.actionMap != null ? action.actionMap.asset : null;
            if (asset == null)
                return "";
            string n = asset.name;
            return n.EndsWith("(Clone)", StringComparison.Ordinal) ? n.Substring(0, n.Length - "(Clone)".Length).TrimEnd() : n;
        }

        /// <summary>Action assets in use: those of enabled actions, project-wide actions, and the PlayerInput and bridge assets of the loaded scenes.</summary>
        internal static List<InputActionAsset> ActionAssets()
        {
            var assets = new List<InputActionAsset>();

            void Add(InputActionAsset asset)
            {
                if (asset != null && !assets.Contains(asset))
                    assets.Add(asset);
            }

            foreach (InputAction enabled in InputSystem.ListEnabledActions())
                Add(enabled.actionMap != null ? enabled.actionMap.asset : null);

            Add(InputSystem.actions);

            var mapped = new List<InputAction>();
            TouchRebinding.CollectRuntimeMappings(mapped, new List<string>());
            foreach (InputAction each in mapped)
                Add(each.actionMap != null ? each.actionMap.asset : null);

            foreach (GameObject root in LoadedRoots())
            {
                foreach (PlayerInput player in root.GetComponentsInChildren<PlayerInput>(true))
                    Add(player.actions);
                foreach (TouchInputBridge bridge in root.GetComponentsInChildren<TouchInputBridge>(true))
                    Add(bridge.Actions);
            }

            return assets;
        }

        static string FullName(InputAction action) =>
            action.actionMap != null ? action.actionMap.name + "/" + action.name : action.name;
#endif

        /// <summary>Every <see cref="TouchRig"/> in the loaded scenes, active or not, in scene and hierarchy order.</summary>
        internal static List<TouchRig> AllRigs()
        {
            var rigs = new List<TouchRig>();
            foreach (GameObject root in LoadedRoots())
                rigs.AddRange(root.GetComponentsInChildren<TouchRig>(true));
            return rigs;
        }

        static List<GameObject> LoadedRoots()
        {
            var roots = new List<GameObject>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded)
                    roots.AddRange(scene.GetRootGameObjects());
            }

            return roots;
        }

        static TouchRig RigNamed(string name)
        {
            List<TouchRig> all = AllRigs();
            var named = all.FindAll(r => string.Equals(r.name, name, StringComparison.Ordinal));
            if (named.Count == 0)
                named = all.FindAll(r => string.Equals(r.name, name, StringComparison.OrdinalIgnoreCase));

            if (named.Count == 1)
                return named[0];
            if (named.Count == 0)
                throw new ArgumentException($"No Touch Rig named '{name}' in the loaded scenes. {RigList(all)}");
            throw new ArgumentException($"{named.Count} Touch Rigs are named '{name}'; rename one so --rig can tell them apart.");
        }

        /// <summary>
        /// The rig a command acts on: the one named, else the only rig with <paramref name="controlId"/>, else the only rig.
        /// </summary>
        static TouchRig ResolveRig(string rigName, string controlId)
        {
            if (!string.IsNullOrEmpty(rigName))
                return RigNamed(rigName);

            List<TouchRig> all = AllRigs();
            if (all.Count == 0)
                throw new ArgumentException("No Touch Rig in the loaded scenes.");

            if (string.IsNullOrEmpty(controlId))
            {
                if (all.Count == 1)
                    return all[0];
                throw new ArgumentException($"{all.Count} Touch Rigs are loaded; give --rig. {RigList(all)}");
            }

            var having = all.FindAll(r => TryControlOn(r, controlId, out _) != null);
            if (having.Count == 1)
                return having[0];
            if (having.Count == 0)
                throw new ArgumentException($"No control '{controlId}' on any Touch Rig. {ControlList(all)}");
            throw new ArgumentException($"{having.Count} Touch Rigs have a control '{controlId}'; give --rig. {RigList(having)}");
        }

        static TouchControl ControlOn(TouchRig rig, string id)
        {
            TouchControl control = TryControlOn(rig, id, out string ambiguity);
            if (control != null)
                return control;
            if (ambiguity != null)
                throw new ArgumentException(ambiguity);
            throw new ArgumentException($"No control '{id}' on rig '{rig.name}'. {ControlList(new List<TouchRig> { rig })}");
        }

        /// <summary>The control with this id under the rig (enabled or not): exact first, then ignoring case if only one matches.</summary>
        static TouchControl TryControlOn(TouchRig rig, string id, out string ambiguity)
        {
            ambiguity = null;
            List<TouchControl> controls = TouchTuning.ControlsOf(rig);

            foreach (TouchControl control in controls)
            {
                if (string.Equals(control.ActionId, id, StringComparison.Ordinal))
                    return control;
            }

            TouchControl match = null;
            foreach (TouchControl control in controls)
            {
                if (!string.Equals(control.ActionId, id, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (match != null && !string.Equals(match.ActionId, control.ActionId, StringComparison.Ordinal))
                {
                    ambiguity = $"'{id}' matches '{match.ActionId}' and '{control.ActionId}' on rig '{rig.name}'; use the exact id.";
                    return null;
                }

                match ??= control;
            }

            return match;
        }

        /// <summary>One-entry tuning JSON for a single setting, or <see cref="ArgumentException"/> naming what is wrong.</summary>
        static string SettingJson(TouchControl control, string setting, string value)
        {
            string canonical = Array.Find(k_Settings, s => string.Equals(s, setting, StringComparison.OrdinalIgnoreCase));
            string type = TouchTuning.TypeName(control);

            if (!(control is TouchJoystick))
                throw new ArgumentException($"'{control.ActionId}' is a {type}; it has no Feel settings to tune yet.");
            if (canonical == null)
                throw new ArgumentException($"A Joystick has no setting '{setting}'. Settings: {string.Join(", ", k_Settings)}.");

            string literal;
            switch (canonical)
            {
                case "deadZone":
                case "saturation":
                    if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float number) ||
                        float.IsNaN(number) || float.IsInfinity(number))
                        throw new ArgumentException($"{canonical} must be a number between 0 and 1, like 0.4; got '{value}'.");
                    literal = TuningJson.Number(number);
                    break;

                case "recenterOnRelease":
                    if (!bool.TryParse(value, out bool flag))
                        throw new ArgumentException($"recenterOnRelease must be true or false; got '{value}'.");
                    literal = flag ? "true" : "false";
                    break;

                default: // originMode
                    if (!Enum.TryParse(value, true, out JoystickOriginMode mode) || !Enum.IsDefined(typeof(JoystickOriginMode), mode) ||
                        int.TryParse(value, out _))
                        throw new ArgumentException($"originMode must be Fixed, Floating or FloatingWithLeash; got '{value}'.");
                    literal = TuningJson.Quote(mode.ToString());
                    break;
            }

            return "{ \"version\": " + TouchTuning.CurrentVersion + ", \"controls\": [ { \"id\": " +
                   TuningJson.Quote(control.ActionId) + ", \"" + canonical + "\": " + literal + " } ] }";
        }

        static TouchRigInfo Describe(TouchRig rig)
        {
            List<TouchControl> controls = TouchTuning.ControlsOf(rig);
            var infos = new TouchControlInfo[controls.Count];
            for (int i = 0; i < controls.Count; i++)
                infos[i] = Describe(controls[i], rig);

            return new TouchRigInfo
            {
                name = rig.name,
                scene = rig.gameObject.scene.name,
                path = HierarchyPath(rig.transform),
#if BB_TOUCH_HAS_INPUTSYSTEM
                bridge = rig.GetComponent<TouchInputBridge>() != null,
#endif
                controls = infos,
            };
        }

        static TouchControlInfo Describe(TouchControl control, TouchRig rig)
        {
            var info = new TouchControlInfo
            {
                id = control.ActionId,
                type = TouchTuning.TypeName(control),
                gameObject = control.name,
                enabled = control.isActiveAndEnabled,
                registered = !string.IsNullOrEmpty(control.ActionId) && rig.Find(control.ActionId) == control,
                pressed = control.IsPressed,
            };

#if BB_TOUCH_HAS_INPUTSYSTEM
            string name = TouchRigLayout.ControlName(control.ActionId);
            info.binding = name.Length > 0 ? TouchRigLayout.PathPrefix + name : null;
            info.mappedAtRuntime = name.Length > 0 ? RuntimeMappedActions(name) : Array.Empty<string>();
#endif

            if (control is TouchJoystick stick)
            {
                Vector2 v = stick.Value;
                info.value = new[] { v.x, v.y };
                info.tuning = new TouchJoystickTuningInfo
                {
                    originMode = stick.OriginMode.ToString(),
                    deadZone = stick.DeadZone,
                    saturation = stick.Saturation,
                    recenterOnRelease = stick.RecenterOnRelease,
                };
            }
            else if (control is TouchButton button)
            {
                info.holdSeconds = button.HoldSeconds;
            }

            return info;
        }

        static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder();
            for (; t != null; t = t.parent)
                sb.Insert(0, "/" + t.name);
            return sb.ToString();
        }

        static string RigList(List<TouchRig> rigs)
        {
            if (rigs.Count == 0)
                return "There is none.";
            var names = new List<string>();
            foreach (TouchRig rig in rigs)
                names.Add($"'{rig.name}' ({rig.gameObject.scene.name})");
            return "Rigs: " + string.Join(", ", names) + ".";
        }

        static string ControlList(List<TouchRig> rigs)
        {
            var ids = new List<string>();
            foreach (TouchRig rig in rigs)
            {
                foreach (TouchControl control in TouchTuning.ControlsOf(rig))
                {
                    if (!string.IsNullOrEmpty(control.ActionId) && !ids.Contains(control.ActionId))
                        ids.Add(control.ActionId);
                }
            }

            return ids.Count == 0 ? "The rig has no controls." : "Controls: " + string.Join(", ", ids) + ".";
        }

        /// <summary>
        /// In the Editor outside Play, marks the tuned controls and their scene dirty so saving keeps the values.
        /// </summary>
        /// <remarks>
        /// Not recorded for Undo. A stray undo record pointing at a replaced scene is the suspected cause of a
        /// test-runner crash.
        /// </remarks>
        static void MarkDirty(TouchRig rig)
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                return;
            foreach (TouchControl control in TouchTuning.ControlsOf(rig))
                UnityEditor.EditorUtility.SetDirty(control);
            if (rig.gameObject.scene.IsValid())
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
#endif
        }
    }
}
