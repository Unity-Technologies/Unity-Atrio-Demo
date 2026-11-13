using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>Draws a field marked with <see cref="InspectorLabelAttribute"/> under its given label.</summary>
    [CustomPropertyDrawer(typeof(InspectorLabelAttribute))]
    sealed class InspectorLabelDrawer : PropertyDrawer
    {
        string Label => ((InspectorLabelAttribute)attribute).Name;

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            // A nested PropertyField on the same property would draw this drawer again, so build it by hand.
            // Fields without children fall back to OnGUI.
            if (!property.hasVisibleChildren)
                return null;

            var foldout = new Foldout { text = Label, value = property.isExpanded, tooltip = property.tooltip };
            foldout.RegisterValueChangedCallback(e =>
            {
                if (e.target != foldout) return;
                property.isExpanded = e.newValue;
                property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            });

            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            if (child.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(child, end)) break;
                    foldout.Add(new PropertyField(child.Copy()));
                } while (child.NextVisible(false));
            }
            return foldout;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.PropertyField(position, property, new GUIContent(Label, label.tooltip), true);
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUI.GetPropertyHeight(property, label, true);
        }
    }
}
