using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Creation paths for <see cref="BuildingBlocksMenu"/>: trigger volumes, interactables, the wired
    /// pair, and the player. Every change goes through <see cref="Undo"/>; nothing asks for confirmation.
    /// </summary>
    /// <remarks>
    /// The player is the package's <c>FirstPersonPlayer</c> prefab (<see cref="PlaygroundPaths"/>),
    /// checked for a mover and Character before it is instantiated.
    /// </remarks>
    static class BlockSpawner
    {
        /// <summary>The parent the combined item makes: an <see cref="InteractableName"/> and a <see cref="TriggerVolumeName"/> under it.</summary>
        public const string InteractionName = "Interaction";

        /// <summary>The object the Interactable sits on when the package creates one.</summary>
        public const string InteractableName = "Interactable";

        /// <summary>The object a Trigger Volume sits on when the package creates one.</summary>
        public const string TriggerVolumeName = "Trigger Volume";

        /// <summary>
        /// Creates a Trigger Volume GameObject under <paramref name="parent"/>, else at the Scene view
        /// pivot, with a trigger Box Collider. Selected on return.
        /// </summary>
        public static GameObject CreateTriggerVolume(GameObject parent = null, string name = TriggerVolumeName)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Trigger Volume");

            if (parent != null) GameObjectUtility.SetParentAndAlign(go, parent);
            else go.transform.position = ScenePivot();

            Undo.AddComponent<TriggerVolume>(go);
            Selection.activeGameObject = go;
            MarkDirty(go);
            return go;
        }

        /// <summary>
        /// Creates a Trigger Volume Group with two member volumes side by side, under
        /// <paramref name="parent"/>, else at the Scene view pivot. Selected on return.
        /// </summary>
        public static GameObject CreateTriggerVolumeGroup(GameObject parent = null, string name = "Trigger Volume Group")
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Trigger Volume Group");

            if (parent != null) GameObjectUtility.SetParentAndAlign(go, parent);
            else go.transform.position = ScenePivot();

            var group = Undo.AddComponent<TriggerVolumeGroup>(go);

            GameObject a = CreateTriggerVolume(go, TriggerVolumeName + " A");
            GameObject b = CreateTriggerVolume(go, TriggerVolumeName + " B");
            a.transform.localPosition = new Vector3(-1.5f, 0f, 0f);
            b.transform.localPosition = new Vector3(1.5f, 0f, 0f);

            Undo.RecordObject(group, "Add group members");
            group.AddVolume(a.GetComponent<TriggerVolume>());
            group.AddVolume(b.GetComponent<TriggerVolume>());

            Selection.activeGameObject = go;
            MarkDirty(go);
            return go;
        }

        /// <summary>
        /// Turns <paramref name="go"/> into a Trigger Volume in place and makes every collider a trigger.
        /// Keeps an existing BoxCollider's shape, fits a new box to the renderer bounds, or uses the
        /// 2 × 2 × 2 default. One Undo step.
        /// </summary>
        /// <returns>The volume, or <c>null</c> when the object already is one. <paramref name="message"/> says what happened.</returns>
        public static TriggerVolume MakeTriggerVolume(GameObject go, out string message)
        {
            TriggerVolume volume = ConvertToTriggerVolume(go, out SpawnNote note);
            message = note.Message;
            return volume;
        }

        /// <summary>
        /// <see cref="MakeTriggerVolume(GameObject, out string)"/>, with the outcome as a <see cref="SpawnNote"/>.
        /// </summary>
        public static TriggerVolume ConvertToTriggerVolume(GameObject go, out SpawnNote note)
        {
            if (go == null)
            {
                note = SpawnNote.Refused("Nothing to turn into a Trigger Volume.");
                return null;
            }

            if (go.GetComponent<TriggerVolume>() != null)
            {
                note = SpawnNote.Refused($"{go.name} is already a Trigger Volume.");
                return null;
            }

            // The volume's Reset edits colliders outside Undo; record them first so Ctrl+Z restores them.
            Collider[] colliders = go.GetComponents<Collider>();
            Undo.RecordObjects(colliders, "Make Trigger Volume");
            var existingBox = go.GetComponent<BoxCollider>();
            bool hadBox = existingBox != null;
            Vector3 size = hadBox ? existingBox.size : Vector3.zero;
            Vector3 center = hadBox ? existingBox.center : Vector3.zero;
            bool wasSolid = false;
            foreach (Collider collider in colliders)
                if (!collider.isTrigger) wasSolid = true;

            var volume = Undo.AddComponent<TriggerVolume>(go);
            BoxCollider box = volume.Box;
            if (hadBox)
            {
                box.size = size;
                box.center = center;
            }
            else if (TryGetLocalBounds(go, out Bounds bounds))
            {
                box.size = bounds.size;
                box.center = bounds.center;
            }

            Selection.activeGameObject = go;
            MarkDirty(go);
            note = wasSolid
                ? SpawnNote.Changed($"{go.name} is a Trigger Volume. Its collider is a trigger now, so it no longer blocks anything; Ctrl+Z if it should.")
                : SpawnNote.Plain($"{go.name} is a Trigger Volume.");
            return volume;
        }

        /// <summary>
        /// Adds an <see cref="Interactable"/> seeded with one "Lock" group holding a
        /// <see cref="LockPlayerStep"/>, so a new interactable locks the player's control until removed.
        /// </summary>
        public static Interactable AddInteractable(GameObject go)
        {
            var interactable = Undo.AddComponent<Interactable>(go);
            interactable.Groups.Add(new StepGroup(new LockPlayerStep()) { Name = "Lock" });
            EditorUtility.SetDirty(interactable);
            return interactable;
        }

        /// <summary>Puts an <see cref="Interactable"/> on <paramref name="go"/>. <c>null</c> when it already has one.</summary>
        public static Interactable MakeInteractable(GameObject go, out string message)
        {
            if (go == null)
            {
                message = "Nothing to turn into an Interactable.";
                return null;
            }

            if (go.GetComponent<Interactable>() != null)
            {
                message = $"{go.name} is already an Interactable.";
                return null;
            }

            var interactable = AddInteractable(go);
            Selection.activeGameObject = go;
            MarkDirty(go);
            message = $"{go.name} is an Interactable. Add steps from its inspector.";
            return interactable;
        }

        /// <summary>The object's renderer bounds in its own space, when it has a renderer.</summary>
        static bool TryGetLocalBounds(GameObject go, out Bounds bounds)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                bounds = renderer.localBounds;
                return true;
            }

            bounds = default;
            return false;
        }

        /// <summary>A new GameObject at the Scene view pivot with an Interactable on it. Selected on return.</summary>
        public static GameObject CreateInteractionObject(string name = InteractionName)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Interaction");
            go.transform.position = ScenePivot();
            AddInteractable(go);
            Selection.activeGameObject = go;
            MarkDirty(go);
            return go;
        }

        /// <summary>
        /// Adds an Interactable to <paramref name="context"/>, or a new <see cref="InteractableName"/> child
        /// when it already has one, or a new object at the Scene view pivot. Selected on return.
        /// </summary>
        /// <returns>The GameObject that carries the interactable.</returns>
        public static GameObject CreateInteractable(GameObject context = null)
        {
            if (context == null)
                return CreateInteractionObject();

            if (context.GetComponent<Interactable>() == null)
            {
                AddInteractable(context);
                Selection.activeGameObject = context;
                MarkDirty(context);
                return context;
            }

            return NewInteractable(context, null);
        }

        /// <summary>
        /// Creates a wired Interactable + Trigger Volume under an <see cref="InteractionName"/> parent
        /// (<see cref="InteractionWiring.Connect"/>). If <paramref name="context"/> already has one half,
        /// only the other is added (<see cref="AddTriggerVolumeTo"/>, <see cref="AddInteractableTo"/>).
        /// The interactable's object is selected on return.
        /// </summary>
        /// <returns>The new parent, or the one half that was added.</returns>
        public static GameObject CreateInteraction(GameObject context = null)
        {
            if (context != null)
            {
                var existing = context.GetComponent<Interactable>();
                if (existing != null) return AddTriggerVolumeTo(existing);

                var volume = context.GetComponent<TriggerVolume>();
                if (volume != null) return AddInteractableTo(volume);
            }

            var root = new GameObject(InteractionName);
            Undo.RegisterCreatedObjectUndo(root, "Create Interaction");
            if (context != null) GameObjectUtility.SetParentAndAlign(root, context);
            else root.transform.position = ScenePivot();

            GameObject body = NewInteractable(root, null);
            GameObject zone = CreateTriggerVolume(root, TriggerVolumeName);
            InteractionWiring.Connect(zone.GetComponent<TriggerVolume>(), body.GetComponent<Interactable>());

            Selection.activeGameObject = body;
            MarkDirty(root);
            return root;
        }

        /// <summary>
        /// A wired <see cref="TriggerVolumeName"/> right after <paramref name="interactable"/> under the same
        /// parent. Selected on return.
        /// </summary>
        public static GameObject AddTriggerVolumeTo(Interactable interactable)
        {
            Transform parent = interactable.transform.parent;
            GameObject zone = CreateTriggerVolume(parent != null ? parent.gameObject : null, TriggerVolumeName);

            if (parent != null) zone.transform.SetSiblingIndex(interactable.transform.GetSiblingIndex() + 1);
            else zone.transform.position = interactable.transform.position;

            InteractionWiring.Connect(zone.GetComponent<TriggerVolume>(), interactable);
            Selection.activeGameObject = zone;
            return zone;
        }

        /// <summary>
        /// A wired <see cref="InteractableName"/> right before <paramref name="volume"/> under the same
        /// parent. Selected on return.
        /// </summary>
        public static GameObject AddInteractableTo(TriggerVolume volume)
        {
            Transform parent = volume.transform.parent;
            GameObject body = NewInteractable(parent != null ? parent.gameObject : null, volume.transform);
            InteractionWiring.Connect(volume, body.GetComponent<Interactable>());
            Selection.activeGameObject = body;
            return body;
        }

        /// <summary>
        /// A new <see cref="InteractableName"/> object with an Interactable: under <paramref name="parent"/>
        /// (aligned) or at <paramref name="beside"/>'s position at the root; placed just before
        /// <paramref name="beside"/> when both share the parent.
        /// </summary>
        static GameObject NewInteractable(GameObject parent, Transform beside)
        {
            var body = new GameObject(InteractableName);
            Undo.RegisterCreatedObjectUndo(body, "Create Interactable");

            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(body, parent);
                if (beside != null && beside.parent == parent.transform)
                    body.transform.SetSiblingIndex(beside.GetSiblingIndex());
            }
            else if (beside != null)
            {
                body.transform.position = beside.position;
            }
            else
            {
                body.transform.position = ScenePivot();
            }

            AddInteractable(body);
            MarkDirty(body);
            return body;
        }

        /// <summary>
        /// The player prefab from the package's Playground folder, checked for a mover and Character.
        /// <c>null</c> when missing.
        /// </summary>
        public static GameObject LoadPlayerPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlaygroundPaths.PlayerPrefab);
            return prefab != null &&
                   prefab.GetComponent<KinematicMover>() != null &&
                   prefab.GetComponent<Character>() != null
                ? prefab
                : null;
        }

        /// <summary>
        /// Instantiates the player in front of the Scene view camera, sets its layer, points its input at the
        /// project's input copy and selects it. One Undo step.
        /// </summary>
        /// <remarks>
        /// The input copy is created under <see cref="PlaygroundPaths.ProjectFolder"/> on first use. The
        /// instance stays linked to the package prefab; use a Prefab Variant to keep edits.
        /// <paramref name="inputActionsPath"/> defaults to <see cref="PlaygroundPaths.ProjectInputActions"/>.
        /// <c>null</c> with a message when the prefab is missing.
        /// </remarks>
        public static GameObject AddCharacter(out string message, string inputActionsPath = null)
        {
            string inputPath = inputActionsPath ?? PlaygroundPaths.ProjectInputActions;

            GameObject prefab = LoadPlayerPrefab();
            if (prefab == null)
            {
                message = $"The player prefab was not found at {PlaygroundPaths.PlayerPrefab}. Reinstall the package.";
                return null;
            }

            // A second character is allowed, but Player-tag volumes will detect both, so warn.
            GameObject alreadyTagged = GameObject.FindWithTag(TriggerVolume.k_DefaultTag);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            if (instance == null)
            {
                message = "The prefab could not be instantiated.";
                return null;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add Character");
            Undo.RegisterCreatedObjectUndo(instance, "Add Character");
            instance.transform.position = SpawnPosition();

            // The prefab uses layer 8, which is only named if this project named it.
            string layerNote = string.Empty;
            if (PlayerLayerFixer.TryEnsureLayer(PlayerLayerFixer.DefaultLayerName, out int layer, out string error))
            {
                PlayerLayerFixer.AssignHierarchy(instance, layer);
            }
            else
            {
                layerNote = $" The Player layer could not be set up: {error}";
            }

            // Bindings live in an editable project copy of the package input asset, made on first use.
            string inputNote = string.Empty;
            var inputAsset = BuildingBlocksInputAsset.EnsureProjectCopy(out bool createdInput, inputPath);
            if (inputAsset != null)
            {
                var missing = new List<string>();
                BuildingBlocksInputAsset.Bind(instance, inputAsset, missing);
                inputNote = createdInput
                    ? $" Its input bindings are in {inputPath} (created now); edit them there."
                    : $" Its input bindings are in {inputPath}.";
                string missingNote = BuildingBlocksInputAsset.DescribeMissing(inputAsset, missing);
                if (missingNote != null)
                {
                    inputNote += " " + missingNote;
                    Debug.LogWarning($"[Building Blocks] {missingNote}", instance);
                }
            }

            string playerNote = string.Empty;
            if (alreadyTagged != null)
            {
                playerNote = $" {alreadyTagged.name} was already tagged {TriggerVolume.k_DefaultTag}; trigger volumes will report both. Ctrl+Z if one was the plan.";
                Debug.LogWarning($"{alreadyTagged.name} is already tagged {TriggerVolume.k_DefaultTag}. {instance.name} was added anyway; trigger volumes detecting the tag will report both. Ctrl+Z if one was the plan.", instance);
            }

            Selection.activeGameObject = instance;
            MarkDirty(instance);
            Undo.CollapseUndoOperations(undoGroup);
            message = $"{instance.name} is in your scene. Its Character inspector shows the setup.{inputNote}{layerNote}{playerNote}";
            return instance;
        }

        /// <summary>Where the Scene view is looking, or the origin.</summary>
        public static Vector3 ScenePivot()
        {
            SceneView view = SceneView.lastActiveSceneView;
            return view != null ? view.pivot : Vector3.zero;
        }

        /// <summary>
        /// A spot in front of the Scene view camera, on the ground when there is one.
        /// </summary>
        public static Vector3 SpawnPosition()
        {
            SceneView view = SceneView.lastActiveSceneView;
            if (view == null || view.camera == null) return new Vector3(0f, 1f, 0f);

            Transform cam = view.camera.transform;
            Vector3 ahead = cam.position + cam.forward * 4f;

            return Physics.Raycast(ahead + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 50f)
                ? hit.point
                : new Vector3(ahead.x, 1f, ahead.z);
        }

        static void MarkDirty(GameObject go)
        {
            if (go != null && go.scene.IsValid()) EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }

    /// <summary>
    /// What a spawner call did: the message, and whether it changed an existing object the user may want to undo.
    /// </summary>
    readonly struct SpawnNote
    {
        /// <summary>The sentence for the user.</summary>
        public readonly string Message;

        /// <summary>True when an existing object was altered in a way Ctrl+Z reverses.</summary>
        public readonly bool ChangedExisting;

        /// <summary>True when nothing was done.</summary>
        public readonly bool WasRefused;

        SpawnNote(string message, bool changedExisting, bool refused)
        {
            Message = message;
            ChangedExisting = changedExisting;
            WasRefused = refused;
        }

        public static SpawnNote Plain(string message) => new SpawnNote(message, false, false);
        public static SpawnNote Changed(string message) => new SpawnNote(message, true, false);
        public static SpawnNote Refused(string message) => new SpawnNote(message, false, true);
    }
}
