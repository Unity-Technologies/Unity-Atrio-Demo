using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>How much of an <see cref="InputPrompt"/> is showing.</summary>
    public enum InputPromptState
    {
        /// <summary>Nothing shows.</summary>
        Hidden = 0,
        /// <summary>Only the hint dot: the player is near enough to notice it, not yet inside the zone.</summary>
        Hint = 1,
        /// <summary>Glyph and label: the player is inside the zone and the button works.</summary>
        Full = 2,
    }

    /// <summary>What an <see cref="IInputPromptView"/> draws this frame.</summary>
    public readonly struct InputPromptContent
    {
        /// <summary>The prompt being drawn.</summary>
        public readonly InputPrompt Prompt;
        /// <summary>The transform the prompt sits on in the world.</summary>
        public readonly Transform Anchor;
        /// <summary>The device the glyph is for.</summary>
        public readonly InputDeviceKind Device;
        /// <summary>Glyph image, or <c>null</c>.</summary>
        public readonly Sprite Glyph;
        /// <summary>Glyph text used when <see cref="Glyph"/> is <c>null</c>. Both empty means no glyph box.</summary>
        public readonly string GlyphText;
        /// <summary>The label, such as "Open".</summary>
        public readonly string Label;
        /// <summary>Backgrounds, colours and sizes. Never <c>null</c>.</summary>
        public readonly InputPromptStyle Style;

        /// <summary>Creates the content of one frame.</summary>
        public InputPromptContent(InputPrompt prompt, Transform anchor, InputDeviceKind device, Sprite glyph,
            string glyphText, string label, InputPromptStyle style)
        {
            Prompt = prompt;
            Anchor = anchor;
            Device = device;
            Glyph = glyph;
            GlyphText = glyphText;
            Label = label;
            Style = style;
        }
    }

    /// <summary>Draws an <see cref="InputPrompt"/>. Put a component that implements it on a prefab to replace the built-in look.</summary>
    public interface IInputPromptView
    {
        /// <summary>Called every frame the prompt is enabled, hidden frames included, so the view can fade.</summary>
        void Present(InputPromptState state, in InputPromptContent content);

        /// <summary>Whether a tap at <paramref name="screenPoint"/>, in pixels, lands on the prompt.</summary>
        bool Contains(Vector2 screenPoint);

        /// <summary>Removes the view. The prompt is disabled or destroyed.</summary>
        void Release();
    }

    /// <summary>
    /// A screen-space "press this to do that" prompt pinned to a point in the world. Shows a hint dot
    /// from a distance, then the glyph and label inside its <see cref="TriggerVolume"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The glyph follows the device the player used last (keyboard, gamepad or touch), set per device on
    /// an <see cref="InputPromptStyle"/>. On touch the prompt itself can be tapped. Pressing the button
    /// inside the zone fires <see cref="OnPressed"/>, which can start an <see cref="Interactable"/> or anything else.
    /// </para>
    /// <para>
    /// Experimental: its fields and look may change. It hides while the player's control is locked,
    /// such as during the interaction it started. The built-in look needs the uGUI package; without it,
    /// assign a view prefab.
    /// </para>
    /// </remarks>
    [AddComponentMenu("Building Blocks/Input Prompt (Experimental)")]
    [DisallowMultipleComponent]
    public sealed class InputPrompt : MonoBehaviour, ISerializationCallbackReceiver
    {
        /// <summary>Which device's glyph to show.</summary>
        public enum DevicePreview
        {
            /// <summary>The device the player used last.</summary>
            Auto = 0,
            /// <summary>Always keyboard and mouse.</summary>
            KeyboardMouse = 1,
            /// <summary>Always gamepad.</summary>
            Gamepad = 2,
            /// <summary>Always touch; mouse clicks on the prompt count as taps.</summary>
            Touch = 3,
            /// <summary>Always the A-Right gamepad look.</summary>
            [InspectorName("A-Right Gamepad")]
            ARightGamepad = 4,
        }

        [SerializeField]
        [Tooltip("The full prompt shows and the button works while the player is inside this volume.")]
        TriggerVolume m_Zone;

        [SerializeField]
        [Tooltip("The button that fires On Pressed. Use Reference to point at your input asset's Interact action.")]
        InputActionProperty m_Button = new InputActionProperty(DefaultInputAction());

        [SerializeField]
        [Tooltip("Short description shown next to the glyph, such as \"Open\".")]
        string m_Label = "Interact";

        [SerializeField]
        [Tooltip("Where the prompt sits in the world, such as the door handle. Empty uses this object.")]
        Transform m_Anchor;

        [SerializeField]
        [Tooltip("Hidden while this component is off, and only the dot shows while it turns a press away, such as an Interactable the player is not looking at. Empty shows it regardless.")]
        Behaviour m_ShowWhileEnabled;

        [SerializeField]
        [Tooltip("Show a small dot before the player reaches the zone.")]
        bool m_ShowHint = true;

        [SerializeField, Min(0f)]
        [Tooltip("The dot shows when the camera is closer than this to the anchor, in metres.")]
        float m_HintDistance = 6f;

        [SerializeField]
        [Tooltip("Glyphs, text and backgrounds per device. Empty uses plain E, A and Tap.")]
        InputPromptStyle m_Style;

        [SerializeField]
        [Tooltip("Replaces the built-in look: a prefab with a component that implements IInputPromptView.")]
        GameObject m_ViewPrefab;

        [SerializeField]
        [Tooltip("Force one device's glyph, for testing. Auto follows the device the player used last.")]
        DevicePreview m_Preview = DevicePreview.Auto;

        [SerializeField]
        [Tooltip("Runs when the button is pressed, or the prompt tapped, while the full prompt shows.")]
        UnityEvent m_OnPressed = new UnityEvent();

        static InputPromptStyle s_DefaultStyle;

        // Set by the uGUI assembly when it is present, so this assembly needs no UI package.
        internal static Func<InputPrompt, IInputPromptView> s_DefaultViewFactory;

        readonly List<Collider> m_InZone = new List<Collider>();
        TriggerVolume m_ListeningTo;
        InputAction m_WiredAction;
        bool m_OwnsWiredAction;
        IInputPromptView m_View;
        GameObject m_ViewInstance;
        bool m_WarnedNoView;
        InputPromptState m_State;

        // True between OnEnable and OnDisable; setters wire only then. EditMode tests call WireZone() and
        // Tick() themselves, because OnEnable and Update never run there.
        bool m_Live;

        /// <summary>The button was pressed, or the prompt tapped, while the full prompt showed.</summary>
        public event Action<InputPrompt> Pressed;

        /// <summary>Inspector event fired with <see cref="Pressed"/>.</summary>
        public UnityEvent OnPressed => m_OnPressed;

        /// <summary>How much of the prompt shows this frame.</summary>
        public InputPromptState State => m_State;

        /// <summary>The volume the full prompt shows in. Without one, only the hint shows.</summary>
        public TriggerVolume Zone
        {
            get => m_Zone;
            set
            {
                if (m_Zone == value) return;
                UnwireZone();
                m_Zone = value;
                if (m_Live) WireZone();
            }
        }

        /// <summary>The button, as an inline action or a reference into an input asset.</summary>
        public InputActionProperty Button
        {
            get => m_Button;
            set
            {
                UnwireInput();
                m_Button = value;
                if (m_Live) WireInput();
            }
        }

        /// <summary>Text shown next to the glyph.</summary>
        public string Label { get => m_Label; set => m_Label = value; }

        /// <summary>Where the prompt sits. Falls back to this transform.</summary>
        public Transform Anchor { get => m_Anchor != null ? m_Anchor : transform; set => m_Anchor = value; }

        /// <summary>The prompt hides while this component is off, and drops to the dot while an Interactable here waits to be looked at. <c>null</c> ignores it.</summary>
        public Behaviour ShowWhileEnabled { get => m_ShowWhileEnabled; set => m_ShowWhileEnabled = value; }

        /// <summary>Whether the hint dot shows before the zone.</summary>
        public bool ShowHint { get => m_ShowHint; set => m_ShowHint = value; }

        /// <summary>Camera distance to the anchor, in metres, under which the hint dot shows.</summary>
        public float HintDistance { get => m_HintDistance; set => m_HintDistance = Mathf.Max(0f, value); }

        /// <summary>The style in use. Never <c>null</c>: falls back to a plain built-in style.</summary>
        public InputPromptStyle Style
        {
            get
            {
                if (m_Style != null) return m_Style;
                if (s_DefaultStyle == null)
                {
                    s_DefaultStyle = ScriptableObject.CreateInstance<InputPromptStyle>();
                    s_DefaultStyle.hideFlags = HideFlags.HideAndDontSave;
                }
                return s_DefaultStyle;
            }
            set => m_Style = value;
        }

        /// <summary>The device whose glyph shows, after <see cref="DevicePreview"/>.</summary>
        public InputDeviceKind Device => m_Preview == DevicePreview.Auto
            ? InputDeviceTracker.Current
            : (InputDeviceKind)((int)m_Preview - 1);

        /// <summary>
        /// Fires <see cref="Pressed"/> and <see cref="OnPressed"/> if the full prompt would show right now:
        /// the player is in the zone, not locked, looking at it when <see cref="ShowWhileEnabled"/> asks for
        /// that, and the device's look is not hidden.
        /// </summary>
        public void Press()
        {
            // Read the zone now, not last frame's state: the press can land between a zone change and Update.
            if (Style.For(Device).Hide || ComputeState() != InputPromptState.Full)
                return;
            Fire();
        }

        void Fire()
        {
            Pressed?.Invoke(this);
            m_OnPressed.Invoke();
        }

        void OnEnable()
        {
            m_Live = true;
            WireZone();
            WireInput();
        }

        void OnDisable()
        {
            m_Live = false;
            UnwireInput();
            UnwireZone();
            m_State = InputPromptState.Hidden;
            ReleaseView();
        }

        void Update() => Tick();

        /// <summary>Works out the state and draws it. Runs every frame; EditMode tests call it by hand.</summary>
        internal void Tick()
        {
            InputDeviceKind device = Device;
            InputPromptStyle style = Style;
            InputPromptDeviceLook look = style.For(device);

            m_State = look.Hide ? InputPromptState.Hidden : ComputeState();
            if (m_State == InputPromptState.Hidden && m_View == null)
                return;

            IInputPromptView view = EnsureView();
            if (view == null)
                return;

            string label = string.IsNullOrEmpty(look.Label) ? m_Label : look.Label;
            view.Present(m_State, new InputPromptContent(this, Anchor, device, look.Glyph, look.GlyphText, label, style));

            if (m_State == InputPromptState.Full && device == InputDeviceKind.Touch)
            {
                Pointer pointer = Pointer.current;
                // A tap goes by what is drawn this frame.
                if (pointer != null && pointer.press.wasPressedThisFrame && view.Contains(pointer.position.ReadValue()))
                    Fire();
            }
        }

        InputPromptState ComputeState()
        {
            // Checked by hand rather than isActiveAndEnabled, which EditMode tests cannot rely on.
            if (m_ShowWhileEnabled != null && !(m_ShowWhileEnabled.enabled && m_ShowWhileEnabled.gameObject.activeInHierarchy))
                return InputPromptState.Hidden;

            KinematicMover player = PlayerInZone(out bool inZone);
            if (player == null)
                player = InteractionUtility.FindPlayer();
            var controlLock = player != null ? player.GetComponent<IControlLock>() : null;
            if (controlLock != null && controlLock.IsLocked)
                return InputPromptState.Hidden;

            if (inZone)
            {
                // Inside but looking away: the dot says where to look, and the button waits.
                // Unity null first: a destroyed target still matches the interface as a managed object.
                if (m_ShowWhileEnabled != null && m_ShowWhileEnabled is IPressGate gate && !gate.AdmitsPress())
                    return HintOrHidden();
                return InputPromptState.Full;
            }

            return HintOrHidden();
        }

        InputPromptState HintOrHidden()
        {
            if (m_ShowHint)
            {
                Transform eye = InteractionUtility.Eye();
                if (eye != null && (eye.position - Anchor.position).sqrMagnitude <= m_HintDistance * m_HintDistance)
                    return InputPromptState.Hint;
            }

            return InputPromptState.Hidden;
        }

        KinematicMover PlayerInZone(out bool inZone)
        {
            inZone = false;
            if (m_Zone == null)
                return null;
            for (int i = m_InZone.Count - 1; i >= 0; i--)
            {
                if (m_InZone[i] == null)
                {
                    m_InZone.RemoveAt(i);
                    continue;
                }
                inZone = true;
                return m_InZone[i].GetComponentInParent<KinematicMover>();
            }
            return null;
        }

        IInputPromptView EnsureView()
        {
            if (m_View != null)
                return m_View;

            if (m_ViewPrefab != null)
            {
                m_ViewInstance = Instantiate(m_ViewPrefab);
                m_View = m_ViewInstance.GetComponentInChildren<IInputPromptView>();
                if (m_View == null)
                {
                    Debug.LogWarning($"{name}: the view prefab has no component that implements IInputPromptView.", this);
                    Destroy(m_ViewInstance);
                    m_ViewInstance = null;
                }
            }
            else if (s_DefaultViewFactory != null)
            {
                m_View = s_DefaultViewFactory(this);
            }

            if (m_View == null && !m_WarnedNoView)
            {
                m_WarnedNoView = true;
                Debug.LogWarning($"{name}: Input Prompt has nothing to draw with. Install the uGUI package or assign a view prefab.", this);
            }
            return m_View;
        }

        internal void ReleaseView()
        {
            m_View?.Release();
            m_View = null;
            if (m_ViewInstance != null)
                Destroy(m_ViewInstance);
            m_ViewInstance = null;
        }

        void WireInput()
        {
            InputAction action = m_Button.action;
            if (action == null || m_WiredAction == action)
                return;
            UnwireInput();
            // An asset action is shared with other readers: enable it, never disable it.
            m_OwnsWiredAction = m_Button.reference == null;
            action.performed += OnInputPerformed;
            action.Enable();
            m_WiredAction = action;
        }

        void UnwireInput()
        {
            if (m_WiredAction == null)
                return;
            m_WiredAction.performed -= OnInputPerformed;
            if (m_OwnsWiredAction) m_WiredAction.Disable();
            m_WiredAction = null;
        }

        void OnInputPerformed(InputAction.CallbackContext context) => Press();

        internal void WireZone()
        {
            if (m_Zone == null || ReferenceEquals(m_ListeningTo, m_Zone))
                return;
            UnwireZone();
            m_ListeningTo = m_Zone;
            m_Zone.TriggerEntered += OnZoneEntered;
            m_Zone.TriggerExited += OnZoneExited;
            IReadOnlyList<Collider> occupants = m_Zone.Occupants;
            for (int i = 0; i < occupants.Count; i++)
            {
                if (occupants[i] != null && !m_InZone.Contains(occupants[i]))
                    m_InZone.Add(occupants[i]);
            }
        }

        internal void UnwireZone()
        {
            if (m_ListeningTo != null)
            {
                m_ListeningTo.TriggerEntered -= OnZoneEntered;
                m_ListeningTo.TriggerExited -= OnZoneExited;
                m_ListeningTo = null;
            }
            m_InZone.Clear();
        }

        void OnZoneEntered(TriggerVolume volume, Collider collider)
        {
            if (!m_InZone.Contains(collider))
                m_InZone.Add(collider);
        }

        void OnZoneExited(TriggerVolume volume, Collider collider) => m_InZone.Remove(collider);

        void ISerializationCallbackReceiver.OnBeforeSerialize()
        {
        }

        /// <summary>Gives an inline Button action a fresh binding cache so its saved bindings take effect.</summary>
        void ISerializationCallbackReceiver.OnAfterDeserialize()
        {
            if (m_Button.reference == null && m_Button.action != null)
                m_Button = new InputActionProperty(InteractionUtility.Uncached(m_Button.action));
        }

        static InputAction DefaultInputAction()
        {
            var action = new InputAction("Interact", InputActionType.Button);
            action.AddBinding("<Keyboard>/e");
            action.AddBinding("<Gamepad>/buttonSouth");
            return action;
        }
    }
}
