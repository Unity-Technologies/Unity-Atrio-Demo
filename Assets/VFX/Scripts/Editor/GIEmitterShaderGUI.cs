using UnityEditor;
using UnityEngine;

namespace Courtyard.VFXEditor
{
    /// <summary>
    /// Inspector for Custom/VFX/GIEmitter. Draws the default properties and
    /// forces Realtime emission GI flags: Surface Cache GI ignores emission
    /// on materials without RealtimeIndirectEmission.
    /// </summary>
    public class GIEmitterShaderGUI : ShaderGUI
    {
        const MaterialGlobalIlluminationFlags k_RequiredFlags = MaterialGlobalIlluminationFlags.RealtimeIndirectEmission;

        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            base.OnGUI(materialEditor, properties);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Meta pass only: invisible in rendering, used by Surface Cache GI.\n" +
                "GI flags are forced to Realtime emission.\n" +
                "Fade at runtime via material.SetFloat(\"_EmissiveMultiplier\", x).",
                MessageType.Info);
        }

        public override void ValidateMaterial(Material material)
        {
            if (material.globalIlluminationFlags != k_RequiredFlags)
                material.globalIlluminationFlags = k_RequiredFlags;
        }
    }
}
