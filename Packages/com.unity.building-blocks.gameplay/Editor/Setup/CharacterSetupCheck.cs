using System;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>How badly a <see cref="CharacterSetupCheck"/> wants attention.</summary>
    public enum CheckSeverity
    {
        /// <summary>Nothing to do.</summary>
        Ok,

        /// <summary>The character works, but something will surprise you later.</summary>
        Warning,

        /// <summary>The character will not move correctly until this is addressed.</summary>
        Error,
    }

    /// <summary>
    /// One finding about a character's setup, with an optional one-click repair.
    /// </summary>
    /// <remarks>
    /// Produced by <see cref="CharacterSetupDoctor"/>. Plain data, so it can be shown in a window,
    /// logged or asserted in a test. Covers the character's own wiring, not the scene around it.
    /// </remarks>
    public readonly struct CharacterSetupCheck
    {
        /// <summary>Short label naming what was checked.</summary>
        public string Title { get; }

        /// <summary>One or two sentences on what was found and why it matters.</summary>
        public string Detail { get; }

        /// <summary>How badly this wants attention.</summary>
        public CheckSeverity Severity { get; }

        /// <summary>Button text for <see cref="Fix"/>, or <c>null</c> when there is no repair.</summary>
        public string FixLabel { get; }

        /// <summary>
        /// Applies the repair, or <c>null</c> when there is none. Records <c>Undo</c> for scene changes.
        /// </summary>
        public Action Fix { get; }

        /// <summary>True when this check has a repair that can be applied.</summary>
        public bool HasFix => Fix != null;

        /// <summary>Creates a finding.</summary>
        public CharacterSetupCheck(
            string title,
            string detail,
            CheckSeverity severity,
            string fixLabel = null,
            Action fix = null)
        {
            Title = title;
            Detail = detail;
            Severity = severity;
            FixLabel = fixLabel;
            Fix = fix;
        }

        /// <summary>A passing check.</summary>
        public static CharacterSetupCheck Ok(string title, string detail) =>
            new CharacterSetupCheck(title, detail, CheckSeverity.Ok);

        /// <summary>A check that found something worth knowing about.</summary>
        public static CharacterSetupCheck Warning(
            string title, string detail, string fixLabel = null, Action fix = null) =>
            new CharacterSetupCheck(title, detail, CheckSeverity.Warning, fixLabel, fix);

        /// <summary>A check that found something that breaks the character.</summary>
        public static CharacterSetupCheck Error(
            string title, string detail, string fixLabel = null, Action fix = null) =>
            new CharacterSetupCheck(title, detail, CheckSeverity.Error, fixLabel, fix);
    }
}
