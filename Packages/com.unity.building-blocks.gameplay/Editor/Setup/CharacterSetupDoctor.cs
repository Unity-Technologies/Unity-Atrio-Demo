using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Inspects a character hierarchy and the project around it, and reports what is missing,
    /// mis-parented or mis-tuned.
    /// </summary>
    /// <remarks>
    /// Catches setups that do not throw but misbehave: a mover with no processors does not fall, an
    /// interpolator with no visual root smooths nothing, a camera inside <c>Visual/</c> inherits animation
    /// bob. Reports only the character's own wiring, not scene materials or geometry, except one check
    /// that a collider lies below the feet. Has no UI.
    /// </remarks>
    public static class CharacterSetupDoctor
    {
        /// <summary>
        /// Every finding for <paramref name="character"/>, the root GameObject carrying
        /// <see cref="KinematicMover"/>.
        /// </summary>
        /// <remarks>A <c>null</c> character yields a single error.</remarks>
        public static List<CharacterSetupCheck> Inspect(GameObject character)
        {
            var checks = new List<CharacterSetupCheck>();

            if (character == null)
            {
                checks.Add(CharacterSetupCheck.Error(
                    "No character selected",
                    "Select the GameObject carrying the Kinematic Mover, or create one."));
                return checks;
            }

            var mover = character.GetComponent<KinematicMover>();
            AddMoverChecks(checks, character, mover);
            AddPhysicsBodyChecks(checks, character);
            AddHostChecks(checks, character, mover);
            AddProcessorChecks(checks, character);
            AddCommandSourceChecks(checks, character);
            AddInputChecks(checks, character);
            AddInterpolatorChecks(checks, character);
            AddCameraRigChecks(checks, character);
            AddLayerChecks(checks, character);
            AddJumpCouplingCheck(checks, character, mover);
            AddGroundCheck(checks, character, mover);

            return checks;
        }

        /// <summary>
        /// The worst severity in <paramref name="checks"/>.
        /// </summary>
        public static CheckSeverity WorstSeverity(IEnumerable<CharacterSetupCheck> checks)
        {
            CheckSeverity worst = CheckSeverity.Ok;
            if (checks == null) return worst;

            foreach (CharacterSetupCheck check in checks)
            {
                if (check.Severity > worst) worst = check.Severity;
            }

            return worst;
        }

        static void AddMoverChecks(
            List<CharacterSetupCheck> checks, GameObject character, KinematicMover mover)
        {
            checks.Add(mover != null
                ? CharacterSetupCheck.Ok("Kinematic Mover", "The simulation component is present.")
                : CharacterSetupCheck.Error(
                    "Kinematic Mover missing",
                    "Nothing simulates this GameObject. Add a Kinematic Mover. It brings its own " +
                    "capsule collider and kinematic rigidbody.",
                    "Add Kinematic Mover",
                    () => Undo.AddComponent<KinematicMover>(character)));
        }

        static void AddPhysicsBodyChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            var body = character.GetComponent<Rigidbody>();
            if (body == null) return;

            bool kinematic = body.isKinematic;
            bool gravityOff = !body.useGravity;
            bool interpolationOff = body.interpolation == RigidbodyInterpolation.None;

            if (kinematic && gravityOff && interpolationOff)
            {
                checks.Add(CharacterSetupCheck.Ok(
                    "Rigidbody", "Kinematic, no gravity, no interpolation, as the solver needs."));
                return;
            }

            checks.Add(CharacterSetupCheck.Warning(
                "Rigidbody settings",
                "The solver owns the transform, so the body must be kinematic with gravity and " +
                "interpolation off, otherwise physics and the mover fight over the same position. " +
                "The mover forces this at runtime; fixing it here makes the prefab honest.",
                "Fix Rigidbody",
                () =>
                {
                    Undo.RecordObject(body, "Fix Rigidbody For Kinematic Mover");
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.interpolation = RigidbodyInterpolation.None;
                    EditorUtility.SetDirty(body);
                }));
        }

        static void AddHostChecks(
            List<CharacterSetupCheck> checks, GameObject character, KinematicMover mover)
        {
            var host = character.GetComponent<Character>();
            if (host != null)
            {
                checks.Add(CharacterSetupCheck.Ok(
                    "Character host", "The host loop is present and will tick the mover."));
                return;
            }

            checks.Add(CharacterSetupCheck.Error(
                "Character host missing",
                "Nothing calls Simulate, so the mover never ticks. Character is the default host " +
                "loop: it reads a command each FixedUpdate and drives the mover.",
                mover != null ? "Add Character" : null,
                mover != null ? () => Undo.AddComponent<Character>(character) : (System.Action)null));
        }

        static void AddProcessorChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            var host = character.GetComponent<Character>();

            foreach (Component custom in FindCustomComponents(character))
            {
                checks.Add(CharacterSetupCheck.Warning(
                    $"{ObjectNames.NicifyVariableName(custom.GetType().Name)} is not run",
                    "It is a move processor component, and a Character only runs the processors in its " +
                    "own list. Rewrite it as a class deriving from MoveProcessor and add it to " +
                    "the list; or register it with the mover yourself."));
            }

            // Without a host the processor list has nowhere to live; the host check already says so.
            if (host == null) return;

            List<MoveProcessor> processors = host.Processors;
            int count = 0;
            foreach (MoveProcessor processor in processors)
                if (processor != null) count++;

            if (count == 0)
            {
                checks.Add(CharacterSetupCheck.Error(
                    "No move processors",
                    "The mover applies no forces of its own (every force, gravity included, is a " +
                    "processor), so with an empty list this character will not fall and will not walk. " +
                    "Ground Locomotion carries gravity and movement.",
                    "Add Ground Locomotion",
                    () => AddProcessor<GroundLocomotionProcessor>(host)));
                return;
            }

            if (host.GetProcessor<GroundLocomotionProcessor>() == null)
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "No gravity source",
                    "The list has processors, but none of them is Ground Locomotion, which is " +
                    "where gravity lives. This character will not fall unless one of your own " +
                    "processors applies it.",
                    "Add Ground Locomotion",
                    () => AddProcessor<GroundLocomotionProcessor>(host)));
            }
            else
            {
                checks.Add(CharacterSetupCheck.Ok(
                    "Move processors",
                    $"{count} in the list, run top to bottom, including gravity and locomotion."));
            }

            if (host.GetProcessor<YawRotationProcessor>() == null)
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "Nothing owns rotation",
                    "No Yaw Rotation processor, so MoveCommand.Yaw is ignored and the character " +
                    "never turns to face where the camera looks.",
                    "Add Yaw Rotation",
                    () => AddProcessor<YawRotationProcessor>(host)));
            }
        }

        /// <summary>Custom <see cref="IMoveProcessor"/> components on <paramref name="character"/>; a Character does not run them.</summary>
        static List<Component> FindCustomComponents(GameObject character)
        {
            var found = new List<Component>();
            var buffer = new List<IMoveProcessor>();
            character.GetComponents(buffer);
            foreach (IMoveProcessor processor in buffer)
            {
                if (processor is Component component && component != null)
                    found.Add(component);
            }

            return found;
        }

        /// <summary>
        /// Appends a new <typeparamref name="T"/> to <paramref name="host"/>'s processor list (one Undo step,
        /// recorded on prefab instances). Returns the new processor.
        /// </summary>
        public static T AddProcessor<T>(Character host) where T : MoveProcessor, new()
        {
            Undo.RecordObject(host, $"Add {ProcessorListView.DisplayName(typeof(T))} processor");
            var processor = new T();
            host.Processors.Add(processor);
            EditorUtility.SetDirty(host);
            PrefabUtility.RecordPrefabInstancePropertyModifications(host);
            return processor;
        }

        static void AddCommandSourceChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            var sources = new List<IMoveCommandSource>();
            character.GetComponents(sources);

            checks.Add(sources.Count > 0
                ? CharacterSetupCheck.Ok(
                    "Command source", $"{sources.Count} source(s). Something drives this character.")
                : CharacterSetupCheck.Warning(
                    "No command source",
                    "Legal, and the tick still runs with a default command: the character falls and " +
                    "collides but takes no input. Add a Player Input Source to drive it from a " +
                    "keyboard and mouse.",
                    "Add Player Input Source",
                    () => Undo.AddComponent<PlayerInputSource>(character)));
        }

        /// <summary>
        /// Checks input wiring: every <see cref="InputSlot"/> names a row on the Player Input Source, every
        /// row has a name and an action, and the five built-in actions resolve to something with bindings.
        /// </summary>
        static void AddInputChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            var host = character.GetComponent<Character>();
            var source = character.GetComponent<PlayerInputSource>();

            InputActionAsset projectAsset = BuildingBlocksInputAsset.ProjectAssetOf(character);

            if (source != null)
            {
                // The package prefab references the package template so it works standalone; only copies of
                // it are told to move to an editable asset.
                if (BuildingBlocksInputAsset.UsesPackageAsset(character) && !IsThisPackagesOwnPrefab(character))
                {
                    bool readOnly = BuildingBlocksInputAsset.UsesReadOnlyAsset(character);
                    checks.Add(CharacterSetupCheck.Warning(
                        "Input is read from the package's asset",
                        (readOnly
                            ? "The actions point into the package folder, which is read-only, so a binding cannot be " +
                              "changed there. "
                            : "The actions point into the package folder, so any rebinding there is lost when the " +
                              "package updates. ") +
                        $"Copy the asset to {PlaygroundPaths.ProjectInputActions} and point this " +
                        "character at the copy; the Input Actions window then edits your copy. The First Person " +
                        "Character menu item does this for you.",
                        "Copy into project",
                        () =>
                        {
                            InputActionAsset copy = BuildingBlocksInputAsset.EnsureProjectCopy(out _);
                            if (copy != null) BindAndReport(character, copy);
                        }));
                }

                AddBuiltInActionChecks(checks, character, source, projectAsset);

                List<CommandButton> rows = source.CustomButtons;
                for (int i = 0; i < rows.Count; i++)
                {
                    CommandButton row = rows[i];
                    if (row == null) continue;

                    if (string.IsNullOrWhiteSpace(row.Name))
                    {
                        checks.Add(CharacterSetupCheck.Warning(
                            "A custom button has no name",
                            $"Row {i + 1} under Player Input Source ▸ Custom Buttons is unnamed, so no processor can " +
                            "pick it. Give it the name the processor asks for."));
                    }
                    else if (!row.HasAction)
                    {
                        string rowName = row.Name.Trim();
                        CommandButton target = row;
                        bool canAdd = projectAsset != null;
                        checks.Add(CharacterSetupCheck.Warning(
                            $"Custom button '{rowName}' has no action",
                            canAdd
                                ? $"Add a '{rowName}' action to {projectAsset.name} and point the row at it, then bind a " +
                                  "key in the Input Actions window. Or drag any Button action into the row yourself."
                                : "Drag a Button action from your .inputactions asset into the row, or untick Use " +
                                  "Reference and bind one inline. Until then the button never fires.",
                            canAdd ? $"Add '{rowName}' to {projectAsset.name}" : null,
                            canAdd ? () => AddActionForRow(source, target, projectAsset) : (System.Action)null));
                    }
                    else if (row.Action.bindings.Count == 0)
                    {
                        checks.Add(CharacterSetupCheck.Warning(
                            $"Custom button '{row.Name.Trim()}' has no bindings yet",
                            $"The action '{row.Action.name}' exists but nothing is bound to it. Open " +
                            $"{(row.Action.actionMap != null && row.Action.actionMap.asset != null ? row.Action.actionMap.asset.name : "the asset")} " +
                            "in the Input Actions window and add a key or a gamepad button."));
                    }
                }
            }

            // Without a host there is no processor list; the host check already says so.
            if (host == null) return;

            int slots = 0;
            int bound = 0;
            foreach (MoveProcessor processor in host.Processors)
            {
                if (processor == null) continue;

                string processorName = ObjectNames.NicifyVariableName(processor.GetType().Name);
                foreach (FieldInfo field in InputSlotBinder.SlotFields(processor.GetType()))
                {
                    slots++;
                    var slot = (InputSlot)field.GetValue(processor);
                    string wanted = slot.Name;

                    if (string.IsNullOrWhiteSpace(wanted))
                    {
                        checks.Add(CharacterSetupCheck.Warning(
                            $"{processorName} has an unnamed input",
                            $"Its {ObjectNames.NicifyVariableName(field.Name)} field does not say which custom button " +
                            "it reads, so it never fires. Pick one in the Character inspector."));
                        continue;
                    }

                    wanted = wanted.Trim();

                    if (source == null)
                    {
                        checks.Add(CharacterSetupCheck.Warning(
                            $"{processorName} reads '{wanted}' with no input source",
                            "There is no Player Input Source on this character to bind the button to, so the " +
                            $"processor never sees a press. Add one, then name a custom button '{wanted}' on it."));
                        continue;
                    }

                    if (source.FindButton(wanted) == InputSlot.Unbound)
                    {
                        string name = wanted;
                        checks.Add(CharacterSetupCheck.Warning(
                            $"{processorName} reads a button that is not set up",
                            $"It asks for '{name}', and Player Input Source has no custom button called that. Add " +
                            "the row here, or pick \"Add button…\" at the bottom of the processor's input dropdown; " +
                            "then drag its action in.",
                            $"Add '{name}' button",
                            () => AddCustomButton(source, name)));
                        continue;
                    }

                    bound++;
                }
            }

            if (slots > 0 && bound == slots)
            {
                checks.Add(CharacterSetupCheck.Ok(
                    "Custom buttons",
                    $"{bound} processor input{(bound == 1 ? string.Empty : "s")} bound to Player Input Source rows."));
            }
        }

        /// <summary>
        /// Appends an empty reference-mode row called <paramref name="name"/> to <paramref name="source"/>'s
        /// custom buttons (one Undo step, recorded on prefab instances). Returns the new row.
        /// </summary>
        public static CommandButton AddCustomButton(PlayerInputSource source, string name)
        {
            Undo.RecordObject(source, $"Add '{name}' custom button");
            var row = new CommandButton(name);
            source.CustomButtons.Add(row);
            BuildingBlocksInputAsset.RefreshRows(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);

            // With the project's input copy, also add the action and point the row at it. A key still
            // needs binding in the Input Actions window.
            InputActionAsset projectAsset = BuildingBlocksInputAsset.ProjectAssetOf(source.gameObject);
            if (projectAsset != null)
                AddActionForRow(source, row, projectAsset);

            return row;
        }

        /// <summary>
        /// Adds a Button action named after <paramref name="row"/> to <paramref name="asset"/> (a file this
        /// package created) and points the row at it. Nothing is bound to it yet.
        /// </summary>
        static void AddActionForRow(PlayerInputSource source, CommandButton row, InputActionAsset asset)
        {
            string name = row.Name.Trim();
            if (asset.FindAction(name, false) == null && !BuildingBlocksInputAsset.AddButtonAction(asset, name))
                return;

            InputActionReference reference = BuildingBlocksInputAsset.FindReference(asset, name);
            if (reference == null) return;

            Undo.RecordObject(source, $"Point '{name}' at {asset.name}");
            row.ActionProperty = new InputActionProperty(reference);
            BuildingBlocksInputAsset.RefreshRows(source);
            PrefabUtility.RecordPrefabInstancePropertyModifications(source);
        }

        /// <summary>
        /// One finding per built-in action that cannot fire: an empty reference, a deleted action, or an
        /// inline action with no bindings.
        /// </summary>
        static void AddBuiltInActionChecks(List<CharacterSetupCheck> checks, GameObject character, PlayerInputSource source, InputActionAsset projectAsset)
        {
            using var so = new SerializedObject(source);
            foreach ((string field, string label) in new[]
            {
                ("m_Move", "Move"), ("m_Look", "Look"), ("m_Jump", "Jump"), ("m_Sprint", "Sprint"), ("m_Crouch", "Crouch"),
            })
            {
                SerializedProperty use = so.FindProperty(field + ".m_UseReference");
                SerializedProperty reference = so.FindProperty(field + ".m_Reference");
                if (use == null || reference == null) continue;

                if (!use.boolValue)
                {
                    InputAction inline = ActionOf(source, label);
                    if (inline == null || inline.bindings.Count == 0)
                    {
                        checks.Add(CharacterSetupCheck.Warning(
                            $"{label} has no bindings",
                            "The inline action has nothing bound to it, so nothing the player does reaches it. Add a " +
                            "binding on the component, or tick Use Reference and drag the action in from your asset."));
                    }

                    continue;
                }

                var target = reference.objectReferenceValue as InputActionReference;
                if (target != null && target.action != null) continue;

                // A deleted action also deletes its reference sub-asset, so read the source asset from
                // the sibling fields instead.
                InputActionAsset sibling = projectAsset;
                if (sibling != null && sibling.FindAction(label, false) != null)
                {
                    checks.Add(CharacterSetupCheck.Warning(
                        $"{label} has no action",
                        $"{label} reads nothing, while the other actions read {sibling.name}, which has a '{label}' " +
                        "action. Point it there, or drag any action in.",
                        $"Point '{label}' at {sibling.name}",
                        () => BindAndReport(character, sibling)));
                    continue;
                }

                bool restorable = sibling != null && BuildingBlocksInputAsset.CanRestore(sibling, label);
                checks.Add(CharacterSetupCheck.Warning(
                    $"{label} has no action",
                    restorable
                        ? $"{label} reads nothing, and {sibling.name} (which the other actions read) has no '{label}' " +
                          "action any more: it was deleted or renamed. Restore puts the package default back."
                        : $"{label}: Use Reference is ticked but nothing is dragged in, so nothing the player does " +
                          "reaches it. Drag the action in from your .inputactions asset, or untick Use Reference to " +
                          "use the inline action.",
                    restorable ? $"Restore '{label}'" : null,
                    restorable
                        ? () =>
                        {
                            if (BuildingBlocksInputAsset.RestoreAction(sibling, label))
                                BindAndReport(character, sibling);
                        }
                : (System.Action)null));
            }
        }

        /// <summary>
        /// Points the character at <paramref name="asset"/> and logs any action the asset lacks.
        /// </summary>
        static void BindAndReport(GameObject character, InputActionAsset asset)
        {
            var missing = new List<string>();
            BuildingBlocksInputAsset.Bind(character, asset, missing);
            string problem = BuildingBlocksInputAsset.DescribeMissing(asset, missing);
            if (problem != null) Debug.LogWarning($"[Building Blocks] {problem}", character);
        }

        /// <summary>
        /// Whether <paramref name="go"/> is the package's own prefab asset (or open in a prefab stage), not
        /// an instance or a project prefab.
        /// </summary>
        static bool IsThisPackagesOwnPrefab(GameObject go)
        {
            string path = null;
            if (PrefabUtility.IsPartOfPrefabAsset(go))
                path = AssetDatabase.GetAssetPath(go);
            else
                path = PrefabStageUtility.GetPrefabStage(go)?.assetPath;

            return !string.IsNullOrEmpty(path) &&
                   path.StartsWith(PlaygroundPaths.PackageAssetRoot + "/", System.StringComparison.Ordinal);
        }

        static InputAction ActionOf(PlayerInputSource source, string label) => label switch
        {
            "Move" => source.MoveAction,
            "Look" => source.LookAction,
            "Jump" => source.JumpAction,
            "Sprint" => source.SprintAction,
            "Crouch" => source.CrouchAction,
            _ => null,
        };

        static void AddInterpolatorChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            var interpolator = character.GetComponent<MoveInterpolator>();
            if (interpolator == null)
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "No interpolator",
                    "The character root steps once per fixed tick, so visuals parented to it judder " +
                    "on any machine whose frame rate is not the tick rate. A Move Interpolator " +
                    "smooths a visual root between ticks.",
                    "Add Move Interpolator",
                    () => Undo.AddComponent<MoveInterpolator>(character)));
                return;
            }

            Transform visualRoot = interpolator.VisualRoot;

            if (visualRoot == null)
            {
                checks.Add(CharacterSetupCheck.Error(
                    "Visual root not assigned",
                    "The interpolator has nothing to smooth. Assign a child transform. The " +
                    "convention is a 'Visual' child holding the model, animator and hands.",
                    "Create Visual/ child",
                    () => CreateVisualRoot(character, interpolator)));
                return;
            }

            if (visualRoot == character.transform)
            {
                checks.Add(CharacterSetupCheck.Error(
                    "Visual root is the character root",
                    "The interpolator would write the same transform the solver owns, fighting the " +
                    "simulation every frame. Point it at a child instead."));
                return;
            }

            if (!visualRoot.IsChildOf(character.transform))
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "Visual root is outside the character",
                    $"'{visualRoot.name}' is not under this character, so it will be moved to the " +
                    "character's pose from somewhere else in the scene. That is legal but rarely " +
                    "intended."));
                return;
            }

            checks.Add(CharacterSetupCheck.Ok(
                "Visual root", $"'{visualRoot.name}' is interpolated between fixed ticks."));
        }

        static void AddCameraRigChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            var camera = character.GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "No camera under the character",
                    "First-person needs a camera in the hierarchy. The convention is a 'CameraRig' " +
                    "child that is a sibling of the visual root, with the camera under it."));
                return;
            }

            var interpolator = character.GetComponent<MoveInterpolator>();
            Transform visualRoot = interpolator != null ? interpolator.VisualRoot : null;

            if (visualRoot != null && visualRoot != character.transform &&
                camera.transform.IsChildOf(visualRoot))
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "Camera is inside the visual root",
                    $"'{camera.name}' is parented under '{visualRoot.name}', so it inherits the " +
                    "interpolated visual pose exactly: every bob, lean and footstep of an animated " +
                    "body, one to one. Move the rig out to be a sibling of the visual root and let " +
                    "it follow only the position."));
                return;
            }

            Animator animator = AnimatorAbove(camera.transform, character.transform);
            if (animator != null && character.GetComponentInChildren<FirstPersonCameraRig>(true) != null)
            {
                checks.Add(CharacterSetupCheck.Warning(
                    "An Animator drives the camera",
                    $"'{animator.name}' has an Animator above '{camera.name}', so a clip can write the camera " +
                    "every frame while the First Person Camera Rig writes it back in LateUpdate: the camera " +
                    "slides but never turns, or does not move at all. To animate the view, put a camera node " +
                    "inside the animated object and assign it to Play Animation's Camera field; the view " +
                    "rides it for the clip."));
                return;
            }

            checks.Add(CharacterSetupCheck.Ok(
                "Camera", $"'{camera.name}' is outside the visual root."));
        }

        /// <summary>The first Animator on <paramref name="from"/> or its parents, stopping below <paramref name="root"/>.</summary>
        static Animator AnimatorAbove(Transform from, Transform root)
        {
            for (Transform t = from; t != null && t != root; t = t.parent)
            {
                var animator = t.GetComponent<Animator>();
                if (animator != null) return animator;
            }

            return null;
        }

        static void AddLayerChecks(List<CharacterSetupCheck> checks, GameObject character)
        {
            int layer = character.layer;
            string layerName = PlayerLayerFixer.LayerName(layer);

            if (layer >= PlayerLayerFixer.FirstUserLayer && !string.IsNullOrEmpty(layerName) &&
                PlayerLayerFixer.IsHierarchyOnLayer(character, layer))
            {
                checks.Add(CharacterSetupCheck.Ok(
                    "Character layer",
                    $"The whole hierarchy is on '{layerName}'. Exclude that layer from any ray the " +
                    "player casts at the world."));
                return;
            }

            string detail = layer < PlayerLayerFixer.FirstUserLayer || string.IsNullOrEmpty(layerName)
                ? "The character's capsule is a real collider, so it blocks everyone's queries, " +
                  "including an interaction ray cast from its own camera, which starts inside it. " +
                  "Put the character on its own named layer so those casts can skip it."
                : $"Part of the hierarchy is not on '{layerName}'. A mask that skips the player " +
                  "will still hit whatever was left behind.";

            checks.Add(CharacterSetupCheck.Warning(
                "Character layer",
                detail,
                $"Create and assign '{PlayerLayerFixer.DefaultLayerName}'",
                () => EnsureAndAssignLayer(character)));
        }

        static void AddJumpCouplingCheck(
            List<CharacterSetupCheck> checks, GameObject character, KinematicMover mover)
        {
            var host = character.GetComponent<Character>();
            var jump = host != null ? host.GetProcessor<JumpProcessor>() : null;
            if (jump == null || mover == null) return;

            float riseInOneTick = jump.JumpSpeed * Time.fixedDeltaTime;
            float probe = mover.GroundProbeDistance;

            if (riseInOneTick > probe)
            {
                checks.Add(CharacterSetupCheck.Ok(
                    "Jump clears the ground probe",
                    $"One tick of jump ({riseInOneTick:0.###} m) exceeds the ground probe " +
                    $"({probe:0.###} m), so a held jump cannot re-fire on the next tick."));
                return;
            }

            checks.Add(CharacterSetupCheck.Warning(
                "Held jump can climb",
                $"One tick of jump is {riseInOneTick:0.###} m but the ground probe reaches " +
                $"{probe:0.###} m, so the character is still 'grounded' the tick after jumping and a " +
                "held jump button fires again. Raise Jump Speed, lower Ground Probe Distance, or " +
                "slow the fixed timestep. The three are coupled and no single Inspector shows it."));
        }

        static void AddGroundCheck(
            List<CharacterSetupCheck> checks, GameObject character, KinematicMover mover)
        {
            if (mover == null) return;

            // Start just above the feet so the cast begins outside the character's own capsule.
            Vector3 origin = character.transform.position + Vector3.up * 0.1f;
            const float reach = 50f;

            bool hitSomething = Physics.Raycast(
                origin, Vector3.down, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore);

            bool hitSelf = hitSomething && hit.collider.transform.IsChildOf(character.transform);

            checks.Add(hitSomething && !hitSelf
                ? CharacterSetupCheck.Ok(
                    "Ground below", $"'{hit.collider.name}' is {hit.distance:0.##} m below the feet.")
                : CharacterSetupCheck.Warning(
                    "Nothing below the character",
                    $"No collider within {reach:0} m beneath this character, so it will fall out of " +
                    "the scene on Play. Put it above some geometry with a collider."));
        }

        static void CreateVisualRoot(GameObject character, MoveInterpolator interpolator)
        {
            var visual = new GameObject("Visual");
            Undo.RegisterCreatedObjectUndo(visual, "Create Visual Root");

            Undo.SetTransformParent(visual.transform, character.transform, "Create Visual Root");
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.layer = character.layer;

            Undo.RecordObject(interpolator, "Create Visual Root");
            interpolator.VisualRoot = visual.transform;
            EditorUtility.SetDirty(interpolator);
        }

        static void EnsureAndAssignLayer(GameObject character)
        {
            if (!PlayerLayerFixer.TryEnsureLayer(
                    PlayerLayerFixer.DefaultLayerName, out int layer, out string error))
            {
                Debug.LogWarning($"[Building Blocks] {error}", character);
                return;
            }

            PlayerLayerFixer.AssignHierarchy(character, layer);
        }
    }
}
