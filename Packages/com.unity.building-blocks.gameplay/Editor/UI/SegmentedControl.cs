using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// A row of buttons with one on, for a setting with two or three short answers.
    /// <see cref="value"/> is the chosen index, or -1 for none (a mixed selection). Fires
    /// <see cref="ChangeEvent{T}"/> of <c>int</c>.
    /// </summary>
    /// <remarks>
    /// Not bound to a property: the caller maps the index to its value, so there is no prefab-override
    /// bar on the row.
    /// </remarks>
    public sealed class SegmentedControl : VisualElement, INotifyValueChanged<int>
    {
        const string k_ButtonClass = "blocks-seg__button";
        const string k_OnClass = "blocks-seg__button--on";
        // USS has no :last-child, so the last button is marked by class.
        const string k_LastClass = "blocks-seg__button--last";

        readonly List<Button> m_Buttons = new List<Button>();
        readonly List<string> m_Tooltips = new List<string>();
        int m_Value = -1;

        /// <summary>Creates the control. Each choice is its button text and the tooltip explaining it.</summary>
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
        /// Greys one choice out with <paramref name="reason"/> as its tooltip; enabling it restores the
        /// original tooltip. A chosen button stays lit while greyed.
        /// </summary>
        public void SetChoiceEnabled(int index, bool enabled, string reason = null)
        {
            if (index < 0 || index >= m_Buttons.Count) return;
            Button button = m_Buttons[index];
            button.SetEnabled(enabled);
            button.tooltip = enabled || string.IsNullOrEmpty(reason) ? m_Tooltips[index] : reason;
        }

        /// <summary>Highlights a button without raising a change event: syncing from data.</summary>
        public void SetValueWithoutNotify(int newValue)
        {
            m_Value = newValue < 0 || newValue >= m_Buttons.Count ? -1 : newValue;
            for (int i = 0; i < m_Buttons.Count; i++)
                m_Buttons[i].EnableInClassList(k_OnClass, i == m_Value);
        }
    }
}
