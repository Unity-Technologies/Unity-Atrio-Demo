using UnityEngine;
using UnityEngine.EventSystems;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>Where a view shows its control.</summary>
    public enum TouchVisibility
    {
        /// <summary>Everywhere, including the Editor's Game view. The default.</summary>
        Always,

        /// <summary>
        /// Only on phones and tablets, including the Device Simulator. Elsewhere the control is invisible and
        /// takes no touches, but still works from C#.
        /// </summary>
        MobilePlatformsOnly,
    }

    /// <summary>
    /// Base class for uGUI views. It turns pointer events into samples for the <see cref="TouchControl"/> beside
    /// it and draws the control each frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The GameObject or a child needs a raycast-target <see cref="UnityEngine.UI.Graphic"/> to receive touches.
    /// The view cancels the control on disable, focus loss, pause, and when it is hidden.
    /// </para>
    /// <para>
    /// To derive, implement <see cref="Draw"/>. Only change the control through <see cref="TouchControl.Feed"/>
    /// and its sizing properties.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public abstract class TouchControlView : MonoBehaviour,
        IPointerDownHandler, IDragHandler, IPointerUpHandler, IInitializePotentialDragHandler
    {
        /// <summary>Default <see cref="FadeSeconds"/>.</summary>
        public const float k_DefaultFadeSeconds = 0.12f;

        [SerializeField]
        [Tooltip("Show the control everywhere, or only on phones and tablets.")]
        TouchVisibility m_ShowOn = TouchVisibility.Always;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Opacity while no finger is on the control.")]
        float m_IdleAlpha = 1f;

        [SerializeField]
        [Range(0f, 1f)]
        [Tooltip("Opacity while a finger is on the control.")]
        float m_ActiveAlpha = 1f;

        [SerializeField]
        [Min(0f)]
        [Tooltip("Fade time between idle and active opacity in seconds, where 0 snaps.")]
        float m_FadeSeconds = k_DefaultFadeSeconds;

        TouchControl m_Control;
        RectTransform m_Rect;
        Canvas m_Canvas;
        CanvasGroup m_Group;
        float m_Alpha = -1f;
        bool m_Live;
        bool m_WasShown = true;

        /// <summary>A view whose idle opacity defaults to 1.</summary>
        protected TouchControlView() { }

        /// <summary>A view whose <see cref="IdleAlpha"/> starts at <paramref name="defaultIdleAlpha"/> (a joystick's is 0.4).</summary>
        protected TouchControlView(float defaultIdleAlpha) => m_IdleAlpha = defaultIdleAlpha;

        /// <summary>Where the control is shown. Default <see cref="TouchVisibility.Always"/>.</summary>
        public TouchVisibility ShowOn
        {
            get => m_ShowOn;
            set => m_ShowOn = value;
        }

        /// <summary>Opacity with no finger on the control, 0..1.</summary>
        public float IdleAlpha
        {
            get => m_IdleAlpha;
            set => m_IdleAlpha = Mathf.Clamp01(value);
        }

        /// <summary>Opacity with a finger on the control, 0..1. Default 1.</summary>
        public float ActiveAlpha
        {
            get => m_ActiveAlpha;
            set => m_ActiveAlpha = Mathf.Clamp01(value);
        }

        /// <summary>Seconds of unscaled time to fade between the two opacities. Default 0.12; 0 snaps.</summary>
        public float FadeSeconds
        {
            get => m_FadeSeconds;
            set => m_FadeSeconds = Mathf.Max(0f, value);
        }

        /// <summary>The opacity the view last applied (-1 before the first frame).</summary>
        public float CurrentAlpha => m_Alpha;

        /// <summary>Whether a control set to <paramref name="showOn"/> is shown on a platform that is (or is not) mobile.</summary>
        public static bool IsShown(TouchVisibility showOn, bool isMobilePlatform) =>
            showOn == TouchVisibility.Always || isMobilePlatform;

        /// <summary>The opacity a view aims for: 0 when hidden, else the active or the idle opacity.</summary>
        public static float TargetAlpha(bool shown, bool active, float idleAlpha, float activeAlpha) =>
            !shown ? 0f : active ? activeAlpha : idleAlpha;

        /// <summary>
        /// One frame of the fade: moves <paramref name="current"/> toward <paramref name="target"/>. A fade of 0,
        /// or a first frame (current below 0), snaps.
        /// </summary>
        public static float StepAlpha(float current, float target, float fadeSeconds, float deltaSeconds)
        {
            if (current < 0f || fadeSeconds <= 0f)
                return target;

            return Mathf.MoveTowards(current, target, Mathf.Max(0f, deltaSeconds) / fadeSeconds);
        }

        /// <summary>The control this view feeds and draws: the <see cref="TouchControl"/> on the same GameObject.</summary>
        public TouchControl Control => m_Control != null ? m_Control : m_Control = GetComponent<TouchControl>();

        /// <summary>This view's rect.</summary>
        protected RectTransform Rect => m_Rect != null ? m_Rect : m_Rect = transform as RectTransform;

        /// <summary>
        /// The camera that maps this view's canvas to the screen, or <c>null</c> for a Screen Space Overlay
        /// canvas. Pass it to <see cref="RectTransformUtility"/>.
        /// </summary>
        protected Camera EventCamera
        {
            get
            {
                if (m_Canvas == null)
                    m_Canvas = GetComponentInParent<Canvas>();

                if (m_Canvas == null)
                    return null;

                Canvas root = m_Canvas.rootCanvas;
                return root.renderMode == RenderMode.ScreenSpaceOverlay ? null : root.worldCamera;
            }
        }

        /// <summary>Converts screen pixels to a world point on this view's plane.</summary>
        protected Vector3 ScreenToWorld(Vector2 screenPosition)
        {
            RectTransformUtility.ScreenPointToWorldPointInRectangle(Rect, screenPosition, EventCamera, out Vector3 world);
            return world;
        }

        /// <summary>Converts a world point to screen pixels.</summary>
        protected Vector2 WorldToScreen(Vector3 worldPosition) =>
            RectTransformUtility.WorldToScreenPoint(EventCamera, worldPosition);

        /// <summary>Feeds a <see cref="PointerPhase.Began"/> sample for this pointer.</summary>
        public void OnPointerDown(PointerEventData eventData) => Feed(eventData, PointerPhase.Began);

        /// <summary>Feeds a <see cref="PointerPhase.Moved"/> sample for this pointer.</summary>
        public void OnDrag(PointerEventData eventData) => Feed(eventData, PointerPhase.Moved);

        /// <summary>Feeds a <see cref="PointerPhase.Ended"/> sample for this pointer.</summary>
        public void OnPointerUp(PointerEventData eventData) => Feed(eventData, PointerPhase.Ended);

        /// <summary>Turns the drag threshold off so the first pixel of movement reaches the control.</summary>
        public void OnInitializePotentialDrag(PointerEventData eventData) => eventData.useDragThreshold = false;

        /// <summary>Draws the control's current state. Called once a frame from <c>LateUpdate</c> while enabled.</summary>
        protected abstract void Draw();

        /// <summary>Called at the end of <see cref="Activate"/>, when <see cref="Control"/> has been found (it may still be <c>null</c>).</summary>
        protected virtual void OnActivated() { }

        /// <summary>Called at the start of <see cref="Deactivate"/>, before the control is cancelled.</summary>
        protected virtual void OnDeactivated() { }

        void Feed(PointerEventData eventData, PointerPhase phase)
        {
            TouchControl control = Control;
            if (control == null)
                return;

            control.Feed(new PointerSample(eventData.pointerId, eventData.position, phase, Time.unscaledTimeAsDouble));
        }

        /// <summary>
        /// Whether the control counts as active for the fade. By default, whether a finger is on it.
        /// </summary>
        protected virtual bool IsActive => Control != null && Control.IsPressed;

        void LateUpdate()
        {
            if (m_Live && Control != null)
                DrawNow(Time.unscaledDeltaTime, UnityEngine.Device.Application.isMobilePlatform, Control.Rig == null || Control.Rig.IsShown);
        }

        /// <summary>
        /// One frame of fade, visibility and <see cref="Draw"/>. Called from <c>LateUpdate</c>, and by hand in tests.
        /// </summary>
        internal void DrawNow(float deltaSeconds, bool isMobilePlatform, bool rigShown = true)
        {
            if (Control == null)
                return;

            bool shown = rigShown && IsShown(m_ShowOn, isMobilePlatform);
            if (m_WasShown && !shown)
                Control.Cancel();
            m_WasShown = shown;

            float target = TargetAlpha(shown, IsActive, m_IdleAlpha, m_ActiveAlpha);
            m_Alpha = StepAlpha(m_Alpha, target, m_FadeSeconds, deltaSeconds);
            ApplyGroup(m_Alpha, shown);
            Draw();
        }

        void ApplyGroup(float alpha, bool shown)
        {
            if (m_Group == null && !TryGetComponent(out m_Group))
            {
                // Nothing to fade or hide, so leave rigs built before 0.1.0 untouched.
                if (Mathf.Approximately(alpha, 1f) && shown)
                    return;

                m_Group = gameObject.AddComponent<CanvasGroup>();
            }

            m_Group.alpha = alpha;
            m_Group.blocksRaycasts = shown;
            m_Group.interactable = shown;
        }

        void OnEnable() => Activate();

        void OnDisable() => Deactivate();

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus && m_Control != null)
                m_Control.Cancel();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && m_Control != null)
                m_Control.Cancel();
        }

        /// <summary>
        /// The body of <c>OnEnable</c>: finds the sibling control and warns when there is none. Internal because
        /// EditMode tests never run <c>OnEnable</c>.
        /// </summary>
        internal void Activate()
        {
            if (m_Live)
                return;

            m_Live = true;
            m_Alpha = -1f;
            m_WasShown = true;
            m_Control = GetComponent<TouchControl>();
            m_Rect = transform as RectTransform;
            m_Canvas = GetComponentInParent<Canvas>();

            if (m_Control == null)
            {
                Debug.LogWarning($"{GetType().Name} on '{name}' has no TouchControl beside it: nothing to feed. " +
                                 "Add a Touch Joystick or Touch Button to this GameObject.", this);
            }

            OnActivated();
        }

        /// <summary>The body of <c>OnDisable</c>: cancels the control so it never stays pressed. See <see cref="Activate"/>.</summary>
        internal void Deactivate()
        {
            if (!m_Live)
                return;

            m_Live = false;
            OnDeactivated();

            if (m_Control != null)
                m_Control.Cancel();
        }
    }
}
