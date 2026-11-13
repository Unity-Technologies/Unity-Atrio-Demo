using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if BB_TOUCH_HAS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Builds touch controls in the editor. Each method is one Undo step and returns the object to select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Missing pieces are created: a <see cref="Canvas"/>, an <see cref="EventSystem"/> and a
    /// <see cref="TouchSafeArea"/> root. An existing Canvas or EventSystem in the scene is reused.
    /// </para>
    /// <para>
    /// Each new control gets the next free id of its kind (<c>Joystick1</c>, <c>Button1</c>) and drives nothing
    /// until an action is picked. With the Input System active, each new rig also gets a <c>TouchInputBridge</c>.
    /// </para>
    /// </remarks>
    static class TouchRigSpawner
    {
        public const string CanvasName = "Touch Canvas";
        public const string EventSystemName = "EventSystem";
        public const string SafeAreaName = "Safe Area";
        public const string RigName = "Touch Rig";

        public const float JoystickSize = 220f;
        /// <summary>The knob image's size; its sprite leaves room for a shadow, so the disc itself is about 84 px.</summary>
        public const float KnobSize = 110f;
        public const float ButtonSize = 150f;
        public const float CornerInset = 40f;

        public static readonly Vector2 ReferenceResolution = new Vector2(1080f, 1920f);
        public const float MatchWidthOrHeight = 0.5f;

        static readonly Color k_AreaColor = new Color(1f, 1f, 1f, 0f);

        /// <summary>A new button's resting tint. The white disc looks like frosted glass over the scene.</summary>
        public static readonly Color ButtonNormalColor = new Color(1f, 1f, 1f, 0.28f);

        /// <summary>A new button's pressed tint.</summary>
        public static readonly Color ButtonPressedColor = new Color(1f, 1f, 1f, 0.78f);
        const int k_UILayer = 5;

        /// <summary>Whether there is a scene to build in.</summary>
        public static bool HasOpenScene() => SceneManager.GetActiveScene().IsValid();

        /// <summary>
        /// Creates a Touch Rig under <paramref name="context"/> if it is inside a Canvas, else under the scene's
        /// safe-area root.
        /// </summary>
        public static GameObject CreateRig(GameObject context)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Touch Rig");

            TouchRig rig = EnsureRig(context, true);

            Undo.CollapseUndoOperations(group);
            return rig.gameObject;
        }

        /// <summary>Creates a joystick under the rig above <paramref name="context"/>, or under a new rig.</summary>
        public static GameObject CreateJoystick(GameObject context)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Joystick");

            TouchRig rig = EnsureRig(context, false);
            GameObject joystick = AddJoystick(rig, ParentFor(context, rig)).gameObject;

            Undo.CollapseUndoOperations(group);
            return joystick;
        }

        /// <summary>Creates a button under the rig above <paramref name="context"/>, or under a new rig.</summary>
        public static GameObject CreateButton(GameObject context)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Button");

            TouchRig rig = EnsureRig(context, false);
            GameObject button = AddButton(rig, ParentFor(context, rig)).gameObject;

            Undo.CollapseUndoOperations(group);
            return button;
        }

        /// <summary>
        /// Creates a rig with a joystick in each bottom corner and a button above the right one.
        /// None of them drives anything until an action is picked.
        /// </summary>
        public static GameObject CreateTwinStickLayout(GameObject context)
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create Twin-Stick Layout");

            TouchRig rig = EnsureRig(context, true);
            Transform parent = rig.transform;
            AddJoystick(rig, parent);
            AddJoystick(rig, parent);
            AddButton(rig, parent);

            Undo.CollapseUndoOperations(group);
            return rig.gameObject;
        }

        /// <summary>The Canvas in <paramref name="scene"/>, or a new Screen Space Overlay one scaled for phones.</summary>
        public static Canvas EnsureCanvas(Scene scene)
        {
            Canvas existing = FindInScene<Canvas>(scene);
            if (existing != null)
                return existing.rootCanvas;

            var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            {
                layer = k_UILayer,
            };
            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");
            if (scene.IsValid() && scene != go.scene)
                SceneManager.MoveGameObjectToScene(go, scene);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = MatchWidthOrHeight;

            return canvas;
        }

        /// <summary>
        /// The EventSystem in <paramref name="scene"/>, or a new one. A new one uses <c>InputSystemUIInputModule</c>
        /// when the Input System is active, else <c>StandaloneInputModule</c>.
        /// </summary>
        public static EventSystem EnsureEventSystem(Scene scene)
        {
            EventSystem existing = FindInScene<EventSystem>(scene);
            if (existing != null)
                return existing;

            var go = new GameObject(EventSystemName, typeof(EventSystem));
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
            if (scene.IsValid() && scene != go.scene)
                SceneManager.MoveGameObjectToScene(go, scene);

#if BB_TOUCH_HAS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            var module = Undo.AddComponent<InputSystemUIInputModule>(go);
            if (module.actionsAsset == null)
                module.AssignDefaultActions();
#else
            Undo.AddComponent<StandaloneInputModule>(go);
#endif

            return go.GetComponent<EventSystem>();
        }

        /// <summary>The safe-area root under <paramref name="canvas"/>, or a new full-stretch one.</summary>
        public static TouchSafeArea EnsureSafeArea(Canvas canvas)
        {
            TouchSafeArea existing = canvas.GetComponentInChildren<TouchSafeArea>(true);
            if (existing != null)
                return existing;

            RectTransform rect = NewRect(SafeAreaName, canvas.transform, "Create Safe Area");
            Stretch(rect);
            return Undo.AddComponent<TouchSafeArea>(rect.gameObject);
        }

        /// <summary>
        /// The rig above <paramref name="context"/>. With <paramref name="alwaysNew"/>, or when there is none,
        /// makes a new rig under the context if it is inside a Canvas, else under the safe-area root.
        /// </summary>
        static TouchRig EnsureRig(GameObject context, bool alwaysNew)
        {
            if (!alwaysNew && context != null)
            {
                TouchRig above = context.GetComponentInParent<TouchRig>(true);
                if (above != null)
                    return above;
            }

            Scene scene = context != null && context.scene.IsValid() ? context.scene : SceneManager.GetActiveScene();
            Canvas canvas = EnsureCanvas(scene);
            EnsureEventSystem(scene);

            Transform parent;
            if (context != null && context.GetComponentInParent<Canvas>(true) != null)
                parent = context.transform;
            else
                parent = EnsureSafeArea(canvas).transform;

            RectTransform rect = NewRect(RigName, parent, "Create Touch Rig");
            Stretch(rect);
            TouchRig rig = Undo.AddComponent<TouchRig>(rect.gameObject);

#if BB_TOUCH_HAS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            // With the Input System, every rig's controls are bindable as <TouchRig>/<id>.
            Undo.AddComponent<TouchInputBridge>(rect.gameObject);
#endif

            return rig;
        }

        /// <summary>
        /// Where a new control goes: beside a right-clicked control (controls never nest), inside a right-clicked
        /// container in the rig, else directly under the rig.
        /// </summary>
        static Transform ParentFor(GameObject context, TouchRig rig)
        {
            if (context == null || !context.transform.IsChildOf(rig.transform))
                return rig.transform;

            TouchControl control = context.GetComponentInParent<TouchControl>(true);
            if (control != null)
                return control.transform.parent;

            return context.transform;
        }

        static TouchJoystick AddJoystick(TouchRig rig, Transform parent)
        {
            bool left = CountControls<TouchJoystick>(rig) % 2 == 0;
            string id = TouchControlIds.NextAutoId(rig, TouchControlIds.JoystickPrefix);

            RectTransform area = NewRect(id, parent, "Create Joystick");
            PlaceInCorner(area, left, new Vector2(JoystickSize, JoystickSize), 0f);
            Image hit = Undo.AddComponent<Image>(area.gameObject);
            hit.color = k_AreaColor;
            hit.raycastTarget = true;

            var joystick = Undo.AddComponent<TouchJoystick>(area.gameObject);
            joystick.ActionId = id;

            Image baseImage = NewImage("Ring", area, TouchSkin.JoystickRing, Color.white, new Vector2(JoystickSize, JoystickSize));
            Image knob = NewImage("Knob", area, TouchSkin.JoystickKnob, Color.white, new Vector2(KnobSize, KnobSize));

            var view = Undo.AddComponent<TouchJoystickView>(area.gameObject);
            view.Base = baseImage;
            view.Knob = knob;

            return joystick;
        }

        static TouchButton AddButton(TouchRig rig, Transform parent)
        {
            int existing = CountControls<TouchButton>(rig);
            string id = TouchControlIds.NextAutoId(rig, TouchControlIds.ButtonPrefix);

            RectTransform rect = NewRect(id, parent, "Create Button");
            float rise = JoystickSize + CornerInset + existing * (ButtonSize + CornerInset * 0.5f);
            PlaceInCorner(rect, false, new Vector2(ButtonSize, ButtonSize), rise);

            Image image = Undo.AddComponent<Image>(rect.gameObject);
            image.sprite = TouchSkin.ButtonDisc;
            image.preserveAspect = true;
            image.color = ButtonNormalColor;
            image.raycastTarget = true;

            // The rim is a child so it scales with the disc when the button is pressed.
            Image rim = NewImage("Rim", rect, TouchSkin.ButtonRing, Color.white, Vector2.zero);
            Stretch(rim.rectTransform);
            rim.preserveAspect = true;

            var button = Undo.AddComponent<TouchButton>(rect.gameObject);
            button.ActionId = id;

            var view = Undo.AddComponent<TouchButtonView>(rect.gameObject);
            view.Target = image;
            view.NormalColor = ButtonNormalColor;
            view.PressedColor = ButtonPressedColor;

            return button;
        }

        static RectTransform NewRect(string name, Transform parent, string undoName)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = k_UILayer };
            Undo.RegisterCreatedObjectUndo(go, undoName);
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        static Image NewImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
        {
            RectTransform rect = NewRect(name, parent, "Create " + name);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;

            Image image = Undo.AddComponent<Image>(rect.gameObject);
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Anchors <paramref name="rect"/> to the bottom-left or bottom-right corner, inset and raised by <paramref name="rise"/>.</summary>
        static void PlaceInCorner(RectTransform rect, bool left, Vector2 size, float rise)
        {
            Vector2 anchor = left ? Vector2.zero : Vector2.right;
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            float x = CornerInset + size.x * 0.5f;
            rect.anchoredPosition = new Vector2(left ? x : -x, CornerInset + rise + size.y * 0.5f);
        }

        static int CountControls<T>(TouchRig rig) where T : TouchControl =>
            rig.GetComponentsInChildren<T>(true).Length;

        static T FindInScene<T>(Scene scene) where T : Component
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T found = root.GetComponentInChildren<T>(true);
                if (found != null)
                    return found;
            }

            return null;
        }
    }
}
