using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Inspector for <see cref="KinematicMover"/>: the default inspector plus hints that link fields.
    /// </summary>
    /// <remarks>
    /// The mover applies no forces of its own, so a mover with no processors does not fall. The
    /// inspector says so.
    /// </remarks>
    [CustomEditor(typeof(KinematicMover))]
    sealed class KinematicMoverEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawProcessorHint();

            serializedObject.Update();
            SerializedProperty property = serializedObject.GetIterator();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                enter = false;
                if (property.propertyPath == "m_Script")
                {
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.PropertyField(property);
                    continue;
                }

                if (property.name == "m_SpeedAtSlopeLimit")
                {
                    DrawSpeedAtSlopeLimit(property);
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
            }

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// A percent slider, then a line showing the resulting speed at three angles up to the slope limit.
        /// </summary>
        void DrawSpeedAtSlopeLimit(SerializedProperty property)
        {
            var label = new GUIContent("Speed At Slope Limit", property.tooltip);
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
            EditorGUI.BeginChangeCheck();
            int percent = EditorGUILayout.IntSlider(label, Mathf.RoundToInt(property.floatValue * 100f), 0, 100);
            if (EditorGUI.EndChangeCheck())
                property.floatValue = percent / 100f;
            EditorGUI.showMixedValue = false;

            if (serializedObject.isEditingMultipleObjects) return;

            // Applied first, so the line follows the slider and Slope Limit above while they are dragged.
            serializedObject.ApplyModifiedProperties();
            var mover = (KinematicMover)target;
            float limit = mover.SlopeLimit;
            var text = new System.Text.StringBuilder();
            foreach (float angle in new[] { Mathf.Round(limit * 0.4f), Mathf.Round(limit * 0.7f), limit })
            {
                if (text.Length > 0) text.Append(" · ");
                text.Append($"{angle:0}° {mover.SpeedOnSlope(angle) * 100f:0}%");
            }

            text.Append(" of walking speed, up and down");
            EditorGUILayout.LabelField(" ", text.ToString(), EditorStyles.miniLabel);
        }

        void DrawProcessorHint()
        {
            var mover = (KinematicMover)target;

            // Other hosts register processors in code, which this inspector cannot see, so it only
            // says what is missing.
            var host = mover.GetComponent<Character>();
            if (host == null)
            {
                EditorGUILayout.HelpBox(
                    "No Character on this object, so nothing ticks this mover and it has no processors: " +
                    "it will not fall and it will not walk. Add a Character (it holds the processors) " +
                    "or drive the mover from your own host.",
                    MessageType.Info);
                EditorGUILayout.Space();
                return;
            }

            if (host.Processors.Count > 0) return;

            EditorGUILayout.HelpBox(
                "This character has no move processors, so the mover applies no forces at all. It " +
                "will not fall and it will not walk. That is by design: the mover applies no forces of " +
                "its own; every force, gravity included, lives in a processor. Add Ground Locomotion in " +
                "the Character's processor list for gravity and movement.",
                MessageType.Info);

            EditorGUILayout.Space();
        }
    }
}
