using UnityEngine;

// Return-to-rest: approach swing, glide-path descent and eased landing onto each agent's captured resting pose.
public partial class BoidsSwarm
{
    // -------------------------------------------------------------------------
    //  Return-to-rest update
    // -------------------------------------------------------------------------
    //
    //  Three phases per agent:
    //
    //  (A) Approach  (until the landing gate is reached):
    //      Steer toward a waypoint offset to the LEFT or RIGHT of the landing
    //      GATE (landingApproachHeight above, and landingApproachDistance behind,
    //      the resting point).  The side is whichever lies nearer the agent's
    //      current facing (the gentler turn); the offset magnitude scales with
    //      how opposed the resting facing is to the current facing (aligned =
    //      straight in, opposed = wide swing) and shrinks to zero as the agent
    //      nears the gate, so the path curls tightly into it.  Speed is capped by
    //      the distance to the gate and reaches exactly descentSpeed at the
    //      descent entry radius, so there is no surplus momentum to overshoot.
    //
    //  (D) Descent  (within descentEntryRadius of the gate, or of the upper
    //      part of the glide path while flying along it; always from above the
    //      landing height):
    //      Follow the glide path from the gate down to the resting point —
    //      forward-and-down along the resting facing — by chasing a point a
    //      short look-ahead along it, so drift is pulled back onto the line
    //      rather than cut across.  Speed eases from descentSpeed to
    //      descentMinSpeed, never steps past the resting point, and never
    //      drops below the landing height (the surface it rests on).  The body
    //      faces its horizontal direction of travel (never backwards), settling
    //      into the resting facing as it nears the end.
    //
    //  (B) Landing  (descending and distance <= arriveRadius):
    //      A single eased interpolation from the pose at entry to the exact
    //      resting pose.  The rate is chosen so the initial speed matches the
    //      agent's entry velocity for a jerk-free
    //      hand-off, and a cubic ease-out keeps that speed near the start while
    //      smoothly decaying to zero at the resting point.
    // -------------------------------------------------------------------------

