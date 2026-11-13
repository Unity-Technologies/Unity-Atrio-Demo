using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

#if BB_TOUCH_HAS_INPUTSYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Checks a <see cref="TouchRig"/> (or one control) and its scene and asset, and reports what stops a control
    /// from working. Each finding comes with a fix labelled with what it changes.
    /// </summary>
    /// <remarks>
    /// The Input System checks run only when that package is installed. Scene fixes record Undo; asset fixes
    /// write through <see cref="TouchBindingAuthoring"/>.
    /// </remarks>
    static class TouchRigDoctor
    {
        public const string IdNoEventSystem = "no-event-system";
        public const string IdWrongInputModule = "wrong-input-module";
        public const string IdNoBridge = "no-bridge";
        public const string IdDuplicateId = "duplicate-id";
        public const string IdEmptyId = "empty-id";
        public const string IdNoView = "no-view";
        public const string IdNoRig = "no-rig";
        public const string IdOutsideSafeArea = "outside-safe-area";
        public const string IdDrivesNothing = "drives-nothing";
        public const string IdNoAsset = "no-asset";
        public const string IdReadOnlyAsset = "read-only-asset";
        public const string IdSeveralAssets = "several-assets";
        public const string IdPointerTrap = "pointer-trap";
        public const string IdTapTrap = "tap-trap";
        public const string IdOutsideScheme = "outside-touch-scheme";
        public const string IdSchemeLacksRig = "scheme-lacks-rig";

        /// <summary>Every problem with <paramref name="rig"/>: scene, ids, asset and each control. Ok findings are left out.</summary>
        public static List<TouchSetupCheck> Inspect(TouchRig rig)
        {
            var checks = new List<TouchSetupCheck>();
            if (rig == null)
                return checks;

            List<TouchControl> controls = TouchControlIds.ControlsOf(rig);
            AddEventSystemChecks(checks, rig.gameObject.scene);
            AddBridgeCheck(checks, rig);
            AddIdChecks(checks, rig, controls, null);

#if BB_TOUCH_HAS_INPUTSYSTEM
            ActionAssetResolution resolved = TouchBindingAuthoring.ResolveAsset(rig);
            AddAssetChecks(checks, rig, resolved);
            if (resolved.Asset != null)
            {
                foreach (TouchControl control in controls)
                    AddBindingChecks(checks, control, resolved.Asset, includeUnbound: true);

                AddTrapChecks(checks, resolved.Asset, controls, null);
            }
#endif

            foreach (TouchControl control in controls)
                AddControlChecks(checks, control, includeRigless: false);

            return checks;
        }

        /// <summary>
        /// Every problem that stops <paramref name="control"/> working, including the scene checks it shares with the rig.
        /// "Drives nothing" is not reported here.
        /// </summary>
        /// <remarks>No inspector shows this list; the rig check covers every control. Tests use it.</remarks>
        public static List<TouchSetupCheck> Inspect(TouchControl control)
        {
            var checks = new List<TouchSetupCheck>();
            if (control == null)
                return checks;

            TouchRig rig = control.GetComponentInParent<TouchRig>(true);
            AddControlChecks(checks, control, includeRigless: true);
            if (control.GetComponentInParent<Canvas>(true) != null)
                AddEventSystemChecks(checks, control.gameObject.scene);

            if (rig == null)
                return checks;

            AddBridgeCheck(checks, rig);
            AddIdChecks(checks, rig, TouchControlIds.ControlsOf(rig), control);

#if BB_TOUCH_HAS_INPUTSYSTEM
            ActionAssetResolution resolved = TouchBindingAuthoring.ResolveAsset(rig);
            AddAssetChecks(checks, rig, resolved);
            if (resolved.Asset != null)
            {
                AddBindingChecks(checks, control, resolved.Asset, includeUnbound: false);
                AddTrapChecks(checks, resolved.Asset, new List<TouchControl> { control }, control);
            }
#endif

            return checks;
        }

        /// <summary>The worst severity in <paramref name="checks"/>.</summary>
        public static CheckSeverity WorstSeverity(IEnumerable<TouchSetupCheck> checks)
        {
            CheckSeverity worst = CheckSeverity.Ok;
            if (checks == null)
                return worst;

            foreach (TouchSetupCheck check in checks)
            {
                if (check.Severity > worst)
                    worst = check.Severity;
            }

            return worst;
        }

        static void AddEventSystemChecks(List<TouchSetupCheck> checks, Scene scene)
        {
            EventSystem eventSystem = FindInLoadedScenes<EventSystem>();
            if (eventSystem == null)
            {
                checks.Add(TouchSetupCheck.With(IdNoEventSystem,
                    "No EventSystem in the scene.",
                    "Touches can't reach the controls without one.",
                    CheckSeverity.Error, null,
                    "Add EventSystem", () =>
                    {
                        TouchRigSpawner.EnsureEventSystem(scene.IsValid() ? scene : SceneManager.GetActiveScene());
                        TouchBindingAuthoring_InvalidateSceneScan();
                    }));
                return;
            }

            BaseInputModule module = eventSystem.GetComponent<BaseInputModule>();
            Type wanted = WantedInputModule();
            if (wanted == null || (module != null && wanted.IsInstanceOfType(module)))
                return;

            if (module != null && AcceptedInputModule(module))
                return;

            string wantedName = ObjectNames.NicifyVariableName(wanted.Name);
            string title = module == null
                ? $"'{eventSystem.name}' has no input module."
                : $"'{eventSystem.name}' has the wrong input module.";
            checks.Add(TouchSetupCheck.With(IdWrongInputModule, title,
                $"Touches can't reach the controls. This project needs the {wantedName}.",
                CheckSeverity.Error, null,
                module == null ? $"Add {wantedName}" : $"Replace with {wantedName}",
                () => ReplaceInputModule(eventSystem, wanted)));
        }

        /// <summary>The module the active input handling needs, or <c>null</c> when either kind works.</summary>
        static Type WantedInputModule()
        {
#if ENABLE_INPUT_SYSTEM && BB_TOUCH_HAS_INPUTSYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return typeof(InputSystemUIInputModule);
#elif ENABLE_LEGACY_INPUT_MANAGER && !ENABLE_INPUT_SYSTEM
            return typeof(StandaloneInputModule);
#else
            return null;
#endif
        }

        /// <summary>Accepts a project's own module that derives from neither built-in one.</summary>
        static bool AcceptedInputModule(BaseInputModule module)
        {
            if (module is StandaloneInputModule)
                return false;
#if BB_TOUCH_HAS_INPUTSYSTEM
            if (module is InputSystemUIInputModule)
                return false;
#endif
            return true;
        }

        static void ReplaceInputModule(EventSystem eventSystem, Type wanted)
        {
            Undo.SetCurrentGroupName("Replace Input Module");
            foreach (BaseInputModule old in eventSystem.GetComponents<BaseInputModule>())
                Undo.DestroyObjectImmediate(old);

            Component added = Undo.AddComponent(eventSystem.gameObject, wanted);
#if BB_TOUCH_HAS_INPUTSYSTEM
            if (added is InputSystemUIInputModule module && module.actionsAsset == null)
                module.AssignDefaultActions();
#endif
        }

        static void AddBridgeCheck(List<TouchSetupCheck> checks, TouchRig rig)
        {
#if BB_TOUCH_HAS_INPUTSYSTEM && ENABLE_INPUT_SYSTEM
            if (rig.GetComponent<TouchInputBridge>() != null)
                return;

            checks.Add(TouchSetupCheck.With(IdNoBridge,
                $"No Touch Input Bridge on '{rig.name}'.",
                "Input System actions can't read the controls. C# still works.",
                CheckSeverity.Warning, null,
                "Add Touch Input Bridge", () => Undo.AddComponent<TouchInputBridge>(rig.gameObject)));
#endif
        }

        static void AddIdChecks(List<TouchSetupCheck> checks, TouchRig rig, List<TouchControl> controls, TouchControl only)
        {
            var seen = new Dictionary<string, TouchControl>();
            foreach (TouchControl control in controls)
            {
                string key = TouchControlIds.Key(control.ActionId);
                if (key.Length == 0)
                {
                    if (only != null && control != only)
                        continue;

                    TouchControl target = control;
                    string next = TouchControlIds.NextAutoId(rig, AutoPrefix(control));
                    checks.Add(TouchSetupCheck.With(IdEmptyId,
                        $"'{control.name}' has no id.",
                        "Code and bindings find a control by its id.",
                        CheckSeverity.Error, control,
                        $"Name it {next}", () => Rename(target, next)));
                    continue;
                }

                if (!seen.TryGetValue(key, out TouchControl first))
                {
                    seen.Add(key, control);
                    continue;
                }

                if (only != null && control != only && first != only)
                    continue;

                TouchControl second = control;
                string free = TouchControlIds.Unique(rig, second.ActionId, second);
                checks.Add(TouchSetupCheck.With(IdDuplicateId,
                    $"'{first.name}' and '{second.name}' have the same id.",
                    $"Only '{first.name}' can be found. Give '{second.name}' a new id.",
                    CheckSeverity.Error, second,
                    $"Rename {second.name} to {free}", () => Rename(second, free)));
            }
        }

        static void Rename(TouchControl control, string id)
        {
#if BB_TOUCH_HAS_INPUTSYSTEM
            TouchBindingAuthoring.RenameControl(control, id);
#else
            TouchControlIds.Apply(control, id);
#endif
        }

        /// <summary>The auto-id prefix a control of this kind gets: <c>Joystick</c> or <c>Button</c>.</summary>
        public static string AutoPrefix(TouchControl control)
        {
            switch (control)
            {
                case TouchJoystick _: return TouchControlIds.JoystickPrefix;
                default: return TouchControlIds.ButtonPrefix;
            }
        }

        static void AddControlChecks(List<TouchSetupCheck> checks, TouchControl control, bool includeRigless)
        {
            if (control.GetComponent<TouchControlView>() == null)
            {
                checks.Add(new TouchSetupCheck(IdNoView,
                    $"'{control.name}' has no view.",
                    "Nothing sends it touches. Add a Touch Joystick View or Touch Button View.",
                    CheckSeverity.Warning, control));
            }

            if (includeRigless && control.GetComponentInParent<TouchRig>(true) == null)
            {
                checks.Add(new TouchSetupCheck(IdNoRig,
                    $"'{control.name}' isn't under a Touch Rig.",
                    "Nothing can read it. Move it under a rig.",
                    CheckSeverity.Error, control));
            }

            if (OutsideSafeArea(control, out string edge, out float pixels))
            {
                checks.Add(new TouchSetupCheck(IdOutsideSafeArea,
                    $"'{control.name}' is partly off the safe area.",
                    $"It sits {pixels:0} px past the {edge} edge. A notch may cover it.",
                    CheckSeverity.Warning, control));
            }
        }

        /// <summary>
        /// Whether the control's rect leaves the nearest <see cref="TouchSafeArea"/>, else the root canvas.
        /// Reports the worst edge and by how much, in canvas units.
        /// </summary>
        internal static bool OutsideSafeArea(TouchControl control, out string edge, out float pixels)
        {
            edge = null;
            pixels = 0f;

            var rect = control.transform as RectTransform;
            if (rect == null)
                return false;

            RectTransform bounds = null;
            TouchSafeArea safe = control.GetComponentInParent<TouchSafeArea>(true);
            if (safe != null)
                bounds = safe.transform as RectTransform;
            else
            {
                Canvas canvas = control.GetComponentInParent<Canvas>(true);
                if (canvas != null)
                    bounds = canvas.rootCanvas.transform as RectTransform;
            }

            if (bounds == null || bounds.rect.width <= 0f || bounds.rect.height <= 0f)
                return false;

            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector2 min = bounds.InverseTransformPoint(corners[0]);
            Vector2 max = bounds.InverseTransformPoint(corners[2]);
            Rect limit = bounds.rect;

            const float k_Tolerance = 1f;
            float left = limit.xMin - min.x, right = max.x - limit.xMax, bottom = limit.yMin - min.y, top = max.y - limit.yMax;
            float worst = Mathf.Max(Mathf.Max(left, right), Mathf.Max(bottom, top));
            if (worst <= k_Tolerance)
                return false;

            pixels = worst;
            edge = worst == left ? "left" : worst == right ? "right" : worst == bottom ? "bottom" : "top";
            return true;
        }

#if BB_TOUCH_HAS_INPUTSYSTEM

        static void AddAssetChecks(List<TouchSetupCheck> checks, TouchRig rig, in ActionAssetResolution resolved)
        {
            if (resolved.IsAmbiguous)
            {
                var fixes = new List<TouchFix>();
                foreach (InputActionAsset candidate in resolved.SceneAssets)
                {
                    InputActionAsset chosen = candidate;
                    fixes.Add(new TouchFix($"Use {candidate.name}", () => SetBridgeActions(rig, chosen)));
                }

                checks.Add(new TouchSetupCheck(IdSeveralAssets,
                    $"The scene has {resolved.SceneAssets.Count} action assets.",
                    "Pick the one your touch controls use.",
                    CheckSeverity.Warning, null, fixes));
                return;
            }

            if (resolved.Asset == null)
            {
                checks.Add(new TouchSetupCheck(IdNoAsset,
                    "No action asset found.",
                    "Set one on the rig, or add project-wide actions. C# works without one.",
                    CheckSeverity.Warning));
                return;
            }

            if (!TouchBindingAuthoring.IsWritable(resolved.Asset, out string reason))
            {
                InputActionAsset source = resolved.Asset;
                checks.Add(TouchSetupCheck.With(IdReadOnlyAsset, reason,
                    "Makes a copy in Assets and uses it. Point your player at the copy too.",
                    CheckSeverity.Warning, null,
                    "Duplicate into Assets", () =>
                    {
                        InputActionAsset copy = TouchBindingAuthoring.DuplicateIntoAssets(source);
                        if (copy != null)
                            SetBridgeActions(rig, copy);
                    }));
            }
        }

        static void SetBridgeActions(TouchRig rig, InputActionAsset asset)
        {
            TouchInputBridge bridge = rig.GetComponent<TouchInputBridge>();
            if (bridge == null)
                bridge = Undo.AddComponent<TouchInputBridge>(rig.gameObject);
            else
                Undo.RecordObject(bridge, "Set Touch Rig Actions");

            bridge.Actions = asset;
            EditorUtility.SetDirty(bridge);
            TouchBindingAuthoring.InvalidateSceneScan();
        }

        static void AddBindingChecks(List<TouchSetupCheck> checks, TouchControl control, InputActionAsset asset, bool includeUnbound)
        {
            string name = TouchBindingAuthoring.ControlName(control);
            if (name.Length == 0)
                return;

            if (!TouchBindingAuthoring.DrivesAnything(asset, control))
            {
                if (includeUnbound)
                {
                    checks.Add(new TouchSetupCheck(IdDrivesNothing,
                        $"{control.ActionId} isn't linked to an action.",
                        $"Pick one, or read it in code: rig.Get<{control.GetType().Name}>(\"{control.ActionId}\").",
                        CheckSeverity.Warning, control, offersActionPicker: true));
                }

                return;
            }

            InputControlScheme? scheme = TouchBindingAuthoring.TouchScheme(asset);
            if (scheme != null && TouchBindingAuthoring.FindBinding(asset, name, out TouchBinding own) &&
                !TouchBindingAuthoring.InGroup(own.Binding.groups, scheme.Value.bindingGroup))
            {
                TouchControl target = control;
                checks.Add(TouchSetupCheck.With(IdOutsideScheme,
                    $"{control.ActionId}'s binding is outside the {scheme.Value.name} scheme.",
                    $"PlayerInput ignores it on {scheme.Value.name}.",
                    CheckSeverity.Warning, control,
                    $"Move it into {scheme.Value.name}", () => TouchBindingAuthoring.MoveIntoTouchSchemeFix(target, asset)));
            }
        }

        static readonly string[] k_PointerPaths = { "<Pointer>/delta", "<Pointer>/position" };
        static readonly string[] k_AnyTapPaths = { "<Touchscreen>/primaryTouch/tap", "<Pointer>/press" };

        static void AddTrapChecks(List<TouchSetupCheck> checks, InputActionAsset asset, List<TouchControl> controls, TouchControl only)
        {
            InputControlScheme? scheme = TouchBindingAuthoring.TouchScheme(asset);
            string group = scheme?.bindingGroup;

            if (scheme != null && only == null && !TouchBindingAuthoring.TouchSchemeListsRig(asset))
            {
                checks.Add(TouchSetupCheck.With(IdSchemeLacksRig,
                    $"The {scheme.Value.name} scheme is missing <TouchRig>.",
                    "PlayerInput won't read the rig on that scheme.",
                    CheckSeverity.Warning, null,
                    $"Add <TouchRig> to {scheme.Value.name}", () => TouchBindingAuthoring.AddRigToTouchSchemeFix(asset)));
            }

            // Actions a touch control drives that also read the pointer.
            var driven = new List<(InputAction action, TouchControl control)>();
            var bindings = new List<TouchBinding>();
            foreach (TouchControl control in controls)
            {
                TouchBindingAuthoring.FindBindings(asset, TouchBindingAuthoring.ControlName(control), bindings);
                foreach (TouchBinding b in bindings)
                {
                    if (!driven.Exists(d => d.action == b.Action))
                        driven.Add((b.Action, control));
                }
            }

            foreach ((InputAction action, TouchControl control) in driven)
            {
                for (int i = 0; i < action.bindings.Count; i++)
                {
                    InputBinding binding = action.bindings[i];
                    if (!Matches(binding.path, k_PointerPaths))
                        continue;

                    bool inTouch = group != null && TouchBindingAuthoring.InGroup(binding.groups, group);
                    bool noSchemes = asset.controlSchemes.Count == 0;
                    if (!inTouch && !noSchemes)
                        continue;

                    string what = action.name == "Look" ? "turns the camera" : $"moves {action.name}";
                    Guid id = binding.id;
                    checks.Add(inTouch
                        ? TouchSetupCheck.With(IdPointerTrap,
                            $"Dragging a control also {what}.",
                            $"{action.name} reads {binding.path}. Every finger counts as a pointer.",
                            CheckSeverity.Warning, control,
                            $"Remove {binding.path} from {scheme.Value.name}", () => TouchBindingAuthoring.RemoveFromTouchSchemeFix(asset, id))
                        : new TouchSetupCheck(IdPointerTrap,
                            $"Dragging a control also {what}.",
                            $"{action.name} reads {binding.path} on every touch. Add schemes to {asset.name}, or remove that binding.",
                            CheckSeverity.Warning, control));
                }
            }

            if (group == null)
                return;

            // Any action that fires on any tap anywhere.
            foreach (InputActionMap map in asset.actionMaps)
            {
                foreach (InputAction action in map.actions)
                {
                    if (only != null && !driven.Exists(d => d.action == action))
                        continue;

                    for (int i = 0; i < action.bindings.Count; i++)
                    {
                        InputBinding binding = action.bindings[i];
                        if (!Matches(binding.path, k_AnyTapPaths) || !TouchBindingAuthoring.InGroup(binding.groups, group))
                            continue;

                        Guid id = binding.id;
                        checks.Add(TouchSetupCheck.With(IdTapTrap,
                            $"Any tap on the screen fires {TouchBindingAuthoring.Label(action)}.",
                            $"It reads {binding.path}, so pressing any control fires it too.",
                            CheckSeverity.Warning, null,
                            $"Remove {binding.path} from {scheme.Value.name}", () => TouchBindingAuthoring.RemoveFromTouchSchemeFix(asset, id)));
                    }
                }
            }
        }

        static bool Matches(string path, string[] options)
        {
            foreach (string option in options)
            {
                if (string.Equals(path, option, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
#endif

        static void TouchBindingAuthoring_InvalidateSceneScan()
        {
#if BB_TOUCH_HAS_INPUTSYSTEM
            TouchBindingAuthoring.InvalidateSceneScan();
#endif
        }

        static T FindInLoadedScenes<T>() where T : Component
        {
            foreach (T found in Object.FindObjectsByType<T>(FindObjectsInactive.Include))
            {
                if (found != null && !EditorUtility.IsPersistent(found))
                    return found;
            }

            return null;
        }
    }
}
