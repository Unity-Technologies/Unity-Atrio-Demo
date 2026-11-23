using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Experimental.Neural.Rendering
{
    /// <summary>
    /// Which NTC shader replaces which conventional shader, and which materials stay conventional.
    /// </summary>
    /// <remarks>
    /// An NTC shader declares only the latent textures and its conventional partner only the
    /// conventional ones, so a material's shader alone decides which set a build ships: the engine
    /// drops texture properties the shader does not declare. Switching NTC on or off is therefore a
    /// shader swap, done by the project in NTCBaker.ntcEnabledChanged, and this table is how the
    /// swap finds the other shader. A material's current shader is its state; nothing is stored
    /// per material.
    ///
    /// Editor-only: lives in ProjectSettings and never ships.
    /// </remarks>
    [FilePath("ProjectSettings/NTCShaderPairs.asset", FilePathAttribute.Location.ProjectFolder)]
    public class NTCShaderPairs : ScriptableSingleton<NTCShaderPairs>
    {
        internal const string k_DefaultConventionalShaderName = "Universal Render Pipeline/Lit";
        // StandardLit_NTC.shadergraph, by GUID so the lookup survives package renames and moves.
        internal const string k_DefaultNTCShaderGuid = "3cd3cb2b89c21fb4ea785cbf12b45b35";

        /// <summary>A conventional shader and the NTC shader that replaces it.</summary>
        [Serializable]
        public struct Pair
        {
            /// <summary>Samples the conventional texture set; used while NTC is off.</summary>
            public Shader conventional;
            /// <summary>Decodes the latent textures; used while NTC is on.</summary>
            public Shader ntc;
        }

        [SerializeField]
        List<Pair> m_Pairs = new List<Pair>();

        [SerializeField]
        List<Material> m_Excluded = new List<Material>();

        /// <summary>The shader pairs. Mutate through <see cref="Save"/> afterwards.</summary>
        public List<Pair> pairs => m_Pairs;

        /// <summary>
        /// Root materials that stay on their conventional shader while NTC is on. A Material Variant
        /// takes its shader from its root, so exclusions apply to roots.
        /// </summary>
        public List<Material> excluded => m_Excluded;

        void OnEnable()
        {
            if (m_Pairs.Count > 0)
                return;

            var conventional = Shader.Find(k_DefaultConventionalShaderName);
            var ntc = AssetDatabase.LoadAssetAtPath<Shader>(AssetDatabase.GUIDToAssetPath(k_DefaultNTCShaderGuid));
            if (conventional != null && ntc != null)
                m_Pairs.Add(new Pair { conventional = conventional, ntc = ntc });
        }

        /// <summary>Writes the table to ProjectSettings.</summary>
        public void Save() => Save(true);

        /// <summary>True when <paramref name="shader"/> is the NTC side of a pair.</summary>
        public bool IsNTCShader(Shader shader) => shader != null && m_Pairs.Exists(p => p.ntc == shader);

        /// <summary>True when <paramref name="shader"/> is the conventional side of a pair.</summary>
        public bool IsConventionalShader(Shader shader) => shader != null && m_Pairs.Exists(p => p.conventional == shader);

        /// <summary>
        /// The shader <paramref name="current"/> swaps to for the requested state: itself when it is
        /// already on that side, null when it is in no pair.
        /// </summary>
        public Shader GetShaderFor(Shader current, bool ntc)
        {
            if (current == null)
                return null;
            foreach (var pair in m_Pairs)
            {
                if (pair.ntc == current || pair.conventional == current)
                    return ntc ? pair.ntc : pair.conventional;
            }
            return null;
        }

        /// <summary>True when <paramref name="material"/>'s root is excluded from NTC.</summary>
        public bool IsExcluded(Material material)
            => material != null && m_Excluded.Contains(GetRoot(material));

        /// <summary>The root of a Material Variant hierarchy; the material itself when it is not a variant.</summary>
        public static Material GetRoot(Material material)
        {
            var root = material;
            while (root != null && root.parent != null)
                root = root.parent;
            return root;
        }
    }

    /// <summary>Project Settings > Graphics > NTC Shader Pairs.</summary>
    static class NTCShaderPairsSettingsProvider
    {
        static class Styles
        {
            public static readonly GUIContent pairs = EditorGUIUtility.TrTextContent("Shader Pairs",
                "Each conventional shader and the NTC shader that replaces it while NTC is on. Properties should share names so values carry across a swap.");
            public static readonly GUIContent excluded = EditorGUIUtility.TrTextContent("Excluded Materials",
                "Root materials that stay on their conventional shader while NTC is on.");
        }

        [SettingsProvider]
        static SettingsProvider Create() => new SettingsProvider("Project/Graphics/NTC Shader Pairs", SettingsScope.Project)
        {
            keywords = new HashSet<string> { "NTC", "Neural", "Shader", "Pair", "Exclude" },
            guiHandler = _ =>
            {
                var so = new SerializedObject(NTCShaderPairs.instance);
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(so.FindProperty("m_Pairs"), Styles.pairs);
                EditorGUILayout.PropertyField(so.FindProperty("m_Excluded"), Styles.excluded);
                if (EditorGUI.EndChangeCheck())
                {
                    so.ApplyModifiedPropertiesWithoutUndo();
                    NTCShaderPairs.instance.Save();
                }
            },
        };
    }
}
