using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// GameObject ▸ Building Blocks (menu bar, Hierarchy and Scene view): the character, each
    /// interaction piece, and a wired Interactable + Trigger Volume pair. Creation goes through
    /// <see cref="BlockSpawner"/>.
    /// </summary>
    /// <remarks>
    /// The right-clicked (else selected) object is the context: new pieces land under it, and the pair
    /// item adds whichever half is missing. Component menus in the Inspector offer the same actions
    /// for existing objects (<i>Make Interactable</i>, <i>Make Trigger Volume</i>, <i>Add ..., connected</i>).
    /// </remarks>
    static class BuildingBlocksMenu
    {
        public const string Root = "GameObject/Building Blocks/";
        public const string CharacterPath = Root + "First Person Character";
        public const string TriggerVolumePath = Root + "Trigger Volume";
        public const string TriggerVolumeGroupPath = Root + "Trigger Volume Group";
        public const string InteractablePath = Root + "Interactable";
        public const string InteractionPath = Root + "Interactable + Trigger Volume";

        public const string MakeInteractablePath = "CONTEXT/Transform/Make Interactable";
        public const string MakeTriggerVolumePath = "CONTEXT/Transform/Make Trigger Volume";
        public const string AddTriggerVolumePath = "CONTEXT/Interactable/Add Trigger Volume, connected";
        public const string AddInteractablePath = "CONTEXT/TriggerVolume/Add Interactable, connected";

        // Priorities more than ten apart draw a separator: the character, the pieces, the pair.
        const int k_CharacterPriority = 10;
        const int k_PiecesPriority = 30;
        const int k_PairPriority = 50;

        // After Unity's own entries on a component menu.
        const int k_ComponentMenuPriority = 1000;

        [MenuItem(CharacterPath, false, k_CharacterPriority)]
        static void Character(MenuCommand command)
        {
            // With several objects selected the Hierarchy invokes this once per object; one player is enough.
            if (command.context != null && command.context != Selection.activeGameObject) return;

            GameObject go = BlockSpawner.AddCharacter(out string message);
            if (go == null) Debug.LogWarning(message);
        }

        [MenuItem(TriggerVolumePath, false, k_PiecesPriority)]
        static void TriggerVolume(MenuCommand command) => BlockSpawner.CreateTriggerVolume(Context(command));

        [MenuItem(TriggerVolumeGroupPath, false, k_PiecesPriority + 1)]
        static void TriggerVolumeGroup(MenuCommand command) => BlockSpawner.CreateTriggerVolumeGroup(Context(command));

        [MenuItem(InteractablePath, false, k_PiecesPriority + 2)]
        static void Interactable(MenuCommand command) => BlockSpawner.CreateInteractable(Context(command));

        [MenuItem(InteractionPath, false, k_PairPriority)]
        static void Interaction(MenuCommand command) => BlockSpawner.CreateInteraction(Context(command));

        [MenuItem(CharacterPath, true)]
        static bool CanAddCharacter() => HasOpenScene();

        [MenuItem(TriggerVolumePath, true)]
        static bool CanAddTriggerVolume() => HasOpenScene();

        [MenuItem(TriggerVolumeGroupPath, true)]
        static bool CanAddTriggerVolumeGroup() => HasOpenScene();

        [MenuItem(InteractablePath, true)]
        static bool CanAddInteractable() => HasOpenScene();

        [MenuItem(InteractionPath, true)]
        static bool CanAddInteraction() => HasOpenScene();

        [MenuItem(MakeInteractablePath, false, k_ComponentMenuPriority)]
        static void MakeInteractable(MenuCommand command)
        {
            GameObject go = OwnerOf(command);
            if (go != null) BlockSpawner.MakeInteractable(go, out _);
        }

        [MenuItem(MakeInteractablePath, true)]
        static bool CanMakeInteractable(MenuCommand command)
        {
            GameObject go = OwnerOf(command);
            return go != null && go.GetComponent<Interactable>() == null;
        }

        [MenuItem(MakeTriggerVolumePath, false, k_ComponentMenuPriority + 1)]
        static void MakeTriggerVolume(MenuCommand command)
        {
            GameObject go = OwnerOf(command);
            if (go == null) return;

            BlockSpawner.ConvertToTriggerVolume(go, out SpawnNote note);
            if (note.ChangedExisting) Debug.Log(note.Message, go);
        }

        [MenuItem(MakeTriggerVolumePath, true)]
        static bool CanMakeTriggerVolume(MenuCommand command)
        {
            GameObject go = OwnerOf(command);
            return go != null && go.GetComponent<TriggerVolume>() == null;
        }

        [MenuItem(AddTriggerVolumePath, false, k_ComponentMenuPriority)]
        static void AddTriggerVolumeBeside(MenuCommand command)
        {
            if (command.context is Interactable interactable && OwnerOf(command) != null)
                BlockSpawner.AddTriggerVolumeTo(interactable);
        }

        [MenuItem(AddTriggerVolumePath, true)]
        static bool CanAddTriggerVolumeBeside(MenuCommand command) => command.context is Interactable && OwnerOf(command) != null;

        [MenuItem(AddInteractablePath, false, k_ComponentMenuPriority)]
        static void AddInteractableBeside(MenuCommand command)
        {
            if (command.context is TriggerVolume volume && OwnerOf(command) != null)
                BlockSpawner.AddInteractableTo(volume);
        }

        [MenuItem(AddInteractablePath, true)]
        static bool CanAddInteractableBeside(MenuCommand command) => command.context is TriggerVolume && OwnerOf(command) != null;

        /// <summary>
        /// The scene object the clicked component sits on, or <c>null</c> for a prefab asset outside a
        /// prefab stage.
        /// </summary>
        static GameObject OwnerOf(MenuCommand command)
        {
            var component = command?.context as Component;
            if (component == null || EditorUtility.IsPersistent(component)) return null;
            return component.gameObject;
        }

        /// <summary>The right-clicked scene object, else the selected one, else <c>null</c>.</summary>
        static GameObject Context(MenuCommand command)
        {
            var clicked = command?.context as GameObject;
            if (clicked != null && !EditorUtility.IsPersistent(clicked) && clicked.scene.IsValid())
                return clicked;

            return SelectedSceneObject();
        }

        /// <summary>The selected scene object, or <c>null</c> when nothing in a scene is selected.</summary>
        static GameObject SelectedSceneObject()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null || EditorUtility.IsPersistent(go) || !go.scene.IsValid()) return null;
            return go;
        }

        /// <summary>Whether any scene is open to add things to.</summary>
        static bool HasOpenScene()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isLoaded) return true;
            return false;
        }
    }
}
