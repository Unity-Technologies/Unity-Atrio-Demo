using UnityEngine;

// Animation: drives each agent's Animator (Flying, Flapping, idle-loop phase and idle flourishes).
public partial class BoidsSwarm
{
    // -------------------------------------------------------------------------
    //  Animation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Caches each agent's Animator (on the agent or its children) and the
    /// parameter hashes.  Called in Awake; call again if agents[] changes at
    /// runtime.
    /// </summary>
    public void CacheAnimators()
    {
        SyncCaches();
        _flyingHash   = Animator.StringToHash(flyingParameter);
        _flappingHash = Animator.StringToHash(flappingParameter);
        _aboutToLandHash   = Animator.StringToHash(aboutToLandParameter);
        _idleIndexHash     = Animator.StringToHash(idleIndexParameter);
        _idleLoopStateHash = Animator.StringToHash(idleLoopState);

        for (int i = 0; i < _anim.Length; i++)
        {
            _anim[i].animator        = agents[i] != null ? agents[i].GetComponentInChildren<Animator>(true) : null;
            _anim[i].paramsChecked   = false;
            _anim[i].flapTimer       = 0f;
            _anim[i].idleTimer       = RandomFlourishInterval(); // staggered so agents don't flourish in sync
            _anim[i].flourishPending = false;
            _anim[i].idlePhaseSet    = false;
            _anim[i].takeOffEffect   = FindTakeOffEffect(agents[i]);
        }
    }

