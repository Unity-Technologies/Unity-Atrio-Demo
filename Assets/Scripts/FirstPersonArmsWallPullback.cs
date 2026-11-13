using System.Collections.Generic;
using UnityEngine;
using Unity.BuildingBlocks.Gameplay;

/// <summary>
/// Eases the first-person arms back and down when the player stands close to a wall,
/// so the hands (and a held torch with its light) do not poke through it.
/// Put it on the arms rig root. It moves only this transform's local position, so it
/// stacks on top of whatever the Animator does to the bones.
/// </summary>
/// <remarks>
/// Probes from the eyes, from the chest, and toward each held item (any MeshRenderer under
/// the rig, like the torch). The pull starts softly a little before a wall gets in reach and
/// depends only on how close the wall is now, so it never overshoots. It relaxes while an interaction locks the player, so arms can
/// still reach buttons, cats and doors.
/// </remarks>
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)] // After FirstPersonCameraRig (100) and CameraPitchAnimatorDriver (105).
public class FirstPersonArmsWallPullback : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Camera rig to probe from. Found automatically if empty.")]
    FirstPersonCameraRig m_CameraRig;

    [SerializeField]
    [Tooltip("What counts as a wall. Leave out the player's own layer.")]
    LayerMask m_WallLayers = ~((1 << 2) | (1 << 8)); // Not Ignore Raycast, not Player.

    [SerializeField, Min(0.05f)]
    [Tooltip("How far in front of the eyes the hands reach, in meters. A wall closer than this pushes the arms back by the difference.")]
    float m_Reach = 0.6f;

    [SerializeField, Min(0.01f)]
    [Tooltip("Thickness of the probe, in meters.")]
    float m_ProbeRadius = 0.12f;

    [SerializeField, Min(0f)]
    [Tooltip("Height below the eyes of the second, chest-level probe, in meters.")]
    float m_ChestDrop = 0.45f;

    [SerializeField]
    [Tooltip("Meshes held in the hands (like the torch) to keep in front of walls. If empty, every MeshRenderer under this rig is used while it is visible.")]
    Renderer[] m_HeldItems;

    [SerializeField, Min(0f)]
    [Tooltip("Gap to keep between a held item's far edge and the wall, in meters.")]
    float m_HeldItemPadding = 0.05f;

    [SerializeField, Min(0f)]
    [Tooltip("Most the arms move back, in meters.")]
    float m_MaxPullBack = 0.35f;

    [SerializeField, Min(0f)]
    [Tooltip("How far the arms move down when fully pulled back, in meters.")]
    float m_MaxDrop = 0.1f;

    [SerializeField, Min(0f)]
    [Tooltip("Extra distance, in meters, over which the pull fades in before a wall reaches the hands. Bigger is gentler. 0 is a hard start.")]
    float m_SoftZone = 0.25f;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds to ease the arms back. 0 is instant.")]
    float m_EaseIn = 0.15f;

    [SerializeField, Min(0f)]
    [Tooltip("Seconds to ease the arms forward again. Slower than easing in reads as more natural.")]
    float m_EaseOut = 0.35f;

    readonly List<MeshRenderer> m_FoundItems = new();
    Vector3 m_RestLocalPosition;
    float m_Amount;
    float m_Velocity;

    /// <summary>How far the arms are pulled back right now, in meters.</summary>
    public float Amount => m_Amount;

    void OnEnable()
    {
        m_RestLocalPosition = transform.localPosition;
        m_Amount = 0f;
        m_Velocity = 0f;
        if (m_CameraRig == null)
            m_CameraRig = FindAnyObjectByType<FirstPersonCameraRig>();
        RefreshFoundItems();
    }

    void OnDisable()
    {
        transform.localPosition = m_RestLocalPosition;
    }

    void OnTransformChildrenChanged()
    {
        RefreshFoundItems();
    }

    void RefreshFoundItems()
    {
        if (m_HeldItems == null || m_HeldItems.Length == 0)
            GetComponentsInChildren(true, m_FoundItems);
    }

    void LateUpdate()
    {
        float target = ComputeTarget();
        float easeTime = target > m_Amount ? m_EaseIn : m_EaseOut;
        m_Amount = easeTime > 0f
            ? Mathf.SmoothDamp(m_Amount, target, ref m_Velocity, easeTime)
            : target;

        // The rig lives under the player's yaw, so local -Z is "back" and -Y is "down".
        float down = m_MaxPullBack > 0f ? m_MaxDrop * m_Amount / m_MaxPullBack : 0f;
        transform.localPosition = m_RestLocalPosition + new Vector3(0f, -down, -m_Amount);
    }

    float ComputeTarget()
    {
        if (m_CameraRig == null || m_CameraRig.IsControlLocked || m_CameraRig.IsAnchored)
            return 0f;

        Transform eye = m_CameraRig.PitchTransform != null ? m_CameraRig.PitchTransform : m_CameraRig.transform;
        Vector3 origin = eye.position;

        // Where the player looks, and straight ahead at chest height (catches waist-high railings).
        float need = NeedFor(origin, eye.forward, m_Reach);
        Vector3 flatForward = Vector3.ProjectOnPlane(eye.forward, Vector3.up);
        if (flatForward.sqrMagnitude > 1e-4f)
            need = Mathf.Max(need, NeedFor(origin - Vector3.up * m_ChestDrop, flatForward.normalized, m_Reach));

        need = Mathf.Max(need, HeldItemsNeed(origin));
        return Mathf.Clamp(need, 0f, m_MaxPullBack);
    }

    // How far something reaching "reach" meters along direction must move back to clear a wall.
    // Inside the soft zone it eases in on a curve, then matches the exact need once the wall is close.
    float NeedFor(Vector3 origin, Vector3 direction, float reach)
    {
        if (!Physics.SphereCast(origin, m_ProbeRadius, direction, out RaycastHit hit, reach + m_SoftZone, m_WallLayers, QueryTriggerInteraction.Ignore))
            return 0f;

        float hard = reach - (hit.distance + m_ProbeRadius);
        if (m_SoftZone <= 0f)
            return Mathf.Max(0f, hard);

        float x = hard + m_SoftZone;
        if (x <= 0f)
            return 0f;
        return x < 2f * m_SoftZone ? x * x / (4f * m_SoftZone) : hard;
    }

    float HeldItemsNeed(Vector3 eyePosition)
    {
        // Undo the current pull so items are measured where they sit at rest.
        Transform parent = transform.parent;
        Vector3 currentOffset = transform.position - (parent != null ? parent.TransformPoint(m_RestLocalPosition) : m_RestLocalPosition);
        Vector3 rigForward = parent != null ? parent.forward : transform.forward;

        float need = 0f;
        if (m_HeldItems != null && m_HeldItems.Length > 0)
        {
            foreach (Renderer item in m_HeldItems)
                need = Mathf.Max(need, HeldItemNeed(item, eyePosition, currentOffset, rigForward));
        }
        else
        {
            foreach (MeshRenderer item in m_FoundItems)
                need = Mathf.Max(need, HeldItemNeed(item, eyePosition, currentOffset, rigForward));
        }

        return need;
    }

    float HeldItemNeed(Renderer item, Vector3 eyePosition, Vector3 currentOffset, Vector3 rigForward)
    {
        if (item == null || !item.enabled || !item.gameObject.activeInHierarchy)
            return 0f;

        Bounds bounds = item.bounds;
        Vector3 toCenter = bounds.center - currentOffset - eyePosition;
        float centerDistance = toCenter.magnitude;
        if (centerDistance < 1e-3f)
            return 0f;

        // Aim at the item and reach to its far edge.
        Vector3 direction = toCenter / centerDistance;
        Vector3 extents = bounds.extents;
        float halfLength = Mathf.Abs(extents.x * direction.x) + Mathf.Abs(extents.y * direction.y) + Mathf.Abs(extents.z * direction.z);
        float need = NeedFor(eyePosition, direction, centerDistance + halfLength + m_HeldItemPadding);

        // The arms move straight back, not along the probe, so an item off to the side needs more travel.
        return need / Mathf.Max(Vector3.Dot(direction, rigForward), 0.7f);
    }
}
