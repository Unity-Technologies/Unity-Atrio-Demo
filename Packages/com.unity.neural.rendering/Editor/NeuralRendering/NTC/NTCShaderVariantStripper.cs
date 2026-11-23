using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Neural.Rendering;
using UnityEngine.Rendering;

namespace UnityEditor.Experimental.Neural.Rendering
{
    /// <summary>
    /// Drops the neural texture compression shader variants a build cannot use.
    /// </summary>
    /// <remarks>
    /// The inference path is declared as a multi_compile on the pass -- StandardLit_NTC.shadergraph
    /// carries "multi_compile_fragment _UNITY_NTC_FMA4 _UNITY_NTC_COOP_VECTORS _UNITY_NTC_OFF" in
    /// Graph Inspector > Preprocessor Directives > Pragmas -- because NTCRuntime picks it from a
    /// project setting rather than from anything a material knows. Three variants per fragment
    /// snippet; at most one ships:
    ///
    ///   _UNITY_NTC_OFF: a deliberate #error (NTCDecode.hlsl), so an NTC shader rendered while NTC
    ///   is disabled shows the error shader in the Editor. Always stripped: it cannot compile, and a
    ///   project keeps its materials off NTC shaders while NTC is off. A player that enables it
    ///   anyway shows the error shader when strict shader variant matching is on.
    ///
    ///   FMA4: kept when the build ships NTC (NTCBaker.ShipsNTC: the Enable NTC toggle, a valid MLP
    ///   asset, and a target NTCBaker allows) in FMA4 mode. No platform check: a target NTCBaker
    ///   allows can run it.
    ///
    ///   Cooperative vectors: kept when the build ships NTC in that mode and the platform can run
    ///   them, in two parts because one does not imply the other. The target has to be built for an
    ///   API that can run them at all -- NTCBaker answers that from its graphics API list, which is
    ///   the only thing that separates D3D12 from D3D11 here. And the individual variant has to be
    ///   the D3D copy: a target built for D3D12 and Vulkan together compiles both, and only one of
    ///   them can run the decode.
    /// </remarks>
    class NTCShaderVariantStripper : IPreprocessShaders, IPreprocessBuildWithReport
    {
        // Runs before URP's own stripper (callbackOrder 0), so the variants it removes never reach
        // that pass and the numbers URP logs describe what is actually left.
        public int callbackOrder => -100;

        // What OnProcessShader gates the cooperative-vector variants on. Taken from the report
        // rather than from EditorUserBuildSettings because a build can target something other than
        // the active platform; the fallback covers shader compilation outside a build, where
        // OnPreprocessBuild never runs. callbackOrder puts this first within a build.
        BuildTarget m_BuildTarget = EditorUserBuildSettings.activeBuildTarget;

        // Once per build rather than per shader: explains why a build with NTC switched on does
        // not ship it.
        public void OnPreprocessBuild(BuildReport report)
        {
            m_BuildTarget = report.summary.platform;

            var settings = GraphicsSettings.GetRenderPipelineSettings<NTCSettings>();
            if (settings != null && settings.enabled && !settings.hasValidWeights)
                Debug.LogWarning(NTCSettings.GetInvalidWeightsMessage(settings.weights) +
                                 " This build does not ship NTC.");
        }

        const string k_FMA4Name = "_UNITY_NTC_FMA4";
        const string k_CoopVectorsName = "_UNITY_NTC_COOP_VECTORS";
        const string k_OffName = "_UNITY_NTC_OFF";

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            var space = shader.keywordSpace;
            var fma4 = space.FindKeyword(k_FMA4Name);
            var coopVectors = space.FindKeyword(k_CoopVectorsName);
            var off = space.FindKeyword(k_OffName);
            if (!fma4.isValid || !coopVectors.isValid || !off.isValid)
                return;

            var settings = GraphicsSettings.GetRenderPipelineSettings<NTCSettings>();
            bool enabled = NTCBaker.ShipsNTC(settings, m_BuildTarget);
            var mode = enabled ? settings.mode : default;
            bool coopVectorsSupported = NTCBaker.SupportsCooperativeVectors(m_BuildTarget);

            // Back to front: removing from the end keeps the indices of the untouched entries
            // valid, which a forward loop over an IList does not.
            for (int i = data.Count - 1; i >= 0; --i)
            {
                if (ShouldStrip(data[i], fma4, coopVectors, off, enabled, mode, coopVectorsSupported))
                    data.RemoveAt(i);
            }
        }

        static bool ShouldStrip(ShaderCompilerData variant, in LocalKeyword fma4, in LocalKeyword coopVectors,
                                in LocalKeyword off, bool enabled, NTCSettings.RuntimeMode mode,
                                bool coopVectorsSupported)
        {
            var keywords = variant.shaderKeywordSet;

            // The #error variant.
            if (keywords.IsEnabled(off))
                return true;

            if (keywords.IsEnabled(coopVectors))
                return !enabled ||
                       mode != NTCSettings.RuntimeMode.CooperativeVectors ||
                       !coopVectorsSupported ||
                       variant.shaderCompilerPlatform != ShaderCompilerPlatform.D3D;

            if (keywords.IsEnabled(fma4))
                return !enabled || mode != NTCSettings.RuntimeMode.FMA4;

            // No keyword of the set: a stage the set is not declared for. The set is
            // multi_compile_fragment, so every vertex variant lands here, and the fragment stage
            // never does (the set has no empty entry). Stripping these leaves the shader without
            // vertex programs, which renders it pink in a player.
            return false;
        }
    }
}