    private void UpdateReturning(float dt)
    {
        bool allRested = true;

        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] == null) continue;
            if (_rested[i]) continue;

            allRested = false;

            Vector3    pos      = agents[i].position;
            Quaternion rot      = agents[i].rotation;
            Vector3    restPos  = _restPositions[i];
            Quaternion restRot  = _restRotations[i];

            Vector3 toRest   = restPos - pos;
            float   distRest = toRest.magnitude;

            // ---------------------------------------------------------------
            //  (B) Landing phase — only entered from the descent, so the
            //      final approach is always from above.
            // ---------------------------------------------------------------
            if (_land[i].active || (_descending[i] && distRest <= arriveRadius))
            {
                if (!_land[i].active)
                {
                    // Enter landing: capture start pose and pick a rate so the
                    // opening speed equals the current speed for a jerk-free
                    // hand-off.  With a cubic ease-out e = 1-(1-u)^3, e'(0) = 3,
                    // so the position speed at u=0 is  dist * 3 * rate.
                    float entrySpeed = Mathf.Max(_velocities[i].magnitude, maxSpeed * 0.05f);
                    float entryDist  = Mathf.Max(distRest, 0.0001f);

                    _land[i].active   = true;
                    _land[i].u        = 0f;
                    _land[i].startPos = pos;
                    _land[i].startRot = rot;
                    _land[i].rate     = entrySpeed / (entryDist * 3f);
                }

                _land[i].u = Mathf.Clamp01(_land[i].u + _land[i].rate * dt);

                float u    = _land[i].u;
                float inv  = 1f - u;
                float ease = 1f - inv * inv * inv; // cubic ease-out

                Vector3    newPos = Vector3.Lerp(_land[i].startPos, restPos, ease);
                Quaternion newRot = Quaternion.Slerp(_land[i].startRot, restRot, ease);

                // Keep the cached velocity consistent with realised motion.
                _velocities[i] = dt > 0f ? (newPos - pos) / dt : Vector3.zero;

                agents[i].position = newPos;
                agents[i].rotation = newRot;

                if (u >= 1f)
                {
                    agents[i].position = restPos;
                    agents[i].rotation = restRot;
                    _velocities[i]     = Vector3.zero;
                    _banks[i]          = 0f;
                    _rested[i]         = true;
                    _land[i].active    = false;
                    _descending[i]     = false;
                }
                continue;
            }

            float boostedSteer = EffectiveMaxSteer * Mathf.Max(1f, restTurnBoost);

            // Glide path from the landing gate down to the resting point.
            Vector3 gatePos  = LandingGate(i);
            Vector3 glide    = restPos - gatePos;
            float   glideLen = glide.magnitude;
            Vector3 glideDir = glideLen > 0.0001f ? glide / glideLen : Vector3.down;
            float   along    = Mathf.Clamp(Vector3.Dot(pos - gatePos, glideDir), 0f, glideLen);

            // ---------------------------------------------------------------
            //  (D) Descent phase — glide forward-and-down onto the resting point
            // ---------------------------------------------------------------
            if (!_descending[i])
            {
                float entrySqr = descentEntryRadius * descentEntryRadius;

                // At the gate (from any direction — the descent turns them), or
                // already on the UPPER part of the glide path and moving along
                // it.  The bottom descentEntryRadius of the path is excluded so
                // agents can never join beside or below the resting point, and
                // never from the far side of it.
                bool atGate = (pos - gatePos).sqrMagnitude <= entrySqr;
                bool onPath = along <= glideLen - descentEntryRadius &&
                              (pos - (gatePos + glideDir * along)).sqrMagnitude <= entrySqr &&
                              Vector3.Dot(_velocities[i], glideDir) > 0f;

                // Either way, only ever start the descent from above the landing
                // height, so it can't begin underneath the landing surface.
                if ((atGate || onPath) && pos.y >= restPos.y)
                    _descending[i] = true;
            }

            if (_descending[i])
            {
                // Ease from descentSpeed at the gate to descentMinSpeed at the
                // resting point, and never step past it in one frame.
                float progress   = Mathf.Clamp01(distRest / Mathf.Max(0.0001f, glideLen));
                float descentCap = Mathf.Lerp(descentMinSpeed, descentSpeed, progress);
                descentCap = Mathf.Min(descentCap, distRest / dt);

                // Chase a point a short look-ahead along the glide path so any
                // sideways drift is pulled back onto the line.
                Vector3 aimPoint = gatePos + glideDir * Mathf.Min(glideLen, along + descentEntryRadius);
                Vector3 toAim    = aimPoint - pos;
                if (toAim.sqrMagnitude < 0.0001f) toAim = toRest;

                Vector3 descentVel = toAim.sqrMagnitude > 0.0001f ? toAim.normalized * descentCap : Vector3.zero;
                Vector3 descentSteer = descentVel - _velocities[i];
                if (descentSteer.magnitude > boostedSteer)
                    descentSteer = descentSteer.normalized * boostedSteer;

                _velocities[i] += descentSteer * dt;
                if (_velocities[i].magnitude > descentCap)
                    _velocities[i] = _velocities[i].normalized * descentCap;

                // Never sink below the landing height: the resting point sits on
                // the landing surface, so anything lower is inside it.  Stop the
                // downward motion there and let the steering slide it in.
                Vector3 nextPos = pos + _velocities[i] * dt;
                if (nextPos.y < restPos.y)
                {
                    nextPos.y = restPos.y;
                    if (_velocities[i].y < 0f) _velocities[i].y = 0f;
                }
                agents[i].position = nextPos;

                // Face the horizontal direction of travel (level — facing a
                // downward velocity would point the nose at the ground) so the
                // agent never flies backwards, settling into the resting facing
                // as it nears the end.  With no horizontal motion, face the rest.
                Vector3 flatVel = new Vector3(_velocities[i].x, 0f, _velocities[i].z);
                Quaternion travelRot = flatVel.sqrMagnitude > 0.01f
                    ? Quaternion.LookRotation(flatVel, Vector3.up)
                    : restRot;
                float      settle  = 1f - progress;
                Quaternion faceRot = Quaternion.Slerp(travelRot, restRot, settle * settle);

                agents[i].rotation = Quaternion.Slerp(rot, faceRot, turnSpeed * dt);
                _banks[i] = Mathf.Lerp(_banks[i], 0f, bankSmoothing * dt);
                continue;
            }

            // ---------------------------------------------------------------
            //  (A) Approach phase — lateral swing waypoint around the landing gate
            // ---------------------------------------------------------------
            Vector3 toGate      = gatePos - pos;
            float   distGate    = toGate.magnitude;

            Vector3 restFwd    = restRot * Vector3.forward;
            Vector3 currentFwd = _velocities[i].sqrMagnitude > 0.0001f
                ? _velocities[i].normalized
                : rot * Vector3.forward;

            // How opposed is the landing facing to the current facing?
            //  0 when aligned (fly straight in), 1 when head-on opposed (full swing).
            float opposition = (1f - Vector3.Dot(currentFwd, restFwd)) * 0.5f;

            // Lateral axis perpendicular to the resting facing (horizontal),
            // with a fallback when the resting facing is near-vertical.
            Vector3 lateral = Vector3.Cross(Vector3.up, restFwd);
            if (lateral.sqrMagnitude < 0.0001f)
                lateral = Vector3.Cross(currentFwd, restFwd);
            lateral = lateral.sqrMagnitude > 0.0001f ? lateral.normalized : Vector3.right;

            // Offset shrinks with distance so the swing tightens into the gate.
            float distFactor = Mathf.Clamp01(distGate / Mathf.Max(0.0001f, returnSlowRadius));
            float offsetDist = swingWidth * Mathf.Clamp01(opposition) * distFactor;

            // Choose the side (left/right) whose waypoint lies nearer the current
            // heading — the gentler turn.
            Vector3 rightPoint = gatePos + lateral * offsetDist;
            Vector3 leftPoint  = gatePos - lateral * offsetDist;
            float   dRight     = Vector3.Dot((rightPoint - pos).normalized, currentFwd);
            float   dLeft      = Vector3.Dot((leftPoint  - pos).normalized, currentFwd);
            Vector3 approachPoint = dRight >= dLeft ? rightPoint : leftPoint;

            Vector3 seek = approachPoint - pos;
            if (seek.sqrMagnitude < 0.0001f) seek = toGate;
            if (seek.sqrMagnitude < 0.0001f) seek = toRest;

            // Arrival cap: desired speed scales down with distance to the GATE
            // (not the offset waypoint), from maxSpeed at returnSlowRadius
            // outside the descent entry radius to exactly descentSpeed on it, so
            // the agent hands over to the descent without surplus momentum.
            //
            // restTurnBoost multiplies the steering budget so the velocity vector
            // can rotate faster, tightening the approach arc (turn radius
            // r ~ v^2 / steer) enough to spiral into descentEntryRadius rather
            // than orbiting outside it.
            float   slowFactor = Mathf.Clamp01((distGate - descentEntryRadius) / Mathf.Max(0.0001f, returnSlowRadius));
            float   arriveCap  = Mathf.Lerp(Mathf.Min(descentSpeed, maxSpeed), maxSpeed, slowFactor);
            Vector3 desiredVel = seek.normalized * arriveCap;
            Vector3 steer        = desiredVel - _velocities[i];
            if (steer.magnitude > boostedSteer)
                steer = steer.normalized * boostedSteer;

            _velocities[i] += steer * dt;
            if (_velocities[i].magnitude > arriveCap)
                _velocities[i] = _velocities[i].normalized * arriveCap;

            agents[i].position += _velocities[i] * dt;

            // Orient to travel with banking (the landing phase handles the final
            // rotation onto the resting pose).  Facing turn rate is boosted to
            // keep the mesh glued to the tightened arc.
            if (_velocities[i].sqrMagnitude > 0.0001f)
            {
                Quaternion flyRot = ComputeOrientation(i, _velocities[i], dt);
                agents[i].rotation = Quaternion.Slerp(
                    rot, flyRot, turnSpeed * Mathf.Max(1f, restTurnBoost) * dt);
            }
        }

        if (allRested)
            _state = SwarmState.Resting;
    }

    private Vector3 LandingGate(int i)
    {
        return LandingGate(_restPositions[i], _restRotations[i]);
    }

    /// <summary>
    /// The point the descent starts from: <see cref="landingApproachHeight"/>
    /// above, and <see cref="landingApproachDistance"/> behind (opposite the
    /// horizontal resting facing), the resting point.
    /// </summary>
    private Vector3 LandingGate(Vector3 restPos, Quaternion restRot)
    {
        Vector3 back = restRot * Vector3.back;
        back.y = 0f;
        back = back.sqrMagnitude > 0.0001f ? back.normalized : Vector3.zero;
        return restPos + back * landingApproachDistance + Vector3.up * landingApproachHeight;
    }
}
