using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.Neural.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Neural.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NTC.Editor
{
    /// <summary>
    /// Keeps the tracked materials on the shader that matches NTC: the NTC side of their
    /// <see cref="NTCShaderPairs"/> pair while it is enabled, the conventional side while it is not.
    /// </summary>
    /// <remarks>
    /// Driven by <see cref="NTCBaker.ntcEnabledChanged"/>, so
    /// any way of changing the setting -- Graphics Settings, undo, a script setting
    /// <see cref="NTCSettings.enabled"/>, assigning or clearing the MLP asset -- swaps the materials.
    ///
    /// A shader swap keeps every saved property and keyword -- the engine only parks what the new
    /// shader does not declare -- so swapping back restores the material exactly, and in the Editor
    /// the new shader's GUI revalidates its keywords on the spot. The build then drops the texture
    /// set the active shader does not declare, with no build-time processing.
    ///
    /// Two modes:
    ///
    ///   Edit mode: a real change to the assets, saved immediately, meant to be committed. It is
    ///   what decides what the next build ships.
    ///
    ///   Play mode: a preview. Material assets are not reverted when play mode ends, so each
    ///   material's full serialized state is snapshotted before its first swap and written back
    ///   on exit, together with Enable NTC -- swapping back would not do: every shader change runs
    ///   the new shader's GUI validation, which rewrites things like the RenderType override tag.
    ///   Until then NTCSwitchSaveGuard keeps the swapped materials and the settings asset from
    ///   being saved. A crash needs no recovery, since nothing was written.
    ///
    /// Material Variants cannot change shader; they take it from their root, so the switcher swaps
    /// roots. Tracked materials are the ones listed in <see cref="NTCSettings.materials"/>.
    /// </remarks>
    [InitializeOnLoad]
    static class NTCMaterialSwitcher
    {
        // Where the NTC shader takes its UV tiling from, and where the conventional shader takes
        // it from. Copied across on every swap so tiling edited in one state carries to the other.
        const string k_ConventionalTiling = "_BaseMap";
        const string k_NTCTiling = "_NeuralTextureLatent0";

        const string k_PlayModeRecordKey = "NTCMaterialSwitcher.PlayModeRecord";

        [Serializable]
        class Snapshot
        {
            public string guid;
            public string json;  // EditorJsonUtility state before the first play-mode swap
            public bool wasDirty;
        }

        [Serializable]
        class PlayModeRecord
        {
            public bool enabledBefore;
            public List<Snapshot> materials = new List<Snapshot>();
        }

        // Set while play mode's changes are written back, so restoring Enable NTC does not swap
        // the materials the snapshots are about to restore.
        static bool s_Reverting;

        static NTCMaterialSwitcher()
        {
            NTCBaker.ntcEnabledChanged -= OnNTCEnabledChanged;
            NTCBaker.ntcEnabledChanged += OnNTCEnabledChanged;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

            // A record found in edit mode belongs to an editor session that never left play mode
            // cleanly; nothing it covered reached disk, so it only has to go.
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                SessionState.EraseString(k_PlayModeRecordKey);
        }

        static void OnNTCEnabledChanged(bool enabled)
        {
            if (!s_Reverting)
                Apply(enabled, CurrentMode());
        }

        static Mode CurrentMode() => EditorApplication.isPlayingOrWillChangePlaymode ? Mode.PlayMode : Mode.Persistent;

        /// <summary>The materials the switcher moves: <see cref="NTCSettings.materials"/>.</summary>
        public static IEnumerable<Material> GetTrackedMaterials()
        {
            if (!TryGetSettings(out var settings) || settings.materials == null)
                return Enumerable.Empty<Material>();
            return settings.materials.Where(m => m != null).Distinct();
        }

        /// <summary>
        /// The shader each tracked root should be on for the given state: the NTC side of its pair
        /// unless NTC is off or the root is excluded. Null when the root's shader is in no pair.
        /// </summary>
        public static Shader GetExpectedShader(Material root, bool ntc)
        {
            var pairs = NTCShaderPairs.instance;
            return pairs.GetShaderFor(root.shader, ntc && !pairs.excluded.Contains(root));
        }

        enum Mode { Persistent, PlayMode }

        static int Apply(bool ntc, Mode mode)
        {
            var tracked = GetTrackedMaterials().ToList();
            var families = tracked.GroupBy(NTCShaderPairs.GetRoot).Where(g => g.Key != null);
            var swapped = new List<Material>();

            foreach (var family in families)
            {
                var root = family.Key;
                var target = GetExpectedShader(root, ntc);
                if (target == null)
                {
                    Debug.LogWarning($"NTC: '{root.name}' uses '{root.shader?.name}', which is in no NTC shader pair " +
                                     "(Project Settings > Graphics > NTC Shader Pairs); left as is.", root);
                    continue;
                }
                if (target == root.shader)
                    continue;

                bool toNTC = NTCShaderPairs.instance.IsNTCShader(target);
                string from = toNTC ? k_ConventionalTiling : k_NTCTiling;
                string to = toNTC ? k_NTCTiling : k_ConventionalTiling;

                // Every material whose tiling has to follow: the root, and variants that override it.
                var members = family.Append(root).Distinct().ToList();
                var tiling = new List<(Material material, Vector2 scale, Vector2 offset)>();
                foreach (var m in members)
                {
                    if (m.HasTexture(from) && (m == root || m.IsPropertyOverriden(from)))
                        tiling.Add((m, m.GetTextureScale(from), m.GetTextureOffset(from)));
                }

                var source = root.shader;
                var keywords = members.ToDictionary(m => m, m => new HashSet<string>(m.enabledKeywords.Select(k => k.name)));

                if (mode == Mode.PlayMode)
                    RecordForPlayMode(members);
                else if (mode == Mode.Persistent)
                    Undo.RecordObjects(members.ToArray(), toNTC ? "Switch Materials to NTC" : "Switch Materials to Conventional");

                // Not ShaderGUI.AssignNewShaderToMaterial: URP's clears every keyword, including the
                // ones parked for the other shader.
                root.shader = target;

                foreach (var m in members)
                    CarryToggleKeywords(m, source, target, keywords[m]);

                foreach (var (m, scale, offset) in tiling)
                {
                    if (!m.HasTexture(to))
                        continue;
                    // Only real differences, so steady-state toggling never adds variant overrides.
                    if (m.GetTextureScale(to) != scale)
                        m.SetTextureScale(to, scale);
                    if (m.GetTextureOffset(to) != offset)
                        m.SetTextureOffset(to, offset);
                }

                swapped.Add(root);
            }

            if (swapped.Count > 0)
            {
                if (mode == Mode.Persistent)
                {
                    foreach (var root in swapped)
                        AssetDatabase.SaveAssetIfDirty(root);

                    // A root's new shader reaches its variants by a lazy merge that never marks
                    // them dirty, so SaveAssetIfDirty alone would leave their .mat files on the
                    // old shader. Reading the shader does not run the merge, but reading the
                    // properties does (Material::EnsurePropertiesExist); dirtying then saves it. Every
                    // variant of a swapped root, not only listed ones: an unlisted variant renders
                    // its root's shader all the same.
                    foreach (var variant in FindVariantsOf(new HashSet<Material>(swapped)))
                    {
                        variant.GetTexturePropertyNameIDs();
                        if (variant.shader != NTCShaderPairs.GetRoot(variant).shader)
                            Debug.LogWarning($"NTC: variant '{variant.name}' did not pick up its root's shader.", variant);
                        EditorUtility.SetDirty(variant);
                        AssetDatabase.SaveAssetIfDirty(variant);
                    }
                }

                // Assigning a shader drops the material's NTC cbuffer windows.
                NTCRuntime.InvalidateMaterialBindings();
                Debug.Log($"NTC: switched {swapped.Count} material{(swapped.Count == 1 ? "" : "s")} to " +
                          $"{(ntc ? "NTC" : "conventional")} shaders{(mode == Mode.PlayMode ? " (play mode only)" : "")}.");
            }

            return swapped.Count;
        }

        static IEnumerable<Material> FindVariantsOf(HashSet<Material> roots)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".mat", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material != null && material.isVariant && roots.Contains(NTCShaderPairs.GetRoot(material)))
                    yield return material;
            }
        }

        // A Shader Graph keyword is driven by a [Toggle(KEYWORD)] float of the same name, and the
        // swap re-applies property drawers, so a float the material never had (URP Lit has no
        // _EMISSION float) would switch the keyword off. Keywords both shaders declare keep the state
        // they had before the swap; only real differences are written, so variants gain no overrides.
        //
        // Only keywords backed by such a float: built-in multi_compile keywords (instancing, stereo,
        // ...) are never a material's to set. By name rather than LocalKeyword, because a variant's
        // shader follows its root's lazily and a target-shader LocalKeyword would not match it yet.
        static void CarryToggleKeywords(Material material, Shader source, Shader target, HashSet<string> enabledBefore)
        {
            foreach (var keyword in target.keywordSpace.keywords)
            {
                string name = keyword.name;
                int index = target.FindPropertyIndex(name);
                if (index < 0)
                    continue;
                var type = target.GetPropertyType(index);
                if (type != ShaderPropertyType.Float && type != ShaderPropertyType.Range)
                    continue;
                if (!source.keywordSpace.FindKeyword(name).isValid)
                    continue;

                bool on = enabledBefore.Contains(name);
                if (material.GetFloat(name) != (on ? 1f : 0f))
                    material.SetFloat(name, on ? 1f : 0f);
                if (material.IsKeywordEnabled(name) != on)
                {
                    if (on)
                        material.EnableKeyword(name);
                    else
                        material.DisableKeyword(name);
                }
            }
        }

        static bool TryGetSettings(out NTCSettings settings)
            => GraphicsSettings.TryGetRenderPipelineSettings(out settings);

        // --- Play mode preview ---------------------------------------------------------------

        internal static bool hasPlayModeRecord => !string.IsNullOrEmpty(SessionState.GetString(k_PlayModeRecordKey, ""));

        // The swapped materials, and the settings asset once Enable NTC differs from what play mode
        // was entered with.
        internal static HashSet<string> GetGuardedAssetPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var record = LoadRecord();
            if (record == null)
                return paths;
            foreach (var snapshot in record.materials)
                paths.Add(AssetDatabase.GUIDToAssetPath(snapshot.guid));

            bool settingsChanged = TryGetSettings(out var settings) && settings.enabled != record.enabledBefore;
            var globalSettings = GraphicsSettings.GetSettingsForRenderPipeline<UniversalRenderPipeline>();
            if ((settingsChanged || record.materials.Count > 0) && globalSettings != null)
                paths.Add(AssetDatabase.GetAssetPath(globalSettings));
            return paths;
        }

        static PlayModeRecord LoadRecord()
        {
            var json = SessionState.GetString(k_PlayModeRecordKey, "");
            return string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<PlayModeRecord>(json);
        }

        static void SaveRecord(PlayModeRecord record)
            => SessionState.SetString(k_PlayModeRecordKey, JsonUtility.ToJson(record));

        // The root and the tracked variants whose tiling or keywords the swap may touch. Only the
        // first snapshot of a material counts: that is the state play mode was entered with.
        static void RecordForPlayMode(IEnumerable<Material> members)
        {
            var record = LoadRecord() ?? new PlayModeRecord();
            bool changed = false;
            foreach (var material in members)
            {
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material));
                if (string.IsNullOrEmpty(guid) || record.materials.Exists(s => s.guid == guid))
                    continue;
                record.materials.Add(new Snapshot
                {
                    guid = guid,
                    json = EditorJsonUtility.ToJson(material),
                    wasDirty = EditorUtility.IsDirty(material),
                });
                changed = true;
            }
            if (changed)
                SaveRecord(record);
        }

        static int VariantDepth(string guid)
        {
            int depth = 0;
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            for (var parent = material != null ? material.parent : null; parent != null; parent = parent.parent)
                ++depth;
            return depth;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                // Before the domain reload, so the record survives it in SessionState.
                case PlayModeStateChange.ExitingEditMode:
                    if (TryGetSettings(out var settings))
                        SaveRecord(new PlayModeRecord { enabledBefore = settings.enabled });
                    break;
                case PlayModeStateChange.ExitingPlayMode:
                case PlayModeStateChange.EnteredEditMode:
                    RevertPlayModeChanges();
                    break;
            }
        }

        static void RevertPlayModeChanges()
        {
            var record = LoadRecord();
            if (record == null)
                return;

            if (TryGetSettings(out var settings) && settings.enabled != record.enabledBefore)
            {
                s_Reverting = true;
                try
                {
                    settings.enabled = record.enabledBefore;
                }
                finally
                {
                    s_Reverting = false;
                }
            }

            // Roots before variants, so a variant is written back over its restored parent.
            foreach (var snapshot in record.materials.OrderBy(s => VariantDepth(s.guid)))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(snapshot.guid));
                if (material == null)
                    continue;
                EditorJsonUtility.FromJsonOverwrite(snapshot.json, material);
                if (!snapshot.wasDirty)
                    EditorUtility.ClearDirty(material);
            }

            // Overwriting a material rebuilds its properties, which drops its NTC cbuffer windows.
            NTCRuntime.InvalidateMaterialBindings();
            SessionState.EraseString(k_PlayModeRecordKey);
        }
    }

    /// <summary>
    /// Keeps materials swapped by a play-mode NTC toggle, and the settings asset, from being saved
    /// before <see cref="NTCMaterialSwitcher"/> puts them back.
    /// </summary>
    class NTCSwitchSaveGuard : AssetModificationProcessor
    {
        static string[] OnWillSaveAssets(string[] paths)
        {
            if (!NTCMaterialSwitcher.hasPlayModeRecord)
                return paths;
            var guarded = NTCMaterialSwitcher.GetGuardedAssetPaths();
            return paths.Where(p => !guarded.Contains(p)).ToArray();
        }
    }
}
