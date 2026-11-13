using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// A collapsible inspector panel: title, a live one-line summary, optional header actions and a
    /// body that opens instantly, like Unity's foldouts.
    /// </summary>
    /// <remarks>
    /// Open state is remembered per object in <see cref="SessionState"/> once <see cref="Remember"/>
    /// is called.
    /// </remarks>
    public sealed class InspectorCard : VisualElement
    {
        readonly string m_Id;
        readonly VisualElement m_Header;
        readonly Label m_Gloss;
        readonly Label m_Summary;
        readonly Label m_Chevron;
        readonly VisualElement m_Actions;
        readonly Fold m_Fold;
        readonly VisualElement m_Body;

        UnityEngine.Object m_StateOwner;
        bool m_Open;
        bool m_Collapsible = true;
        bool m_CollapsibleSet;

        /// <summary>Creates a card. <paramref name="id"/> keys its remembered open state.</summary>
        public InspectorCard(string id, string title, bool defaultOpen = true)
        {
            m_Id = id;
            m_Open = defaultOpen;

            AddToClassList("blocks-panel");
            AddToClassList("blocks-panel--collapsible");

            m_Header = new VisualElement();
            m_Header.AddToClassList("blocks-panel__header");
            m_Header.RegisterCallback<ClickEvent>(OnHeaderClicked);
            hierarchy.Add(m_Header);

            var titleLabel = new Label(title);
            titleLabel.AddToClassList("blocks-panel__title");
            m_Header.Add(titleLabel);

            m_Gloss = new Label(string.Empty);
            m_Gloss.AddToClassList("blocks-panel__gloss");
            m_Gloss.style.display = DisplayStyle.None;
            m_Header.Add(m_Gloss);

            m_Summary = new Label(string.Empty);
            m_Summary.AddToClassList("blocks-panel__summary");
            m_Header.Add(m_Summary);

            m_Actions = new VisualElement();
            m_Actions.AddToClassList("blocks-panel__actions");
            m_Actions.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
            m_Header.Add(m_Actions);

            m_Chevron = new Label("▸");
            m_Chevron.AddToClassList("blocks-panel__chevron");
            m_Header.Add(m_Chevron);

            m_Fold = new Fold();
            m_Body = new VisualElement();
            m_Body.AddToClassList("blocks-panel__body");
            m_Fold.Add(m_Body);
            hierarchy.Add(m_Fold);

            Apply();

            // A card that never received a body is a summary row: nothing to open, so no chevron.
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                if (!m_CollapsibleSet && m_Body.childCount == 0) Collapsible = false;
            });
        }

        /// <inheritdoc/>
        public override VisualElement contentContainer => m_Body;

        /// <summary>
        /// Runs when the header of a non-collapsible card is clicked. Ignored while the card is collapsible.
        /// </summary>
        public System.Action HeaderClicked { get; set; }

        /// <summary>
        /// Whether the header opens and closes a body. Defaults to true; false automatically for a card
        /// with no content unless set explicitly.
        /// </summary>
        public bool Collapsible
        {
            get => m_Collapsible;
            set
            {
                m_Collapsible = value;
                m_CollapsibleSet = true;
                EnableInClassList("blocks-panel--collapsible", value);
                EnableInClassList("blocks-panel--static", !value);
                if (!value)
                {
                    m_Fold.SetOpen(false);
                    RemoveFromClassList("blocks-panel--open");
                }
                else
                {
                    Apply();
                }
            }
        }

        /// <summary>Whether the body is showing.</summary>
        public bool IsOpen => m_Open;

        /// <summary>
        /// Sets the short muted gloss after the title ("the box in the scene"). Empty hides it.
        /// </summary>
        public InspectorCard SetGloss(string gloss)
        {
            m_Gloss.text = gloss ?? string.Empty;
            m_Gloss.style.display = string.IsNullOrEmpty(gloss) ? DisplayStyle.None : DisplayStyle.Flex;
            return this;
        }

        /// <summary>
        /// Puts the chevron before the title, foldout-style, with the summary at the right edge.
        /// </summary>
        public InspectorCard LeadingChevron()
        {
            AddToClassList("blocks-panel--leading");
            // Unity's foldout arrow draws here (inspector-card.uss); the glyph would overlap it.
            m_Chevron.text = string.Empty;
            m_Chevron.SendToBack();
            return this;
        }

        /// <summary>Sets the muted line beside the title. <paramref name="warn"/> tints it amber.</summary>
        public InspectorCard SetSummary(string summary, bool warn = false)
        {
            m_Summary.text = summary ?? string.Empty;
            m_Summary.EnableInClassList("blocks-panel__summary--warn", warn);
            return this;
        }

        /// <summary>Marks the header as an action target (hover feedback) for a summary row with a <see cref="HeaderClicked"/>.</summary>
        public InspectorCard Clickable(string tooltipText)
        {
            AddToClassList("blocks-panel--clickable");
            tooltip = tooltipText;
            return this;
        }

        /// <summary>Adds a control to the header, right of the summary. Clicks there do not toggle the card.</summary>
        public InspectorCard AddAction(VisualElement action)
        {
            action.AddToClassList("blocks-panel__action");
            m_Actions.Add(action);
            return this;
        }

        /// <summary>
        /// Adds an element below the body that stays visible when the card is closed.
        /// </summary>
        public InspectorCard AddFooter(VisualElement footer)
        {
            footer.AddToClassList("blocks-panel__footer");
            hierarchy.Add(footer);
            return this;
        }

        /// <summary>Opens or closes the card.</summary>
        public void SetOpen(bool open)
        {
            if (m_Open == open) return;

            m_Open = open;
            Apply();

            if (m_StateOwner != null)
                SessionState.SetBool(StateKey(), open);
        }

        /// <summary>
        /// Restores the open state for <paramref name="owner"/> and remembers later changes. Call once,
        /// after construction.
        /// </summary>
        public InspectorCard Remember(UnityEngine.Object owner)
        {
            m_StateOwner = owner;
            if (owner == null) return this;

            bool remembered = SessionState.GetBool(StateKey(), m_Open);
            if (remembered != m_Open)
            {
                m_Open = remembered;
                Apply();
            }

            return this;
        }

        void OnHeaderClicked(ClickEvent evt)
        {
            if (m_Collapsible)
            {
                SetOpen(!m_Open);
                return;
            }

            HeaderClicked?.Invoke();
        }

        void Apply()
        {
            EnableInClassList("blocks-panel--open", m_Open);
            m_Fold.SetOpen(m_Open);
        }

        string StateKey() => $"BuildingBlocks.Panel.{InspectorState.OwnerId(m_StateOwner)}.{m_Id}";
    }
}
