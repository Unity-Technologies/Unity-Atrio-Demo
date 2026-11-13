using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Shows the on-screen touch controls on phones and tablets by toggling the Canvas on this GameObject.
/// The controls show at the start, hide once the screen goes untouched for a while, and come back on the next touch.
/// On other platforms they stay hidden. In the Editor, <see cref="_showInEditor"/> decides, and mouse clicks count as touches.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Canvas))]
public class TouchControlsVisibility : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Seconds without a touch before the controls hide. A finger held down keeps them shown.")]
    private float _hideAfterSeconds = 10f;

    [SerializeField, Tooltip("Editor only: show the touch controls in Play mode. Can be changed while playing. Mouse clicks count as touches.")]
    private bool _showInEditor;

    private Canvas _canvas;
    private float _lastTouchTime;
    private bool _wasAllowed;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
    }

    private void OnEnable()
    {
        _wasAllowed = IsAllowed;
        _lastTouchTime = Time.unscaledTime;
        _canvas.enabled = _wasAllowed;
    }

    private void Update()
    {
        bool allowed = IsAllowed;
        if (!allowed)
        {
            _wasAllowed = false;
            SetShown(false);
            return;
        }

        // Just switched on in the Editor: show straight away, as at the start.
        if (!_wasAllowed)
        {
            _wasAllowed = true;
            _lastTouchTime = Time.unscaledTime;
        }

        if (IsTouching())
            _lastTouchTime = Time.unscaledTime;

        SetShown(Time.unscaledTime - _lastTouchTime < _hideAfterSeconds);
    }

    private void SetShown(bool shown)
    {
        if (_canvas.enabled != shown)
            _canvas.enabled = shown;
    }

    /// <summary>Whether this device should use touch controls at all.</summary>
    private bool IsAllowed
    {
        get
        {
#if UNITY_EDITOR
            return _showInEditor;
#else
            // True on iOS, Android and Web in a phone or tablet browser.
            return Application.isMobilePlatform;
#endif
        }
    }

    /// <summary>Whether any finger (or, in the Editor, the left mouse button) is on the screen this frame.</summary>
    private static bool IsTouching()
    {
        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null)
        {
            var touches = touchscreen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                if (touches[i].press.isPressed)
                    return true;
            }
        }

#if UNITY_EDITOR
        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
            return true;
#endif

        return false;
    }
}
