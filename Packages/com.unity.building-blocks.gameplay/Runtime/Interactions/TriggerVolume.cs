using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace Unity.BuildingBlocks.Gameplay
{
    /// <summary>What a <see cref="TriggerVolume"/> reacts to.</summary>
    public enum TriggerVolumeDetect
    {
        /// <summary>Anything whose GameObject carries <see cref="TriggerVolume.Tag"/>. The default: the First Person Player is tagged "Player".</summary>
        Tag,

        /// <summary>One specific GameObject (or anything under it), dragged into <see cref="TriggerVolume.TargetObject"/>.</summary>
        Object,

        /// <summary>Anything on the layers in <see cref="TriggerVolume.Layers"/>.</summary>
        Layers,
    }

    /// <summary>How often a <see cref="TriggerVolume"/> fires.</summary>
    public enum TriggerVolumeFire
    {
        /// <summary>Every entry is reported. The default.</summary>
        EveryTime,

        /// <summary>Only the first entry is reported. <see cref="TriggerVolume.AfterFiring"/> decides what happens once the occupant has left.</summary>
        Once,
    }

    /// <summary>What a <see cref="TriggerVolume"/> set to fire <see cref="TriggerVolumeFire.Once"/> does to itself once the occupant has left.</summary>
    public enum TriggerVolumeAfterFiring
    {
        /// <summary>Stays as it is, ignoring further entries, until <see cref="TriggerVolume.Rearm"/>.</summary>
        Keep,

        /// <summary>Switches this component off. The box stays; nothing is reported until something enables it again.</summary>
        TurnOff,

        /// <summary>Destroys the GameObject it sits on.</summary>
        DestroyObject,
    }

    /// <summary>
    /// A box in the scene that reports when something enters and leaves it. An
    /// <see cref="Interactable"/> uses it as its <c>Zone</c>; anything else listens to
    /// <see cref="TriggerEnter"/> and <see cref="TriggerExit"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Add one from GameObject > Building Blocks > Trigger Volume. The shape is a trigger
    /// <see cref="BoxCollider"/>. <see cref="Detect"/> picks the filter (tag, object or layers). Only
    /// colliders with a <see cref="Rigidbody"/> on one side register; the player always does.
    /// </para>
    /// <para>
    /// Where volumes overlap, only the highest <see cref="Priority"/> reports a collider; equal
    /// priorities both report. The events fire per occupant and pass its collider.
    /// </para>
    /// </remarks>
    [Icon("Packages/com.unity.building-blocks.gameplay/Editor/Icons/TriggerVolume.png")]
    [AddComponentMenu("Building Blocks/Trigger Volume")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TriggerVolume : MonoBehaviour
    {
        [Header("Volume")]
        [SerializeField]
        [Tooltip("Shown in the scene view above the box. Empty shows the GameObject's name.")]
        string m_Label = "";

        [SerializeField]
        [Tooltip("Gizmo colour in the scene view. Purely visual; use it to tell volumes apart.")]
        Color m_Color = k_DefaultColor;

        [SerializeField]
        [Tooltip("When volumes overlap, only the highest priority reports a collider inside both. " +
                 "Equal priorities both report.")]
        int m_Priority;

        [Header("Detect")]
        [SerializeField]
        [Tooltip("What sets this volume off: anything with a tag, one specific object, or anything " +
                 "on some layers.")]
        TriggerVolumeDetect m_Detect = TriggerVolumeDetect.Tag;

        [SerializeField]
        [Tooltip("Only GameObjects carrying this tag are reported. The sample player is tagged Player.")]
        string m_Tag = k_DefaultTag;

        [SerializeField]
        [Tooltip("Only this GameObject, or anything under it, is reported.")]
        GameObject m_Object;

        [SerializeField]
        [Tooltip("Only colliders on these layers are reported.")]
        LayerMask m_Layers = ~0;

        [Header("Reactions")]
        [SerializeField]
        [Tooltip("Switched on when someone enters and off when the last occupant leaves: the Press E " +
                 "sign, a glow. GameObjects, or components with an enabled checkbox.")]
        Object[] m_ActiveWhileInside = Array.Empty<Object>();

        [SerializeField]
        [Tooltip("Every Time: every entry is reported. Once: only the first; later entries are ignored " +
                 "and After Firing applies when the occupant leaves.")]
        TriggerVolumeFire m_Fire = TriggerVolumeFire.EveryTime;

        [SerializeField]
        [Tooltip("With Fire: Once, what happens to this volume once the occupant has left. Keep leaves " +
                 "it armed against nothing. Turn Off switches the component off. Destroy Object removes it.")]
        TriggerVolumeAfterFiring m_AfterFiring = TriggerVolumeAfterFiring.Keep;

        [Header("Events")]
        [SerializeField]
        [Tooltip("Something that passes the filter entered the volume. The argument is its collider.")]
        UnityEvent<Collider> m_OnTriggerEnter = new UnityEvent<Collider>();

        [SerializeField]
        [Tooltip("Something that had entered left the volume (or was disabled, destroyed, or this " +
                 "volume was disabled).")]
        UnityEvent<Collider> m_OnTriggerExit = new UnityEvent<Collider>();

        /// <summary>Default gizmo colour: a soft blue.</summary>
        public static readonly Color k_DefaultColor = new Color(0.35f, 0.65f, 1f, 1f);

        /// <summary>Default <see cref="Tag"/>: Unity's built-in "Player" tag, which every project has.</summary>
        public const string k_DefaultTag = "Player";

        // Every enabled volume, so overlapping ones can settle who reports a collider.
        static readonly List<TriggerVolume> s_Live = new List<TriggerVolume>();

        // Physically inside and accepted by the filter.
        readonly List<Collider> m_Inside = new List<Collider>();

        // What listeners were told is inside: m_Inside minus colliders a higher-priority volume reports.
        readonly List<Collider> m_Reported = new List<Collider>();

        BoxCollider m_Box;
        bool m_Fired;
        bool m_WarnedUnsupported;

        /// <summary>Something entered. Code-side twin of <see cref="TriggerEnter"/>.</summary>
        public event Action<TriggerVolume, Collider> TriggerEntered;

        /// <summary>Something left. Code-side twin of <see cref="TriggerExit"/>.</summary>
        public event Action<TriggerVolume, Collider> TriggerExited;

        /// <summary>Text drawn above the box in the scene view. Empty draws the GameObject's name.</summary>
        public string Label
        {
            get => m_Label;
            set => m_Label = value ?? "";
        }

        /// <summary>Gizmo colour.</summary>
        public Color Color
        {
            get => m_Color;
            set => m_Color = value;
        }

        /// <summary>Wins over lower values when volumes overlap. Default <c>0</c>.</summary>
        public int Priority
        {
            get => m_Priority;
            set => m_Priority = value;
        }

        /// <summary>Which filter <see cref="Accepts"/> applies. Default <see cref="TriggerVolumeDetect.Tag"/>.</summary>
        public TriggerVolumeDetect Detect
        {
            get => m_Detect;
            set => m_Detect = value;
        }

        /// <summary>The tag accepted in <see cref="TriggerVolumeDetect.Tag"/> mode. Empty accepts any tag.</summary>
        public string Tag
        {
            get => m_Tag;
            set => m_Tag = value ?? "";
        }

        /// <summary>The object accepted in <see cref="TriggerVolumeDetect.Object"/> mode, children included.</summary>
        public GameObject TargetObject
        {
            get => m_Object;
            set => m_Object = value;
        }

        /// <summary>The layers accepted in <see cref="TriggerVolumeDetect.Layers"/> mode.</summary>
        public LayerMask Layers
        {
            get => m_Layers;
            set => m_Layers = value;
        }

        /// <summary>
        /// GameObjects or components switched on when someone enters and off when the last occupant leaves.
        /// Replaced while someone is inside, the dropped ones switch off and the new ones on at once.
        /// </summary>
        public Object[] ActiveWhileInside
        {
            get => m_ActiveWhileInside;
            set
            {
                Object[] next = value ?? Array.Empty<Object>();
                // Someone is inside: the old list is on, so swap what is on to match the new one.
                if (m_Reported.Count > 0)
                    SwapActiveWhileInside(m_ActiveWhileInside, next, this, ref m_WarnedUnsupported);
                m_ActiveWhileInside = next;
            }
        }

        /// <summary>How often the volume fires. Default <see cref="TriggerVolumeFire.EveryTime"/>.</summary>
        public TriggerVolumeFire Fire
        {
            get => m_Fire;
            set => m_Fire = value;
        }

        /// <summary>With <see cref="Fire"/> set to Once, what happens once the occupant has left. Default <see cref="TriggerVolumeAfterFiring.Keep"/>.</summary>
        public TriggerVolumeAfterFiring AfterFiring
        {
            get => m_AfterFiring;
            set => m_AfterFiring = value;
        }

        /// <summary>Whether a volume set to fire once has fired. Always <c>false</c> for a volume that fires every time.</summary>
        public bool HasFired => m_Fire == TriggerVolumeFire.Once && m_Fired;

        /// <summary>The box that is the volume's shape. Always a trigger.</summary>
        public BoxCollider Box => m_Box != null ? m_Box : m_Box = GetComponent<BoxCollider>();

        /// <summary>Inspector event: something entered. Fires for every occupant that passes the filter.</summary>
        public UnityEvent<Collider> TriggerEnter => m_OnTriggerEnter;

        /// <summary>Inspector event: something left.</summary>
        public UnityEvent<Collider> TriggerExit => m_OnTriggerExit;

        /// <summary>Whether anything this volume reports is inside right now.</summary>
        public bool IsOccupied => OccupantCount > 0;

        /// <summary>How many colliders this volume currently reports as inside.</summary>
        public int OccupantCount
        {
            get
            {
                Prune();
                return m_Reported.Count;
            }
        }

        /// <summary>The colliders this volume currently reports as inside.</summary>
        public IReadOnlyList<Collider> Occupants => m_Reported;

        /// <summary>Whether this volume currently reports <paramref name="collider"/> as inside.</summary>
        public bool Contains(Collider collider) => collider != null && m_Reported.Contains(collider);

        /// <summary>
        /// Whether <paramref name="collider"/> is physically inside and passed the filter, even if a
        /// higher-priority volume reports it instead of this one (see <see cref="Contains"/>).
        /// </summary>
        public bool IsInside(Collider collider) => collider != null && m_Inside.Contains(collider);

        /// <summary>Whether the filter lets <paramref name="collider"/> through.</summary>
        public bool Accepts(Collider collider)
        {
            if (collider == null)
                return false;

            switch (m_Detect)
            {
                case TriggerVolumeDetect.Tag:
                    return string.IsNullOrEmpty(m_Tag) ||
                           string.Equals(collider.tag, m_Tag, StringComparison.Ordinal);

                case TriggerVolumeDetect.Object:
                    return m_Object != null && collider.transform.IsChildOf(m_Object.transform);

                case TriggerVolumeDetect.Layers:
                    return (m_Layers.value & (1 << collider.gameObject.layer)) != 0;
            }

            return false;
        }

        /// <summary>
        /// Lets a volume that fired once fire again. Does nothing for a volume that fires every time, and
        /// does not switch the component back on after <see cref="TriggerVolumeAfterFiring.TurnOff"/>.
        /// </summary>
        public void Rearm() => m_Fired = false;

        void OnTriggerEnter(Collider other) => HandleEnter(other);

        void OnTriggerExit(Collider other) => HandleExit(other);

        void OnEnable() => Register();

        void OnDisable() => Deactivate();

        /// <summary>The body of <c>OnDisable</c>: empties the volume without applying After Firing. EditMode tests call it.</summary>
        internal void Deactivate()
        {
            Prune();

            // HandleExit can remove more than one entry per call, hence "while not empty". Turning the
            // volume off is not the occupant leaving, so After Firing does not apply.
            while (m_Inside.Count > 0)
            {
                int before = m_Inside.Count;
                HandleExit(m_Inside[m_Inside.Count - 1], applyAfterFiring: false);

                if (m_Inside.Count >= before)
                {
                    // Nothing was removed (a listener re-added it mid-exit); do not spin.
                    m_Inside.Clear();
                    m_Reported.Clear();
                    break;
                }
            }

            s_Live.Remove(this);
        }

        internal void HandleEnter(Collider other)
        {
            if (Prune())
                MaybeApplyAfterFiring();

            // Physics sends trigger messages to disabled components too. Not isActiveAndEnabled: it stays
            // false in EditMode tests, where OnEnable never runs.
            if (!enabled || !gameObject.activeInHierarchy)
                return;

            if (!Accepts(other) || m_Inside.Contains(other))
                return;

            // Fired once and empty: stop tracking. While the first occupant is inside, its other
            // colliders are still tracked so "left" means all of them are out.
            if (m_Fire == TriggerVolumeFire.Once && m_Fired && m_Inside.Count == 0)
                return;

            m_Inside.Add(other);
            Register();

            if (IsShadowedFor(other))
                return;

            Report(other);

            // This volume outranks any lower one that was reporting the same collider: take over.
            for (int i = s_Live.Count - 1; i >= 0; i--)
            {
                TriggerVolume other_ = s_Live[i];
                if (other_ == null || ReferenceEquals(other_, this))
                    continue;

                if (other_.m_Priority < m_Priority && other_.m_Reported.Contains(other))
                    other_.Unreport(other);
            }
        }

        internal void HandleExit(Collider other) => HandleExit(other, applyAfterFiring: true);

        void HandleExit(Collider other, bool applyAfterFiring)
        {
            // Remove before pruning so a collider destroyed while inside still counts as leaving.
            bool wasInside = m_Inside.Remove(other);
            Prune();

            if (!wasInside)
                return;

            if (m_Reported.Contains(other))
            {
                Unreport(other);
                if (other != null)
                    HandOver(other);
            }

            if (applyAfterFiring)
                MaybeApplyAfterFiring();
        }

        /// <summary>A higher-priority enabled volume has <paramref name="collider"/> inside too.</summary>
        bool IsShadowedFor(Collider collider)
        {
            for (int i = 0; i < s_Live.Count; i++)
            {
                TriggerVolume other = s_Live[i];
                if (other == null || ReferenceEquals(other, this))
                    continue;

                if (other.m_Priority > m_Priority && other.m_Inside.Contains(collider))
                    return true;
            }

            return false;
        }

        /// <summary>Hands <paramref name="collider"/> to the highest-priority volume it is still inside.</summary>
        void HandOver(Collider collider)
        {
            TriggerVolume best = null;

            for (int i = 0; i < s_Live.Count; i++)
            {
                TriggerVolume other = s_Live[i];
                if (other == null || ReferenceEquals(other, this))
                    continue;

                if (!other.m_Inside.Contains(collider) || other.m_Reported.Contains(collider))
                    continue;

                if (best == null || other.m_Priority > best.m_Priority)
                    best = other;
            }

            if (best != null && !best.IsShadowedFor(collider))
                best.Report(collider);
        }

        void Report(Collider collider)
        {
            m_Reported.Add(collider);

            if (m_Reported.Count == 1)
            {
                // Empty → occupied: the first report is the one that counts as "fired".
                if (m_Fire == TriggerVolumeFire.Once)
                    m_Fired = true;
                SetActiveWhileInside(true);
            }

            TriggerEntered?.Invoke(this, collider);
            m_OnTriggerEnter.Invoke(collider);
        }

        void Unreport(Collider collider)
        {
            if (!m_Reported.Remove(collider))
                return;

            TriggerExited?.Invoke(this, collider);
            m_OnTriggerExit.Invoke(collider);

            if (m_Reported.Count == 0)
            {
                // Occupied to empty for listeners. A higher-priority takeover also lands here, so After
                // Firing waits for the occupant to physically leave.
                SetActiveWhileInside(false);
            }
        }

        /// <summary>Fired once, and nobody is physically inside any more: apply After Firing.</summary>
        void MaybeApplyAfterFiring()
        {
            if (m_Fire != TriggerVolumeFire.Once || !m_Fired || m_Inside.Count > 0)
                return;

            switch (m_AfterFiring)
            {
                case TriggerVolumeAfterFiring.TurnOff:
                    enabled = false;
                    break;

                case TriggerVolumeAfterFiring.DestroyObject:
                    // Destroy is refused outside Play mode.
                    if (Application.isPlaying) Destroy(gameObject);
                    else DestroyImmediate(gameObject);
                    break;
            }
        }

        void SetActiveWhileInside(bool on) => SetActiveWhileInside(m_ActiveWhileInside, on, this, ref m_WarnedUnsupported);

        /// <summary>Switches off what is in <paramref name="current"/> but not <paramref name="next"/>, then switches <paramref name="next"/> on.</summary>
        internal static void SwapActiveWhileInside(Object[] current, Object[] next, Component owner, ref bool warned)
        {
            // Off only what leaves the list, so a target in both never blinks.
            if (current != null)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    Object target = current[i];
                    if (target != null && Array.IndexOf(next, target) < 0 && SetActiveStep.TryGetState(target, out _))
                        SetActiveStep.SetState(target, false);
                }
            }

            SetActiveWhileInside(next, true, owner, ref warned);
        }

        /// <summary>
        /// Switches each of <paramref name="targets"/> on or off the way Set Active does. Warns once, through
        /// <paramref name="warned"/>, about a target with no on/off switch.
        /// </summary>
        internal static void SetActiveWhileInside(Object[] targets, bool on, Component owner, ref bool warned)
        {
            if (targets == null)
                return;

            for (int i = 0; i < targets.Length; i++)
            {
                Object target = targets[i];
                if (target == null)
                    continue;

                if (!SetActiveStep.TryGetState(target, out _))
                {
                    if (!warned)
                    {
                        warned = true;
                        Debug.LogWarning(
                            $"[Building Blocks] {owner.name}: Active While Inside has '{target.name}' ({target.GetType().Name}), which has no " +
                            "on/off switch. Drop in the GameObject, or a Behaviour, Renderer or Collider on it.", owner);
                    }

                    continue;
                }

                SetActiveStep.SetState(target, on);
            }
        }

        void Register()
        {
            if (!s_Live.Contains(this))
                s_Live.Add(this);
        }

        /// <summary>Drops destroyed colliders; a reported one counts as having left.</summary>
        bool Prune()
        {
            bool dropped = false;
            for (int i = m_Reported.Count - 1; i >= 0; i--)
            {
                if (i >= m_Reported.Count)
                    continue;   // a listener changed the list mid-prune

                Collider dead = m_Reported[i];
                if (dead != null)
                    continue;

                dropped = true;
                m_Inside.Remove(dead);
                Unreport(dead);
            }

            for (int i = m_Inside.Count - 1; i >= 0; i--)
            {
                if (m_Inside[i] == null)
                    m_Inside.RemoveAt(i);
            }

            for (int i = s_Live.Count - 1; i >= 0; i--)
            {
                if (s_Live[i] == null)
                    s_Live.RemoveAt(i);
            }

            return dropped;
        }

        void Reset()
        {
            BoxCollider box = Box;
            if (box != null && box.size == Vector3.one && box.center == Vector3.zero)
            {
                // A fresh BoxCollider: give it a room-ish default and stand it on the ground.
                box.size = new Vector3(2f, 2f, 2f);
                box.center = Vector3.up;
            }

            ForceTriggers();
        }

        void OnValidate() => ForceTriggers();

        void ForceTriggers()
        {
            foreach (Collider collider in GetComponents<Collider>())
                collider.isTrigger = true;
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            BoxCollider box = Box;
            if (box == null)
                return;

            Color fill = m_Color;
            fill.a = m_Reported.Count > 0 ? 0.4f : 0.2f;
            Color wire = m_Color;
            wire.a = 0.9f;

            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.color = fill;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = wire;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;

            Vector3 top = box.transform.TransformPoint(box.center + Vector3.up * (box.size.y * 0.5f));
            string text = string.IsNullOrEmpty(m_Label) ? name : m_Label;

            var style = new GUIStyle(UnityEditor.EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.LowerCenter,
                normal = { textColor = UnityEngine.Color.white },
            };
            UnityEditor.Handles.Label(top + Vector3.up * 0.15f, text, style);
        }
#endif
    }
}
