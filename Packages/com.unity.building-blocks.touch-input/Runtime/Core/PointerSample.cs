using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>Where a pointer is in its life: it just landed, it moved, it lifted, or the OS took it away.</summary>
    public enum PointerPhase
    {
        /// <summary>The finger (or mouse button) just touched down. A control may take ownership here.</summary>
        Began,

        /// <summary>The finger moved while down.</summary>
        Moved,

        /// <summary>The finger lifted normally.</summary>
        Ended,

        /// <summary>
        /// The OS or the UI took the pointer away, for example on a call or focus loss. Controls return to
        /// rest; a gesture in progress is dropped, not completed.
        /// </summary>
        Canceled,
    }

    /// <summary>
    /// One pointer event, the only input a <see cref="TouchControl"/> receives. A view builds one per
    /// pointer event and passes it to <see cref="TouchControl.Feed"/>; tests build them by hand.
    /// </summary>
    /// <remarks>
    /// Positions are screen pixels with a bottom-left origin, as in uGUI's <c>PointerEventData.position</c>.
    /// <see cref="Time"/> is in seconds on any clock that only goes forward; controls only subtract two
    /// times, so a test can use 0, 0.1, 0.2.
    /// </remarks>
    public readonly struct PointerSample
    {
        /// <summary>
        /// The pointer this sample belongs to. Under uGUI the mouse is <c>-1</c> and counts as a finger.
        /// </summary>
        public readonly int PointerId;

        /// <summary>Screen position in pixels.</summary>
        public readonly Vector2 ScreenPosition;

        /// <summary>What happened to the pointer.</summary>
        public readonly PointerPhase Phase;

        /// <summary>When it happened, in seconds on the view's clock (unscaled time in production).</summary>
        public readonly double Time;

        /// <summary>Builds a sample. See the field docs for units.</summary>
        public PointerSample(int pointerId, Vector2 screenPosition, PointerPhase phase, double time)
        {
            PointerId = pointerId;
            ScreenPosition = screenPosition;
            Phase = phase;
            Time = time;
        }

        /// <summary>Readable form for logs and test failure messages.</summary>
        public override string ToString() => $"#{PointerId} {Phase} {ScreenPosition} @{Time:0.###}s";
    }
}
