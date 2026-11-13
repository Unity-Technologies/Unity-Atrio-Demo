using UnityEngine;

/// <summary>
/// Raises the render frame rate cap to 60 on startup. Android and other mobile platforms otherwise
/// default to 30. Only applies while VSync is off in the active Quality level.
/// </summary>
[DefaultExecutionOrder(-10000)]
[DisallowMultipleComponent]
public class SetTargetFPSTo60 : MonoBehaviour
{
    private const int TargetFrameRate = 60;

    private void Awake()
    {
        Application.targetFrameRate = TargetFrameRate;
    }
}
