using System;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// The shared pieces the Touch Input inspectors are built from, styled by <c>touch-input.uss</c>.
    /// </summary>
    static class TouchInspectorKit
    {
        /// <summary>A label on the fixed column and the controls after it.</summary>
        public static VisualElement Row(string label, params VisualElement[] controls)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-touch-row");
            var name = new Label(label);
            name.AddToClassList("blocks-touch-row__label");
            row.Add(name);
            var control = new VisualElement();
            control.AddToClassList("blocks-touch-row__control");
            foreach (VisualElement c in controls)
                control.Add(c);
            row.Add(control);
            return row;
        }

        /// <summary>A muted hint paragraph.</summary>
        public static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-touch-hint");
            return label;
        }

        /// <summary>A bold sub-heading inside a card body.</summary>
        public static Label Sub(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-touch-sub");
            return label;
        }

        /// <summary>A read-only value in a field's frame; clickable when <paramref name="onClick"/> is given.</summary>
        public static Label Value(string text, Action onClick = null, string tooltip = null)
        {
            var label = new Label(text) { tooltip = tooltip };
            label.AddToClassList("blocks-touch-value");
            if (onClick != null)
            {
                label.AddToClassList("blocks-touch-value--link");
                label.RegisterCallback<ClickEvent>(_ => onClick());
            }

            return label;
        }

        /// <summary>
        /// A slider with its number, bound to <paramref name="property"/>. Writes record Undo, and outside changes are followed.
        /// </summary>
        public static Slider Slider(SerializedProperty property, float min, float max, string tooltip = null)
        {
            var slider = new Slider(min, max) { showInputField = true, tooltip = tooltip };
            slider.AddToClassList("blocks-touch-slider");
            slider.BindProperty(property);
            return slider;
        }

        /// <summary>
        /// A row labelled <paramref name="label"/> with a field bound to <paramref name="property"/>.
        /// The field's own label is hidden so all rows share one label column.
        /// </summary>
        public static VisualElement Field(SerializedProperty property, string label, string tooltip = null)
        {
            var field = new PropertyField(property, string.Empty) { tooltip = tooltip ?? property.tooltip };
            field.AddToClassList("blocks-touch-row__field");
            field.BindProperty(property);
            VisualElement row = Row(label, field);
            row.tooltip = field.tooltip;
            return row;
        }

        /// <summary>
        /// A row with an object field of <paramref name="objectType"/> bound to <paramref name="property"/>,
        /// drawn without the usual indent so it lines up.
        /// </summary>
        public static VisualElement ObjectRow(SerializedProperty property, string label, System.Type objectType, string tooltip = null)
        {
            var field = new ObjectField { objectType = objectType, allowSceneObjects = !typeof(ScriptableObject).IsAssignableFrom(objectType) && objectType != typeof(Sprite), tooltip = tooltip ?? property.tooltip };
            field.AddToClassList("blocks-touch-row__field");
            field.BindProperty(property);
            VisualElement row = Row(label, field);
            row.tooltip = field.tooltip;
            return row;
        }

        /// <summary>The small drawn icon for a control kind: a stick or a button.</summary>
        public static VisualElement Icon(TouchControl control)
        {
            var icon = new VisualElement();
            icon.AddToClassList("blocks-touch-icon");

            var dot = new VisualElement();
            dot.AddToClassList("blocks-touch-icon__dot");
            icon.Add(dot);
            if (!(control is TouchJoystick))
                icon.AddToClassList("blocks-touch-icon--button");

            return icon;
        }

        /// <summary>The kind's menu name, such as "Joystick" or "Button".</summary>
        public static string KindName(TouchControl control)
        {
            switch (control)
            {
                case TouchJoystick _: return "Joystick";
                case TouchButton _: return "Button";
                default: return ObjectNames.NicifyVariableName(control.GetType().Name);
            }
        }

        /// <summary>The C# line gameplay uses to read the control.</summary>
        public static string CSharpLine(TouchControl control)
        {
            string type = control.GetType().Name;
            string id = control.ActionId;
            switch (control)
            {
                case TouchJoystick _: return $"rig.Get<{type}>(\"{id}\").Value";
                case TouchButton _: return $"rig.Get<{type}>(\"{id}\").Pressed += …";
                default: return $"rig.Get<{type}>(\"{id}\").IsPressed";
            }
        }

        /// <summary>A control's live value, for the rig's mapping rows.</summary>
        public static string LiveValue(TouchControl control)
        {
            switch (control)
            {
                case TouchJoystick joystick:
                    Vector2 v = joystick.Value;
                    return $"({v.x:0.00}, {v.y:0.00})";
                case TouchButton button:
                    return button.IsPressed ? $"pressed {button.HoldSeconds:0.0} s" : "released";
                default:
                    return control.IsPressed ? "pressed" : "released";
            }
        }
    }
}
