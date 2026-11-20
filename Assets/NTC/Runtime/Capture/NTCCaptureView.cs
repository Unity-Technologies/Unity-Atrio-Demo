using System.Collections.Generic;
using UnityEngine;

namespace NTC.Capture
{
    /// <summary>
    /// Picks the points of view <see cref="NTCCapture"/> renders: the Tools/NTC capture menus and the
    /// hotkey capture from each of <see cref="pointsOfView"/> in turn, enabling it for its capture and
    /// disabling the main camera. Without one in the scene, the capture falls back to the camera tagged
    /// <see cref="NTCCapture.ntcCameraTag"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NTCCaptureView : MonoBehaviour
    {
        [Tooltip("Cameras to capture from, in order, each into its own folder. Usually the PoV cameras under this object, kept disabled until a capture needs them.")]
        public List<Camera> pointsOfView = new List<Camera>();
    }
}
