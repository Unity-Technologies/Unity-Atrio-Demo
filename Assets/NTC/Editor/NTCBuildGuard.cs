using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Experimental.Neural.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Neural.Rendering;
using UnityEngine.Rendering;

namespace NTC.Editor
{
    /// <summary>
    /// Fails a build whose materials are not on the shaders that match what it ships.
    /// </summary>
    /// <remarks>
    /// Which texture set a build contains follows from each material's shader, so a material left
    /// on the wrong side would ship textures it never samples, or -- on an NTC shader in a build
    /// without NTC -- render the error shader. This only checks; it never edits assets. The fix is
    /// switching Enable NTC off and on again, which makes NTCMaterialSwitcher swap them.
    /// </remarks>
    class NTCBuildGuard : IPreprocessBuildWithReport
    {
        // After NTCShaderVariantStripper (-100), before anything that collects content.
        public int callbackOrder => -90;

        public void OnPreprocessBuild(BuildReport report)
        {
            GraphicsSettings.TryGetRenderPipelineSettings(out NTCSettings settings);
            bool shipNTC = NTCBaker.ShipsNTC(settings, report.summary.platform);

            var mismatches = new StringBuilder();
            int count = 0;
            foreach (var root in NTCMaterialSwitcher.GetTrackedMaterials().Select(NTCShaderPairs.GetRoot).Distinct())
            {
                var expected = NTCMaterialSwitcher.GetExpectedShader(root, shipNTC);
                if (expected == null || expected == root.shader)
                    continue;
                mismatches.AppendLine($"  {AssetDatabase.GetAssetPath(root)}: '{root.shader.name}', expected '{expected.name}'");
                ++count;
            }

            if (count > 0)
            {
                string reason = shipNTC
                    ? "This build ships NTC"
                    : settings != null && settings.enabled
                        ? "This build does not ship NTC (no valid MLP asset, or the target is not supported)"
                        : "Enable NTC is off";
                throw new BuildFailedException(
                    $"NTC: {reason}, but {count} material{(count == 1 ? " is" : "s are")} on the other shader:\n{mismatches}" +
                    "Switch Enable NTC in Project Settings > Graphics off and on again to swap them.");
            }

            WarnAboutUntrackedNTCMaterials();
        }

        // Materials on an NTC shader that the switcher does not track have no network, so they
        // render flat in any build. Only a warning: the search covers the whole project, not what
        // the build uses.
        static void WarnAboutUntrackedNTCMaterials()
        {
            var pairs = NTCShaderPairs.instance;
            // By root: an untracked variant of a tracked root shares its network.
            var tracked = NTCMaterialSwitcher.GetTrackedMaterials().Select(NTCShaderPairs.GetRoot).ToHashSet();
            foreach (var guid in AssetDatabase.FindAssets("t:Material"))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (material != null && !tracked.Contains(NTCShaderPairs.GetRoot(material)) && pairs.IsNTCShader(material.shader))
                    Debug.LogWarning($"NTC: '{AssetDatabase.GetAssetPath(material)}' is on an NTC shader but not listed " +
                                     "in Encoded Materials, so it has no network and renders flat. " +
                                     "Move it to its conventional shader, or train it and add it to Encoded Materials.", material);
            }
        }
    }
}
