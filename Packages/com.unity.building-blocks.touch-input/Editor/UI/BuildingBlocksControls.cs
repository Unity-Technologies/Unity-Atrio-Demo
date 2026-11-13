// Copied from the gameplay package's Editor/UI/BuildingBlocksControls.cs.
// Copied, not referenced, so this package does not depend on gameplay. Only the parts this package uses.
using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>The tint of a <see cref="Callout"/>.</summary>
    enum CalloutTone
    {
        /// <summary>Neutral, accent-tinted.</summary>
        Default,

        /// <summary>Everything is in order.</summary>
        Success,

        /// <summary>Worth knowing about.</summary>
        Warning,

        /// <summary>Something is broken.</summary>
        Danger,
    }

    /// <summary>A tinted prompt box with a small caps label and body text.</summary>
    sealed class Callout : VisualElement
    {
        /// <summary>Creates a callout.</summary>
        public Callout(string label, string body = null, CalloutTone tone = CalloutTone.Default)
        {
            AddToClassList("blocks-prompt-card");

            switch (tone)
            {
                case CalloutTone.Success: AddToClassList("blocks-prompt-card--success"); break;
                case CalloutTone.Warning: AddToClassList("blocks-prompt-card--warning"); break;
                case CalloutTone.Danger: AddToClassList("blocks-prompt-card--danger"); break;
            }

            if (!string.IsNullOrEmpty(label))
            {
                var labelElement = new Label(label);
                labelElement.AddToClassList("blocks-prompt-card__label");
                Add(labelElement);
            }

            if (!string.IsNullOrEmpty(body)) Add(Body(body));
        }

        /// <summary>A body paragraph styled for a callout.</summary>
        public static Label Body(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-prompt-card__body");
            return label;
        }
    }

    /// <summary>
    /// Renders one <see cref="TouchSetupCheck"/>: a severity glyph, the title and detail, and a button per fix.
    /// A check that offers the action picker shows what <c>pickerFactory</c> builds instead.
    /// </summary>
    sealed class CheckRow : VisualElement
    {
        /// <summary>
        /// Creates a row for <paramref name="check"/>. <paramref name="onFixed"/> runs after a repair, so the owner
        /// can re-run the doctor and rebuild itself.
        /// </summary>
        public CheckRow(TouchSetupCheck check, Action onFixed = null, Func<TouchSetupCheck, VisualElement> pickerFactory = null)
        {
            AddToClassList("blocks-check");

            string glyph;
            switch (check.Severity)
            {
                case CheckSeverity.Error:
                    AddToClassList("blocks-check--error");
                    glyph = "✕";
                    break;
                case CheckSeverity.Warning:
                    AddToClassList("blocks-check--warning");
                    glyph = "!";
                    break;
                case CheckSeverity.Info:
                    AddToClassList("blocks-check--info");
                    glyph = "○";
                    break;
                default:
                    AddToClassList("blocks-check--ok");
                    glyph = "✓";
                    break;
            }

            var icon = new Label(glyph);
            icon.AddToClassList("blocks-check__icon");
            Add(icon);

            var body = new VisualElement();
            body.AddToClassList("blocks-check__body");

            var title = new Label(check.Title);
            title.AddToClassList("blocks-check__title");
            body.Add(title);

            if (!string.IsNullOrEmpty(check.Detail))
            {
                var detail = new Label(check.Detail);
                detail.AddToClassList("blocks-check__detail");
                body.Add(detail);
            }

            Add(body);

            if (check.OffersActionPicker && pickerFactory != null)
            {
                VisualElement picker = pickerFactory(check);
                if (picker != null)
                {
                    picker.AddToClassList("blocks-check__fix");
                    Add(picker);
                }

                return;
            }

            if (!check.HasFix) return;

            // One fix goes beside the text. Several (one per asset, say) stack in a column.
            VisualElement buttons = this;
            if (check.Fixes.Count > 1)
            {
                buttons = new VisualElement();
                buttons.AddToClassList("blocks-check__fixes");
                Add(buttons);
            }

            foreach (TouchFix fix in check.Fixes)
            {
                TouchFix f = fix;
                Button button = Buttons.Primary(f.Label, () =>
                {
                    f.Apply?.Invoke();
                    onFixed?.Invoke();
                });
                button.AddToClassList("blocks-check__fix");
                buttons.Add(button);
            }
        }
    }

    /// <summary>Button factories carrying the Building Blocks button styles.</summary>
    static class Buttons
    {
        /// <summary>An accented button for the main action in a group.</summary>
        public static Button Primary(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-button--primary");
            return button;
        }

        /// <summary>A standard button.</summary>
        public static Button Secondary(string label, Action onClick) =>
            new Button(onClick) { text = label };

        /// <summary>A low-emphasis button for secondary actions.</summary>
        public static Button Quiet(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-button--quiet");
            return button;
        }

        /// <summary>A text link with no chrome, for "show me" actions.</summary>
        public static Button Link(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-button--link");
            return button;
        }
    }

    /// <summary>Label factories for hints.</summary>
    static class Heading
    {
        /// <summary>A muted, wrapping hint line.</summary>
        public static Label Hint(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-muted");
            return label;
        }
    }
}
