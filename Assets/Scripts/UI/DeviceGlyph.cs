using TMPro;
using Unity.BuildingBlocks.Gameplay;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a screen-space button hint in step with the device the player used last, using the same
/// <see cref="InputPromptStyle"/> rows as world-space input prompts (Keyboard Mouse, Gamepad, Touch, A-Right Gamepad).
/// </summary>
/// <remarks>
/// A row with a glyph sprite shows it in <see cref="_glyph"/>; a row with only glyph text shows that in
/// <see cref="_glyphText"/> instead. A row marked Hide fades this whole hint out through its CanvasGroup,
/// without touching its active state, so interaction steps can still turn it on and off.
/// </remarks>
[DisallowMultipleComponent]
[RequireComponent(typeof(CanvasGroup))]
[AddComponentMenu("UI/Device Glyph")]
public class DeviceGlyph : MonoBehaviour
{
    [SerializeField, Tooltip("Glyph per device. Use one style per button.")]
    private InputPromptStyle _style;

    [SerializeField, Tooltip("Image that shows the row's glyph sprite.")]
    private Image _glyph;

    [SerializeField, Tooltip("Text that shows the row's glyph text when the row has no sprite.")]
    private TMP_Text _glyphText;

    private CanvasGroup _canvasGroup;

    public InputPromptStyle Style
    {
        get => _style;
        set { _style = value; Apply(InputDeviceTracker.Current); }
    }

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
    }

    private void OnEnable()
    {
        InputDeviceTracker.Changed += Apply;
        Apply(InputDeviceTracker.Current);
    }

    private void OnDisable()
    {
        InputDeviceTracker.Changed -= Apply;
    }

    private void Apply(InputDeviceKind device)
    {
        if (_style == null)
            return;

        InputPromptDeviceLook look = _style.For(device);
        if (_canvasGroup != null)
            _canvasGroup.alpha = look.Hide ? 0f : 1f;

        bool hasSprite = look.Glyph != null;
        if (_glyph != null)
        {
            if (hasSprite)
                _glyph.sprite = look.Glyph;
            _glyph.enabled = hasSprite;
        }

        if (_glyphText != null)
        {
            _glyphText.text = hasSprite ? string.Empty : look.GlyphText;
            _glyphText.enabled = !hasSprite;
        }
    }
}
