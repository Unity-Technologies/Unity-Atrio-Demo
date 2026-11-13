using System;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Atrio.RenderingEditor
{
    /// <summary>
    /// Inspector for "Atrio/Lit (Scale-Adaptive UV)": URP's own Lit inspector, followed by a Scale
    /// Adaptation section for choosing which object-scale axis drives each UV axis.
    /// </summary>
    /// <remarks>
    /// URP's Lit inspector class is internal, so it is created by name and wrapped rather than
    /// derived from. Everything above the Scale Adaptation section is therefore exactly what URP/Lit
    /// shows, and follows it across URP upgrades.
    /// </remarks>
    internal sealed class LitScaleAdaptiveUVShaderGUI : ShaderGUI
    {
        /// <summary>Which axis of the object's scale multiplies a UV axis. Mirrors the shader enum.</summary>
        enum ScaleSource
        {
            None = 0,
            ScaleX = 1,
            ScaleY = 2,
            ScaleZ = 3
        }

        static class Styles
        {
            public static readonly GUIContent header = EditorGUIUtility.TrTextContent("Scale Adaptation",
                "Multiplies UV0 by the object's world scale before tiling, so textures keep their texel " +
                "density as instances are stretched. Shared meshes stay shared, so instancing is unaffected.");

            public static readonly GUIContent uvX = EditorGUIUtility.TrTextContent("UV X Source",
                "Which axis of the object's world scale multiplies UV.x.");

            public static readonly GUIContent uvY = EditorGUIUtility.TrTextContent("UV Y Source",
                "Which axis of the object's world scale multiplies UV.y.");

            public static readonly string[] sourceNames = { "None", "Scale X", "Scale Y", "Scale Z" };
        }

        const string k_LitShaderGUITypeName = "UnityEditor.Rendering.Universal.ShaderGUI.LitShader, Unity.RenderPipelines.Universal.Editor";

        internal const string k_UVXProperty = "_ScaleAdaptiveUVX";
        internal const string k_UVYProperty = "_ScaleAdaptiveUVY";

        // Indexed by ScaleSource; None has no keyword.
        static readonly string[] k_UVXKeywords = { null, "_SCALE_ADAPTIVE_UV_X_FROM_X", "_SCALE_ADAPTIVE_UV_X_FROM_Y", "_SCALE_ADAPTIVE_UV_X_FROM_Z" };
        static readonly string[] k_UVYKeywords = { null, "_SCALE_ADAPTIVE_UV_Y_FROM_X", "_SCALE_ADAPTIVE_UV_Y_FROM_Y", "_SCALE_ADAPTIVE_UV_Y_FROM_Z" };

        // URP's Lit foldouts use bits 0-3 of the expanded state.
        const uint k_ScaleAdaptationExpandable = 1 << 4;

        readonly ShaderGUI m_LitGUI = CreateLitGUI();

        static ShaderGUI CreateLitGUI()
        {
            var type = Type.GetType(k_LitShaderGUITypeName, throwOnError: true);
            return (ShaderGUI)Activator.CreateInstance(type, nonPublic: true);
        }

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            m_LitGUI.OnGUI(materialEditor, properties);

            var uvX = FindProperty(k_UVXProperty, properties);
            var uvY = FindProperty(k_UVYProperty, properties);

            using (var header = new MaterialHeaderScope(Styles.header, k_ScaleAdaptationExpandable, materialEditor))
            {
                if (header.expanded)
                {
                    materialEditor.PopupShaderProperty(uvX, Styles.uvX, Styles.sourceNames);
                    materialEditor.PopupShaderProperty(uvY, Styles.uvY, Styles.sourceNames);
                }
            }
        }

        public override void ValidateMaterial(Material material)
        {
            m_LitGUI.ValidateMaterial(material);
            SetMaterialKeywords(material);
        }

        public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
        {
            m_LitGUI.AssignNewShaderToMaterial(material, oldShader, newShader);
            SetMaterialKeywords(material);
        }

        public override void OnClosed(Material material) => m_LitGUI.OnClosed(material);

        /// <summary>Enables exactly the keywords matching the material's two source properties.</summary>
        internal static void SetMaterialKeywords(Material material)
        {
            SetSourceKeywords(material, k_UVXProperty, k_UVXKeywords);
            SetSourceKeywords(material, k_UVYProperty, k_UVYKeywords);
        }

        static void SetSourceKeywords(Material material, string property, string[] keywords)
        {
            if (!material.HasProperty(property))
                return;

            var selected = (ScaleSource)Mathf.RoundToInt(material.GetFloat(property));
            for (int i = 1; i < keywords.Length; i++)
                material.SetKeyword(new LocalKeyword(material.shader, keywords[i]), (int)selected == i);
        }
    }
}
