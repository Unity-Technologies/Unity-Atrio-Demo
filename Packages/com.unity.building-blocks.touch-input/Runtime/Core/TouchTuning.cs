using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Saves and loads how a rig's controls feel, as JSON keyed by <see cref="TouchControl.ActionId"/>. Tune on a
    /// phone while the game runs, save, and get the same values next time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only feel settings are saved, such as a joystick's dead zone. Size, placement and looks are not. A
    /// <see cref="TouchButton"/> has no feel settings yet, so it is listed by id and type only.
    /// </para>
    /// <code>
    /// {
    ///   "version": 1,
    ///   "controls": [
    ///     { "id": "Move", "type": "Joystick", "originMode": "Floating", "deadZone": 0.2, "saturation": 0.9, "recenterOnRelease": true },
    ///     { "id": "Jump", "type": "Button" }
    ///   ]
    /// }
    /// </code>
    /// <para>
    /// Nothing throws. Unknown ids and wrong value types are reported in <see cref="TouchTuningResult.Problems"/>.
    /// Disabled controls are covered too. The API is safe to call from hot-reloaded code.
    /// </para>
    /// </remarks>
    public static class TouchTuning
    {
        /// <summary>The <c>version</c> this package writes. A higher version is applied as far as it is understood.</summary>
        public const int CurrentVersion = 1;

        /// <summary>Folder under <see cref="Application.persistentDataPath"/> that <see cref="DefaultPath"/> uses.</summary>
        public const string FolderName = "TouchTuning";

        const string k_Joystick = "Joystick";
        const string k_Button = "Button";

        /// <summary>
        /// Raised after <see cref="Save(TouchRig, string)"/> wrote a file, with the rig and the JSON written.
        /// In the Editor, saving during Play also keeps those values in the scene when Play stops.
        /// </summary>
        public static event Action<TouchRig, string> Saved;

        /// <summary>
        /// The rig's current tuning as indented JSON, or <c>""</c> for a <c>null</c> rig. Skips empty ids and
        /// writes only the first of a repeated id.
        /// </summary>
        public static string Export(TouchRig rig)
        {
            if (rig == null)
                return "";

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": ").Append(CurrentVersion).Append(",\n  \"controls\": [");

            bool first = true;
            foreach (TouchControl control in ControlsOf(rig))
            {
                string id = control.ActionId;
                if (string.IsNullOrEmpty(id) || !seen.Add(id))
                    continue;

                sb.Append(first ? "\n    { " : ",\n    { ");
                first = false;
                sb.Append("\"id\": ").Append(TuningJson.Quote(id));
                sb.Append(", \"type\": ").Append(TuningJson.Quote(TypeName(control)));

                if (control is TouchJoystick stick)
                {
                    sb.Append(", \"originMode\": ").Append(TuningJson.Quote(stick.OriginMode.ToString()));
                    sb.Append(", \"deadZone\": ").Append(TuningJson.Number(stick.DeadZone));
                    sb.Append(", \"saturation\": ").Append(TuningJson.Number(stick.Saturation));
                    sb.Append(", \"recenterOnRelease\": ").Append(stick.RecenterOnRelease ? "true" : "false");
                }

                sb.Append(" }");
            }

            sb.Append(first ? "]\n}\n" : "\n  ]\n}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Applies tuning JSON to the rig's controls, matched by id. Never throws; problems are in the result and
        /// logged once as a warning.
        /// </summary>
        /// <param name="rig">The rig to tune. <c>null</c> gives a result with one problem.</param>
        /// <param name="json">Text in the shape <see cref="Export"/> writes.</param>
        public static TouchTuningResult Apply(TouchRig rig, string json)
        {
            TouchTuningResult result = ApplyQuietly(rig, json);
            if (result.Problems.Length > 0)
                Debug.LogWarning($"TouchTuning on '{(rig != null ? rig.name : "null")}': {result}", rig);
            return result;
        }

        /// <summary>
        /// Where <see cref="Save(TouchRig)"/> and <see cref="Load(TouchRig)"/> keep this rig's tuning:
        /// <c>&lt;persistentDataPath&gt;/TouchTuning/&lt;scene&gt;.&lt;rig&gt;.json</c>.
        /// </summary>
        public static string DefaultPath(TouchRig rig)
        {
            string scene = rig != null ? rig.gameObject.scene.name : "";
            string name = rig != null ? rig.name : "";
            string file = SafeFileName(string.IsNullOrEmpty(scene) ? "Untitled" : scene) + "." +
                          SafeFileName(string.IsNullOrEmpty(name) ? "Rig" : name) + ".json";
            return Path.Combine(Application.persistentDataPath, FolderName, file);
        }

        /// <summary>Saves the rig's tuning to <see cref="DefaultPath"/>. See <see cref="Save(TouchRig, string)"/>.</summary>
        public static bool Save(TouchRig rig) => rig != null && Save(rig, DefaultPath(rig));

        /// <summary>
        /// Writes <see cref="Export"/> to <paramref name="path"/> and raises <see cref="Saved"/>. Never throws; returns
        /// <c>false</c> and logs a warning if it cannot write.
        /// </summary>
        public static bool Save(TouchRig rig, string path)
        {
            if (rig == null || string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("TouchTuning.Save: needs a rig and a file path.");
                return false;
            }

            string json = Export(rig);
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"TouchTuning.Save: could not write '{path}': {e.Message}", rig);
                return false;
            }

            Saved?.Invoke(rig, json);
            return true;
        }

        /// <summary>Applies the tuning saved at <see cref="DefaultPath"/>. See <see cref="Load(TouchRig, string)"/>.</summary>
        public static TouchTuningResult Load(TouchRig rig) => Load(rig, rig != null ? DefaultPath(rig) : null);

        /// <summary>
        /// Reads <paramref name="path"/> and applies it, never throwing. A missing or unreadable file leaves the rig
        /// unchanged and gives <see cref="TouchTuningResult.Parsed"/> <c>false</c>.
        /// </summary>
        public static TouchTuningResult Load(TouchRig rig, string path)
        {
            string json;
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    return new TouchTuningResult(false, 0, new List<string> { $"No saved tuning at '{path}'." });

                json = File.ReadAllText(path);
            }
            catch (Exception e)
            {
                return new TouchTuningResult(false, 0, new List<string> { $"Could not read '{path}': {e.Message}" });
            }

            return Apply(rig, json);
        }

        /// <summary><see cref="Apply"/> without the warning, for callers that report the result themselves.</summary>
        internal static TouchTuningResult ApplyQuietly(TouchRig rig, string json)
        {
            var problems = new List<string>();

            if (rig == null)
                return Fail(problems, "No rig to apply tuning to.");
            if (string.IsNullOrWhiteSpace(json))
                return Fail(problems, "The tuning text is empty.");
            if (!TuningJson.TryParse(json, out object root, out string error))
                return Fail(problems, "The tuning text is " + error + ".");
            if (!(root is TuningJson.JsonObject doc))
                return Fail(problems, "The tuning text must be a JSON object with a \"controls\" list.");

            if (doc.TryGet("version", out object versionValue))
            {
                if (!(versionValue is TuningJson.JsonNumber n) || !n.TryInt(out int version))
                    problems.Add("\"version\" must be a whole number; read as " + CurrentVersion + ".");
                else if (version > CurrentVersion)
                    problems.Add($"Written by tuning version {version}; this package reads {CurrentVersion}, so only the settings it knows were applied.");
            }

            if (!doc.TryGet("controls", out object controlsValue))
                return new TouchTuningResult(true, 0, Add(problems, "No \"controls\" list; nothing to apply."));
            if (!(controlsValue is List<object> entries))
                return new TouchTuningResult(true, 0, Add(problems, "\"controls\" must be a list."));

            List<TouchControl> controls = ControlsOf(rig);
            int applied = 0;

            for (int i = 0; i < entries.Count; i++)
            {
                if (!(entries[i] is TuningJson.JsonObject entry))
                {
                    problems.Add($"controls[{i}] is not an object; skipped.");
                    continue;
                }

                if (!entry.TryGet("id", out object idValue) || !(idValue is string id) || id.Length == 0)
                {
                    problems.Add($"controls[{i}] has no \"id\"; skipped.");
                    continue;
                }

                string type = null;
                if (entry.TryGet("type", out object typeValue))
                {
                    type = typeValue as string;
                    if (type == null)
                        problems.Add($"\"type\" of '{id}' must be text; ignored.");
                }

                bool found = false;
                foreach (TouchControl control in controls)
                {
                    if (!string.Equals(control.ActionId, id, StringComparison.Ordinal))
                        continue;

                    found = true;
                    string actual = TypeName(control);
                    if (type != null && !string.Equals(type, actual, StringComparison.Ordinal))
                    {
                        problems.Add($"'{id}' is a {actual} on this rig but the tuning says {type}; skipped.");
                        continue;
                    }

                    ApplyEntry(control, id, entry, problems);
                    applied++;
                }

                if (!found)
                    problems.Add($"No control with id '{id}' on rig '{rig.name}'; skipped.");
            }

            return new TouchTuningResult(true, applied, problems);
        }

        static void ApplyEntry(TouchControl control, string id, TuningJson.JsonObject entry, List<string> problems)
        {
            var stick = control as TouchJoystick;

            foreach (KeyValuePair<string, object> member in entry.Members)
            {
                object value = member.Value;
                switch (member.Key)
                {
                    case "id":
                    case "type":
                        continue;

                    case "originMode" when stick != null:
                        if (value is string modeName && TryParseOrigin(modeName, out JoystickOriginMode mode))
                            stick.OriginMode = mode;
                        else
                            problems.Add($"\"originMode\" of '{id}' must be Fixed, Floating or FloatingWithLeash; kept {stick.OriginMode}.");
                        continue;

                    case "deadZone" when stick != null:
                        if (value is TuningJson.JsonNumber dz && dz.TryFloat(out float deadZone))
                            stick.DeadZone = deadZone;
                        else
                            problems.Add($"\"deadZone\" of '{id}' must be a number; kept {TuningJson.Number(stick.DeadZone)}.");
                        continue;

                    case "saturation" when stick != null:
                        if (value is TuningJson.JsonNumber sat && sat.TryFloat(out float saturation))
                            stick.Saturation = saturation;
                        else
                            problems.Add($"\"saturation\" of '{id}' must be a number; kept {TuningJson.Number(stick.Saturation)}.");
                        continue;

                    case "recenterOnRelease" when stick != null:
                        if (value is bool recenter)
                            stick.RecenterOnRelease = recenter;
                        else
                            problems.Add($"\"recenterOnRelease\" of '{id}' must be true or false; kept {(stick.RecenterOnRelease ? "true" : "false")}.");
                        continue;

                    default:
                        problems.Add($"'{id}' ({TypeName(control)}) has no setting \"{member.Key}\"; ignored.");
                        continue;
                }
            }
        }

        static bool TryParseOrigin(string name, out JoystickOriginMode mode)
        {
            foreach (JoystickOriginMode candidate in (JoystickOriginMode[])Enum.GetValues(typeof(JoystickOriginMode)))
            {
                if (string.Equals(candidate.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    mode = candidate;
                    return true;
                }
            }

            mode = default;
            return false;
        }

        /// <summary>Every control whose nearest rig is <paramref name="rig"/>, enabled or not, in hierarchy order.</summary>
        internal static List<TouchControl> ControlsOf(TouchRig rig)
        {
            var result = new List<TouchControl>();
            foreach (TouchControl control in rig.GetComponentsInChildren<TouchControl>(true))
            {
                if (control.GetComponentInParent<TouchRig>(true) == rig)
                    result.Add(control);
            }

            return result;
        }

        internal static string TypeName(TouchControl control)
        {
            if (control is TouchJoystick) return k_Joystick;
            if (control is TouchButton) return k_Button;
            return control.GetType().Name;
        }

        static string SafeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            return sb.ToString();
        }

        static TouchTuningResult Fail(List<string> problems, string problem) =>
            new TouchTuningResult(false, 0, Add(problems, problem));

        static List<string> Add(List<string> problems, string problem)
        {
            problems.Add(problem);
            return problems;
        }
    }
}
