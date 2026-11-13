using UnityEditor;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// The stable id an inspector keys its remembered UI state by (which cards and sections are open).
    /// </summary>
    static class InspectorState
    {
        static UnityEngine.Object s_LastOwner;
        static string s_LastId;

        /// <summary>
        /// Uses the object's <see cref="GlobalObjectId"/>, which survives Play mode and domain reloads.
        /// Falls back to the instance id when there is no stable id yet (an unsaved scene). Caches the
        /// last answer.
        /// </summary>
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
