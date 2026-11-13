using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// The GameObject ▸ Building Blocks ▸ Touch Input menu: Joystick, Button and a twin-stick layout.
    /// Every item calls <see cref="TouchRigSpawner"/>.
    /// </summary>
    /// <remarks>
    /// There is no empty-rig item. Adding a control makes a rig if there is none above the right-clicked
    /// (or selected) object. The scene gets a Canvas, EventSystem and safe area if it has none.
    /// </remarks>
    static class TouchInputMenu
    {
        public const string Root = "GameObject/Building Blocks/Touch Input/";
        public const string JoystickPath = Root + "Joystick";
        public const string ButtonPath = Root + "Button";
        public const string TwinStickPath = Root + "Twin-Stick Layout";

        // Priorities more than ten apart draw a separator between the pieces and the layout.
        const int k_PiecesPriority = 10;
        const int k_LayoutPriority = 30;

        [MenuItem(JoystickPath, false, k_PiecesPriority)]
        static void Joystick(MenuCommand command) => Select(TouchRigSpawner.CreateJoystick(Context(command)));

        [MenuItem(ButtonPath, false, k_PiecesPriority + 1)]
        static void Button(MenuCommand command) => Select(TouchRigSpawner.CreateButton(Context(command)));

        [MenuItem(TwinStickPath, false, k_LayoutPriority)]
        static void TwinStick(MenuCommand command) => Select(TouchRigSpawner.CreateTwinStickLayout(Context(command)));

        [MenuItem(JoystickPath, true)]
        static bool CanCreateJoystick() => TouchRigSpawner.HasOpenScene();

        [MenuItem(ButtonPath, true)]
        static bool CanCreateButton() => TouchRigSpawner.HasOpenScene();

        [MenuItem(TwinStickPath, true)]
        static bool CanCreateTwinStick() => TouchRigSpawner.HasOpenScene();

        static void Select(GameObject created)
        {
            if (created == null)
                return;

            Selection.activeGameObject = created;
            EditorGUIUtility.PingObject(created);
        }

        /// <summary>The right-clicked scene object, else the selected one, else <c>null</c>.</summary>
        static GameObject Context(MenuCommand command)
        {
            var clicked = command?.context as GameObject;
            if (clicked != null && !EditorUtility.IsPersistent(clicked) && clicked.scene.IsValid())
                return clicked;

            GameObject selected = Selection.activeGameObject;
            if (selected != null && !EditorUtility.IsPersistent(selected) && selected.scene.IsValid())
                return selected;

            return null;
        }
    }
}
