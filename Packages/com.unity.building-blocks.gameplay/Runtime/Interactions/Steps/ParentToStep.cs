using System;
using System.Collections;
using UnityEngine;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>How <see cref="ParentToStep"/> places the object under its new parent.</summary>
    public enum ParentToPlacement
    {
        /// <summary>Put it exactly on the parent, position and rotation, plus the offset fields. A socket you place is the pose.</summary>
        SnapToParent,

        /// <summary>Leave it where it is in the world and only change what it moves with.</summary>
        KeepWorldPose,
    }

    /// <summary>
    /// Moves an object under a new parent in the hierarchy, such as a torch into a socket on the hand.
    /// </summary>
    /// <remarks>
    /// <see cref="ParentToPlacement.SnapToParent"/> puts the object on the parent, nudged by
    /// <see cref="LocalOffset"/> and <see cref="LocalRotation"/>. An empty <see cref="NewParent"/> moves
    /// it to the scene root. Rigidbodies and Colliders are left alone.
    /// </remarks>
    [Serializable]
    [InteractionStep("Parent To")]
    public sealed class ParentToStep : InteractionStep
    {
        [SerializeField, SceneObject]
        [Tooltip("The object to move in the hierarchy. The torch.")]
        Transform m_Target;

        [SerializeField, SceneObject]
        [Tooltip("Where it goes: a socket on the hand, the table it came from. Empty puts it at the top " +
                 "of the scene, under nothing.")]
        Transform m_NewParent;

        [SerializeField]
        [Tooltip("Snap To Parent puts it exactly on the parent, position and rotation, so a socket you " +
                 "place is the pose. Keep World Pose leaves it where it is and only changes what it " +
                 "moves with.")]
        ParentToPlacement m_Placement = ParentToPlacement.SnapToParent;

        [SerializeField]
        [Tooltip("Nudge from the parent when snapping, along the parent's own axes. Moves the torch into " +
                 "the palm without moving the socket.")]
        Vector3 m_LocalOffset;

        [SerializeField]
        [Tooltip("Extra turn from the parent when snapping, in degrees.")]
        Vector3 m_LocalRotation;

        /// <summary>The object that moves.</summary>
        public Transform Target
        {
            get => m_Target;
            set => m_Target = value;
        }

        /// <summary>Its new parent. <c>null</c> puts it at the scene root.</summary>
        public Transform NewParent
        {
            get => m_NewParent;
            set => m_NewParent = value;
        }

        /// <summary>Snap onto the parent, or keep the world pose. Default snap.</summary>
        public ParentToPlacement Placement
        {
            get => m_Placement;
            set => m_Placement = value;
        }

        /// <summary>Local position after a snap. Default zero.</summary>
        public Vector3 LocalOffset
        {
            get => m_LocalOffset;
            set => m_LocalOffset = value;
        }

        /// <summary>Local Euler rotation after a snap, in degrees. Default zero.</summary>
        public Vector3 LocalRotation
        {
            get => m_LocalRotation;
            set => m_LocalRotation = value;
        }

        /// <inheritdoc/>
        public override IEnumerator Run(Interaction interaction)
        {
            if (m_Target == null)
                return null;

            if (m_NewParent != null && (m_NewParent == m_Target || m_NewParent.IsChildOf(m_Target)))
            {
                Debug.LogWarning(
                    $"[Building Blocks] Parent To: '{m_Target.name}' cannot be parented under itself or one of " +
                    "its own children.", interaction?.Interactable);
                return null;
            }

            bool keepWorld = m_Placement == ParentToPlacement.KeepWorldPose;
            m_Target.SetParent(m_NewParent, keepWorld);

            if (!keepWorld)
            {
                m_Target.localPosition = m_LocalOffset;
                m_Target.localRotation = Quaternion.Euler(m_LocalRotation);
            }

            return null;
        }
    }
}
