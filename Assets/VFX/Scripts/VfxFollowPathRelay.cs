using UnityEngine;

namespace Courtyard.VFX
{
    /// <summary>
    /// Hidden helper added at runtime by <see cref="VfxFollowPath"/> to each
    /// listed ParticleSystem's GameObject. Unity only sends
    /// OnParticleUpdateJobScheduled to components on the ParticleSystem's own
    /// GameObject, so this forwards it to the shared VfxFollowPath.
    /// Never saved (HideAndDontSave) and never added by hand.
    /// </summary>
    [ExecuteAlways]
    [AddComponentMenu("")]
    public class VfxFollowPathRelay : MonoBehaviour
    {
        internal VfxFollowPath Owner;
        internal int Index;

        void OnParticleUpdateJobScheduled()
        {
            if (Owner != null)
                Owner.ScheduleFor(Index);
        }
    }
}
