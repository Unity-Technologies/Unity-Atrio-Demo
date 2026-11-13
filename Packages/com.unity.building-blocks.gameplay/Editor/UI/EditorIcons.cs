using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Looks up built-in editor icons by name, trying the skin-appropriate variant first and never
    /// logging when one is missing.
    /// </summary>
    static class EditorIcons
    {
        static readonly Dictionary<string, Texture2D> s_Cache = new Dictionary<string, Texture2D>();

        // FindTexture returns the 1x asset on a 2x display; the internal LoadIcon picks @2x and stays
        // silent for a missing name. Reflected once.
        static readonly System.Reflection.MethodInfo s_LoadIcon = typeof(EditorGUIUtility).GetMethod(
            "LoadIcon",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
            null, new[] { typeof(string) }, null);

        /// <summary>
        /// The first icon that resolves, or <c>null</c>. Pass names without the <c>d_</c> prefix; the
        /// dark-skin variant is tried automatically.
        /// </summary>
        public static Texture2D Find(params string[] names)
        {
            bool pro = EditorGUIUtility.isProSkin;

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name)) continue;

                string key = (pro ? "p:" : "l:") + name;
                if (s_Cache.TryGetValue(key, out Texture2D cached))
                {
                    if (cached != null) return cached;
                    continue;
                }

                Texture2D texture = pro
                    ? Load("d_" + name) ?? Load(name)
                    : Load(name) ?? Load("d_" + name);

                s_Cache[key] = texture;
                if (texture != null) return texture;
            }

            return null;
        }

        static Texture2D Load(string name)
        {
            if (s_LoadIcon != null)
            {
                try
                {
                    if (s_LoadIcon.Invoke(null, new object[] { name }) is Texture2D icon) return icon;
                }
                catch (System.Exception)
                {
                    // Fall through to the public lookup.
                }
            }

            return EditorGUIUtility.FindTexture(name);
        }
    }
}
