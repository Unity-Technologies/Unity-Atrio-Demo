using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Keeps tuning saved during Play. When Play stops, each save from
    /// <see cref="TouchTuning.Save(TouchRig, string)"/> is applied to the same rig in the scene, with Undo.
    /// </summary>
    /// <remarks>
    /// The rig is found by its scene and hierarchy path. A rig renamed or moved during Play is not found,
    /// and the console says so. Tuning that was not saved is discarded as usual.
    /// </remarks>
    [InitializeOnLoad]
    static class TouchTuningKeeper
    {
        struct Kept
        {
            public string ScenePath;
            public string HierarchyPath;
            public string Json;
        }

        static readonly List<Kept> s_Kept = new List<Kept>();

        static TouchTuningKeeper()
        {
            TouchTuning.Saved += OnSaved;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnSaved(TouchRig rig, string json)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                Remember(rig, json);
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode)
                RestoreAll();
            else if (change == PlayModeStateChange.ExitingEditMode)
                s_Kept.Clear();
        }

        /// <summary>Remembers the rig's saved JSON, replacing an earlier save of the same rig.</summary>
        internal static void Remember(TouchRig rig, string json)
        {
            if (rig == null)
                return;

            var kept = new Kept
            {
                ScenePath = rig.gameObject.scene.path,
                HierarchyPath = HierarchyPath(rig.transform),
                Json = json,
            };

            for (int i = 0; i < s_Kept.Count; i++)
            {
                if (s_Kept[i].ScenePath == kept.ScenePath && s_Kept[i].HierarchyPath == kept.HierarchyPath)
                {
                    s_Kept[i] = kept;
                    return;
                }
            }

            s_Kept.Add(kept);
        }

        internal static int PendingCount => s_Kept.Count;

        internal static void Forget() => s_Kept.Clear();

        /// <summary>Applies every remembered save onto its rig in the open scenes, then forgets them.</summary>
        internal static void RestoreAll()
        {
            foreach (Kept kept in s_Kept)
            {
                TouchRig rig = FindRig(kept.ScenePath, kept.HierarchyPath);
                if (rig == null)
                {
                    Debug.LogWarning($"Touch tuning saved in Play for '{kept.HierarchyPath}' was not kept: no Touch Rig there now.");
                    continue;
                }

                TouchTuningResult result = ApplyToScene(rig, kept.Json);
                Debug.Log($"Touch Rig '{rig.name}': kept the tuning saved in Play ({result}). Save the scene to keep it.", rig);
            }

            s_Kept.Clear();
        }

        /// <summary>Applies JSON to a rig in an edited scene, with Undo and prefab support, and marks the scene dirty.</summary>
        internal static TouchTuningResult ApplyToScene(TouchRig rig, string json)
        {
            TouchControl[] controls = rig.GetComponentsInChildren<TouchControl>(true);
            Undo.RecordObjects(controls, "Keep Touch Tuning");
            TouchTuningResult result = TouchTuning.Apply(rig, json);

            foreach (TouchControl control in controls)
                PrefabUtility.RecordPrefabInstancePropertyModifications(control);

            if (rig.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
            return result;
        }

        static TouchRig FindRig(string scenePath, string hierarchyPath)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.path != scenePath)
                    continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (TouchRig rig in root.GetComponentsInChildren<TouchRig>(true))
                    {
                        if (HierarchyPath(rig.transform) == hierarchyPath)
                            return rig;
                    }
                }
            }

            return null;
        }

        static string HierarchyPath(Transform t)
        {
            string path = t.name;
            for (Transform p = t.parent; p != null; p = p.parent)
                path = p.name + "/" + path;
            return path;
        }
    }
}
