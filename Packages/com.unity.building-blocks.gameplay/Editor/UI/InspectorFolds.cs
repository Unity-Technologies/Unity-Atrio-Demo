using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// Opens and closes a component's fold in the Inspector, so a panel above it can act as a table
    /// of contents.
    /// </summary>
    /// <remarks>
    /// Unity stores fold state per type, not per instance. This class uses the tracker's per-editor
    /// visibility while an inspector is up and falls back to the type flag after a rebuild.
    /// </remarks>
    static class InspectorFolds
    {
        /// <summary>Whether <paramref name="component"/>'s inspector is currently expanded.</summary>
        public static bool IsExpanded(Component component)
        {
            if (component == null) return false;

            int index = EditorIndex(component);
            int visible = index >= 0 ? ActiveEditorTracker.sharedTracker.GetVisible(index) : -1;
            return visible >= 0 ? visible == 1 : InternalEditorUtility.GetIsInspectorExpanded(component);
        }

        /// <summary>Expands or collapses <paramref name="component"/>'s inspector.</summary>
        /// <remarks>Also records the state as the type's default, as clicking the header does.</remarks>
        public static void Set(Component component, bool expanded)
        {
            if (component == null) return;

            InternalEditorUtility.SetIsInspectorExpanded(component, expanded);

            // Repaint, never rebuild: a rebuild makes every same-type sibling re-read the shared type flag.
            int index = EditorIndex(component);
            if (index >= 0) ActiveEditorTracker.sharedTracker.SetVisible(index, expanded ? 1 : 0);

            InternalEditorUtility.RepaintAllViews();
        }

        /// <summary>Flips <paramref name="component"/>'s inspector fold.</summary>
        public static void Toggle(Component component) => Set(component, !IsExpanded(component));

        /// <summary>
        /// The index of <paramref name="component"/>'s editor in the shared tracker, or -1 when not shown.
        /// </summary>
        static int EditorIndex(Component component)
        {
            UnityEditor.Editor[] editors = ActiveEditorTracker.sharedTracker.activeEditors;
            for (int i = 0; i < editors.Length; i++)
            {
                if (editors[i] != null && editors[i].target == component) return i;
            }

            return -1;
        }
    }
}
