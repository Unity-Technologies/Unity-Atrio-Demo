using UnityEngine;
using UnityEngine.UI;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Draws a <see cref="TouchButton"/> by tinting and scaling an <see cref="Image"/>. The image also receives
    /// the touches.
    /// </summary>
    [AddComponentMenu("Building Blocks/Touch Input/Touch Button View")]
    public sealed class TouchButtonView : TouchControlView
    {
        /// <summary>Default <see cref="NormalColor"/>: translucent white.</summary>
        public static readonly Color k_DefaultNormalColor = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>Default <see cref="PressedColor"/>: solid white.</summary>
        public static readonly Color k_DefaultPressedColor = new Color(1f, 1f, 1f, 0.95f);

        /// <summary>Default <see cref="PressedScale"/>: the button shrinks a little under the finger.</summary>
        public const float k_DefaultPressedScale = 0.92f;

        [SerializeField]
        [Tooltip("The image to tint, or empty to use the Image on this GameObject.")]
        Image m_Target;

        [SerializeField]
        [Tooltip("Tint while no finger is on the button.")]
        Color m_NormalColor = k_DefaultNormalColor;

        [SerializeField]
        [Tooltip("Tint while a finger is on the button.")]
        Color m_PressedColor = k_DefaultPressedColor;

        [SerializeField]
        [Range(0.5f, 1.2f)]
        [Tooltip("Image scale while pressed, where 1 turns the effect off.")]
        float m_PressedScale = k_DefaultPressedScale;

        /// <summary>The tinted image. Falls back to the <see cref="Image"/> on this GameObject.</summary>
        public Image Target
        {
            get => m_Target != null ? m_Target : m_Target = GetComponent<Image>();
            set => m_Target = value;
        }

        /// <summary>Tint at rest.</summary>
        public Color NormalColor
        {
            get => m_NormalColor;
            set => m_NormalColor = value;
        }

        /// <summary>Tint while pressed.</summary>
        public Color PressedColor
        {
            get => m_PressedColor;
            set => m_PressedColor = value;
        }

        /// <summary>Image scale while pressed. Default 0.92; 1 turns the effect off.</summary>
        public float PressedScale
        {
            get => m_PressedScale;
            set => m_PressedScale = Mathf.Clamp(value, 0.5f, 1.2f);
        }

        /// <summary>The button this view draws, or <c>null</c> if the sibling control is not a button.</summary>
        public TouchButton Button => Control as TouchButton;

        /// <summary>The image's scale for a pressed or released button.</summary>
        public static float ScaleFor(bool pressed, float pressedScale) => pressed ? pressedScale : 1f;

        /// <inheritdoc/>
        protected override void Draw()
        {
            Image target = Target;
            if (target == null)
                return;

            bool pressed = Control.IsPressed;
            target.color = pressed ? m_PressedColor : m_NormalColor;
            float scale = ScaleFor(pressed, m_PressedScale);
            target.rectTransform.localScale = new Vector3(scale, scale, 1f);
        }

        /// <inheritdoc/>
        protected override void OnDeactivated()
        {
            Image target = Target;
            if (target != null)
                target.rectTransform.localScale = Vector3.one;
        }
    }
}
