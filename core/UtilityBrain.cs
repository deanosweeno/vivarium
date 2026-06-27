using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// The decision layer of Pillar-1 Behavioral AI. Each creature owns a brain (composed like
/// <see cref="Creature.Movement"/>). On a fixed interval it scores the config's
/// <see cref="BehaviorAction"/>s against the creature's <see cref="Drives"/> and a
/// <see cref="SenseContext"/>, picks one with anti-dithering stickiness, and every tick
/// translates the chosen action into <see cref="Creature.DesiredVelocity"/> via
/// <see cref="Steering"/>. Deterministic given a seeded RNG; pure of Godot.
/// </summary>
public sealed class UtilityBrain
{
    private readonly BehaviorConfig _config;

    private double _decisionTimer;
    private float _commitment;           // remaining commitment bonus on the current action
    private Vector3 _wanderDir;
    private double _wanderTimer;
    private Vector3 _frolicDir;
    private double _frolicTimer;

    /// <summary>The action currently being executed (null until the first decision).</summary>
    public BehaviorAction? Current { get; private set; }

    /// <summary>Name of the current action, or "" — handy for a debug overlay and tests.</summary>
    public string CurrentName => Current?.Name ?? "";

    public UtilityBrain(BehaviorConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Advance the brain by <paramref name="delta"/> seconds: re-decide if the interval
    /// elapsed (or no action yet), then recompute the steering vector from the current
    /// action and fresh senses, writing it to <paramref name="self"/>.DesiredVelocity.
    /// </summary>
    public void Tick(double delta, Creature self, in SenseContext senses, Random rng)
    {
        _decisionTimer -= delta;
        if (Current is null || _decisionTimer <= 0)
        {
            Decide(self.Drives, senses, rng, (float)Math.Max(delta, _config.DecisionInterval));
            _decisionTimer = _config.DecisionInterval;
        }

        self.DesiredVelocity = ComputeSteering(self, senses, delta, rng);
    }

    /// <summary>
    /// Select the next action. Highest-scoring action wins unless the current one is held
    /// by stickiness: a challenger must beat current by SwitchMargin + remaining commitment,
    /// EXCEPT an emergency-capable action scoring past its threshold interrupts immediately.
    /// </summary>
    private void Decide(Drives drives, in SenseContext senses, Random rng, float elapsed)
    {
        BehaviorAction? best = null;
        float bestScore = float.NegativeInfinity;
        float currentScore = 0f;

        foreach (var action in _config.Actions)
        {
            float score = action.Score(senses, drives);
            if (_config.DecisionNoise > 0f)
                score += (float)(rng.NextDouble() * _config.DecisionNoise);

            if (ReferenceEquals(action, Current))
                currentScore = score;

            if (score > bestScore)
            {
                bestScore = score;
                best = action;
            }
        }

        if (best is null) return;

        // First decision: just take the best.
        if (Current is null)
        {
            Commit(best);
            return;
        }

        // Decay the current commitment over the elapsed time.
        _commitment = Math.Max(0f, _commitment - _config.CommitmentDecayPerSec * elapsed);

        // Satiation latch: a creature mid-graze stays on Forage until it has eaten down to the
        // satiation threshold, so Flock/Approach can't yank it off the food after a single bite
        // (the oscillation we're fixing). Once Hunger drops to threshold it releases normally.
        float hold = 0f;
        if (Current.Steering == SteeringKind.Forage && senses.Hunger > _config.SatiationThreshold)
            hold = 1f;
        // Frolic latch: play until boredom is fully drained.
        else if (Current.Steering == SteeringKind.Frolic && senses.Boredom > 0f)
            hold = 1f;
        // Rest latch: rest until fatigue is fully recovered.
        else if (Current.Steering == SteeringKind.Rest && senses.Fatigue > 0f)
            hold = 1f;

        bool isEmergency = best.EmergencyCapable && bestScore >= best.EmergencyThreshold;
        bool beatsStickyCurrent = bestScore > currentScore + _config.SwitchMargin + _commitment + hold;

        if (!ReferenceEquals(best, Current) && (isEmergency || beatsStickyCurrent))
            Commit(best);
    }

    private void Commit(BehaviorAction action)
    {
        Current = action;
        _commitment = _config.CommitmentBonus;
    }

    private Vector3 ComputeSteering(Creature self, in SenseContext senses, double delta, Random rng)
    {
        float maxSpeed = self.Traits.MaxSpeed;
        switch (Current?.Steering)
        {
            case SteeringKind.Rest:
                return Steering.Stop();

            case SteeringKind.Approach:
            {
                if (!senses.HasNeighbor)
                    return Wander(delta, maxSpeed, rng);
                // Close to a comfortable side-by-side distance and hold there. A single smooth
                // Standoff ramp (eases to exactly zero at the personal-space edge) avoids the
                // jitter that Arrive-toward-the-body + a separate push produced: their two terms
                // handed off abruptly at the edge, leaving sheep orbiting it. Standoff target is
                // the personal-space radius; the band softens the approach over ~2 body radii.
                float standoff = self.Traits.Radius * _config.PersonalSpaceRadii;
                var settle = Steering.Standoff(self.Position, senses.NeighborPosition, maxSpeed,
                    standoff, self.Traits.Radius * 2f);
                // Idle drift floor: a settled cluster eases to a motionless Standoff equilibrium, so
                // mix in a small wander drift to keep it gently milling instead of freezing in place
                // (same anti-freeze trick the Flock case uses; shared Wander state stays coherent).
                var drift = Wander(delta, maxSpeed, rng) * _config.FlockWanderFloor;
                // Add the multi-neighbor push so a crowd (more than the nearest body) still spreads.
                var blended = settle + senses.SeparationPush * maxSpeed + drift;
                return blended.LengthSquared() > maxSpeed * maxSpeed
                    ? Vector3.Normalize(blended) * maxSpeed
                    : blended;
            }

            case SteeringKind.Flee:
                return senses.HasNeighbor
                    ? Steering.Flee(self.Position, senses.NeighborPosition, maxSpeed)
                    : Steering.Stop();

            case SteeringKind.Forage:
                // Always path toward the nearest known food. When food is within eat range the
                // Simulator handles actual grazing; steering just moves toward it. No Wander
                // fallthrough — a foraging creature targets food, it doesn't drift.
                return Steering.Arrive(self.Position, senses.FoodPosition, maxSpeed,
                    self.Traits.Radius * 2f);

            case SteeringKind.Flock:
            {
                // Not in a flock → wander to look for one (the flock system will group nearby kin).
                if (!senses.HasFlock)
                    return Wander(delta, maxSpeed, rng);

                // Smooth cohesion: Standoff toward the anchor with no fixed offset, ramping
                // speed linearly from 0 (at anchor) to full maxSpeed (at FlockLeaveRadius).
                // Replaces Arrive-based Cohesion which snapped to full speed at the FlockRadius
                // boundary — a member outside the circle now accelerates smoothly back.
                var cohere = Steering.Standoff(self.Position, senses.FlockAnchor,
                    maxSpeed, standoff: 0f, band: _config.FlockLeaveRadius);
                // Separation pushes out of crowders' personal space. Clamp it to half max speed so a
                // dense pack can no longer out-shove cohesion and explode the herd (the old bug).
                var separate = senses.SeparationPush * maxSpeed;
                float sepLen = separate.Length();
                float sepCap = maxSpeed * 0.5f;
                if (sepLen > sepCap)
                    separate *= sepCap / sepLen;
                // Idle drift floor: keeps a settled herd milling instead of freezing where
                // cohere and separate both cancel to zero. Reuses the shared Wander state so
                // the drift is coherent with a later Wander stroll.
                var drift = Wander(delta, maxSpeed, rng) * _config.FlockWanderFloor;
                var blended = cohere + separate + drift;
                return blended.LengthSquared() > maxSpeed * maxSpeed
                    ? Vector3.Normalize(blended) * maxSpeed
                    : blended;
            }

            case SteeringKind.SeekFlock:
            {
                if (senses.HasNearbyFlock)
                    return Steering.Standoff(self.Position, senses.NearestFlockAnchor,
                        maxSpeed, standoff: 0f, band: _config.FlockJoinRadius);
                // No kin flock in range — wander to search for one.
                return Wander(delta, maxSpeed, rng);
            }

            case SteeringKind.Frolic:
            {
                // Single Frolic steering: darty zig-zag + soft anchor tether when in a flock.
                // No flavor branching — one playful movement pattern regardless of neighbors.
                var darty = FrolicWander(delta, maxSpeed, rng);

                if (senses.HasFlock)
                {
                    var anchorPull = Steering.Standoff(self.Position, senses.FlockAnchor,
                        maxSpeed, standoff: 0f, band: _config.FlockLeaveRadius);
                    var blended = anchorPull + darty * 0.5f;
                    return blended.LengthSquared() > maxSpeed * maxSpeed
                        ? Vector3.Normalize(blended) * maxSpeed
                        : blended;
                }

                return darty;
            }

            case SteeringKind.Wander:
            default:
                return Wander(delta, maxSpeed, rng);
        }
    }

    /// <summary>Energetic play roam: re-rolls direction far more often than Wander (FrolicDwell),
    /// producing a darty zig-zag that reads as play. Owns its own timer/dir so it doesn't disturb
    /// the shared Wander state used by settled-herd drift.</summary>
    private Vector3 FrolicWander(double delta, float maxSpeed, Random rng)
    {
        _frolicTimer -= delta;
        if (_frolicTimer <= 0 || _frolicDir.LengthSquared() < 1e-6f)
        {
            double angle = rng.NextDouble() * 2.0 * Math.PI;
            _frolicDir = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
            _frolicTimer = _config.FrolicDwellMin
                + rng.NextDouble() * (_config.FrolicDwellMax - _config.FrolicDwellMin);
        }
        return _frolicDir * maxSpeed;
    }

    /// <summary>Relaxed roaming: re-roll a random XZ direction periodically, move at full speed.</summary>
    private Vector3 Wander(double delta, float maxSpeed, Random rng)
    {
        _wanderTimer -= delta;
        if (_wanderTimer <= 0 || _wanderDir.LengthSquared() < 1e-6f)
        {
            double angle = rng.NextDouble() * 2.0 * Math.PI;
            _wanderDir = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
            _wanderTimer = _config.WanderDwellMin
                + rng.NextDouble() * (_config.WanderDwellMax - _config.WanderDwellMin);
        }
        return _wanderDir * maxSpeed;
    }
}
