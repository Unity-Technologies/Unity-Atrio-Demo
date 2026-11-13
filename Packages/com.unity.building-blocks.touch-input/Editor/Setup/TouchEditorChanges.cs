using UnityEditor;
using UnityEditor.SceneManagement;

namespace Unity.BuildingBlocks.TouchInput.Editor
{
    /// <summary>
    /// A counter that goes up when anything the Touch Input inspectors show might have changed.
    /// Inspectors compare it instead of rescanning every tick.
    /// </summary>
    [InitializeOnLoad]
    static class TouchEditorChanges
    {
        /// <summary>Goes up on every change event. Compare it with a stored value; the number itself means nothing.</summary>
        public static int Version { get; private set; }

        static TouchEditorChanges()
        {
            EditorApplication.hierarchyChanged += Bump;
            EditorApplication.projectChanged += Bump;
            Undo.undoRedoPerformed += Bump;
            ObjectChangeEvents.changesPublished += (ref ObjectChangeEventStream _) => Bump();
            EditorSceneManager.sceneOpened += (_, __) => Bump();
            EditorSceneManager.sceneClosed += _ => Bump();
        }

        static void Bump() => Version++;
    }
}
