using UnityEngine.Experimental.Neural.Rendering;
using UnityEngine.Rendering;

namespace UnityEditor.Experimental.Neural.Rendering
{
    /// <summary>
    /// Leaves <see cref="NTCSettings"/> out of builds that do not ship NTC.
    /// </summary>
    /// <remarks>
    /// The settings hold the MLP weights and reference every NTC material, so keeping them would
    /// pull the weights blob and those materials into a build that never decodes. The stripper API
    /// passes no build report, so the target is the active one, which is what a player build uses.
    /// </remarks>
    class NTCSettingsStripper : IRenderPipelineGraphicsSettingsStripper<NTCSettings>
    {
        public bool active => true;

        public bool CanRemoveSettings(NTCSettings settings)
            => !NTCBaker.ShipsNTC(settings, EditorUserBuildSettings.activeBuildTarget);
    }
}
