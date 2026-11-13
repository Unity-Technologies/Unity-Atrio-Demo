// Copied from the gameplay package's Editor/UI/InspectorState.cs.
// Copied, not referenced, so this package does not depend on gameplay. Keep the blocks- class names;
// a shared UI package may replace both copies.
using UnityEditor;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// The stable id an inspector keys its remembered UI state by (which cards and sections are
    /// open). One place for the rule, so every card and section of one object agrees.
    /// </summary>
    static class InspectorState
    {
        static UnityEngine.Object s_LastOwner;
        static string s_LastId;

        /// <summary>
        /// The object's <see cref="GlobalObjectId"/>, or its instance id if it has none yet (an unsaved scene).
        /// </summary>
        /// <remarks>
        /// Instance ids change after Play mode and domain reloads, so a card keyed by one could inherit another
        /// object's state. The last answer is cached because one inspector asks several times while it builds.
        /// </remarks>
        public static string OwnerId(UnityEngine.Object owner)
        {
            if (owner == null) return "0";
            if (owner == s_LastOwner && s_LastId != null) return s_LastId;

            string id;
            GlobalObjectId global = GlobalObjectId.GetGlobalObjectIdSlow(owner);
            if (global.targetObjectId != 0)
            {
                id = global.ToString();
            }
            else
            {
#if UNITY_6000_4_OR_NEWER
                id = owner.GetEntityId().ToString();
#else
                id = owner.GetInstanceID().ToString();
#endif
            }

            s_LastOwner = owner;
            s_LastId = id;
            return id;
        }
    }
}
