using UnityEditor.PackageManager;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Paths to the package's playground content (player prefab, input actions, material) under
    /// <c>Playground/</c>. Resolved against the installed package, or the project if it was copied in.
    /// </summary>
    static class PlaygroundPaths
    {
        /// <summary>The package name, and the fallback asset root when it is not installed as a package.</summary>
        public const string PackageName = "com.unity.building-blocks.gameplay";

        /// <summary>The player prefab's name.</summary>
        public const string PlayerPrefabName = "FirstPersonPlayer";

        /// <summary>The material every grey-box surface in the playground wears.</summary>
        public const string MaterialName = "Playground";

        /// <summary>The package's asset root, e.g. <c>Packages/com.unity.building-blocks.gameplay</c>.</summary>
        public static string PackageAssetRoot
        {
            get
            {
                PackageInfo info = PackageInfo.FindForAssembly(typeof(PlaygroundPaths).Assembly);
                return info != null ? info.assetPath : "Packages/" + PackageName;
            }
        }

        /// <summary>The package's folder on disk, or <c>null</c> when it is not installed as a package.</summary>
        public static string PackageDiskRoot => PackageInfo.FindForAssembly(typeof(PlaygroundPaths).Assembly)?.resolvedPath;

        /// <summary>The Playground folder.</summary>
        public static string Folder => PackageAssetRoot + "/Playground";

        /// <summary>The player prefab.</summary>
        public static string PlayerPrefab => Folder + "/Prefabs/" + PlayerPrefabName + ".prefab";

        /// <summary>
        /// The package's input actions asset: referenced by the player prefab, and copied into the
        /// project by <see cref="BuildingBlocksInputAsset"/>.
        /// </summary>
        public static string InputActions => Folder + "/Input/FirstPersonControls.inputactions";

        /// <summary>The project folder Building Blocks owns for its copies of package content.</summary>
        public const string ProjectFolder = "Assets/BuildingBlocks";

        /// <summary>Where the project's editable copy of <see cref="InputActions"/> lives.</summary>
        public const string ProjectInputActions = ProjectFolder + "/Input/FirstPersonControls.inputactions";

        /// <summary>The playground's grey-box material (URP Lit).</summary>
        public static string Material => Folder + "/Materials/" + MaterialName + ".mat";
    }
}
