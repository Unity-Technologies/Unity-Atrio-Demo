using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// The default skin for new controls: four white sprites in <c>Runtime/UGUI/Skin/</c>, loaded by GUID.
    /// </summary>
    /// <remarks>If a sprite is missing, a built-in UI sprite is used so a control is never invisible.</remarks>
    static class TouchSkin
    {
        public const string JoystickRingFile = "JoystickRing.png";
        public const string JoystickKnobFile = "JoystickKnob.png";
        public const string ButtonDiscFile = "ButtonDisc.png";
        public const string ButtonRingFile = "ButtonRing.png";

        // The .meta GUIDs of the checked-in sprites. Regenerating keeps them (the .meta files are not rewritten).
        const string k_JoystickRingGuid = "418268b6a63c2490cad7c5819c506dc7";
        const string k_JoystickKnobGuid = "80835639e8d9e41b79196eaecc870177";
        const string k_ButtonDiscGuid = "0d024e3ce2e4148d7b6475d07f638b85";
        const string k_ButtonRingGuid = "122aed4c7d2a5406087b42c472732683";

        const string k_BuiltinKnob = "UI/Skin/Knob.psd";

        public static Sprite JoystickRing => Load(k_JoystickRingGuid, JoystickRingFile);
        public static Sprite JoystickKnob => Load(k_JoystickKnobGuid, JoystickKnobFile);
        public static Sprite ButtonDisc => Load(k_ButtonDiscGuid, ButtonDiscFile);
        public static Sprite ButtonRing => Load(k_ButtonRingGuid, ButtonRingFile);

        /// <summary>Whether <paramref name="sprite"/> is one of the default skin's.</summary>
        public static bool IsDefault(Sprite sprite)
        {
            if (sprite == null)
                return false;

            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(sprite));
            return guid == k_JoystickRingGuid || guid == k_JoystickKnobGuid || guid == k_ButtonDiscGuid || guid == k_ButtonRingGuid;
        }

        static Sprite Load(string guid, string file)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Sprite sprite = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Sprite>(path);
            return sprite != null ? sprite : AssetDatabase.GetBuiltinExtraResource<Sprite>(k_BuiltinKnob);
        }
    }
}
