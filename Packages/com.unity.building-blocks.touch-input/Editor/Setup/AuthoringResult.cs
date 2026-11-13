namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>The outcome of an authoring call: whether it wrote, and a sentence for the inspector.</summary>
    readonly struct AuthoringResult
    {
        public readonly bool Ok;
        public readonly string Message;
        public readonly bool AddedTouchScheme;

        AuthoringResult(bool ok, string message, bool addedScheme)
        {
            Ok = ok;
            Message = message;
            AddedTouchScheme = addedScheme;
        }

        public static AuthoringResult Done(string message = null, bool addedScheme = false) => new AuthoringResult(true, message, addedScheme);
        public static AuthoringResult Refused(string reason) => new AuthoringResult(false, reason, false);
    }
}
