using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Building pieces for the sentence-style cards: labelled rows, muted words, the closing summary
    /// bar and the warning line. Styled by <c>trigger-volume.uss</c>.
    /// </summary>
    static class CardRows
    {
        /// <summary>A label on the fixed column and the controls after it: "Detects | Tag".</summary>
        public static VisualElement Row(string label, params VisualElement[] controls)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-tv-field");
            var name = new Label(label);
            name.AddToClassList("blocks-tv-field__label");
            row.Add(name);
            var control = new VisualElement();
            control.AddToClassList("blocks-tv-field__control");
            foreach (VisualElement c in controls) control.Add(c);
            row.Add(control);
            return row;
        }

        /// <summary>The small muted words beside a control.</summary>
        public static Label Sub(string text)
        {
            var label = new Label(text);
            label.AddToClassList("blocks-tv-sub");
            return label;
        }

        /// <summary>The tinted bar closing a card: the sentence its settings add up to.</summary>
        public static Label SumBar()
        {
            var label = new Label();
            label.AddToClassList("blocks-tv-sumbar");
            return label;
        }

        /// <summary>A warning line: "!" then the sentence.</summary>
        public static VisualElement Warn(string text)
        {
            var warn = new VisualElement();
            warn.AddToClassList("blocks-tv-warn");
            var icon = new Label("!");
            icon.AddToClassList("blocks-tv-warn__icon");
            warn.Add(icon);
            var body = new Label(text);
            body.AddToClassList("blocks-tv-warn__text");
            warn.Add(body);
            return warn;
        }

        /// <summary>A segmented control over <paramref name="choices"/>, in the order given.</summary>
        public static SegmentedControl Segmented<T>((T mode, string label, string tooltip)[] choices, string tooltip)
        {
            var options = new List<(string, string)>(choices.Length);
            foreach ((T _, string label, string tip) in choices) options.Add((label, tip));
            return new SegmentedControl(options) { tooltip = tooltip };
        }

        /// <summary>A scene name inside rich text: shown as typed, never read as a tag.</summary>
        public static string Rich(string text) => $"<noparse>{text}</noparse>";

        /// <summary>Rich text back to plain words, for a tooltip: the tags stripped, <see cref="Rich"/> unwrapped.</summary>
        public static string Plain(string rich) =>
            string.IsNullOrEmpty(rich) ? rich : System.Text.RegularExpressions.Regex.Replace(rich, "<[^>]+>", string.Empty);
    }
}
