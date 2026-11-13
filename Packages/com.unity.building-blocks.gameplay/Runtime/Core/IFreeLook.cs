namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// A view the player can keep turning while their control is locked, so an interaction can stop
    /// walking and leave looking free.
    /// </summary>
    /// <remarks>
    /// While <see cref="IControlLock"/> is held and <see cref="LookWhileLocked"/> is off, the view follows
    /// the body. With it on, look input turns the view and the body keeps its facing. The shipped
    /// implementation is <c>FirstPersonCameraRig</c>.
    /// </remarks>
    public interface IFreeLook
    {
        /// <summary>
        /// Whether look input still turns the view while control is locked. Default <c>false</c>: a
        /// locked view follows the body.
        /// </summary>
        bool LookWhileLocked { get; set; }

        /// <summary>Which way the view faces, in degrees of world yaw.</summary>
        float Yaw { get; }
    }
}
