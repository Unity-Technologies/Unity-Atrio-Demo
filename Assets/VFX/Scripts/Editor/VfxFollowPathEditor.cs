using System.Collections.Generic;
using Courtyard.VFX;
using UnityEditor;
using UnityEngine;

namespace Courtyard.VFXEditor
{
    /// <summary>
    /// Scene-view move handles for VfxFollowPath waypoints and destinations.
    /// The default inspector is kept unchanged.
    /// </summary>
    [CustomEditor(typeof(VfxFollowPath))]
    public class VfxFollowPathEditor : Editor
    {
        void OnSceneGUI()
        {
            var followPath = (VfxFollowPath)target;
            Quaternion handleRotation = Tools.pivotRotation == PivotRotation.Local
                ? followPath.transform.rotation
                : Quaternion.identity;

            DrawHandles(followPath, followPath.Waypoints,    "W", handleRotation, "Move Waypoint");
            DrawHandles(followPath, followPath.Destinations, "D", handleRotation, "Move Destination");
        }

        static void DrawHandles(VfxFollowPath followPath, List<Vector3> points, string labelPrefix,
                                Quaternion handleRotation, string undoName)
        {
            Transform t = followPath.transform;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 world = t.TransformPoint(points[i]);
                Handles.Label(world, labelPrefix + i);

                EditorGUI.BeginChangeCheck();
                Vector3 newWorld = Handles.PositionHandle(world, handleRotation);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(followPath, undoName);
                    points[i] = t.InverseTransformPoint(newWorld);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(followPath);
                    followPath.MarkDirty(); // OnValidate is not called for handle edits
                }
            }
        }
    }
}
