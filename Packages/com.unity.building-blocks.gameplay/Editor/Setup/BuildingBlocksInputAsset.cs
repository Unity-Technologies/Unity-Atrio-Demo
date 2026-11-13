using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// The project's editable copy of the package's input actions asset: creates it, points a character's
    /// <see cref="PlayerInputSource"/> and <see cref="FirstPersonCameraRig"/> at it, and restores missing
    /// actions from the package template.
    /// </summary>
    /// <remarks>
    /// The copy lives at <see cref="PlaygroundPaths.ProjectInputActions"/>. Only that path and files
    /// tagged with <see cref="CopyLabel"/> are ever written; an asset already at that path is treated
    /// as the copy. Actions are matched by name on
    /// the first map, so renaming one breaks the character until it is re-pointed; the setup check reports it.
    /// </remarks>
    public static class BuildingBlocksInputAsset
    {
        /// <summary>The asset label stamped on a copy this package made, so repairs never touch a file the user made.</summary>
        public const string CopyLabel = "BuildingBlocksInputCopy";

        /// <summary>The action names the input source binds, in field order.</summary>
        static readonly (string field, string action)[] k_SourceActions =
        {
            ("m_Move", "Move"), ("m_Look", "Look"), ("m_Jump", "Jump"), ("m_Sprint", "Sprint"), ("m_Crouch", "Crouch"),
        };

        /// <summary>The action names the camera rig binds, in field order.</summary>
        static readonly (string field, string action)[] k_RigActions =
        {
            ("m_Look", "Look"), ("m_Unlock", "Unlock Cursor"),
        };

        const string k_EditorWindowTypeName = "UnityEngine.InputSystem.Editor.InputActionsEditorWindow";
        const string k_LegacyEditorWindowTypeName = "UnityEngine.InputSystem.Editor.InputActionEditorWindow";

        /// <summary>The template asset the copy is made from: the package's own.</summary>
        public static string TemplatePath => PlaygroundPaths.InputActions;

        /// <summary>
        /// Whether the asset at <paramref name="assetPath"/> is in a read-only package (registry, git,
        /// tarball, built-in). Embedded, local and <c>Assets/</c> paths answer <c>false</c>.
        /// </summary>
        public static bool IsReadOnlyPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Packages/", StringComparison.Ordinal))
                return false;

            UnityEditor.PackageManager.PackageInfo info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (info == null)
                return true;

            return info.source != UnityEditor.PackageManager.PackageSource.Embedded && info.source != UnityEditor.PackageManager.PackageSource.Local;
        }

        /// <summary>
        /// Whether <paramref name="asset"/> lives under <c>Packages/</c>, editable or not (see
        /// <see cref="IsReadOnlyPath"/>).
        /// </summary>
        public static bool IsPackageAsset(UnityEngine.Object asset)
        {
            if (asset == null) return false;
            string path = AssetDatabase.GetAssetPath(asset);
            return !string.IsNullOrEmpty(path) && path.StartsWith("Packages/", StringComparison.Ordinal);
        }

        /// <summary>Whether <paramref name="asset"/> lives in a package that cannot be edited (<see cref="IsReadOnlyPath"/>).</summary>
        public static bool IsReadOnlyAsset(UnityEngine.Object asset)
        {
            return asset != null && IsReadOnlyPath(AssetDatabase.GetAssetPath(asset));
        }

        /// <summary>
        /// Whether any action the character reads is a reference into a package asset (<see cref="IsPackageAsset"/>).
        /// </summary>
        public static bool UsesPackageAsset(GameObject character)
        {
            foreach (InputActionAsset asset in ReferencedAssets(character))
            {
                if (IsPackageAsset(asset)) return true;
            }

            return false;
        }

        /// <summary>Whether any action the character reads is a reference into a package that cannot be edited.</summary>
        public static bool UsesReadOnlyAsset(GameObject character)
        {
            foreach (InputActionAsset asset in ReferencedAssets(character))
            {
                if (IsReadOnlyAsset(asset)) return true;
            }

            return false;
        }

        /// <summary>
        /// The project asset the character's actions point at, or <c>null</c> when they point elsewhere
        /// (inline, a package asset).
        /// </summary>
        public static InputActionAsset ProjectAssetOf(GameObject character)
        {
            foreach (InputActionAsset asset in ReferencedAssets(character))
            {
                if (!IsPackageAsset(asset)) return asset;
            }

            return null;
        }

        /// <summary>
        /// Loads the project copy at <paramref name="path"/> (default <see cref="PlaygroundPaths.ProjectInputActions"/>),
        /// creating and labelling it from the template if needed; <paramref name="created"/> says which.
        /// <c>null</c> when the template is missing or the copy failed. An existing copy is returned as is.
        /// </summary>
        public static InputActionAsset EnsureProjectCopy(out bool created, string path = null)
        {
            created = false;
            path = path ?? PlaygroundPaths.ProjectInputActions;

            var existing = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (existing != null)
                return existing;

            if (AssetDatabase.LoadAssetAtPath<InputActionAsset>(TemplatePath) == null)
            {
                Debug.LogError($"[Building Blocks] The input template is missing at {TemplatePath}. Reinstall the package.");
                return null;
            }

            EnsureFolder(Path.GetDirectoryName(path).Replace('\\', '/'));

            if (!AssetDatabase.CopyAsset(TemplatePath, path))
            {
                Debug.LogError($"[Building Blocks] Could not copy {TemplatePath} to {path}.");
                return null;
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            created = true;
            var copy = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
            if (copy != null)
                AssetDatabase.SetLabels(copy, new[] { CopyLabel });
            return copy;
        }

        /// <summary>
        /// Points each action on <paramref name="character"/> (and custom rows pointing into a package asset)
        /// at the same-named action in <paramref name="asset"/>. One Undo step. Returns how many fields changed.
        /// </summary>
        /// <param name="missing">
        /// When given, receives each needed action name <paramref name="asset"/> lacks; those fields are left
        /// untouched. See <see cref="DescribeMissing"/>.
        /// </param>
        public static int Bind(GameObject character, InputActionAsset asset, List<string> missing = null)
        {
            if (character == null || asset == null) return 0;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName($"Point {character.name} at {asset.name}");

            int changed = 0;
            var source = character.GetComponent<PlayerInputSource>();
            if (source != null)
            {
                changed += BindFields(source, asset, k_SourceActions, missing);
                changed += BindCustomButtons(source, asset);
            }

            var rig = character.GetComponentInChildren<FirstPersonCameraRig>(true);
            if (rig != null)
                changed += BindFields(rig, asset, k_RigActions, missing);

            Undo.CollapseUndoOperations(group);
            return changed;
        }

        /// <summary>
        /// The sentence for a <see cref="Bind(GameObject, InputActionAsset, List{string})"/> that found actions
        /// missing from <paramref name="asset"/>, or <c>null</c> when <paramref name="missing"/> is empty.
        /// </summary>
        public static string DescribeMissing(InputActionAsset asset, List<string> missing)
        {
            if (asset == null || missing == null || missing.Count == 0) return null;

            string names = string.Join(", ", missing);
            string plural = missing.Count == 1 ? string.Empty : "s";
            return $"{asset.name} has no '{names}' action{plural}, so {names} will not fire on this character. " +
                   $"Use Restore in the Character inspector's setup checks to put {(missing.Count == 1 ? "it" : "them")} " +
                   "back from the package template.";
        }

        /// <summary>
        /// Restores <paramref name="actionName"/> and its bindings to <paramref name="projectCopy"/> from the
        /// template. Returns <c>false</c> when the template lacks it, the copy was not created by this package,
        /// or it has unsaved changes in the Input Actions window (logged). Writes the file; not undoable.
        /// </summary>
        public static bool RestoreAction(InputActionAsset projectCopy, string actionName)
        {
            bool done = RestoreAction(projectCopy, actionName, out string problem);
            if (problem != null) Debug.LogWarning($"[Building Blocks] {problem}", projectCopy);
            return done;
        }

        /// <summary>
        /// <see cref="RestoreAction(InputActionAsset, string)"/>, with any actionable failure reason in
        /// <paramref name="problem"/>.
        /// </summary>
        public static bool RestoreAction(InputActionAsset projectCopy, string actionName, out string problem)
        {
            problem = null;
            if (!CanEdit(projectCopy, out string path)) return false;

            var template = AssetDatabase.LoadAssetAtPath<InputActionAsset>(TemplatePath);
            InputAction wanted = template != null ? template.FindAction(actionName, false) : null;
            if (wanted == null) return false;

            return Edit(path, out problem, working =>
            {
                InputActionMap map = MapFor(working, wanted.actionMap != null ? wanted.actionMap.name : null);
                if (map.FindAction(actionName, false) != null)
                    return;

                InputAction added = map.AddAction(actionName, wanted.type);
                added.expectedControlType = wanted.expectedControlType;
                foreach (InputBinding binding in wanted.bindings)
                {
                    InputBinding copy = binding;
                    copy.action = actionName;
                    copy.id = Guid.NewGuid();
                    map.AddBinding(copy);
                }
            });
        }

        /// <summary>
        /// Adds an unbound Button action called <paramref name="name"/> to <paramref name="projectCopy"/>'s first
        /// map. Returns <c>false</c> when it exists, the copy was not created by this package, or it has unsaved
        /// changes in the Input Actions window (logged). Writes the file; not undoable.
        /// </summary>
        public static bool AddButtonAction(InputActionAsset projectCopy, string name)
        {
            bool done = AddButtonAction(projectCopy, name, out string problem);
            if (problem != null) Debug.LogWarning($"[Building Blocks] {problem}", projectCopy);
            return done;
        }

        /// <summary>
        /// <see cref="AddButtonAction(InputActionAsset, string)"/>, with any actionable failure reason in
        /// <paramref name="problem"/>.
        /// </summary>
        public static bool AddButtonAction(InputActionAsset projectCopy, string name, out string problem)
        {
            problem = null;
            if (!CanEdit(projectCopy, out string path) || string.IsNullOrWhiteSpace(name)) return false;
            if (projectCopy.FindAction(name, false) != null) return false;

            return Edit(path, out problem, working =>
            {
                InputActionMap map = MapFor(working, null);
                if (map.FindAction(name, false) == null)
                    map.AddAction(name.Trim(), InputActionType.Button);
            });
        }

        /// <summary>
        /// Whether <see cref="RestoreAction(InputActionAsset, string)"/> would do anything.
        /// </summary>
        public static bool CanRestore(InputActionAsset projectCopy, string actionName)
        {
            if (!CanEdit(projectCopy, out _) || projectCopy.FindAction(actionName, false) != null) return false;
            var template = AssetDatabase.LoadAssetAtPath<InputActionAsset>(TemplatePath);
            return template != null && template.FindAction(actionName, false) != null;
        }

        /// <summary>The importer's reference sub-asset for the action called <paramref name="actionName"/>, or <c>null</c>.</summary>
        public static InputActionReference FindReference(InputActionAsset asset, string actionName)
        {
            if (asset == null) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) return null;

            foreach (UnityEngine.Object sub in AssetDatabase.LoadAllAssetRepresentationsAtPath(path))
            {
                if (sub is InputActionReference reference && reference.action != null &&
                    string.Equals(reference.action.name, actionName, StringComparison.OrdinalIgnoreCase))
                    return reference;
            }

            return null;
        }

        /// <summary>
        /// Assigns bits to the rows on <paramref name="source"/> after an editor edit and marks it dirty. In Play
        /// mode also re-enables the actions so the change is live.
        /// </summary>
        public static void RefreshRows(PlayerInputSource source)
        {
            if (source == null) return;

            if (Application.isPlaying)
                source.RefreshCustomButtons();
            else
                source.AssignButtonBits();

            EditorUtility.SetDirty(source);
        }

        static IEnumerable<InputActionAsset> ReferencedAssets(GameObject character)
        {
            if (character == null) yield break;

            var source = character.GetComponent<PlayerInputSource>();
            if (source != null)
            {
                using (var so = new SerializedObject(source))
                {
                    foreach ((string field, string _) in k_SourceActions)
                    {
                        InputActionAsset asset = ReferencedAsset(so, field);
                        if (asset != null) yield return asset;
                    }
                }

                foreach (CommandButton row in source.CustomButtons)
                {
                    InputActionAsset asset = row != null && row.ActionProperty.reference != null ? row.ActionProperty.reference.asset : null;
                    if (asset != null) yield return asset;
                }
            }

            var rig = character.GetComponentInChildren<FirstPersonCameraRig>(true);
            if (rig != null)
            {
                using (var so = new SerializedObject(rig))
                {
                    foreach ((string field, string _) in k_RigActions)
                    {
                        InputActionAsset asset = ReferencedAsset(so, field);
                        if (asset != null) yield return asset;
                    }
                }
            }
        }

        static InputActionAsset ReferencedAsset(SerializedObject so, string field)
        {
            SerializedProperty use = so.FindProperty(field + ".m_UseReference");
            SerializedProperty reference = so.FindProperty(field + ".m_Reference");
            if (use == null || reference == null || !use.boolValue) return null;
            return reference.objectReferenceValue is InputActionReference r ? r.asset : null;
        }

        static int BindFields(Component component, InputActionAsset asset, (string field, string action)[] fields, List<string> missing)
        {
            int changed = 0;
            using (var so = new SerializedObject(component))
            {
                foreach ((string field, string action) in fields)
                {
                    InputActionReference reference = FindReference(asset, action);
                    if (reference == null)
                    {
                        if (missing != null && !missing.Contains(action)) missing.Add(action);
                        continue;
                    }

                    SerializedProperty use = so.FindProperty(field + ".m_UseReference");
                    SerializedProperty target = so.FindProperty(field + ".m_Reference");
                    if (use == null || target == null) continue;
                    if (use.boolValue && target.objectReferenceValue == reference) continue;

                    use.boolValue = true;
                    target.objectReferenceValue = reference;
                    changed++;
                }

                if (changed > 0)
                {
                    so.ApplyModifiedProperties();
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
            }

            return changed;
        }

        /// <summary>Whether a row points into a package asset, so it should move to the copy.</summary>
        static int BindCustomButtons(PlayerInputSource source, InputActionAsset asset)
        {
            int changed = 0;
            List<CommandButton> rows = source.CustomButtons;
            for (int i = 0; i < rows.Count; i++)
            {
                CommandButton row = rows[i];
                InputActionReference current = row?.ActionProperty.reference;
                if (current == null || !IsPackageAsset(current.asset) || current.action == null) continue;

                InputActionReference replacement = FindReference(asset, current.action.name);
                if (replacement == null) continue;

                if (changed == 0) Undo.RecordObject(source, "Point custom buttons at the project input asset");
                row.ActionProperty = new InputActionProperty(replacement);
                changed++;
            }

            if (changed > 0)
            {
                RefreshRows(source);
                PrefabUtility.RecordPrefabInstancePropertyModifications(source);
            }

            return changed;
        }

        /// <summary>
        /// Whether the file was made by this package: at <see cref="PlaygroundPaths.ProjectInputActions"/>, or
        /// under <c>Assets/</c> with <see cref="CopyLabel"/> (set by <see cref="EnsureProjectCopy"/>).
        /// </summary>
        static bool CanEdit(InputActionAsset asset, out string path)
        {
            path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                return false;

            if (string.Equals(path, PlaygroundPaths.ProjectInputActions, StringComparison.Ordinal))
                return true;

            return Array.IndexOf(AssetDatabase.GetLabels(asset), CopyLabel) >= 0;
        }

        /// <summary>
        /// Loads the file at <paramref name="path"/>, applies <paramref name="edit"/>, writes it once and re-imports.
        /// Refuses when an Input Actions window has unsaved changes to it. <paramref name="problem"/> carries the
        /// refusal or I/O error; <c>null</c> on success.
        /// </summary>
        static bool Edit(string path, out string problem, Action<InputActionAsset> edit)
        {
            problem = null;
            if (HasUnsavedEditorWindow(path))
            {
                problem = $"Save or close the Input Actions window for {Path.GetFileName(path)} first, then run the fix again.";
                return false;
            }

            InputActionAsset working = null;
            try
            {
                working = InputActionAsset.FromJson(File.ReadAllText(path));
                edit(working);
                File.WriteAllText(path, working.ToJson());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                return true;
            }
            catch (Exception e)
            {
                problem = $"Could not edit {path}: {e.Message}";
                return false;
            }
            finally
            {
                if (working != null) UnityEngine.Object.DestroyImmediate(working);
            }
        }

        /// <summary>
        /// Whether an Input Actions window has unsaved changes to the asset at <paramref name="path"/>. Read by
        /// reflection; an unreadable window counts as unsaved.
        /// </summary>
        static bool HasUnsavedEditorWindow(string path)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (EditorWindow window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (window == null) continue;
                Type type = window.GetType();
                string typeName = type.FullName;
                if (typeName != k_EditorWindowTypeName && typeName != k_LegacyEditorWindowTypeName) continue;

                PropertyInfo guidProperty = type.GetProperty("assetGUID", flags);
                if (guidProperty != null && guidProperty.PropertyType == typeof(string))
                {
                    var windowGuid = guidProperty.GetValue(window) as string;
                    if (!string.IsNullOrEmpty(windowGuid) && windowGuid != guid) continue;
                }

                PropertyInfo dirtyProperty = type.GetProperty("isDirty", flags);
                if (dirtyProperty != null && dirtyProperty.PropertyType == typeof(bool))
                    return (bool)dirtyProperty.GetValue(window);

                return true;
            }

            return false;
        }

        static InputActionMap MapFor(InputActionAsset asset, string preferred)
        {
            if (!string.IsNullOrEmpty(preferred))
            {
                InputActionMap named = asset.FindActionMap(preferred, false);
                if (named != null) return named;
            }

            return asset.actionMaps.Count > 0 ? asset.actionMaps[0] : asset.AddActionMap("Player");
        }

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;

            string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
