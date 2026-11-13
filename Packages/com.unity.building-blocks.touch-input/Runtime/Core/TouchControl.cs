using System;
using UnityEngine;

namespace Unity.BuildingBlocks.TouchInput
{
    /// <summary>
    /// Base class for on-screen controls. A view feeds it pointer samples and it exposes a value to gameplay.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One finger owns a control. A second finger's <see cref="PointerPhase.Began"/> is ignored until the
    /// owner lifts. If the pointer stream can stop mid-gesture, call <see cref="Cancel"/>; the uGUI views do
    /// so on disable and on focus loss.
    /// </para>
    /// <para>
    /// To derive, override <see cref="OnBegan"/>, <see cref="OnMoved"/>, <see cref="OnEnded"/> and
    /// <see cref="OnCancel"/>, and keep them allocation-free. Override <see cref="Accept"/> only to track
    /// several fingers.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public abstract class TouchControl : MonoBehaviour
    {
        /// <summary>The value of <see cref="OwnerPointerId"/> while no finger is on the control.</summary>
        public const int NoPointer = int.MinValue;

        [SerializeField]
        [Tooltip("Unique name within the rig that gameplay uses to find this control, such as \"Move\".")]
        string m_ActionId = "";

        int m_OwnerPointerId = NoPointer;
        bool m_Live;
        TouchRig m_Rig;

        /// <summary>
        /// Raised after each accepted sample and after a <see cref="Cancel"/> that changed something. The
        /// argument is the control, so one handler can serve several.
        /// </summary>
        public event Action<TouchControl> Changed;

        /// <summary>
        /// The name gameplay finds this control by, unique within its <see cref="TouchRig"/>. Changing it while
        /// registered raises <see cref="TouchRig.ControlsChanged"/>.
        /// </summary>
        public string ActionId
        {
            get => m_ActionId;
            set
            {
                string id = value ?? "";
                if (string.Equals(id, m_ActionId, StringComparison.Ordinal))
                    return;

                m_ActionId = id;
                if (m_Live && m_Rig != null)
                    m_Rig.NotifyControlsChanged();
            }
        }

        /// <summary>The pointer currently owning this control, or <see cref="NoPointer"/>.</summary>
        public int OwnerPointerId => m_OwnerPointerId;

        /// <summary>Whether a finger is on the control right now.</summary>
        public virtual bool IsPressed => m_OwnerPointerId != NoPointer;

        /// <summary>The rig this control registered with when it was enabled, or <c>null</c> if none is above it.</summary>
        public TouchRig Rig => m_Rig;

        /// <summary>
        /// Passes one pointer sample to the control; views call it once per pointer event. It allocates nothing,
        /// and the same samples always give the same values.
        /// </summary>
        /// <returns>
        /// <c>false</c> if the sample was ignored and nothing changed: a <see cref="PointerPhase.Began"/> while
        /// another finger owns the control, or any other phase from a finger that does not own it.
        /// </returns>
        public bool Feed(in PointerSample sample)
        {
            if (!Accept(in sample))
                return false;

            Changed?.Invoke(this);
            return true;
        }

        /// <summary>
        /// Releases the owning finger and returns the control to rest. Call it when the pointer stream may
        /// have stopped mid-gesture; the uGUI views do so on disable and on focus loss.
        /// </summary>
        /// <remarks>
        /// A pressed <see cref="TouchButton"/> raises <c>Released</c>. A <see cref="TouchJoystick"/> recenters
        /// even when set not to.
        /// </remarks>
        public void Cancel()
        {
            bool wasPressed = IsPressed;
            bool wasAtRest = IsAtRest;

            m_OwnerPointerId = NoPointer;
            OnCancel(wasPressed);

            if (!wasAtRest)
                Changed?.Invoke(this);
        }

        /// <summary>
        /// Whether there is nothing to reset: no finger and the value at rest. Override when a value can outlive
        /// the finger: <see cref="Cancel"/> raises <see cref="Changed"/> only when this is false.
        /// </summary>
        protected virtual bool IsAtRest => !IsPressed;

        /// <summary>
        /// Applies the one-finger ownership rule and calls the hooks. Override only for different ownership.
        /// </summary>
        /// <returns>Whether the sample was taken into account; see <see cref="Feed"/>.</returns>
        protected virtual bool Accept(in PointerSample sample)
        {
            switch (sample.Phase)
            {
                case PointerPhase.Began:
                    if (m_OwnerPointerId != NoPointer)
                        return false;

                    m_OwnerPointerId = sample.PointerId;
                    OnBegan(in sample);
                    return true;

                case PointerPhase.Moved:
                    if (sample.PointerId != m_OwnerPointerId)
                        return false;

                    OnMoved(in sample);
                    return true;

                case PointerPhase.Ended:
                case PointerPhase.Canceled:
                    if (sample.PointerId != m_OwnerPointerId)
                        return false;

                    // The owner is gone before the hook runs, so IsPressed reads false inside Released handlers.
                    m_OwnerPointerId = NoPointer;
                    OnEnded(in sample, sample.Phase == PointerPhase.Canceled);
                    return true;
            }

            return false;
        }

        /// <summary>For controls that override <see cref="Accept"/>: sets <see cref="OwnerPointerId"/>.</summary>
        protected void SetOwner(int pointerId) => m_OwnerPointerId = pointerId;

        /// <summary>The owning finger landed. <see cref="IsPressed"/> is already <c>true</c>.</summary>
        protected abstract void OnBegan(in PointerSample sample);

        /// <summary>The owning finger moved.</summary>
        protected abstract void OnMoved(in PointerSample sample);

        /// <summary>
        /// The owning finger lifted (<paramref name="canceled"/> <c>false</c>) or was taken away by the OS
        /// (<c>true</c>). <see cref="IsPressed"/> is already <c>false</c>.
        /// </summary>
        protected abstract void OnEnded(in PointerSample sample, bool canceled);

        /// <summary>
        /// <see cref="Cancel"/> was called: drop every finger and return every value to rest, with no sample or
        /// time available. <paramref name="wasPressed"/> says whether a finger was on the control.
        /// </summary>
        protected abstract void OnCancel(bool wasPressed);

        void OnEnable() => Activate();

        void OnDisable() => Deactivate();

        // Destroy finishes at the end of the frame. A listener that looked after OnDisable may still have seen
        // this control, so tell the rig again.
        void OnDestroy()
        {
            TouchRig rig = GetComponentInParent<TouchRig>(true);
            if (rig != null)
                rig.NotifyControlsChanged();
        }

#if UNITY_EDITOR
        // An id typed in the Inspector during Play skips the ActionId setter. Any other field change costs
        // only one rescan.
        void OnValidate()
        {
            if (m_Live && m_Rig != null)
                m_Rig.NotifyControlsChanged();
        }
#endif

        /// <summary>
        /// The body of <c>OnEnable</c>: registers with the nearest parent <see cref="TouchRig"/>. Internal
        /// because EditMode tests never run a new component's <c>OnEnable</c>.
        /// </summary>
        internal void Activate()
        {
            if (m_Live)
                return;

            m_Live = true;
            m_Rig = GetComponentInParent<TouchRig>(true);

            if (m_Rig != null)
                m_Rig.Register(this);
        }

        /// <summary>The body of <c>OnDisable</c>: cancels and leaves the rig. See <see cref="Activate"/>.</summary>
        internal void Deactivate()
        {
            if (!m_Live)
                return;

            m_Live = false;
            Cancel();

            if (m_Rig != null)
                m_Rig.Unregister(this);

            m_Rig = null;
        }
    }
}
