using UnityEngine;

// Flocking: neighbour rules, target seek/orbit, integration and banked orientation while following the target.
public partial class BoidsSwarm
{
    // -------------------------------------------------------------------------
    //  Following update
    // -------------------------------------------------------------------------

    private void UpdateFollowing(float dt)
    {
        // 1. Snapshot positions so every agent steers from the same frame state
        //    (independent of array order), and the neighbour scan reads a plain
        //    array instead of Transform properties.
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null)
                _positions[i] = agents[i].position;
        }

        // 2. Compute every agent's steering before any agent moves.
        for (int i = 0; i < agents.Length; i++)
            _steering[i] = ComputeSteering(i);

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] == null) continue;

            // 3. Nudge velocity and clamp to max speed
            _velocities[i] += _steering[i] * dt;
            if (_velocities[i].sqrMagnitude > maxSpeed * maxSpeed)
                _velocities[i] = _velocities[i].normalized * maxSpeed;

            // 4. Integrate position
            _positions[i] += _velocities[i] * dt;
            agents[i].position = _positions[i];

            // 5. Orient: +Z forward, banked soft-up
            if (_velocities[i].sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = ComputeOrientation(i, _velocities[i], dt);
                agents[i].rotation = Quaternion.Slerp(
                    agents[i].rotation, targetRot, turnSpeed * dt);
            }
            // Near-zero velocity: keep existing rotation (no LookRotation on zero vector)
        }
    }

    // -------------------------------------------------------------------------
    //  Steering
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the combined steering vector for agent <paramref name="i"/>
    /// by summing separation, alignment, cohesion, and target-seek/orbit forces.
    /// </summary>
    private Vector3 ComputeSteering(int i)
    {
        if (agents[i] == null) return Vector3.zero;

        Vector3 pos = _positions[i];

        // Accumulators for neighbour rules
        Vector3 separationSum = Vector3.zero;
        Vector3 alignmentSum  = Vector3.zero;
        Vector3 cohesionSum   = Vector3.zero;
        int     neighborCount = 0;
        int     sepCount      = 0;

        // Compare squared distances so the scan needs no square roots.
        float sepRadiusSqr      = separationRadius * separationRadius;
        float neighborRadiusSqr = neighborRadius * neighborRadius;

        for (int j = 0; j < agents.Length; j++)
        {
            if (j == i || agents[j] == null) continue;

            Vector3 toOther = pos - _positions[j];
            float   distSqr = toOther.sqrMagnitude;

            // Separation
            if (distSqr < sepRadiusSqr && distSqr > 0.00000001f)
            {
                // Unit direction weighted by inverse distance:
                // (toOther / dist) / dist = toOther / dist².
                separationSum += toOther / distSqr;
                sepCount++;
            }

            // Alignment + cohesion share the same neighbour radius
            if (distSqr < neighborRadiusSqr)
            {
                alignmentSum += _velocities[j];
                cohesionSum  += _positions[j];
                neighborCount++;
            }
        }

        // --- Separation ---
        Vector3 sep = Vector3.zero;
        if (sepCount > 0)
        {
            sep = separationSum / sepCount;
            sep = SteerTowards(sep, _velocities[i]) * separationWeight;
        }

        // --- Alignment ---
        Vector3 ali = Vector3.zero;
        if (neighborCount > 0)
        {
            Vector3 avgVelocity = alignmentSum / neighborCount;
            ali = SteerTowards(avgVelocity, _velocities[i]) * alignmentWeight;
        }

        // --- Cohesion ---
        Vector3 coh = Vector3.zero;
        if (neighborCount > 0)
        {
            Vector3 avgPos = cohesionSum / neighborCount;
            coh = SteerTowards(avgPos - pos, _velocities[i]) * cohesionWeight;
        }

        // --- Target seek + orbit ---
        Vector3 tgt = Vector3.zero;
        if (target != null)
        {
            Vector3 toTarget = target.position - pos;
            float   distToTarget = toTarget.magnitude;

            // Inside orbit radius: steer tangentially to maintain orbit
            // Outside: steer directly toward target
            Vector3 desired;
            if (distToTarget > orbitRadius)
            {
                // Steer toward the orbit shell surface
                desired = toTarget.normalized * (distToTarget - orbitRadius);
            }
            else if (distToTarget > 0.0001f)
            {
                // Already inside orbit: gently push away to maintain spacing
                desired = -toTarget.normalized * (orbitRadius - distToTarget);
            }
            else
            {
                desired = Vector3.zero;
            }

            tgt = SteerTowards(desired, _velocities[i]) * targetWeight;
        }

        return sep + ali + coh + tgt;
    }

    /// <summary>
    /// Converts a desired direction vector into a clamped steering force
    /// (the difference between the desired velocity and the current velocity,
    /// clamped to <see cref="EffectiveMaxSteer"/>).
    /// </summary>
    private Vector3 SteerTowards(Vector3 desired, Vector3 currentVelocity)
    {
        if (desired.sqrMagnitude < 0.0001f) return Vector3.zero;

        Vector3 desiredVel = desired.normalized * maxSpeed;
        Vector3 steer      = desiredVel - currentVelocity;

        float steerLimit = EffectiveMaxSteer;
        if (steer.magnitude > steerLimit)
            steer = steer.normalized * steerLimit;

        return steer;
    }

    // -------------------------------------------------------------------------
    //  Orientation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes the target rotation for agent <paramref name="i"/>.
    /// Forward = +Z along <paramref name="velocity"/>.
    /// Up = world-up rotated by the eased bank angle around forward,
    ///      then blended toward strict world-up by <see cref="worldUpBias"/>.
    /// </summary>
    private Quaternion ComputeOrientation(int i, Vector3 velocity, float dt)
    {
        Vector3 forward = velocity.normalized;

        // --- Measure horizontal turn rate for banking ---
        // Project current and previous forward onto the horizontal plane, compare.
        Quaternion currentRot = agents[i].rotation;
        Vector3    prevForward = currentRot * Vector3.forward; // +Z in world space last frame

        // Horizontal (world-XZ) component of the heading change
        Vector3 prevH = new Vector3(prevForward.x, 0f, prevForward.z);
        Vector3 currH = new Vector3(forward.x,     0f, forward.z);

        float turnRate = 0f;
        if (prevH.sqrMagnitude > 0.0001f && currH.sqrMagnitude > 0.0001f && dt > 0f)
        {
            // Signed angle of horizontal heading change per second
            turnRate = Vector3.SignedAngle(
                prevH.normalized, currH.normalized, Vector3.up) / dt;
        }

        // Ease bank toward target
        float targetBank = Mathf.Clamp(
            -turnRate * bankStrength, -maxBankAngle, maxBankAngle);
        _banks[i] = Mathf.Lerp(_banks[i], targetBank, bankSmoothing * dt);

        // --- Build banked up-vector ---
        // Rotate world-up around the agent's forward by the bank angle
        Quaternion bankRot = Quaternion.AngleAxis(_banks[i], forward);
        Vector3    bankedUp = bankRot * Vector3.up;

        // Blend toward strict world-up (worldUpBias = 1 → always world-up)
        Vector3 finalUp = Vector3.Lerp(bankedUp, Vector3.up, worldUpBias);
        if (finalUp.sqrMagnitude < 0.0001f)
            finalUp = Vector3.up;

        return Quaternion.LookRotation(forward, finalUp);
    }
}
