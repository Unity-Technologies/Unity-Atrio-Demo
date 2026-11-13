using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Draws an <see cref="InputSlot"/> as a dropdown of the custom-button names on the character's
    /// <see cref="PlayerInputSource"/>. Without a Player Input Source on the object it is a text field.
    /// </summary>
    /// <remarks>
    /// The names refresh every half second. A name the source lacks stays selectable and shows a
    /// warning. <i>Add button…</i> adds a row named after the slot to the source (with Undo) and selects
    /// it. In Play mode a change rebinds the character's processors immediately.
    /// </remarks>
    [CustomPropertyDrawer(typeof(InputSlot))]
    sealed class InputSlotDrawer : PropertyDrawer
    {
        const long k_PollMs = 500;
        const string k_NameField = "m_Name";
        const string k_AddChoice = "Add button…";
        const string k_DefaultTooltip = "The custom button this processor reads, by name (Player Input Source ▸ Custom Buttons).";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            SerializedProperty nameProperty = property.FindPropertyRelative(k_NameField);
            PlayerInputSource source = SourceOf(property);
            string tooltip = TooltipOf(property);

            var root = new VisualElement();

            if (source == null)
            {
                var text = new TextField(property.displayName) { tooltip = tooltip };
                text.AddToClassList(BaseField<string>.alignedFieldUssClassName);
                text.BindProperty(nameProperty);
                root.Add(text);
                root.Add(new HelpBox(
                    "No Player Input Source on this character to pick a button from. Type the name the " +
                    "row will have, or add the source first.", HelpBoxMessageType.Info));
                return root;
            }

            var choices = new List<string>();
            Collect(source, choices, nameProperty.stringValue);

            // Not bound, so "Add button…" is never written into the property; Refresh() keeps it in sync.
            var dropdown = new DropdownField(property.displayName, choices, nameProperty.stringValue) { tooltip = tooltip };
            dropdown.AddToClassList(BaseField<string>.alignedFieldUssClassName);
            root.Add(dropdown);

            var warning = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            warning.style.display = DisplayStyle.None;
            root.Add(warning);

            IVisualElementScheduledItem poll = null;

            void Refresh()
            {
                try
                {
                    if (property.serializedObject.targetObject == null)
                    {
                        poll?.Pause();
                        return;
                    }

                    property.serializedObject.UpdateIfRequiredOrScript();
                    string current = nameProperty.stringValue;
                    var fresh = new List<string>();
                    Collect(source, fresh, current);
                    if (!SameList(fresh, dropdown.choices))
                        dropdown.choices = fresh;
                    if (dropdown.value != current)
                        dropdown.SetValueWithoutNotify(current);

                    bool known = source.FindButton(current) != InputSlot.Unbound;
                    bool empty = string.IsNullOrWhiteSpace(current);
                    warning.text = empty
                        ? "Pick which custom button this processor reads, or choose \"Add button…\" to make one."
                        : $"Player Input Source has no custom button called '{current}'. Add a row of that name, pick another, or choose \"Add button…\".";
                    warning.style.display = known ? DisplayStyle.None : DisplayStyle.Flex;
                }
                catch (Exception)
                {
                    // The serialized object was disposed under us (inspector rebuilt); stop polling.
                    poll?.Pause();
                }
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (property.serializedObject.targetObject == null) return;

                string picked = evt.newValue;
                if (picked == k_AddChoice)
                {
                    dropdown.SetValueWithoutNotify(evt.previousValue);
                    picked = AddRowFor(source, property);
                    if (picked == null) return;
                }

                nameProperty.stringValue = picked;
                property.serializedObject.ApplyModifiedProperties();
                Refresh();
                if (Application.isPlaying && property.serializedObject.targetObject is Character host)
                    host.RebindInputs();
            });

            poll = root.schedule.Execute(Refresh).Every(k_PollMs);
            Refresh();
            return root;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty nameProperty = property.FindPropertyRelative(k_NameField);
            PlayerInputSource source = SourceOf(property);

            if (source == null)
            {
                EditorGUI.PropertyField(position, nameProperty, label);
                return;
            }

            var choices = new List<string>();
            Collect(source, choices, nameProperty.stringValue);
            int index = choices.IndexOf(nameProperty.stringValue);
            int addIndex = choices.Count - 1;

            EditorGUI.BeginProperty(position, label, property);
            int picked = EditorGUI.Popup(position, label.text, index, choices.ToArray());
            if (picked == addIndex)
            {
                string added = AddRowFor(source, property);
                if (added != null) nameProperty.stringValue = added;
            }
            else if (picked != index && picked >= 0 && picked < choices.Count)
            {
                nameProperty.stringValue = choices[picked];
            }
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Adds a custom-button row named after the slot to <paramref name="source"/> (or reuses one) and
        /// returns the name, or <c>null</c> when the slot has no usable name.
        /// </summary>
        static string AddRowFor(PlayerInputSource source, SerializedProperty property)
        {
            string name = property.displayName?.Trim();
            if (string.IsNullOrEmpty(name) || name == k_AddChoice) return null;

            if (source.FindButton(name) == InputSlot.Unbound && !HasRow(source, name))
                CharacterSetupDoctor.AddCustomButton(source, name);

            return name;
        }

        static bool HasRow(PlayerInputSource source, string name)
        {
            foreach (CommandButton row in source.CustomButtons)
            {
                if (row != null && !string.IsNullOrWhiteSpace(row.Name) &&
                    string.Equals(row.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static PlayerInputSource SourceOf(SerializedProperty property)
        {
            var component = property.serializedObject.targetObject as Component;
            return component != null ? component.GetComponent<PlayerInputSource>() : null;
        }

        string TooltipOf(SerializedProperty property)
        {
            if (fieldInfo != null)
            {
                object[] attributes = fieldInfo.GetCustomAttributes(typeof(TooltipAttribute), true);
                if (attributes.Length > 0)
                    return ((TooltipAttribute)attributes[0]).tooltip;
            }

            return string.IsNullOrEmpty(property.tooltip) ? k_DefaultTooltip : property.tooltip;
        }

        /// <summary>
        /// The distinct non-blank row names, plus <paramref name="current"/> if missing, then
        /// <see cref="k_AddChoice"/> last.
        /// </summary>
        static void Collect(PlayerInputSource source, List<string> into, string current)
        {
            into.Clear();
            List<CommandButton> rows = source.CustomButtons;
            for (int i = 0; i < rows.Count; i++)
            {
                CommandButton row = rows[i];
                if (row == null || string.IsNullOrWhiteSpace(row.Name))
                    continue;

                string name = row.Name.Trim();
                if (!Contains(into, name))
                    into.Add(name);
            }

            if (!string.IsNullOrWhiteSpace(current) && !Contains(into, current))
                into.Add(current);

            into.Add(k_AddChoice);
        }

        static bool Contains(List<string> list, string name)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        static bool SameList(List<string> a, List<string> b)
        {
            if (b == null || a.Count != b.Count)
                return false;

            for (int i = 0; i < a.Count; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
