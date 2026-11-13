using UnityEditor;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// <c>Create ▸ Building Blocks ▸ Interaction Step</c>: writes a step script from the package
    /// template, named after the file the user types. Once compiled, the class appears in every
    /// Interactable's "+ Add step" menu.
    /// </summary>
    static class InteractionStepTemplate
    {
        public const string MenuPath = "Assets/Create/Building Blocks/Interaction Step";
        public const string DefaultFileName = "NewInteractionStep.cs";
        const string k_TemplateRelativePath = "ScriptTemplates/InteractionStep.cs.txt";

        /// <summary>Where the template lives, wherever the package is mounted.</summary>
        public static string TemplatePath => $"{BuildingBlocksEditorPaths.EditorFolder}/{k_TemplateRelativePath}";

        [MenuItem(MenuPath, false, 82)]
        static void Create() => ProjectWindowUtil.CreateScriptAssetFromTemplateFile(TemplatePath, DefaultFileName);

        /// <summary>The same as the menu item; the step list's add menu calls it.</summary>
        public static void CreateFromMenu() => Create();
    }
}
