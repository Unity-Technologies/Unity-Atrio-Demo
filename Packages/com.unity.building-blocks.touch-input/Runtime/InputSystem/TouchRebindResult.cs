using System.Collections.Generic;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// What happened when <see cref="TouchRebinding"/> changed or loaded binding overrides. Those methods do not
    /// throw for expected failures; read this instead.
    /// </summary>
    /// <remarks>
    /// <see cref="Succeeded"/> is <c>false</c> only when nothing changed. A success can still list warnings in
    /// <see cref="Problems"/>, such as an override for a binding the asset no longer has.
    /// </remarks>
    public sealed class TouchRebindResult
    {
        static readonly string[] k_None = new string[0];

        /// <summary>Whether the change was made. <c>false</c>: no binding changed.</summary>
        public bool Succeeded { get; }

        /// <summary>How many bindings received (or lost) an override.</summary>
        public int BindingsChanged { get; }

        /// <summary>One sentence per failed or skipped item, in order. Empty when all went well.</summary>
        public string[] Problems { get; }

        /// <summary>Shortcut: <see cref="Succeeded"/> and no <see cref="Problems"/>.</summary>
        public bool Clean => Succeeded && Problems.Length == 0;

        internal TouchRebindResult(bool succeeded, int bindingsChanged, List<string> problems)
        {
            Succeeded = succeeded;
            BindingsChanged = bindingsChanged;
            Problems = problems == null || problems.Count == 0 ? k_None : problems.ToArray();
        }

        /// <summary>One line for a log: how many bindings changed and every problem.</summary>
        public override string ToString()
        {
            string head = Succeeded ? $"Changed {BindingsChanged} binding(s)" : "Nothing changed";
            return Problems.Length == 0 ? head + "." : head + ": " + string.Join(" ", Problems);
        }
    }
}
