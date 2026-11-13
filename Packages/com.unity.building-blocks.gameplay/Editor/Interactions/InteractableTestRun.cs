using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// The Interactable inspector's Test card: enters Play mode and runs the interactable, skipping
    /// Starts. Moves the player into the Zone (if any) and switches the component and GameObject on
    /// for the run.
    /// </summary>
    /// <remarks>
    /// The request survives the domain reload in <see cref="SessionState"/> as the object's address in
    /// the loaded scenes, since instance ids change. The run waits two frames so every <c>Start</c> has run.
    /// </remarks>
    static class InteractableTestRun
    {
        const string k_PendingKey = "BuildingBlocks.InteractableTest.Pending";
        const string k_PlayerTag = "Player";
        const int k_FramesToWait = 2;

        static int s_WaitUntilFrame = -1;

        /// <summary>
        /// Whether Play can reach <paramref name="interactable"/>. In Edit mode it must be in an open
        /// scene, not a prefab asset or Prefab Mode.
        /// </summary>
        public static bool CanTest(Interactable interactable, out string reason)
        {
            reason = null;
            if (interactable == null) return false;
            if (Application.isPlaying) return true;

            if (EditorUtility.IsPersistent(interactable) || PrefabStageUtility.GetPrefabStage(interactable.gameObject) != null)
            {
                reason = "Place this interactable in a scene to test it.";
                return false;
            }

            return true;
        }

        /// <summary>Runs <paramref name="interactable"/> now in Play mode, or enters Play mode and runs it there.</summary>
        public static void Request(Interactable interactable)
        {
            if (!CanTest(interactable, out _)) return;

            if (Application.isPlaying)
            {
                Run(interactable);
                return;
            }

            string address = Address(interactable);
            if (address == null) return;
            SessionState.SetString(k_PendingKey, address);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(k_PendingKey, string.Empty).Length > 0)
            {
                s_WaitUntilFrame = Time.frameCount + k_FramesToWait;
                EditorApplication.update -= RunPending;
                EditorApplication.update += RunPending;
            }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                // Entering Play mode can fail (compile errors); a request left over would fire on the next Play.
                SessionState.EraseString(k_PendingKey);
                EditorApplication.update -= RunPending;
            }
        }

        static void RunPending()
        {
            if (!Application.isPlaying)
            {
                EditorApplication.update -= RunPending;
                return;
            }

            if (Time.frameCount < s_WaitUntilFrame) return;

            EditorApplication.update -= RunPending;
            string address = SessionState.GetString(k_PendingKey, string.Empty);
            SessionState.EraseString(k_PendingKey);

            Interactable interactable = Resolve(address);
            if (interactable == null)
            {
                Debug.LogWarning("Test: couldn't find the interactable in Play mode. Select it and press Play on its Test card again.");
                return;
            }

            Run(interactable);
        }

        /// <summary>
        /// Switches the interactable on, moves the player into its Zone, and plays it. Returns whether a
        /// run started, including one that ended at once.
        /// </summary>
        internal static bool Run(Interactable interactable) => Run(interactable, FindPlayer());

        /// <summary>The same, for a named <paramref name="player"/> (<c>null</c>: nobody is moved).</summary>
        internal static bool Run(Interactable interactable, KinematicMover player)
        {
            if (interactable == null) return false;

            // A run of instant steps, or of empty groups, starts and ends inside Play, or inside switching it
            // on for one that starts when enabled: that still counts as started.
            bool ended = false;
            void OnEnded(Interactable _) => ended = true;
            interactable.Finished += OnEnded;
            interactable.Interrupted += OnEnded;
            try
            {
                if (!interactable.gameObject.activeSelf)
                    interactable.gameObject.SetActive(true);

                if (interactable.Zone != null && player != null)
                    player.Teleport(StandPoint(interactable.Zone, player.transform), player.transform.rotation);

                // Moved first: an interactable that starts when enabled starts here, with the player in place.
                if (!interactable.enabled)
                    interactable.enabled = true;

                interactable.Play(player);
            }
            finally
            {
                interactable.Finished -= OnEnded;
                interactable.Interrupted -= OnEnded;
            }

            bool started = interactable.IsPlaying || ended;
            if (!started)
            {
                string why = !interactable.gameObject.activeInHierarchy ? "a parent GameObject is inactive"
                    : interactable.CooldownRemaining > 0f ? $"it's cooling down ({interactable.CooldownRemaining:0.0} s)"
                    : "the player is held by another interactable";
                Debug.LogWarning($"Test: {interactable.name} didn't start because {why}.", interactable);
            }

            return started;
        }

        /// <summary>
        /// Where the player stands for a zone test: the floor under the box centre, or the box's bottom
        /// face when there is no floor. Ignores the player's own colliders.
        /// </summary>
        internal static Vector3 StandPoint(TriggerVolume zone, Transform player)
        {
            Transform box = zone.transform;
            var collider = zone.GetComponent<BoxCollider>();
            if (collider == null) return box.position;

            Vector3 center = box.TransformPoint(collider.center);
            Vector3 bottom = box.TransformPoint(collider.center - new Vector3(0f, collider.size.y * 0.5f, 0f));
            float reach = Vector3.Distance(center, bottom) + 2f;

            RaycastHit[] hits = Physics.RaycastAll(center, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            Vector3 point = bottom;
            foreach (RaycastHit hit in hits)
            {
                if (player != null && hit.collider.transform.IsChildOf(player)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                point = hit.point;
            }

            return point;
        }

        static KinematicMover FindPlayer()
        {
            GameObject tagged = GameObject.FindWithTag(k_PlayerTag);
            return tagged != null ? tagged.GetComponentInParent<KinematicMover>() : null;
        }

        /// <summary>"scene|sibling/sibling/…|component": where the interactable sits in the loaded scenes.</summary>
        internal static string Address(Interactable interactable)
        {
            if (interactable == null) return null;
            Scene scene = interactable.gameObject.scene;
            int sceneIndex = -1;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i) == scene) sceneIndex = i;
            }

            if (sceneIndex < 0) return null;

            var path = new List<int>();
            for (Transform t = interactable.transform; t != null; t = t.parent)
                path.Insert(0, t.GetSiblingIndex());

            Interactable[] onObject = interactable.GetComponents<Interactable>();
            int component = System.Array.IndexOf(onObject, interactable);

            var text = new StringBuilder();
            text.Append(sceneIndex).Append('|');
            for (int i = 0; i < path.Count; i++)
            {
                if (i > 0) text.Append('/');
                text.Append(path[i]);
            }

            return text.Append('|').Append(component).ToString();
        }

        /// <summary>The interactable at an <see cref="Address"/>, or <c>null</c>.</summary>
        internal static Interactable Resolve(string address)
        {
            if (string.IsNullOrEmpty(address)) return null;
            string[] parts = address.Split('|');
            if (parts.Length != 3) return null;
            if (!int.TryParse(parts[0], out int sceneIndex) || sceneIndex < 0 || sceneIndex >= SceneManager.sceneCount) return null;
            if (!int.TryParse(parts[2], out int component)) return null;

            Scene scene = SceneManager.GetSceneAt(sceneIndex);
            if (!scene.isLoaded) return null;

            string[] steps = parts[1].Split('/');
            GameObject[] roots = scene.GetRootGameObjects();
            if (!int.TryParse(steps[0], out int root) || root < 0 || root >= roots.Length) return null;

            Transform current = roots[root].transform;
            for (int i = 1; i < steps.Length; i++)
            {
                if (!int.TryParse(steps[i], out int child) || child < 0 || child >= current.childCount) return null;
                current = current.GetChild(child);
            }

            Interactable[] onObject = current.GetComponents<Interactable>();
            return component >= 0 && component < onObject.Length ? onObject[component] : null;
        }
    }
}
