using System.Collections.Generic;
using Unity.BuildingBlocks.Gameplay;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// Plays footsteps for the first person player, which has no animated feet to drive them.
/// A step plays each time the player has travelled StrideLength along the ground, so steps follow
/// actual movement (and stop when walking into a wall). The surface is found with a short raycast
/// down from the player's feet when each step plays.
/// Place on a child of the player at foot height with one or more AudioSources; steps alternate
/// between them so the tail of one step isn't cut off by the next.
/// </summary>
public class FootstepAudio : MonoBehaviour
{
    /// <summary>Used when the ground has no FootstepSurface, i.e. everything that isn't tagged.</summary>
    public FootstepSurfaceType DefaultSurfaceType;

    /// <summary>Distance travelled (metres) between steps at a StrideMultiplier of 1.</summary>
    public float StrideLength = 0.7f;
    /// <summary>Below this horizontal speed (m/s) the player counts as standing still.</summary>
    public float MinSpeed = 0.5f;
    /// <summary>Time in the air (seconds) before touching down plays a landing sound.</summary>
    public float MinAirTimeForLand = 0.3f;
    /// <summary>How far below the feet to look for the surface.</summary>
    public float GroundCheckDistance = 0.5f;

    public float FootstepMasterVolume = 0.8f;
    /// <summary>Logs the collider and surface under each step, to find colliders that need a FootstepSurface.</summary>
    public bool LogSurfaces;

    private KinematicMover m_Mover;
    private AudioSource[] m_AudioSources;
    private int m_NextAudioSource;
    private readonly Dictionary<Collider, FootstepSurfaceType> m_SurfaceCache = new Dictionary<Collider, FootstepSurfaceType>();

    private Vector3 m_LastPosition;
    private float m_DistanceSinceStep;
    private float m_AirTime;
    private bool m_WasGrounded;
    private float m_CurrentStrideMultiplier = 1f;

    void Awake()
    {
        m_Mover = GetComponentInParent<KinematicMover>();
        m_AudioSources = GetComponents<AudioSource>();
    }

    void Start()
    {
        if (m_Mover != null)
        {
            MoveState state = m_Mover.GetState();
            m_LastPosition = state.Position;
            m_WasGrounded = state.IsGrounded;
        }
        // First step comes half a stride after starting to walk
        m_DistanceSinceStep = StrideLength * 0.5f;
    }

    void Update()
    {
        if (m_Mover == null || m_AudioSources.Length == 0) return;

        MoveState state = m_Mover.GetState();
        Vector3 moved = state.Position - m_LastPosition;
        m_LastPosition = state.Position;

        // Ignore teleports and respawns
        if (moved.sqrMagnitude > 4f) moved = Vector3.zero;

        if (state.IsGrounded)
        {
            if (!m_WasGrounded && m_AirTime >= MinAirTimeForLand)
            {
                FootstepSurfaceType surface = FindSurface(state.Position);
                if (surface != null) Play(surface.LandAudioRandomContainer, surface.Volume);
                m_DistanceSinceStep = 0f;
            }
            m_AirTime = 0f;

            Vector3 horizontalVelocity = new Vector3(state.Velocity.x, 0f, state.Velocity.z);
            if (horizontalVelocity.magnitude >= MinSpeed)
            {
                // 3D distance, so climbing stairs counts the rise as well
                m_DistanceSinceStep += moved.magnitude;
                if (m_DistanceSinceStep >= StrideLength * m_CurrentStrideMultiplier)
                {
                    m_DistanceSinceStep = 0f;
                    FootstepSurfaceType surface = FindSurface(state.Position);
                    if (surface != null)
                    {
                        m_CurrentStrideMultiplier = surface.StrideMultiplier;
                        Play(surface.StepAudioRandomContainer, surface.Volume);
                    }
                }
            }
            else
            {
                m_DistanceSinceStep = StrideLength * m_CurrentStrideMultiplier * 0.5f;
            }
        }
        else
        {
            m_AirTime += Time.deltaTime;
        }

        m_WasGrounded = state.IsGrounded;
    }

    private FootstepSurfaceType FindSurface(Vector3 feetPosition)
    {
        Vector3 origin = feetPosition + Vector3.up * 0.5f;
        float distance = 0.5f + GroundCheckDistance;
        if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, distance,
                m_Mover.CollisionMask, QueryTriggerInteraction.Ignore))
        {
            if (LogSurfaces)
            {
                Debug.DrawRay(origin, Vector3.down * distance, Color.red, 2f);
                Debug.Log("Footstep: no ground hit below " + feetPosition + " (using default surface)", this);
            }
            return DefaultSurfaceType;
        }

        if (LogSurfaces)
        {
            Debug.DrawLine(origin, hit.point, Color.green, 2f);
        }

        if (!m_SurfaceCache.TryGetValue(hit.collider, out FootstepSurfaceType surfaceType))
        {
            FootstepSurface surface = hit.collider.GetComponentInParent<FootstepSurface>();
            surfaceType = surface != null && surface.SurfaceType != null ? surface.SurfaceType : DefaultSurfaceType;
            m_SurfaceCache[hit.collider] = surfaceType;
        }

        if (LogSurfaces)
        {
            Debug.Log("Footstep on " + hit.collider.name + " (" + (surfaceType != null ? surfaceType.name : "none") + ")", hit.collider);
        }
        return surfaceType;
    }

    private void Play(AudioResource audioRandomContainer, float volume)
    {
        if (audioRandomContainer == null) return;

        AudioSource audioSource = m_AudioSources[m_NextAudioSource];
        m_NextAudioSource = (m_NextAudioSource + 1) % m_AudioSources.Length;

        audioSource.resource = audioRandomContainer;
        audioSource.volume = volume * FootstepMasterVolume;
        audioSource.Play();
    }
}
