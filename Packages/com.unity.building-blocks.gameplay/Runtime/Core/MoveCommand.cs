using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Per-tick input for a <see cref="KinematicMover"/>, produced by an
    /// <see cref="IMoveCommandSource"/> and consumed by <see cref="KinematicMover.Simulate"/>.
    /// </summary>
    /// <remarks>
    /// Blittable so it can be recorded and replayed; do not add reference types. Besides the named
    /// inputs it carries <see cref="ButtonCount"/> custom buttons as bit masks, which a processor
    /// reads through an <see cref="InputSlot"/> with <see cref="Pressed(InputSlot)"/> or
    /// <see cref="Held(InputSlot)"/>.
    /// </remarks>
    public struct MoveCommand
    {
        /// <summary>How many custom buttons a command carries: the width of the two masks.</summary>
        public const int ButtonCount = 32;

        /// <summary>Desired movement direction in world space. Magnitude ≤ 1.</summary>
        public Vector3 MoveDirection;

        /// <summary>Desired facing yaw in degrees, world space.</summary>
        public float Yaw;

        /// <summary>Jump was requested this tick.</summary>
        public bool Jump;

        /// <summary>Sprint is held.</summary>
        public bool Sprint;

        /// <summary>Crouch is held.</summary>
        public bool Crouch;

        /// <summary>
        /// Custom buttons that went down since the previous tick, one bit each. A press is
        /// delivered to exactly one tick, the way <see cref="Jump"/> is. Read through
        /// <see cref="Pressed(InputSlot)"/>.
        /// </summary>
        public uint PressedButtons;

        /// <summary>
        /// Custom buttons that are down right now, one bit each. Stays set for as long as the
        /// button is held, the way <see cref="Sprint"/> does. Read through <see cref="Held(InputSlot)"/>.
        /// </summary>
        public uint HeldButtons;

        /// <summary>
        /// Whether the custom button <paramref name="slot"/> is bound to went down since the last
        /// tick. <c>false</c> for a slot the host has not bound (see <see cref="InputSlot.IsBound"/>).
        /// </summary>
        public bool Pressed(InputSlot slot) => slot.IsBound && (PressedButtons & (1u << slot.Bit)) != 0;

        /// <summary>
        /// Whether the custom button <paramref name="slot"/> is bound to is down right now.
        /// <c>false</c> for a slot the host has not bound.
        /// </summary>
        public bool Held(InputSlot slot) => slot.IsBound && (HeldButtons & (1u << slot.Bit)) != 0;

        /// <summary>
        /// Whether custom button <paramref name="bit"/> (0 to 31) went down since the last tick. For
        /// sources, recorders and tests; processors read through an <see cref="InputSlot"/>.
        /// </summary>
        internal bool Pressed(int bit) => IsValidButton(bit) && (PressedButtons & (1u << bit)) != 0;

        /// <summary>Whether custom button <paramref name="bit"/> (0 to 31) is down right now. Internal, like <see cref="Pressed(int)"/>.</summary>
        internal bool Held(int bit) => IsValidButton(bit) && (HeldButtons & (1u << bit)) != 0;

        /// <summary>Whether <paramref name="bit"/> names one of the <see cref="ButtonCount"/> custom buttons.</summary>
        public static bool IsValidButton(int bit) => bit >= 0 && bit < ButtonCount;

        /// <summary>The single-bit mask for custom button <paramref name="bit"/>, or <c>0</c> when it is out of range.</summary>
        public static uint MaskOf(int bit) => IsValidButton(bit) ? 1u << bit : 0u;
    }
}
