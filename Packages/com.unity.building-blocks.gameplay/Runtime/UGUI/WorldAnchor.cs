using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Keeps a screen-space UI element over a point in the world: interaction prompts, nametags, health
    /// bars. Put it on a RectTransform under a Canvas.
    /// </summary>
    /// <remarks>
    /// The element stays the same size on screen at any distance. It is faded out while the point is
    /// behind the camera, and stops tracking while the element is inactive.
    /// </remarks>
    [AddComponentMenu("Building Blocks/World Anchor")]
    [RequireComponent(typeof(RectTransform))]
    [DefaultExecutionOrder(10000)]
    public sealed class WorldAnchor : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The world object to follow.")]
        Transform m_Target;

        [SerializeField]
        [Tooltip("Offset from the target, in the target's local space, in metres.")]
        Vector3 m_Offset;

        [SerializeField]
        [Tooltip("The camera that sees the target. Empty uses the main camera.")]
        Camera m_Camera;

        RectTransform m_Rect;
        Canvas m_Canvas;
        CanvasGroup m_Group;

        /// <summary>The world object followed.</summary>
        public Transform Target { get => m_Target; set => m_Target = value; }

        /// <summary>Offset from the target in its local space, in metres.</summary>
        public Vector3 Offset { get => m_Offset; set => m_Offset = value; }

        /// <summary>The camera used, or <c>null</c> for the main camera.</summary>
        public Camera Camera { get => m_Camera; set => m_Camera = value; }

        /// <summary>Whether the point was in front of the camera on the last update.</summary>
        public bool IsInFront { get; private set; }

        void Awake()
        {
            m_Rect = (RectTransform)transform;
            m_Group = GetComponent<CanvasGroup>();
            if (m_Group == null)
                m_Group = gameObject.AddComponent<CanvasGroup>();
        }

        void LateUpdate() => Track();

        /// <summary>Moves the element over the target now. Runs every frame after the camera has moved.</summary>
        public void Track()
        {
            if (m_Canvas == null)
                m_Canvas = GetComponentInParent<Canvas>();
            Camera camera = m_Camera != null ? m_Camera : Camera.main;
            if (m_Target == null || camera == null || m_Canvas == null)
            {
                SetInFront(false);
                return;
            }

            Vector3 screen = camera.WorldToScreenPoint(m_Target.TransformPoint(m_Offset));
            SetInFront(screen.z > 0f);
            if (!IsInFront)
                return;

            var parent = m_Rect.parent as RectTransform;
            Camera uiCamera = m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : m_Canvas.worldCamera;
            if (parent != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, uiCamera, out Vector2 local))
                m_Rect.localPosition = local;
        }

        void SetInFront(bool inFront)
        {
            IsInFront = inFront;
            if (m_Group != null)
                m_Group.alpha = inFront ? 1f : 0f;
        }
    }
}
