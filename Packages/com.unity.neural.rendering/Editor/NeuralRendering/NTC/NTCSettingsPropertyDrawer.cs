using UnityEngine;
using UnityEngine.Experimental.Neural.Rendering;

namespace UnityEditor.Experimental.Neural.Rendering
{
    /// <summary>
    /// Draws the Neural Texture Compression block in Project Settings > Graphics (URP).
    /// </summary>
    [CustomPropertyDrawer(typeof(NTCSettings))]
    class NTCSettingsPropertyDrawer : PropertyDrawer
    {
        static class Styles
        {
            public static readonly GUIContent enabled = EditorGUIUtility.TrTextContent("Enable NTC",
                "Turns neural texture compression on for the project. On by default, but NTC only runs when a valid MLP asset is also assigned.");

            public static readonly GUIContent weights = EditorGUIUtility.TrTextContent("MLP Asset",
                "Asset holding the weights and biases for the NTC networks trained for this project. Leaving this empty turns neural texture compression off.");

            public static readonly GUIContent materials = EditorGUIUtility.TrTextContent("Encoded Materials",
                "Materials whose networks the MLP asset holds. Each material's NTC Material Index selects its network; the runtime binds every material listed here its own window of the MLP buffer.");

            public static readonly GUIContent mode = EditorGUIUtility.TrTextContent("NTC Runtime Mode",
                "How the MLP is evaluated. FMA4 runs on every graphics API. Cooperative Vectors needs D3D12 and a shader compiler that can reach Shader Model 6.10.");

            public static readonly GUIContent modeUnavailable = EditorGUIUtility.TrTextContent("NTC Runtime Mode",
                "Assign an MLP asset above to choose a runtime mode.");

            public static readonly GUIContent coopVectorsUnavailable = EditorGUIUtility.TrTextContent(
                "Cooperative Vectors needs D3D12. Add Direct3D12 to the graphics APIs for the active build target to enable it.");

            public static readonly GUIContent platformUnsupported = EditorGUIUtility.TrTextContent(
                "The target platform contains graphics APIs that don't support Neural Texture Compression.");

            public static readonly string[] modeNames = { "FMA4", "Cooperative Vectors" };
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var featureEnabled = property.FindPropertyRelative("m_Enabled");
            var weights = property.FindPropertyRelative("m_Weights");
            var mode = property.FindPropertyRelative("m_Mode");

            bool supported = NTCBaker.IsSupported(EditorUserBuildSettings.activeBuildTarget);

            // A plain edit: NTCBaker reports the resulting state change to the project through
            // ntcEnabledChanged, which is where materials get switched.
            using (new EditorGUI.DisabledScope(!supported))
                EditorGUILayout.PropertyField(featureEnabled, Styles.enabled);

            if (!supported)
            {
                EditorGUILayout.HelpBox(Styles.platformUnsupported.text, MessageType.Warning);
                return;
            }

            using (new EditorGUI.DisabledScope(!featureEnabled.boolValue))
            {
                EditorGUILayout.PropertyField(weights, Styles.weights);

                bool hasWeights = weights.objectReferenceValue != null;
                using (new EditorGUI.DisabledScope(!hasWeights))
                    DrawModeDropdown(mode, hasWeights);

                if (hasWeights && !coopVectorsSupported && mode.enumValueIndex == (int)NTCSettings.RuntimeMode.CooperativeVectors)
                    EditorGUILayout.HelpBox(Styles.coopVectorsUnavailable.text, MessageType.Warning);

                EditorGUILayout.PropertyField(property.FindPropertyRelative("m_Materials"), Styles.materials);
            }

            // Same rule and wording as the runtime console warning, so what the settings page says
            // matches what happens when a camera renders.
            var asset = weights.objectReferenceValue as TextAsset;
            if (featureEnabled.boolValue && !weights.hasMultipleDifferentValues && !NTCSettings.IsValidWeights(asset))
                EditorGUILayout.HelpBox(NTCSettings.GetInvalidWeightsMessage(asset), MessageType.Warning);
        }

        static void DrawModeDropdown(SerializedProperty mode, bool enabled)
        {
            int current = Mathf.Clamp(mode.enumValueIndex, 0, Styles.modeNames.Length - 1);
            var content = EditorGUI.showMixedValue
                ? EditorGUIUtility.TrTextContent("\u2014")
                : new GUIContent(Styles.modeNames[current]);

            var rect = EditorGUILayout.GetControlRect();
            var label = enabled ? Styles.mode : Styles.modeUnavailable;

            if (!EditorGUI.DropdownButton(EditorGUI.PrefixLabel(rect, label), content, FocusType.Keyboard))
                return;

            var menu = new GenericMenu();
            AddModeItem(menu, mode, NTCSettings.RuntimeMode.FMA4, true, current);

            // Left in the menu but disabled, so the option is discoverable on a machine that cannot
            // run it and the reason lives in the tooltip and the help box rather than in its
            // absence.
            AddModeItem(menu, mode, NTCSettings.RuntimeMode.CooperativeVectors, coopVectorsSupported, current);
            menu.DropDown(rect);
        }

        static void AddModeItem(GenericMenu menu, SerializedProperty mode, NTCSettings.RuntimeMode value,
                                bool selectable, int current)
        {
            var content = new GUIContent(Styles.modeNames[(int)value]);
            if (!selectable)
            {
                menu.AddDisabledItem(content);
                return;
            }

            menu.AddItem(content, current == (int)value, () =>
            {
                mode.enumValueIndex = (int)value;
                mode.serializedObject.ApplyModifiedProperties();
            });
        }

        // Asked of the target currently selected for building, which is also what the variant
        // stripper gates on, so the greyed-out entry and the stripped variants agree.
        static bool coopVectorsSupported
            => NTCBaker.SupportsCooperativeVectors(EditorUserBuildSettings.activeBuildTarget);
    }
}
