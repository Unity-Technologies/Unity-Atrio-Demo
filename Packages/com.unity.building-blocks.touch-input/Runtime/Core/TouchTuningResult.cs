using System.Collections.Generic;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// What happened when tuning was applied to a rig. Applying never throws; read this instead.
    /// </summary>
    /// <remarks>
    /// <see cref="Parsed"/> is <c>false</c> only when nothing was applied, such as bad JSON or a missing file.
    /// Otherwise, each part that could not be used is one line in <see cref="Problems"/>. Settings the JSON
    /// leaves out keep their values.
    /// </remarks>
    public sealed class TouchTuningResult
    {
        static readonly string[] k_None = new string[0];

        /// <summary>Whether the text was read as tuning JSON. <c>false</c>: nothing on the rig changed.</summary>
        public bool Parsed { get; }

        /// <summary>How many controls on the rig received settings from the JSON.</summary>
        public int ControlsApplied { get; }

        /// <summary>One sentence per skipped or unreadable item, in order. Empty when everything was used.</summary>
        public string[] Problems { get; }

        /// <summary>Shortcut: <see cref="Parsed"/> and no <see cref="Problems"/>.</summary>
        public bool Clean => Parsed && Problems.Length == 0;

        internal TouchTuningResult(bool parsed, int controlsApplied, List<string> problems)
        {
            Parsed = parsed;
            ControlsApplied = controlsApplied;
            Problems = problems == null || problems.Count == 0 ? k_None : problems.ToArray();
        }

        /// <summary>One line for a log: how many controls were tuned and every problem.</summary>
        public override string ToString()
        {
            string head = Parsed ? $"Tuned {ControlsApplied} control(s)" : "Nothing applied";
            return Problems.Length == 0 ? head + "." : head + ": " + string.Join(" ", Problems);
        }
    }
}
