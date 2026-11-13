using System;
using System.Collections.Generic;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>How much a <see cref="TouchSetupCheck"/> needs attention.</summary>
    enum CheckSeverity
    {
        /// <summary>Nothing to do.</summary>
        Ok,

        /// <summary>Not a problem, just worth knowing.</summary>
        Info,

        /// <summary>The controls work, but something will surprise you on a phone.</summary>
        Warning,

        /// <summary>A control will not work until this is addressed.</summary>
        Error,
    }

    /// <summary>One button on a finding: its label says exactly what it changes.</summary>
    readonly struct TouchFix
    {
        public readonly string Label;
        public readonly Action Apply;

        public TouchFix(string label, Action apply)
        {
            Label = label;
            Apply = apply;
        }
    }

    /// <summary>
    /// One finding about a touch rig's setup, with the fixes that repair it. Plain data, made by
    /// <see cref="TouchRigDoctor"/>.
    /// </summary>
    readonly struct TouchSetupCheck
    {
        /// <summary>A stable key for the kind of finding, one of <see cref="TouchRigDoctor"/>'s <c>Id</c> constants.</summary>
        public readonly string Id;

        /// <summary>One line naming what was found.</summary>
        public readonly string Title;

        /// <summary>One or two sentences on why it matters.</summary>
        public readonly string Detail;

        public readonly CheckSeverity Severity;

        /// <summary>The control the finding is about, or <c>null</c> for the rig or scene.</summary>
        public readonly TouchControl Subject;

        /// <summary>Repairs, each labelled with what it changes. Empty when a human has to decide.</summary>
        public readonly IReadOnlyList<TouchFix> Fixes;

        /// <summary>
        /// Whether the repair is picking one of the asset's actions. The inspector then shows an action picker for
        /// <see cref="Subject"/> instead of a fix button, because no action is ever picked automatically.
        /// </summary>
        public readonly bool OffersActionPicker;

        public TouchSetupCheck(string id, string title, string detail, CheckSeverity severity,
            TouchControl subject = null, IReadOnlyList<TouchFix> fixes = null, bool offersActionPicker = false)
        {
            Id = id;
            Title = title;
            Detail = detail;
            Severity = severity;
            Subject = subject;
            Fixes = fixes ?? Array.Empty<TouchFix>();
            OffersActionPicker = offersActionPicker;
        }

        /// <summary>A finding with one fix.</summary>
        public static TouchSetupCheck With(string id, string title, string detail, CheckSeverity severity,
            TouchControl subject, string fixLabel, Action fix) =>
            new TouchSetupCheck(id, title, detail, severity, subject, new[] { new TouchFix(fixLabel, fix) });

        public bool HasFix => Fixes.Count > 0;

        /// <summary>The first fix's label, or <c>null</c>.</summary>
        public string FixLabel => Fixes.Count > 0 ? Fixes[0].Label : null;

        /// <summary>Applies the first fix. Does nothing when there is none.</summary>
        public void Fix()
        {
            if (Fixes.Count > 0)
                Fixes[0].Apply?.Invoke();
        }
    }
}