    /// <summary>Finds the child ParticleSystem named <see cref="takeOffEffectName"/> on an agent and stops it so it only plays on take-off.</summary>
    private ParticleSystem FindTakeOffEffect(Transform agent)
    {
        if (agent == null || string.IsNullOrEmpty(takeOffEffectName)) return null;

        foreach (ParticleSystem ps in agent.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps.name != takeOffEffectName) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }
        return null;
    }

    /// <summary>True when agent <paramref name="i"/> is sitting on its resting pose.</summary>
    private bool IsLanded(int i)
    {
        return _state == SwarmState.Resting ||
               (_state == SwarmState.Returning && _rested[i]);
    }

    /// <summary>True while agent <paramref name="i"/> is coming in to land.</summary>
    private bool IsNearLanding(int i)
    {
        if (_state != SwarmState.Returning || _rested[i]) return false;
        if (_descending[i] || _land[i].active) return true;
        return (_restPositions[i] - agents[i].position).sqrMagnitude <= landingFlapDistance * landingFlapDistance;
    }

    /// <summary>
    /// True when agent <paramref name="i"/> will touch down within
    /// <paramref name="leadTime"/> seconds.
    /// </summary>
    private bool IsAboutToLand(int i, float leadTime)
    {
        if (leadTime <= 0f) return false;
        if (_state != SwarmState.Returning || _rested[i]) return false;
        return EstimatedTimeToLand(i) <= leadTime;
    }

    /// <summary>
    /// Seconds until agent <paramref name="i"/> reaches its resting pose, or
    /// infinity before the descent has begun.  Exact during the landing phase;
    /// during the descent it estimates the time to the arrive radius at the
    /// current speed plus the eased landing that follows.
    /// </summary>
    private float EstimatedTimeToLand(int i)
    {
        if (_land[i].active)
            return _land[i].rate > 0f ? (1f - _land[i].u) / _land[i].rate : 0f;

        if (!_descending[i]) return float.PositiveInfinity;

        float speed = _velocities[i].magnitude;
        if (speed < 0.0001f) return float.PositiveInfinity;

        // The landing phase lasts 1 / rate = 3 * entryDist / entrySpeed (see UpdateReturning).
        float distRest   = (_restPositions[i] - agents[i].position).magnitude;
        float toArrive   = Mathf.Max(0f, distRest - arriveRadius) / speed;
        float entrySpeed = Mathf.Max(speed, maxSpeed * 0.05f);
        float landing    = 3f * Mathf.Min(distRest, arriveRadius) / entrySpeed;
        return toArrive + landing;
    }

    /// <summary>True while agent <paramref name="i"/> is climbing at or above <see cref="steepClimbAngle"/>.</summary>
    private bool IsClimbingSteeply(int i)
    {
        Vector3 v     = _velocities[i];
        float   speed = v.magnitude;
        if (speed < Mathf.Max(0.0001f, steepClimbMinSpeed)) return false;
        return v.y / speed >= Mathf.Sin(steepClimbAngle * Mathf.Deg2Rad);
    }

    /// <summary>
    /// Ticks the timed flapping and pushes Flying / Flapping to each Animator:
    ///   • landed            → neither
    ///   • flying            → Flying
    ///   • take-off / steep climb (timed) or coming in to land → also Flapping
    /// </summary>
    private void UpdateAnimators(float dt)
    {
        for (int i = 0; i < _anim.Length; i++)
        {
            if (agents[i] == null) continue;

            bool landed = IsLanded(i);

            if (landed)
            {
                _anim[i].flapTimer = 0f;
            }
            else
            {
                _anim[i].flapTimer = Mathf.Max(0f, _anim[i].flapTimer - dt);
                if (IsClimbingSteeply(i))
                    _anim[i].flapTimer = Mathf.Max(_anim[i].flapTimer, steepClimbFlapDuration);
            }

            Animator animator = _anim[i].animator;
            if (animator == null || !animator.isActiveAndEnabled) continue;

            if (!_anim[i].paramsChecked)
            {
                _anim[i].hasFlying     = HasParameter(animator, _flyingHash,    AnimatorControllerParameterType.Bool);
                _anim[i].hasFlapping   = HasParameter(animator, _flappingHash,  AnimatorControllerParameterType.Bool);
                _anim[i].hasAboutToLand = HasParameter(animator, _aboutToLandHash, AnimatorControllerParameterType.Bool);
                _anim[i].hasIdleIndex  = HasParameter(animator, _idleIndexHash, AnimatorControllerParameterType.Int);
                _anim[i].paramsChecked = true;
            }

            // Flying clears ahead of touch-down so the blend to idle lands with the agent.
            bool flying   = !landed && !IsAboutToLand(i, landingFlyingStopTime);
            bool flapping = flying && !IsAboutToLand(i, landingFlapStopTime) &&
                            (_anim[i].flapTimer > 0f || IsNearLanding(i));

            if (_anim[i].hasFlying)   animator.SetBool(_flyingHash, flying);
            if (_anim[i].hasFlapping) animator.SetBool(_flappingHash, flapping);
            if (_anim[i].hasAboutToLand) animator.SetBool(_aboutToLandHash, !landed && IsAboutToLand(i, landingTriggerLeadTime));

            if (!_anim[i].idlePhaseSet) RandomizeIdlePhase(i, animator, landed);
            if (_anim[i].hasIdleIndex) UpdateIdleFlourish(i, animator, landed, dt);
        }
    }

    /// <summary>
    /// While agent <paramref name="i"/> is landed, counts down idle-loop time and
    /// then sets IdleIndex to a random flourish.  IdleIndex is reset to 0 as soon
    /// as the Animator leaves the idle loop, so the flourish plays once and
    /// returns.  The countdown only runs while the Animator is settled in the idle
    /// loop, so a flourish is never requested while another is playing.
    /// </summary>
    private void UpdateIdleFlourish(int i, Animator animator, bool landed, float dt)
    {
        if (!landed || idleFlourishCount <= 0)
        {
            if (_anim[i].flourishPending)
                animator.SetInteger(_idleIndexHash, 0);
            _anim[i].flourishPending = false;
            if (!landed) _anim[i].idleTimer = RandomFlourishInterval(); // fresh wait after the next landing
            return;
        }

        bool inIdleLoop = !animator.IsInTransition(0) &&
                          animator.GetCurrentAnimatorStateInfo(0).shortNameHash == _idleLoopStateHash;

        if (_anim[i].flourishPending)
        {
            // Started (left the idle loop) or timed out: clear the request.
            _anim[i].pendingTime += dt;
            if (!inIdleLoop || _anim[i].pendingTime >= FlourishPendingTimeout)
            {
                animator.SetInteger(_idleIndexHash, 0);
                _anim[i].flourishPending = false;
                _anim[i].idleTimer       = RandomFlourishInterval();
            }
            return;
        }

        if (!inIdleLoop) return; // flourish playing, or still settling after landing

        _anim[i].idleTimer -= dt;
        if (_anim[i].idleTimer > 0f) return;

        animator.SetInteger(_idleIndexHash, Random.Range(1, idleFlourishCount + 1));
        _anim[i].flourishPending = true;
        _anim[i].pendingTime     = 0f;
    }

    /// <summary>
    /// The first time agent <paramref name="i"/>'s Animator is settled in the idle
    /// loop, restarts it at a random normalised time so the agents' idle loops
    /// don't play in sync.  Skipped if the agent takes off first.
    /// </summary>
    private void RandomizeIdlePhase(int i, Animator animator, bool landed)
    {
        if (!landed)
        {
            _anim[i].idlePhaseSet = true;
            return;
        }

        if (animator.IsInTransition(0)) return;

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (state.shortNameHash != _idleLoopStateHash) return;

        animator.Play(state.fullPathHash, 0, Random.value);
        _anim[i].idlePhaseSet = true;
    }

    private float RandomFlourishInterval()
    {
        return Random.Range(idleFlourishIntervalMin, idleFlourishIntervalMax);
    }

    private static bool HasParameter(Animator animator, int hash, AnimatorControllerParameterType type)
    {
        if (animator.runtimeAnimatorController == null) return false;

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int k = 0; k < parameters.Length; k++)
        {
            if (parameters[k].nameHash == hash && parameters[k].type == type)
                return true;
        }
        return false;
    }
}
