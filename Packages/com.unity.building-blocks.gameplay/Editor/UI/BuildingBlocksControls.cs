using System;
using UnityEngine.UIElements;

// Reusable UI Toolkit pieces for the Building Blocks editor windows (Callout, CheckRow, Buttons),
// styled by UI/Styles/BuildingBlocksEditor.uss.
namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>The tint of a <see cref="Callout"/>.</summary>
    public enum CalloutTone
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
    public sealed class Callout : VisualElement
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
    /// Renders one <see cref="CharacterSetupCheck"/>: a severity glyph, the title and detail, and a
    /// Fix button when the check carries a repair.
    /// </summary>
    public sealed class CheckRow : VisualElement
    {
        /// <summary>
        /// Creates a row for <paramref name="check"/>. <paramref name="onFixed"/> runs after a successful repair.
        /// </summary>
        public CheckRow(CharacterSetupCheck check, Action onFixed = null)
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

            if (!check.HasFix) return;

            Button fix = Buttons.Primary(check.FixLabel ?? "Fix", () =>
            {
                check.Fix();
                onFixed?.Invoke();
            });
            fix.AddToClassList("blocks-check__fix");
            Add(fix);
        }
    }

    /// <summary>Button factories carrying the Building Blocks button styles.</summary>
    public static class Buttons
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

        /// <summary>A low-emphasis button for secondary or destructive-adjacent actions.</summary>
        public static Button Quiet(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-button--quiet");
            return button;
        }

        /// <summary>A text link with no chrome, for "open this ↗" actions.</summary>
        public static Button Link(string label, Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("blocks-button--link");
            return button;
        }
    }
}
