using System.IO;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Resolves this assembly's folder so editor assets (stylesheets) can be loaded by path,
    /// wherever the package is mounted.
    /// </summary>
    /// <remarks>
    /// Locates the editor assembly definition, so it works for registry, git, <c>file:</c> and
    /// embedded installs. Cached; falls back to the standard <c>Packages/</c> path.
    /// </remarks>
    static class BuildingBlocksEditorPaths
    {
        const string k_AsmdefName = "Unity.BuildingBlocks.Gameplay.Editor.asmdef";
        const string k_Fallback = "Packages/com.unity.building-blocks.gameplay/Editor";

        static string s_EditorFolder;

        public static string EditorFolder
        {
            get
            {
                if (!string.IsNullOrEmpty(s_EditorFolder)) return s_EditorFolder;

                foreach (string guid in AssetDatabase.FindAssets(
                             "Unity.BuildingBlocks.Gameplay.Editor t:asmdef"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (string.IsNullOrEmpty(path)) continue;
                    if (Path.GetFileName(path) != k_AsmdefName) continue;

                    s_EditorFolder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                    if (!string.IsNullOrEmpty(s_EditorFolder)) return s_EditorFolder;
                }

                s_EditorFolder = k_Fallback;
                return s_EditorFolder;
            }
        }
    }

    /// <summary>
    /// Attaches the Building Blocks stylesheets to a UI Toolkit root, and keeps them following the
    /// editor's light/dark skin.
    /// </summary>
    /// <remarks>
    /// <c>core.uss</c> holds the dark tokens; <c>core-light.uss</c> overrides them in the light skin.
    /// Unity has no skin-changed event, so themed roots poll until their panel closes.
    /// </remarks>
    public static class BuildingBlocksStyles
    {
        const long k_ThemePollMs = 500;

        /// <summary>Applies the shared editor stylesheet to <paramref name="root"/>.</summary>
        public static void Apply(VisualElement root)
        {
            if (root == null) return;

            AddSheet(root, "UI/Styles/BuildingBlocksEditor.uss");
            ApplyThemeSwitching(root);
        }

        static void ApplyThemeSwitching(VisualElement root)
        {
            StyleSheet lightSheet = LoadSheet("UI/Styles/core-light.uss");
            if (lightSheet == null) return;

            SyncLightSheet(root, lightSheet);
            root.schedule.Execute(() => SyncLightSheet(root, lightSheet)).Every(k_ThemePollMs);
        }

        // Add/remove is idempotent, so this is safe to call on every poll.
        static void SyncLightSheet(VisualElement root, StyleSheet lightSheet)
        {
            bool wantLight = !EditorGUIUtility.isProSkin;
            bool hasLight = root.styleSheets.Contains(lightSheet);

            if (wantLight && !hasLight) root.styleSheets.Add(lightSheet);
            else if (!wantLight && hasLight) root.styleSheets.Remove(lightSheet);
        }

        static StyleSheet LoadSheet(string relativePath) =>
            AssetDatabase.LoadAssetAtPath<StyleSheet>(
                $"{BuildingBlocksEditorPaths.EditorFolder}/{relativePath}");

        static void AddSheet(VisualElement root, string relativePath)
        {
            StyleSheet sheet = LoadSheet(relativePath);
            if (sheet != null) root.styleSheets.Add(sheet);
        }
    }
}
