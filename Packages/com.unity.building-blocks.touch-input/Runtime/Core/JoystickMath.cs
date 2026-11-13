using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// The math behind <see cref="TouchJoystick"/>, kept apart from the component so it is quick to test.
    /// Takes pixels and returns normalized values.
    /// </summary>
    public static class JoystickMath
    {
        /// <summary>
        /// Turns a finger offset from the joystick origin into a joystick value of magnitude ≤ 1.
        /// </summary>
        /// <param name="delta">Finger position minus origin, in pixels.</param>
        /// <param name="radius">The joystick's radius in pixels. A finger this far out reads as a full deflection.</param>
        /// <param name="deadZone">
        /// Fraction of the radius (0..1) inside which the value is zero, so a resting thumb does not creep.
        /// </param>
        /// <param name="saturation">
        /// Fraction of the radius (0..1) where the value reaches 1, so the player need not reach the rim.
        /// </param>
        /// <remarks>
        /// The length is clamped as a whole, not per axis, so diagonals are no faster than straight lines.
        /// If <paramref name="saturation"/> ≤ <paramref name="deadZone"/>, anything past the dead zone is a full deflection.
        /// </remarks>
        public static Vector2 Normalize(Vector2 delta, float radius, float deadZone, float saturation)
        {
            if (radius <= 0f)
                return Vector2.zero;

            float distance = delta.magnitude;
            if (distance <= 0f)
                return Vector2.zero;

            float fraction = distance / radius;
            if (fraction <= deadZone)
                return Vector2.zero;

            float length = saturation <= deadZone
                ? 1f
                : Mathf.Clamp01((fraction - deadZone) / (saturation - deadZone));

            Vector2 value = delta * (length / distance);
            return Vector2.ClampMagnitude(value, 1f);
        }

        /// <summary>
        /// Pulls <paramref name="origin"/> along when the finger goes past <paramref name="radius"/>, so the
        /// finger sits on the rim. A drifted thumb can then reverse without crossing back over the stick.
        /// </summary>
        /// <returns><c>true</c> if the origin moved.</returns>
        public static bool AdvanceLeash(ref Vector2 origin, Vector2 finger, float radius)
        {
            Vector2 offset = finger - origin;
            float distance = offset.magnitude;

            if (radius < 0f)
                radius = 0f;

            if (distance <= radius)
                return false;

            if (distance <= 0f)
                return false;

            origin = finger - offset * (radius / distance);
            return true;
        }
    }
}
