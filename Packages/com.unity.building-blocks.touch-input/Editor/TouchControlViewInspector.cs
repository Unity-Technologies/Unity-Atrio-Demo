using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// A one-line inspector for views. Their fields are drawn in the Look card of the control beside them.
    /// </summary>
    /// <remarks>A view with no control beside it keeps the default inspector.</remarks>
    [CustomEditor(typeof(TouchControlView), true)]
    [CanEditMultipleObjects]
    sealed class TouchControlViewInspector : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            BuildingBlocksStyles.Apply(root);

            var view = target as TouchControlView;
            TouchControl control = view != null ? view.GetComponent<TouchControl>() : null;
            if (control == null || targets.Length > 1)
            {
                UnityEditor.UIElements.InspectorElement.FillDefaultInspector(root, serializedObject, this);
                return root;
            }

            root.Add(Pointer($"Edited in the Look card of {control.ActionId} above.", "Show", () =>
            {
                InternalEditorUtility.SetIsInspectorExpanded(control, true);
                SessionState.SetBool($"BuildingBlocks.Panel.{InspectorState.OwnerId(control)}.look", true);
                ActiveEditorTracker.sharedTracker.ForceRebuild();
            }));
            return root;
        }

        /// <summary>One muted line and a link-style button.</summary>
        internal static VisualElement Pointer(string text, string link, System.Action onClick)
        {
            var row = new VisualElement();
            row.AddToClassList("blocks-touch-pointer");
            var label = new Label(text);
            label.AddToClassList("blocks-touch-pointer__text");
            row.Add(label);
            row.Add(Buttons.Link(link, onClick));
            return row;
        }
    }
}
