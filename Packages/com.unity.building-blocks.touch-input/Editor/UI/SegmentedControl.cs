// Copied from the gameplay package's Editor/UI/SegmentedControl.cs.
// Copied, not referenced, so this package does not depend on gameplay. Keep the blocks- class names;
// a shared UI package may replace both copies.
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// A row of buttons where one is on, for a setting with two or three short answers.
    /// <see cref="value"/> is the chosen index, or -1 when the selected objects disagree.
    /// </summary>
    /// <remarks>
    /// Unlike Unity's <c>ToggleButtonGroup</c>, it returns an index, not a bit mask. It is not bound to a
    /// property: the caller maps the index to its value. So the row has no prefab-override bar.
    /// </remarks>
    sealed class SegmentedControl : VisualElement, INotifyValueChanged<int>
    {
        const string k_ButtonClass = "blocks-seg__button";
        const string k_OnClass = "blocks-seg__button--on";
        // USS has no :last-child, so the last button is marked by class.
        const string k_LastClass = "blocks-seg__button--last";

        readonly List<Button> m_Buttons = new List<Button>();
        readonly List<string> m_Tooltips = new List<string>();
        int m_Value = -1;

        /// <summary>Creates the control. Each choice is its button text and its tooltip.</summary>
        public SegmentedControl(IReadOnlyList<(string label, string tooltip)> choices)
        {
            AddToClassList("blocks-seg");
            for (int i = 0; i < choices.Count; i++)
            {
                int index = i;
                var button = new Button(() => value = index) { text = choices[i].label, tooltip = choices[i].tooltip };
                button.AddToClassList(k_ButtonClass);
                if (i == choices.Count - 1) button.AddToClassList(k_LastClass);
                m_Buttons.Add(button);
                m_Tooltips.Add(choices[i].tooltip);
                Add(button);
            }
        }

        /// <summary>The chosen button's index, or -1 for none. Setting it notifies listeners.</summary>
        public int value
        {
            get => m_Value;
            set
            {
                int clamped = value < 0 || value >= m_Buttons.Count ? -1 : value;
                if (clamped == m_Value) return;
                int previous = m_Value;
                SetValueWithoutNotify(clamped);
                using (ChangeEvent<int> evt = ChangeEvent<int>.GetPooled(previous, clamped))
                {
                    evt.target = this;
                    SendEvent(evt);
                }
            }
        }

        /// <summary>
        /// Greys out one choice, with <paramref name="reason"/> as its tooltip. Enabling it restores its own tooltip.
        /// </summary>
        /// <remarks>A chosen button stays lit while greyed, because the data still holds that value.</remarks>
        public void SetChoiceEnabled(int index, bool enabled, string reason = null)
        {
            if (index < 0 || index >= m_Buttons.Count) return;
            Button button = m_Buttons[index];
            button.SetEnabled(enabled);
            button.tooltip = enabled || string.IsNullOrEmpty(reason) ? m_Tooltips[index] : reason;
        }

        /// <summary>Highlights a button without raising a change event. Used when syncing from data.</summary>
        public void SetValueWithoutNotify(int newValue)
        {
            m_Value = newValue < 0 || newValue >= m_Buttons.Count ? -1 : newValue;
            for (int i = 0; i < m_Buttons.Count; i++)
                m_Buttons[i].EnableInClassList(k_OnClass, i == m_Value);
        }
    }
}
