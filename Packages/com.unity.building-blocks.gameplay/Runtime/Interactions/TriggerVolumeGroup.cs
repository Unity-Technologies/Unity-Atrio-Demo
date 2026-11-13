using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>
    /// Several <see cref="TriggerVolume"/>s that behave as one: a collider is inside the group while
    /// it is inside any member, and leaves only once it has left all of them.
    /// </summary>
    /// <remarks>
    /// Members keep their own events. Walking from one member into an overlapping neighbour fires
    /// nothing on the group, and <see cref="ActiveWhileInside"/> stays on. A disabled group listens to nothing.
    /// </remarks>
    [Icon("Packages/com.unity.building-blocks.gameplay/Editor/Icons/TriggerVolume.png")]
    [AddComponentMenu("Building Blocks/Trigger Volume Group")]
    [DisallowMultipleComponent]
    public sealed class TriggerVolumeGroup : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The volumes that make up this group. Reset collects every Trigger Volume in the children.")]
        List<TriggerVolume> m_Volumes = new List<TriggerVolume>();

        [Header("Reactions")]
        [SerializeField]
        [Tooltip("Switched on when someone enters any member and off when the last occupant has left " +
                 "them all: the Press E sign, a glow. GameObjects, or components with an enabled checkbox.")]
        Object[] m_ActiveWhileInside = Array.Empty<Object>();

        [Header("Events")]
        [SerializeField]
        [Tooltip("A collider entered the union: it was in none of the members and is now in one.")]
        UnityEvent<Collider> m_OnEnteredAny = new UnityEvent<Collider>();

        [SerializeField]
        [Tooltip("A collider left the union: it is no longer inside any member.")]
        UnityEvent<Collider> m_OnExitedAll = new UnityEvent<Collider>();

        // Collider → how many member volumes currently contain it.
        readonly Dictionary<Collider, int> m_Membership = new Dictionary<Collider, int>();

        // True between OnEnable and OnDisable: only a live group subscribes to its members.
        bool m_Live;

        bool m_WarnedUnsupported;

        /// <summary>A collider entered the union. Code-side twin of <see cref="OnEnteredAny"/>.</summary>
        public event Action<TriggerVolumeGroup, Collider> EnteredAny;

        /// <summary>A collider left the union. Code-side twin of <see cref="OnExitedAll"/>.</summary>
        public event Action<TriggerVolumeGroup, Collider> ExitedAll;

        /// <summary>The member volumes.</summary>
        public IReadOnlyList<TriggerVolume> Volumes => m_Volumes;

        /// <summary>
        /// GameObjects or components switched on when someone enters any member and off when the last occupant has left them all.
        /// Replaced while someone is inside, the dropped ones switch off and the new ones on at once.
        /// </summary>
        public Object[] ActiveWhileInside
        {
            get => m_ActiveWhileInside;
            set
            {
                Object[] next = value ?? Array.Empty<Object>();
                // Someone is inside: the old list is on, so swap what is on to match the new one.
                if (m_Live && m_Membership.Count > 0)
                    TriggerVolume.SwapActiveWhileInside(m_ActiveWhileInside, next, this, ref m_WarnedUnsupported);
                m_ActiveWhileInside = next;
            }
        }

        /// <summary>Inspector event: a collider entered the union.</summary>
        public UnityEvent<Collider> OnEnteredAny => m_OnEnteredAny;

        /// <summary>Inspector event: a collider left every member.</summary>
        public UnityEvent<Collider> OnExitedAll => m_OnExitedAll;

        /// <summary>Whether any collider is inside any member.</summary>
        public bool IsOccupied => m_Membership.Count > 0;

        /// <summary>Whether <paramref name="collider"/> is inside at least one member.</summary>
        public bool Contains(Collider collider) => collider != null && m_Membership.ContainsKey(collider);

        /// <summary>Adds a member. Its current occupants join the union immediately.</summary>
        public void AddVolume(TriggerVolume volume)
        {
            if (volume == null || m_Volumes.Contains(volume))
                return;

            m_Volumes.Add(volume);

            if (m_Live)
                Attach(volume);
        }

        /// <summary>Removes a member. Colliders that were inside only this member leave the union.</summary>
        public void RemoveVolume(TriggerVolume volume)
        {
            if (volume == null || !m_Volumes.Remove(volume))
                return;

            if (m_Live)
                Detach(volume);
        }

        void OnEnable() => Activate();

        void OnDisable() => Deactivate();

        /// <summary>The body of <c>OnEnable</c>, callable by EditMode tests, which never run it.</summary>
        internal void Activate()
        {
            if (m_Live)
                return;

            m_Live = true;

            // A copy: an Entered Any raised while seeding may add or remove members.
            foreach (TriggerVolume volume in m_Volumes.ToArray())
            {
                // A callback may have switched the group off, or removed a member it has not reached yet.
                if (!m_Live)
                    return;
                if (m_Volumes.Contains(volume))
                    Attach(volume);
            }
        }

        /// <summary>The body of <c>OnDisable</c>. See <see cref="Activate"/>.</summary>
        internal void Deactivate()
        {
            if (!m_Live)
                return;

            m_Live = false;

            // A copy: an Exited All raised here may remove a member, which would shift the list under the loop.
            foreach (TriggerVolume volume in m_Volumes.ToArray())
                Detach(volume);

            // Colliders Detach missed (a member destroyed mid-way) leave now, so no stale "inside" remains.
            if (m_Membership.Count == 0)
                return;

            var remaining = new List<Collider>(m_Membership.Keys);
            m_Membership.Clear();

            foreach (Collider collider in remaining)
            {
                ExitedAll?.Invoke(this, collider);
                m_OnExitedAll.Invoke(collider);
            }

            SetActiveWhileInside(false);
        }

        void Attach(TriggerVolume volume)
        {
            if (volume == null)
                return;

            volume.TriggerEntered += OnMemberEntered;
            volume.TriggerExited += OnMemberExited;

            // Seed with current occupants so enabling mid-scene reports the right state.
            IReadOnlyList<Collider> occupants = volume.Occupants;
            for (int i = 0; i < occupants.Count; i++)
                OnMemberEntered(volume, occupants[i]);
        }

        void Detach(TriggerVolume volume)
        {
            if (volume == null)
                return;

            volume.TriggerEntered -= OnMemberEntered;
            volume.TriggerExited -= OnMemberExited;

            // A copy: an Exited All raised here may switch the member off, which empties its list.
            var occupants = new List<Collider>(volume.Occupants);
            for (int i = occupants.Count - 1; i >= 0; i--)
                MemberExited(volume, occupants[i]);
        }

        void OnMemberEntered(TriggerVolume volume, Collider collider)
        {
            // Off is off, even for a member a callback left subscribed.
            if (!m_Live || collider == null)
                return;

            m_Membership.TryGetValue(collider, out int count);
            m_Membership[collider] = count + 1;

            if (count != 0)
                return;

            // Empty to occupied: the first collider into any member.
            if (m_Membership.Count == 1)
                SetActiveWhileInside(true);

            EnteredAny?.Invoke(this, collider);
            m_OnEnteredAny.Invoke(collider);
        }

        void OnMemberExited(TriggerVolume volume, Collider collider)
        {
            if (m_Live)
                MemberExited(volume, collider);
        }

        /// <summary>The exit itself; Detach calls it on a group that is switching off.</summary>
        void MemberExited(TriggerVolume volume, Collider collider)
        {
            // ReferenceEquals: a collider destroyed inside a member still has to leave.
            if (ReferenceEquals(collider, null) || !m_Membership.TryGetValue(collider, out int count))
                return;

            count--;
            if (count > 0)
            {
                m_Membership[collider] = count;
                return;
            }

            m_Membership.Remove(collider);

            ExitedAll?.Invoke(this, collider);
            m_OnExitedAll.Invoke(collider);

            if (m_Membership.Count == 0)
                SetActiveWhileInside(false);
        }

        void SetActiveWhileInside(bool on) =>
            TriggerVolume.SetActiveWhileInside(m_ActiveWhileInside, on, this, ref m_WarnedUnsupported);

        /// <summary>Collects every <see cref="TriggerVolume"/> in the children as the initial membership.</summary>
        void Reset()
        {
            m_Volumes.Clear();
            GetComponentsInChildren(true, m_Volumes);
        }
    }
}
