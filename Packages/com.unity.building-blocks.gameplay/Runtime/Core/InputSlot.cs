using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// A processor's end of a custom button: the button's <see cref="Name"/>, as the command source
    /// calls it, and the <see cref="Bit"/> of <see cref="MoveCommand.PressedButtons"/> /
    /// <see cref="MoveCommand.HeldButtons"/> the host bound that name to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declare one as a serialized field on a <c>MoveProcessor</c> and read it with
    /// <see cref="MoveCommand.Pressed(InputSlot)"/> or <see cref="MoveCommand.Held(InputSlot)"/>:
    /// </para>
    /// <code>
    /// [SerializeField] InputSlot m_Dash = "Dash";
    /// ...
    /// if (command.Pressed(m_Dash)) StartDash();
    /// </code>
    /// <para>
    /// Only the name is serialized. The host binds the bit when it registers the processor, using
    /// the source's <see cref="IMoveCommandLayout"/>. An unbound slot reads as not pressed.
    /// </para>
    /// </remarks>
    [Serializable]
    public struct InputSlot : IEquatable<InputSlot>
    {
        /// <summary>The <see cref="Bit"/> of a slot no host has bound.</summary>
        public const int Unbound = -1;

        [SerializeField]
        [Tooltip("A custom button on the command source, by name (Player Input Source ▸ Custom Buttons).")]
        string m_Name;

        // Stored as bit + 1 so default and freshly deserialized slots are unbound, not button 0.
        [NonSerialized] int m_BitPlusOne;

        /// <summary>A slot asking for the button called <paramref name="name"/>, not yet bound.</summary>
        public InputSlot(string name)
        {
            m_Name = name;
            m_BitPlusOne = 0;
        }

        /// <summary>The button's name on the command source. Case and surrounding spaces are ignored when binding.</summary>
        public string Name => m_Name;

        /// <summary>The custom button this slot is bound to (0 to 31), or <see cref="Unbound"/>.</summary>
        public int Bit => m_BitPlusOne - 1;

        /// <summary>Whether the host has bound this slot to a button.</summary>
        public bool IsBound => m_BitPlusOne > 0;

        /// <summary>
        /// This slot bound to custom button <paramref name="bit"/>; unbound when <paramref name="bit"/>
        /// is not one of the <see cref="MoveCommand.ButtonCount"/> buttons. Hosts call this; processors
        /// do not.
        /// </summary>
        public InputSlot Bound(int bit)
        {
            var slot = this;
            slot.m_BitPlusOne = MoveCommand.IsValidButton(bit) ? bit + 1 : 0;
            return slot;
        }

        /// <summary>A name becomes an unbound slot, so a field can be declared as <c>InputSlot m_Dash = "Dash";</c>.</summary>
        public static implicit operator InputSlot(string name) => new InputSlot(name);

        /// <summary>
        /// Two slots are equal when they ask for the same button name; the bit is not compared.
        /// </summary>
        public bool Equals(InputSlot other) => string.Equals(m_Name, other.m_Name, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is InputSlot other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() => m_Name != null ? m_Name.GetHashCode() : 0;

        /// <summary>The name and, when bound, the button: <c>Dash (button 3)</c>.</summary>
        public override string ToString() => IsBound ? $"{m_Name} (button {Bit})" : $"{m_Name} (unbound)";
    }
}
