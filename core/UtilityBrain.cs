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

        bool isEmergency = best.EmergencyCapable && bestScore >= best.EmergencyThreshold;
        bool beatsStickyCurrent = bestScore > currentScore + _config.SwitchMargin + _commitment;

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
                return senses.HasNeighbor
                    ? Steering.Arrive(self.Position, senses.NeighborPosition, maxSpeed, self.Traits.Radius * 3f)
                    : Wander(delta, maxSpeed, rng);

            case SteeringKind.Flee:
                return senses.HasNeighbor
                    ? Steering.Flee(self.Position, senses.NeighborPosition, maxSpeed)
                    : Steering.Stop();

            case SteeringKind.SeekComfort:
                return senses.ComfortGradient.LengthSquared() > 1e-6f
                    ? Steering.Along(senses.ComfortGradient, maxSpeed)
                    : Wander(delta, maxSpeed, rng);

            case SteeringKind.Wander:
            default:
                return Wander(delta, maxSpeed, rng);
        }
    }

    /// <summary>Relaxed roaming: re-roll a random XZ direction periodically, move at ~0.6× speed.</summary>
    private Vector3 Wander(double delta, float maxSpeed, Random rng)
    {
        _wanderTimer -= delta;
        if (_wanderTimer <= 0 || _wanderDir.LengthSquared() < 1e-6f)
        {
            double angle = rng.NextDouble() * 2.0 * Math.PI;
            _wanderDir = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
            _wanderTimer = 1.0 + rng.NextDouble() * 3.0;
        }
        return _wanderDir * (maxSpeed * 0.6f);
    }
}
