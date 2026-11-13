using UnityEngine.UIElements;

namespace Unity.BuildingBlocks.Gameplay.Editor
{
    /// <summary>
    /// A container that shows or hides its content instantly, like Unity's foldouts. Children added
    /// through <see cref="Add"/> land in the content.
    /// </summary>
    public sealed class Fold : VisualElement
    {
        readonly VisualElement m_Content;
        bool m_Open = true;

        /// <summary>Creates an open fold.</summary>
        public Fold()
        {
            AddToClassList("blocks-fold");

            m_Content = new VisualElement();
            m_Content.AddToClassList("blocks-fold__content");
            hierarchy.Add(m_Content);
        }

        /// <inheritdoc/>
        public override VisualElement contentContainer => m_Content;

        /// <summary>Whether the fold is open.</summary>
        public bool IsOpen => m_Open;

        /// <summary>Opens or closes the fold.</summary>
        public void SetOpen(bool open)
        {
            m_Open = open;
            style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
