using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Turns Input System actions (move, look, jump, sprint, crouch and any custom buttons) into the
    /// <see cref="MoveCommand"/> the host feeds the mover. Swap it for an AI or replay source to
    /// change who drives the character.
    /// </summary>
    /// <remarks>
    /// For your own processor's input (a dash, a grapple), add a row under <i>Custom Buttons</i>. The
    /// processor declares <c>[SerializeField] InputSlot m_Dash = "Dash";</c> and reads
    /// <c>command.Pressed(m_Dash)</c> or <c>command.Held(m_Dash)</c>. Assign <see cref="YawSource"/>
    /// (normally the camera rig) for the heading; with none, look input is integrated here. It is read
    /// once per frame. Held inputs are sampled once per frame; presses and look delta each reach exactly
    /// one tick (this is not jump buffering). On disable, only inline actions are disabled; asset
    /// actions may be shared.
    /// </remarks>
    [AddComponentMenu("Building Blocks/Player Input Source")]
    [DisallowMultipleComponent]
    public sealed class PlayerInputSource : MonoBehaviour, IMoveCommandSource, IMoveCommandLayout
    {
        [Header("Actions")]
        [SerializeField]
        [Tooltip("Walk direction. Inline WASD and the left stick by default; tick Use Reference to drag in your asset's Move action.")]
        InputActionProperty m_Move = new InputActionProperty(DefaultMoveAction());

        [SerializeField]
        [Tooltip("Look delta. Mouse delta by default; only integrated by this component when no Yaw Source is assigned.")]
        InputActionProperty m_Look = new InputActionProperty(DefaultLookAction());

        [SerializeField]
        [Tooltip("Jump. Space and the south face button by default. A press reaches exactly one tick.")]
        InputActionProperty m_Jump = new InputActionProperty(DefaultJumpAction());

        [SerializeField]
        [Tooltip("Sprint, while held. Left shift and the left stick press by default.")]
        InputActionProperty m_Sprint = new InputActionProperty(DefaultSprintAction());

        [SerializeField]
        [Tooltip("Crouch, while held. Left control and the east face button by default.")]
        InputActionProperty m_Crouch = new InputActionProperty(DefaultCrouchAction());

        [Space(6)]
        [SerializeField]
        [Tooltip("Buttons your own processors read. Name a row and drag a Button action in; the processor " +
                 "picks the name in its InputSlot field. The Bit is assigned automatically and kept stable.")]
        List<CommandButton> m_CustomButtons = new List<CommandButton>();

        [Header("Look")]
        [SerializeField]
        [Tooltip("The transform whose world yaw becomes the character's heading, normally the " +
                 "camera rig. Leave empty and this component integrates the look input itself, " +
                 "which is useful before a rig exists but means two things own looking.")]
        Transform m_YawSource;

        [SerializeField, Min(0f)]
        [Tooltip("Degrees of yaw per unit of look input (per mouse pixel). Ignored when Yaw " +
                 "Source is assigned. The rig's own sensitivity applies instead.")]
        float m_LookSensitivity = k_DefaultLookSensitivity;

        /// <summary>
        /// Default <see cref="LookSensitivity"/>: 0.1 degrees per unit of look input.
        /// </summary>
        /// <remarks>
        /// 0.1 degrees per mouse pixel, a 360 degree turn in 3600 px. Ignored once
        /// <see cref="YawSource"/> is assigned.
        /// </remarks>
        const float k_DefaultLookSensitivity = 0.1f;

        // Input-layer state, not simulation truth: one frame of device events waiting for the next
        // tick. Replays use recorded commands, so none of this needs to be snapshotable.
        bool m_JumpLatched;
        Vector2 m_LookDelta;
        Vector2 m_Move2D;
        bool m_SprintHeld;
        bool m_CrouchHeld;
        float m_Yaw;

        // Custom buttons: presses latch by callback until a tick consumes them; held states are
        // sampled once per frame.
        uint m_PressedLatch;
        uint m_HeldButtons;

        // What OnEnable enabled and subscribed, so OnDisable undoes exactly that even if the fields
        // or list changed since.
        InputAction m_MoveAction;
        InputAction m_LookAction;
        InputAction m_JumpAction;
        InputAction m_SprintAction;
        InputAction m_CrouchAction;

        // Which actions this component created inline. Only those are disabled on disable.
        bool m_OwnsMove;
        bool m_OwnsLook;
        bool m_OwnsJump;
        bool m_OwnsSprint;
        bool m_OwnsCrouch;

        InputAction[] m_SubscribedActions = Array.Empty<InputAction>();
        uint[] m_SubscribedMasks = Array.Empty<uint>();
        bool[] m_SubscribedOwned = Array.Empty<bool>();
        int m_SubscribedCount;

        // True from a rendered frame until the first tick reads the yaw source; later ticks in the
        // same frame reuse that reading. Starts true so the first tick reads it.
        bool m_YawStale = true;

        /// <summary>
        /// Transform whose world yaw drives facing (normally the camera rig). When assigned it
        /// replaces this component's own look integration entirely.
        /// </summary>
        public Transform YawSource
        {
            get => m_YawSource;
            set => m_YawSource = value;
        }

        /// <summary>
        /// Degrees of yaw per unit of look input, used only when <see cref="YawSource"/> is unset.
        /// Default <c>0.1</c>.
        /// </summary>
        public float LookSensitivity => m_LookSensitivity;

        /// <summary>
        /// The heading this source last reported, in degrees. Reads the <see cref="YawSource"/>
        /// when one is assigned, otherwise the integrated look input.
        /// </summary>
        public float Yaw => m_YawSource != null ? m_YawSource.eulerAngles.y : m_Yaw;

        /// <summary>
        /// The move action in use: the inline one, or the asset's when the field is a reference.
        /// <c>null</c> when it is a reference to nothing.
        /// </summary>
        public InputAction MoveAction => m_Move.action;

        /// <summary>The look action in use (see <see cref="MoveAction"/> for what "in use" means).</summary>
        public InputAction LookAction => m_Look.action;

        /// <summary>The jump action in use (see <see cref="MoveAction"/>).</summary>
        public InputAction JumpAction => m_Jump.action;

        /// <summary>The sprint action in use (see <see cref="MoveAction"/>).</summary>
        public InputAction SprintAction => m_Sprint.action;

        /// <summary>The crouch action in use (see <see cref="MoveAction"/>).</summary>
        public InputAction CrouchAction => m_Crouch.action;

        /// <summary>
        /// The custom-button rows, in Inspector order. Edit the list from code, then call
        /// <see cref="RefreshCustomButtons"/> so the new rows are enabled and subscribed; the
        /// Inspector does that for you.
        /// </summary>
        public List<CommandButton> CustomButtons => m_CustomButtons;

        /// <inheritdoc/>
        /// <remarks>
        /// Called at bind time, not per tick, and changes no row. A row with no bit yet, or whose bit
        /// an earlier row holds, answers unbound.
        /// </remarks>
        public int FindButton(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return InputSlot.Unbound;

            string wanted = name.Trim();
            uint used = 0;
            for (int i = 0; i < m_CustomButtons.Count; i++)
            {
                CommandButton row = m_CustomButtons[i];
                if (row == null)
                    continue;

                bool live = IsLiveRow(row, ref used);
                if (string.IsNullOrWhiteSpace(row.Name))
                    continue;

                if (string.Equals(row.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    return live ? row.Bit : InputSlot.Unbound;
            }

            return InputSlot.Unbound;
        }

        /// <summary>
        /// Whether <paramref name="row"/> owns a bit no earlier row owns, using <paramref name="used"/>
        /// as the running mask. Rows that fail are never subscribed or resolvable by name.
        /// </summary>
        static bool IsLiveRow(CommandButton row, ref uint used)
        {
            uint mask = MoveCommand.MaskOf(row.Bit);
            if (mask == 0 || (used & mask) != 0)
                return false;

            used |= mask;
            return true;
        }

        /// <summary>
        /// Appends to <paramref name="labels"/> the name of every built-in action that cannot fire:
        /// a reference to nothing, or an action with no bindings. Returns how many.
        /// </summary>
        public int MissingActions(List<string> labels)
        {
            int before = labels.Count;
            Check("Move", m_Move.action, labels);
            Check("Look", m_Look.action, labels);
            Check("Jump", m_Jump.action, labels);
            Check("Sprint", m_Sprint.action, labels);
            Check("Crouch", m_Crouch.action, labels);
            return labels.Count - before;

            static void Check(string label, InputAction action, List<string> into)
            {
                if (action == null || action.bindings.Count == 0)
                    into.Add(label);
            }
        }

        /// <summary>
        /// Re-reads <see cref="CustomButtons"/>: gives rows without a <see cref="CommandButton.Bit"/>
        /// the lowest free one, then, if enabled, enables and subscribes the rows' actions. Call it
        /// after editing the list from code.
        /// </summary>
        public void RefreshCustomButtons()
        {
            AssignButtonBits();

            if (!isActiveAndEnabled)
                return;

            UnsubscribeCustomButtons();
            SubscribeCustomButtons();
        }

        /// <summary>
        /// Builds this tick's command: world-space move direction, the current heading, the three
        /// named button states, and the custom buttons.
        /// </summary>
        /// <remarks>
        /// Move input is rotated by the heading, so the command is in world space. Called once per tick
        /// by the host; it consumes the latched presses and look delta, so each reaches one tick.
        /// </remarks>
        /// <param name="deltaTime">The tick this command is being produced for, in seconds.</param>
        public MoveCommand ReadCommand(float deltaTime)
        {
            // Yaw first: the move vector is measured against the heading this command reports.
            if (m_YawSource != null)
            {
                // First tick of the frame only; a later tick would read the source after the body's
                // own turn dragged it along, and compound it.
                if (m_YawStale)
                {
                    m_Yaw = m_YawSource.eulerAngles.y;
                    m_YawStale = false;
                }
            }
            else
            {
                // Not scaled by deltaTime: the delta is the device's total movement since the last
                // command.
                m_Yaw += m_LookDelta.x * m_LookSensitivity;
            }

            m_LookDelta = Vector2.zero;

            Vector2 move = m_Move2D;

            // Clamp to magnitude 1; a half-deflected stick must stay half.
            if (move.sqrMagnitude > 1f)
                move.Normalize();

            var local = new Vector3(move.x, 0f, move.y);
            Vector3 world = Quaternion.Euler(0f, m_Yaw, 0f) * local;

            bool jump = m_JumpLatched;
            m_JumpLatched = false;

            uint pressed = m_PressedLatch;
            m_PressedLatch = 0;

            return new MoveCommand
            {
                MoveDirection = world,
                Yaw = m_Yaw,
                Jump = jump,
                Sprint = m_SprintHeld,
                Crouch = m_CrouchHeld,
                PressedButtons = pressed,
                HeldButtons = m_HeldButtons,
            };
        }

        /// <summary>
        /// Samples the held inputs once per render frame.
        /// </summary>
        /// <remarks>
        /// A fixed step can run zero or several times per frame, so sampling here gives every tick in
        /// a frame the same input. It also keeps device reads out of <c>FixedUpdate</c>, which the
        /// Input System's dynamic-update state reports as an error under strict checks.
        /// </remarks>
        void Update()
        {
            m_Move2D = IsLive(m_MoveAction) ? m_MoveAction.ReadValue<Vector2>() : Vector2.zero;
            m_SprintHeld = IsLive(m_SprintAction) && m_SprintAction.IsPressed();
            m_CrouchHeld = IsLive(m_CrouchAction) && m_CrouchAction.IsPressed();

            uint held = 0;
            for (int i = 0; i < m_SubscribedCount; i++)
            {
                InputAction action = m_SubscribedActions[i];
                if (IsLive(action) && action.IsPressed())
                    held |= m_SubscribedMasks[i];
            }

            m_HeldButtons = held;

            // The yaw source publishes in LateUpdate after this; the next frame's first tick reads it.
            m_YawStale = true;
        }

        void OnEnable()
        {
            m_MoveAction = m_Move.action;
            m_LookAction = m_Look.action;
            m_JumpAction = m_Jump.action;
            m_SprintAction = m_Sprint.action;
            m_CrouchAction = m_Crouch.action;

            m_OwnsMove = Owns(m_Move);
            m_OwnsLook = Owns(m_Look);
            m_OwnsJump = Owns(m_Jump);
            m_OwnsSprint = Owns(m_Sprint);
            m_OwnsCrouch = Owns(m_Crouch);

            if (m_JumpAction != null) m_JumpAction.performed += OnJumpPerformed;
            if (m_LookAction != null) m_LookAction.performed += OnLookPerformed;

            Enable(m_MoveAction);
            Enable(m_LookAction);
            Enable(m_JumpAction);
            Enable(m_SprintAction);
            Enable(m_CrouchAction);

            AssignButtonBits();
            SubscribeCustomButtons();

            // Start from the rig's heading so enabling does not snap the character to world forward.
            if (m_YawSource != null)
                m_Yaw = m_YawSource.eulerAngles.y;

            m_YawStale = true;
        }

        void OnDisable()
        {
            // Callbacks always come off; only inline actions are disabled. Asset actions are shared.
            if (m_JumpAction != null) m_JumpAction.performed -= OnJumpPerformed;
            if (m_LookAction != null) m_LookAction.performed -= OnLookPerformed;

            DisableOwned(m_MoveAction, m_OwnsMove);
            DisableOwned(m_LookAction, m_OwnsLook);
            DisableOwned(m_JumpAction, m_OwnsJump);
            DisableOwned(m_SprintAction, m_OwnsSprint);
            DisableOwned(m_CrouchAction, m_OwnsCrouch);

            UnsubscribeCustomButtons();

            m_MoveAction = null;
            m_LookAction = null;
            m_JumpAction = null;
            m_SprintAction = null;
            m_CrouchAction = null;

            // Drop presses made while disabled, so re-enabling does not act on its own.
            m_JumpLatched = false;
            m_LookDelta = Vector2.zero;
            m_Move2D = Vector2.zero;
            m_SprintHeld = false;
            m_CrouchHeld = false;
            m_PressedLatch = 0;
            m_HeldButtons = 0;
        }

#if UNITY_EDITOR
        void OnValidate() => AssignButtonBits();
#endif

        void OnJumpPerformed(InputAction.CallbackContext context) => m_JumpLatched = true;

        void OnLookPerformed(InputAction.CallbackContext context)
            => m_LookDelta += context.ReadValue<Vector2>();

        /// <summary>
        /// One handler for every custom button: the action that fired tells which row(s) it is.
        /// </summary>
        void OnCustomButtonPerformed(InputAction.CallbackContext context)
        {
            for (int i = 0; i < m_SubscribedCount; i++)
            {
                if (ReferenceEquals(m_SubscribedActions[i], context.action))
                    m_PressedLatch |= m_SubscribedMasks[i];
            }
        }

        /// <summary>
        /// Gives every row without a valid bit the lowest free one; rows with a bit keep it.
        /// </summary>
        /// <remarks>
        /// A row whose bit an earlier row holds logs an error and is skipped until fixed. Past 32 rows the
        /// rest stay unbound. Use this in edit mode; at runtime call <see cref="RefreshCustomButtons"/>,
        /// which also subscribes.
        /// </remarks>
        public void AssignButtonBits()
        {
            // Two passes, so an unassigned row never takes a bit a later row has pinned.
            uint pinned = 0;
            for (int i = 0; i < m_CustomButtons.Count; i++)
            {
                CommandButton row = m_CustomButtons[i];
                if (row != null)
                    pinned |= MoveCommand.MaskOf(row.Bit);
            }

            uint used = 0;
            for (int i = 0; i < m_CustomButtons.Count; i++)
            {
                CommandButton row = m_CustomButtons[i];
                if (row == null)
                    continue;

                int bit = row.Bit;
                if (!MoveCommand.IsValidButton(bit))
                {
                    bit = LowestFreeBit(used | pinned);
                    row.Bit = bit;
                }
                else if ((used & MoveCommand.MaskOf(bit)) != 0)
                {
                    Debug.LogError(
                        $"{name}: custom buttons '{RowLabel(FirstRowWithBit(bit))}' and '{RowLabel(row)}' both use " +
                        $"bit {bit}. '{RowLabel(row)}' is ignored until you give it another bit.", this);
                    continue;
                }

                used |= MoveCommand.MaskOf(bit);
            }
        }

        CommandButton FirstRowWithBit(int bit)
        {
            for (int i = 0; i < m_CustomButtons.Count; i++)
            {
                CommandButton row = m_CustomButtons[i];
                if (row != null && row.Bit == bit)
                    return row;
            }

            return null;
        }

        static string RowLabel(CommandButton row)
            => row == null || string.IsNullOrWhiteSpace(row.Name) ? "(unnamed)" : row.Name.Trim();

        static int LowestFreeBit(uint used)
        {
            for (int bit = 0; bit < MoveCommand.ButtonCount; bit++)
            {
                if ((used & (1u << bit)) == 0)
                    return bit;
            }

            return InputSlot.Unbound;
        }

        /// <summary>
        /// Enables and subscribes the live rows' actions. One action that sits on two rows is
        /// subscribed once and fires both bits; a row skipped by <see cref="IsLiveRow"/> is not
        /// subscribed at all.
        /// </summary>
        void SubscribeCustomButtons()
        {
            int count = m_CustomButtons.Count;
            if (m_SubscribedActions.Length < count)
            {
                m_SubscribedActions = new InputAction[count];
                m_SubscribedMasks = new uint[count];
                m_SubscribedOwned = new bool[count];
            }

            m_SubscribedCount = 0;
            uint used = 0;
            for (int i = 0; i < count; i++)
            {
                CommandButton row = m_CustomButtons[i];
                if (row == null || !IsLiveRow(row, ref used))
                    continue;

                InputAction action = row.Action;
                uint mask = MoveCommand.MaskOf(row.Bit);
                if (action == null || mask == 0)
                    continue;

                int existing = IndexOfSubscribed(action);
                if (existing >= 0)
                {
                    m_SubscribedMasks[existing] |= mask;
                    continue;
                }

                action.performed += OnCustomButtonPerformed;
                action.Enable();

                m_SubscribedActions[m_SubscribedCount] = action;
                m_SubscribedMasks[m_SubscribedCount] = mask;
                m_SubscribedOwned[m_SubscribedCount] = Owns(row.ActionProperty);
                m_SubscribedCount++;
            }
        }

        int IndexOfSubscribed(InputAction action)
        {
            for (int i = 0; i < m_SubscribedCount; i++)
            {
                if (ReferenceEquals(m_SubscribedActions[i], action))
                    return i;
            }

            return -1;
        }

        void UnsubscribeCustomButtons()
        {
            for (int i = 0; i < m_SubscribedCount; i++)
            {
                InputAction action = m_SubscribedActions[i];
                if (action != null)
                {
                    action.performed -= OnCustomButtonPerformed;
                    DisableOwned(action, m_SubscribedOwned[i]);
                }

                m_SubscribedActions[i] = null;
                m_SubscribedMasks[i] = 0;
                m_SubscribedOwned[i] = false;
            }

            m_SubscribedCount = 0;
            m_PressedLatch = 0;
            m_HeldButtons = 0;
        }

        static bool IsLive(InputAction action) => action != null && action.enabled;

        static void Enable(InputAction action)
        {
            if (action != null) action.Enable();
        }

        /// <summary>
        /// Disables <paramref name="action"/> only when this component created it; asset actions are
        /// shared.
        /// </summary>
        static void DisableOwned(InputAction action, bool owned)
        {
            if (owned && action != null) action.Disable();
        }

        /// <summary>Whether the property's action is this component's own inline one, not a reference into an asset.</summary>
        static bool Owns(InputActionProperty property) => property.reference == null && property.action != null;

        /// <summary>WASD and the left stick, as a normalized 2D vector.</summary>
        /// <remarks>
        /// Uses plain <c>AddBinding</c> for the composite parts, not <c>With</c>: these run in the
        /// MonoBehaviour constructor, and on a domain reload <c>With</c> throws before the Input System
        /// manager exists. Keep it that way (see <c>PlayerInputSourceDefaultsTests</c>).
        /// </remarks>
        static InputAction DefaultMoveAction()
        {
            var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");

            action.AddCompositeBinding("2DVector");
            AddCompositePart(action, "Up", "<Keyboard>/w");
            AddCompositePart(action, "Down", "<Keyboard>/s");
            AddCompositePart(action, "Left", "<Keyboard>/a");
            AddCompositePart(action, "Right", "<Keyboard>/d");

            action.AddBinding("<Gamepad>/leftStick");
            return action;
        }

        /// <summary>
        /// Adds one part of the composite just added to <paramref name="action"/>, without
        /// <c>CompositeSyntax.With</c> (see <see cref="DefaultMoveAction"/>).
        /// </summary>
        static void AddCompositePart(InputAction action, string name, string path)
            => action.AddBinding(new InputBinding { name = name, path = path, isPartOfComposite = true });

        /// <summary>
        /// Mouse delta only: the inline default this component ships with.
        /// </summary>
        /// <remarks>
        /// No stick: this component treats look as a distance, not a rate. The
        /// <see cref="FirstPersonCameraRig"/> handles the stick when reading the asset's Look action.
        /// </remarks>
        static InputAction DefaultLookAction()
        {
            var action = new InputAction("Look", InputActionType.Value, expectedControlType: "Vector2");
            action.AddBinding("<Mouse>/delta");
            return action;
        }

        /// <summary>Space and the south face button.</summary>
        static InputAction DefaultJumpAction()
        {
            var action = new InputAction("Jump", InputActionType.Button);
            action.AddBinding("<Keyboard>/space");
            action.AddBinding("<Gamepad>/buttonSouth");
            return action;
        }

        /// <summary>Left shift and the left stick press.</summary>
        static InputAction DefaultSprintAction()
        {
            var action = new InputAction("Sprint", InputActionType.Button);
            action.AddBinding("<Keyboard>/leftShift");
            action.AddBinding("<Gamepad>/leftStickPress");
            return action;
        }

        /// <summary>Left control and the east face button.</summary>
        static InputAction DefaultCrouchAction()
        {
            var action = new InputAction("Crouch", InputActionType.Button);
            action.AddBinding("<Keyboard>/leftCtrl");
            action.AddBinding("<Gamepad>/buttonEast");
            return action;
        }
    }
}
