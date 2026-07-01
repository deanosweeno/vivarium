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
    private readonly IFleeStrategy _fleeStrategy;

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

    public UtilityBrain(BehaviorConfig config, IFleeStrategy? fleeStrategy = null)
    {
        _config = config;
        _fleeStrategy = fleeStrategy ?? new SheepFleeStrategy();
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
            Decide(self, senses, rng, (float)Math.Max(delta, _config.DecisionInterval));
            _decisionTimer = _config.DecisionInterval;
        }

        self.DesiredVelocity = ComputeSteering(self, senses, delta, rng);
    }

    /// <summary>
    /// Select the next action. Highest-scoring action wins unless the current one is held
    /// by stickiness: a challenger must beat current by SwitchMargin + remaining commitment,
    /// EXCEPT an emergency-capable action scoring past its threshold interrupts immediately.
    /// </summary>
    private void Decide(Creature self, in SenseContext senses, Random rng, float elapsed)
    {
        var drives = self.Drives;
        // Per-creature-type override (set by HerdSpawner from CreatureDef.FleeStrategy), falling
        // back to the strategy this brain was constructed with.
        var fleeStrategy = self.FleeStrategy ?? _fleeStrategy;

        // Unconditional flee override: when the strategy says this creature panics at
        // any cost, skip the entire scoring loop and immediately commit to AvoidPlayer.
        // Gated on !HasFlock so flock-level flee handles the group case separately.
        if (fleeStrategy.FleeOverridesAll && senses.PlayerPanic)
        {
            var fleeAction = _config.Actions
                .FirstOrDefault(a => a.Steering == SteeringKind.AvoidPlayer);
            if (fleeAction != null)
            {
                Commit(fleeAction);
                return;
            }
        }

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

        // Anti-dither latch: the current action declares its own hold condition (data, not a
        // SteeringKind switch) — e.g. Forage holds until eaten down to the satiation floor,
        // Rest/Frolic hold until their need drains, FleePlayer holds while PlayerPanic. An
        // action's own gates (e.g. FleePlayer's Isolation term) still release it naturally.
        float hold = Current.HoldWhile is { } latch && latch.Active(senses) ? 1f : 0f;

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
        var fleeStrategy = self.FleeStrategy ?? _fleeStrategy;
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
                    standoff, self.Traits.Radius * _config.SteeringSlowRadiusRadii);
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
                    self.Traits.Radius * _config.SteeringSlowRadiusRadii);

            case SteeringKind.Flock:
            {
                // Not in a flock → wander to look for one (the flock system will group nearby kin).
                if (!senses.HasFlock)
                    return Wander(delta, maxSpeed, rng);

                // When the flock is fleeing the player, boost to gallop-panic speed so
                // members match the fleeing anchor and the herd stays a tight ball.
                // The strategy owns the formula — one knob (FleeSpeedMultiplier) controls
                // both anchor and member flee speed.
                float cap = self.Flock?.Current == FlockAction.FleePlayer
                    ? fleeStrategy.FlockFleeCap(maxSpeed)
                    : maxSpeed;

                // Smooth cohesion: Standoff toward the anchor with no fixed offset, ramping
                // speed linearly from 0 (at anchor) to full cap (at FlockLeaveRadius).
                // Replaces Arrive-based Cohesion which snapped to full speed at the FlockRadius
                // boundary — a member outside the circle now accelerates smoothly back.
                var cohere = Steering.Standoff(self.Position, senses.FlockAnchor,
                    cap, standoff: 0f, band: _config.FlockLeaveRadius);
                // Separation pushes out of crowders' personal space. Clamp it to half cap so a
                // dense pack can no longer out-shove cohesion and explode the herd (the old bug).
                var separate = senses.SeparationPush * cap;
                float sepLen = separate.Length();
                float sepCap = cap * _config.FlockSeparationCapFraction;
                if (sepLen > sepCap)
                    separate *= sepCap / sepLen;
                // Idle drift floor: keeps a settled herd milling instead of freezing where
                // cohere and separate both cancel to zero. Reuses the shared Wander state so
                // the drift is coherent with a later Wander stroll.
                var drift = Wander(delta, cap, rng) * _config.FlockWanderFloor;
                // Light peer alignment (boids-style): nudge toward the average heading of nearby
                // flock-mates, layered on top of anchor cohesion so the herd reads as members
                // loosely following each other, not just independently orbiting one point.
                var align = senses.NeighborHeading * cap * _config.FlockAlignmentWeight;
                var blended = cohere + separate + drift + align;
                return blended.LengthSquared() > cap * cap
                    ? Vector3.Normalize(blended) * cap
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
                    var blended = anchorPull + darty * _config.FrolicDriftWeight;
                    return blended.LengthSquared() > maxSpeed * maxSpeed
                        ? Vector3.Normalize(blended) * maxSpeed
                        : blended;
                }

                return darty;
            }

            case SteeringKind.AvoidPlayer:
            {
                // Panic flee: gallop away from the player at strategy-driven speed.
                // An isolated creature with a kin flock in range flees toward that flock
                // for safety; otherwise flees directly away from the player.
                if (!senses.HasPlayer)
                    return Wander(delta, maxSpeed, rng);
                float speed = maxSpeed * fleeStrategy.FleeSpeedMultiplier;
                var target = fleeStrategy.GetFleeTarget(
                    self.Position, senses.PlayerPosition,
                    senses.HasNearbyFlock ? senses.NearestFlockAnchor : null);
                return target is Vector3 tgt
                    ? Steering.Seek(self.Position, tgt, speed)         // flee toward flock
                    : Steering.Flee(self.Position, senses.PlayerPosition, speed); // flee away
            }

            case SteeringKind.FollowPlayer:
            {
                // Approach the player eagerly when food is offered. Arrive with a small
                // settle radius (one body-radius) so the sheep walks right up into arm's
                // reach and the movement direction naturally turns its body to face the
                // player. The old Standoff(standoff=2.0) left a 0.5u gap past InteractReach.
                if (!senses.HasPlayer)
                    return Wander(delta, maxSpeed, rng);
                return Steering.Arrive(self.Position, senses.PlayerPosition, maxSpeed,
                    slowRadius: self.Traits.Radius);
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
        => Roam(ref _frolicDir, ref _frolicTimer, delta, maxSpeed, rng, _config.FrolicDwellMin, _config.FrolicDwellMax);

    /// <summary>Relaxed roaming: re-roll a random XZ direction periodically, move at full speed.</summary>
    private Vector3 Wander(double delta, float maxSpeed, Random rng)
        => Roam(ref _wanderDir, ref _wanderTimer, delta, maxSpeed, rng, _config.WanderDwellMin, _config.WanderDwellMax);

    /// <summary>
    /// Shared roam primitive: holds a random XZ direction for a randomized dwell in
    /// [dwellMin, dwellMax] seconds, re-rolling both when the dwell expires. Wander and
    /// FrolicWander are the same pattern at different dwell timescales; each keeps its own
    /// (dir, timer) pair so a wandering creature's stroll direction doesn't reset mid-frolic.
    /// </summary>
    private static Vector3 Roam(
        ref Vector3 dir, ref double timer, double delta, float maxSpeed, Random rng,
        float dwellMin, float dwellMax)
    {
        timer -= delta;
        if (timer <= 0 || dir.LengthSquared() < 1e-6f)
        {
            double angle = rng.NextDouble() * 2.0 * Math.PI;
            dir = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
            timer = dwellMin + rng.NextDouble() * (dwellMax - dwellMin);
        }
        return dir * maxSpeed;
    }
}
