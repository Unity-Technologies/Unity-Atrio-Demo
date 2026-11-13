using System;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>The kind of device the player used last. An <see cref="InputPrompt"/> picks its glyph from it.</summary>
    public enum InputDeviceKind
    {
        /// <summary>Keyboard and mouse.</summary>
        KeyboardMouse = 0,
        /// <summary>Any gamepad.</summary>
        Gamepad = 1,
        /// <summary>A touchscreen.</summary>
        Touch = 2,
        /// <summary>
        /// A gamepad on a platform with the A-Right layout: A on the east face button, B south, X north, Y west.
        /// </summary>
        ARightGamepad = 3,
    }

    /// <summary>Shows a field in the Inspector under <see cref="Name"/> instead of its nicified field name.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorLabelAttribute : PropertyAttribute
    {
        /// <summary>The label shown in the Inspector.</summary>
        public readonly string Name;

        /// <summary>Shows the field as <paramref name="name"/>.</summary>
        public InspectorLabelAttribute(string name) => Name = name;
    }

    /// <summary>What an <see cref="InputPrompt"/> shows on one kind of device: a glyph image or text, and the label.</summary>
    [Serializable]
    public sealed class InputPromptDeviceLook
    {
        [SerializeField]
        [Tooltip("Glyph image, such as the E key or the A button. Wins over Glyph Text.")]
        Sprite m_Glyph;

        [SerializeField]
        [Tooltip("Text drawn in the glyph box when there is no glyph image, such as \"Tap\". Empty with no image shows no glyph box.")]
        string m_GlyphText;

        [SerializeField]
        [Tooltip("Replaces the prompt's label on this device, such as \"Tap to open\". Empty keeps the prompt's label.")]
        string m_Label;

        [SerializeField]
        [Tooltip("Show nothing on this device, hint dot included.")]
        bool m_Hide;

        /// <summary>Creates a look that shows <paramref name="glyphText"/> in the glyph box.</summary>
        public InputPromptDeviceLook(string glyphText) => m_GlyphText = glyphText;

        /// <summary>Glyph image. <c>null</c> falls back to <see cref="GlyphText"/>.</summary>
        public Sprite Glyph { get => m_Glyph; set => m_Glyph = value; }

        /// <summary>Text in the glyph box when there is no <see cref="Glyph"/>.</summary>
        public string GlyphText { get => m_GlyphText; set => m_GlyphText = value; }

        /// <summary>Label for this device. Empty keeps the prompt's own label.</summary>
        public string Label { get => m_Label; set => m_Label = value; }

        /// <summary>Whether the prompt shows nothing on this device.</summary>
        public bool Hide { get => m_Hide; set => m_Hide = value; }

        /// <summary>Whether anything is set: a glyph, glyph text, a label or Hide.</summary>
        internal bool IsSet => m_Glyph != null || !string.IsNullOrEmpty(m_GlyphText) || !string.IsNullOrEmpty(m_Label) || m_Hide;
    }

    /// <summary>
    /// The look of <see cref="InputPrompt"/>s: one glyph and label per kind of device, plus the shared
    /// backgrounds, colours and sizes. One asset can serve every prompt in a game.
    /// </summary>
    [CreateAssetMenu(menuName = "Building Blocks/Input Prompt Style", fileName = "InputPromptStyle")]
    public sealed class InputPromptStyle : ScriptableObject
    {
        [Header("Per device")]
        [SerializeField]
        InputPromptDeviceLook m_KeyboardMouse = new InputPromptDeviceLook("E");

        [SerializeField]
        InputPromptDeviceLook m_Gamepad = new InputPromptDeviceLook("A");

        [SerializeField]
        InputPromptDeviceLook m_Touch = new InputPromptDeviceLook("Tap");

        [SerializeField]
        [InspectorLabel("A-Right Gamepad")]
        [Tooltip("Used instead of Gamepad on platforms with the A-Right layout (A on the east button). Leave empty to use Gamepad there too.")]
        InputPromptDeviceLook m_ARightGamepad = new InputPromptDeviceLook(string.Empty);

        [Header("Look")]
        [SerializeField]
        [Tooltip("Behind the glyph. Empty draws a plain box.")]
        Sprite m_GlyphBackground;

        [SerializeField]
        [Tooltip("Behind the label. Empty draws a plain box.")]
        Sprite m_LabelBackground;

        [SerializeField]
        [Tooltip("The hint dot shown from far away. Empty draws a soft round dot.")]
        Sprite m_HintDot;

        [SerializeField]
        [Tooltip("Colour of the label, the glyph text and the hint dot.")]
        Color m_TextColor = new Color32(0xFE, 0xDA, 0xAA, 0xFF);

        [SerializeField]
        [Tooltip("Font of the label and glyph text. Empty uses Unity's built-in font.")]
        Font m_Font;

        [SerializeField, Min(1)]
        [Tooltip("Label text size, in reference pixels at 1920x1080.")]
        int m_FontSize = 28;

        [SerializeField, Min(1f)]
        [Tooltip("Width and height of the glyph box, in reference pixels at 1920x1080.")]
        float m_GlyphSize = 56f;

        [SerializeField, Min(1f)]
        [Tooltip("Diameter of the hint dot, in reference pixels at 1920x1080.")]
        float m_HintDotSize = 18f;

        [SerializeField]
        [Tooltip("Where the glyph and label sit relative to the anchor point, in reference pixels. Positive is up and right.")]
        Vector2 m_Offset = new Vector2(14f, 14f);

        /// <summary>The look for <paramref name="device"/>.</summary>
        public InputPromptDeviceLook For(InputDeviceKind device)
        {
            switch (device)
            {
                case InputDeviceKind.Gamepad: return m_Gamepad;
                case InputDeviceKind.Touch: return m_Touch;
                // An empty A-Right look falls back to the gamepad one.
                case InputDeviceKind.ARightGamepad: return m_ARightGamepad != null && m_ARightGamepad.IsSet ? m_ARightGamepad : m_Gamepad;
                default: return m_KeyboardMouse;
            }
        }

        /// <summary>Look on keyboard and mouse.</summary>
        public InputPromptDeviceLook KeyboardMouse => m_KeyboardMouse;

        /// <summary>Look on a gamepad.</summary>
        public InputPromptDeviceLook Gamepad => m_Gamepad;

        /// <summary>Look on a touchscreen.</summary>
        public InputPromptDeviceLook Touch => m_Touch;

        /// <summary>Look for an A-Right gamepad. <see cref="For"/> falls back to <see cref="Gamepad"/> while it is empty.</summary>
        public InputPromptDeviceLook ARightGamepad => m_ARightGamepad;

        /// <summary>Behind the glyph, or <c>null</c> for a plain box.</summary>
        public Sprite GlyphBackground { get => m_GlyphBackground; set => m_GlyphBackground = value; }

        /// <summary>Behind the label, or <c>null</c> for a plain box.</summary>
        public Sprite LabelBackground { get => m_LabelBackground; set => m_LabelBackground = value; }

        /// <summary>The hint dot, or <c>null</c> for a soft round dot.</summary>
        public Sprite HintDot { get => m_HintDot; set => m_HintDot = value; }

        /// <summary>Colour of text and the hint dot.</summary>
        public Color TextColor { get => m_TextColor; set => m_TextColor = value; }

        /// <summary>Font of the label and glyph text, or <c>null</c> for the built-in font.</summary>
        public Font Font { get => m_Font; set => m_Font = value; }

        /// <summary>Label text size in reference pixels.</summary>
        public int FontSize { get => m_FontSize; set => m_FontSize = Mathf.Max(1, value); }

        /// <summary>Glyph box size in reference pixels.</summary>
        public float GlyphSize { get => m_GlyphSize; set => m_GlyphSize = Mathf.Max(1f, value); }

        /// <summary>Hint dot diameter in reference pixels.</summary>
        public float HintDotSize { get => m_HintDotSize; set => m_HintDotSize = Mathf.Max(1f, value); }

        /// <summary>Offset of the glyph and label from the anchor point, in reference pixels.</summary>
        public Vector2 Offset { get => m_Offset; set => m_Offset = value; }
    }
}
