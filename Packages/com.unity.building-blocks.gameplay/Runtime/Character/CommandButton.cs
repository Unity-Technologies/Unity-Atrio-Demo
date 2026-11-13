using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// One row of <i>Player Input Source ▸ Custom Buttons</i>: a <see cref="Name"/> a processor
    /// picks, the Button <see cref="Action"/> that fires it, and the <see cref="Bit"/> of
    /// <see cref="MoveCommand.PressedButtons"/> / <see cref="MoveCommand.HeldButtons"/> it owns.
    /// </summary>
    /// <remarks>
    /// The bit is a stable identifier, not the row's position: the source assigns the lowest free bit
    /// once and never renumbers it, so recordings stay valid. Two rows on the same bit log an error and
    /// the later row is ignored. Use a Button action; a Value action reports every change as a press.
    /// </remarks>
    [Serializable]
    public sealed class CommandButton
    {
        [SerializeField]
        [Tooltip("What a processor calls this button: the name it picks in its InputSlot dropdown. " +
                 "Case and surrounding spaces do not matter.")]
        string m_Name = "";

        [SerializeField]
        [Tooltip("A Button action. Drag one in from your .inputactions asset, or untick Use Reference " +
                 "and bind one inline. A Value action reports every change as a press.")]
        InputActionProperty m_Action = new InputActionProperty((InputActionReference)null);

        [SerializeField]
        [Tooltip("Which of the command's 32 custom buttons this row is. Assigned automatically to the " +
                 "lowest free bit and then kept stable, so recordings made with this table stay valid. " +
                 "Two rows on the same bit is an error: the later row is ignored until you change it.")]
        int m_Bit = InputSlot.Unbound;

        /// <summary>An unnamed row with no action; the Inspector's "+" makes these.</summary>
        public CommandButton()
        {
        }

        /// <summary>A row called <paramref name="name"/> with no action yet, in reference mode so it is a drop target.</summary>
        public CommandButton(string name)
        {
            m_Name = name;
        }

        /// <summary>A row called <paramref name="name"/> firing on an inline <paramref name="action"/>.</summary>
        public CommandButton(string name, InputAction action)
        {
            m_Name = name;
            m_Action = new InputActionProperty(action);
        }

        /// <summary>A row called <paramref name="name"/> firing on an action <paramref name="reference"/> from an asset.</summary>
        public CommandButton(string name, InputActionReference reference)
        {
            m_Name = name;
            m_Action = new InputActionProperty(reference);
        }

        /// <summary>The name a processor's <see cref="InputSlot"/> asks for.</summary>
        public string Name
        {
            get => m_Name;
            set => m_Name = value;
        }

        /// <summary>The action in use: the inline one, or the asset's. <c>null</c> for a reference to nothing.</summary>
        public InputAction Action => m_Action.action;

        /// <summary>Whether <see cref="Action"/> resolves to something.</summary>
        public bool HasAction => m_Action.action != null;

        /// <summary>The inline-or-reference field behind <see cref="Action"/>.</summary>
        public InputActionProperty ActionProperty
        {
            get => m_Action;
            set => m_Action = value;
        }

        /// <summary>
        /// The custom button this row owns (0 to 31), or <see cref="InputSlot.Unbound"/> until the
        /// source assigns one. See the class remarks before setting it.
        /// </summary>
        public int Bit
        {
            get => m_Bit;
            set => m_Bit = value;
        }
    }
}
