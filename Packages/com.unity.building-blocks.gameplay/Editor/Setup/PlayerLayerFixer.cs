using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Creates the character's own layer and puts a character hierarchy on it.
    /// </summary>
    /// <remarks>
    /// A <see cref="KinematicMover"/> has an enabled <see cref="CapsuleCollider"/> and kinematic
    /// <see cref="Rigidbody"/>, which block the player's own queries (a ray from its camera hits its
    /// capsule). A dedicated layer lets those casts exclude it.
    /// </remarks>
    public static class PlayerLayerFixer
    {
        /// <summary>The layer name the First Person Player prefab is authored against.</summary>
        public const string DefaultLayerName = "Player";

        /// <summary>Layers 0-7 are Unity's; 8 upward are the project's to name.</summary>
        public const int FirstUserLayer = 8;

        const int k_LayerCount = 32;
        const string k_TagManagerPath = "ProjectSettings/TagManager.asset";

        /// <summary>
        /// The index of the layer called <paramref name="layerName"/>, or -1 when there is none.
        /// </summary>
        public static int FindLayer(string layerName) => LayerMask.NameToLayer(layerName);

        /// <summary>
        /// Ensures a layer called <paramref name="layerName"/> exists, claiming the lowest free user
        /// layer if it does not, and reports its index.
        /// </summary>
        /// <returns>
        /// True when the layer exists on return. False when all 24 user layers are taken;
        /// <paramref name="error"/> explains and the project is untouched.
        /// </returns>
        public static bool TryEnsureLayer(string layerName, out int index, out string error)
        {
            error = null;
            index = FindLayer(layerName);
            if (index >= 0) return true;

            SerializedObject tagManager = LoadTagManager();
            if (tagManager == null)
            {
                index = -1;
                error = $"Could not open {k_TagManagerPath}.";
                return false;
            }

            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                index = -1;
                error = $"{k_TagManagerPath} has no 'layers' array.";
                return false;
            }

            for (int i = FirstUserLayer; i < k_LayerCount && i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                tagManager.ApplyModifiedProperties();
                index = i;
                return true;
            }

            index = -1;
            error = $"All {k_LayerCount - FirstUserLayer} user layers are already named, so " +
                    $"'{layerName}' could not be added. Free one, or rename an existing layer and " +
                    "move the character onto it by hand.";
            return false;
        }

        /// <summary>
        /// Puts <paramref name="root"/> and every descendant on <paramref name="layer"/>, recorded
        /// with <see cref="Undo"/>. Returns the number of objects whose layer changed.
        /// </summary>
        /// <remarks>
        /// The whole hierarchy moves, so the camera, visuals and child objects are skipped by an
        /// "ignore the player" mask too.
        /// </remarks>
        public static int AssignHierarchy(GameObject root, int layer)
        {
            if (root == null || layer < 0 || layer >= k_LayerCount) return 0;

            int changed = 0;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject target = transform.gameObject;
                if (target.layer == layer) continue;

                Undo.RecordObject(target, "Set Character Layer");
                target.layer = layer;
                EditorUtility.SetDirty(target);
                changed++;
            }

            return changed;
        }

        /// <summary>
        /// True when <paramref name="root"/> and every descendant already sit on
        /// <paramref name="layer"/>.
        /// </summary>
        public static bool IsHierarchyOnLayer(GameObject root, int layer)
        {
            if (root == null) return false;

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.gameObject.layer != layer) return false;
            }

            return true;
        }

        /// <summary>
        /// The name of <paramref name="layer"/>, or an empty string when it is unnamed.
        /// </summary>
        public static string LayerName(int layer) =>
            layer >= 0 && layer < k_LayerCount ? LayerMask.LayerToName(layer) : string.Empty;

        static SerializedObject LoadTagManager()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(k_TagManagerPath);
            return assets != null && assets.Length > 0 ? new SerializedObject(assets[0]) : null;
        }
    }
}
