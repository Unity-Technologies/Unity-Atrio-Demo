#if BB_TOUCH_HAS_INPUTSYSTEM
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// Inspector for <see cref="TouchInputBridge"/>. It shows one line pointing at the Touch Rig's Mappings card,
    /// so no value is on screen twice.
    /// </summary>
    [CustomEditor(typeof(TouchInputBridge))]
    sealed class TouchInputBridgeInspector : UnityEditor.Editor
    {
        public override UnityEngine.UIElements.VisualElement CreateInspectorGUI()
        {
            var root = new UnityEngine.UIElements.VisualElement();
            BuildingBlocksStyles.Apply(root);

            var bridge = target as TouchInputBridge;
            TouchRig rig = bridge != null ? bridge.GetComponent<TouchRig>() : null;
            if (rig == null || targets.Length > 1)
            {
                UnityEditor.UIElements.InspectorElement.FillDefaultInspector(root, serializedObject, this);
                return root;
            }

            root.Add(TouchControlViewInspector.Pointer("Mapped in the Touch Rig inspector above.", "Show", () =>
            {
                UnityEditorInternal.InternalEditorUtility.SetIsInspectorExpanded(rig, true);
                SessionState.SetBool($"BuildingBlocks.Panel.{InspectorState.OwnerId(rig)}.mappings", true);
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }));
            return root;
        }

        /// <summary>
        /// What the device carries: the live plan while the bridge runs, else the plan it would make now.
        /// </summary>
        /// <remarks>Problems are only reported for the second case. A live device logged them when it was built.</remarks>
        internal static IReadOnlyList<TouchRigLayout.Entry> Describe(TouchInputBridge bridge, List<TouchControl> controls, List<string> problems)
        {
            problems.Clear();
            if (bridge.Device != null)
                return bridge.Entries;

            TouchInputBridge.CollectControls(bridge.Rig, controls);
            return TouchRigLayout.Plan(controls, problems);
        }
    }
}
#endif
