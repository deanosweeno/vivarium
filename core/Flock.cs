using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// What a flock's high-level brain is doing this interval. The flock is "treated as one
/// creature": it decides among these and moves its <see cref="Flock.Anchor"/> accordingly,
/// while member creatures hold a loose circle around the anchor via their Flock steering.
/// </summary>
public enum FlockAction
{
    /// <summary>Drift the whole circle in a random direction at a relaxed group pace (long dwell).</summary>
    Wander,
    /// <summary>Ease the whole circle onto a nearby food patch when members are collectively hungry.</summary>
    Graze,
}

/// <summary>
/// Read-only world access a <see cref="Flock"/> needs to advance its anchor without the core
/// duplicating the Simulator's map/food plumbing. Implemented by <see cref="Simulator"/>.
/// </summary>
public interface IFlockEnv
{
    /// <summary>Resting ground height under a world position.</summary>
    float GroundFloor(Vector3 pos);

    /// <summary>Nearest available food (by horizontal distance) edible under <paramref name="diet"/>.</summary>
    (Vector3 Position, bool Has) NearestFood(Vector3 from, HashSet<string>? diet);
}

/// <summary>
/// An explicit herd: a first-class group entity treated as one creature for high-level AI. It owns
/// a moving <see cref="Anchor"/> (the center of a circle of radius <see cref="Radius"/>) and a small
/// brain that picks <see cref="FlockAction.Wander"/> or <see cref="FlockAction.Graze"/>. Member
/// creatures cohere toward the anchor through their individual <see cref="SteeringKind.Flock"/>
/// steering, so the whole group milling-circle translates as one. Membership is managed by the
/// Simulator's flock system (form / join / merge / leave). Pure and deterministic given a seeded RNG.
/// </summary>
public sealed class Flock
{
    /// <summary>Current members. Mutated by the Simulator's flock system.</summary>
    public List<Creature> Members { get; } = new();

    /// <summary>Center of the wandering circle — the flock's "position".</summary>
    public Vector3 Anchor { get; private set; }

    /// <summary>Circle radius, recomputed from member count each tick so the group stays uncramped.</summary>
    public float Radius { get; private set; }

    /// <summary>The flock brain's current high-level action.</summary>
    public FlockAction Current { get; private set; }

    private Vector3 _wanderDir;
    private double _wanderTimer;
    private double _decisionTimer;
    private Vector3 _grazeTarget;

    public Flock(Vector3 anchor)
    {
        Anchor = anchor;
    }

    /// <summary>
    /// Advance the flock by <paramref name="delta"/> seconds: recompute the circle radius, re-decide
    /// Wander vs Graze on a long interval, then translate the anchor (slow group pace) and rest it on
    /// the ground. Deterministic given the seeded <paramref name="rng"/>.
    /// </summary>
    public void AdvanceAnchor(double delta, Arena arena, Random rng, IFlockEnv env, BehaviorConfig cfg)
    {
        if (Members.Count == 0) return;

        Radius = cfg.FlockBaseRadius + cfg.FlockRadiusPerMember * MathF.Sqrt(Members.Count);

        // --- Flock brain: re-decide on a long interval ---
        _decisionTimer -= delta;
        if (_decisionTimer <= 0)
        {
            _decisionTimer = cfg.FlockDecisionInterval;

            float avgHunger = 0f;
            foreach (var m in Members) avgHunger += m.Needs.Hunger;
            avgHunger /= Members.Count;

            // Members are kin, so any member's diet represents the flock's.
            var (foodPos, hasFood) = env.NearestFood(Anchor, Members[0].Diet);
            bool foodNear = hasFood && HorizDist(Anchor, foodPos) <= cfg.SenseRadius * cfg.FlockGrazeFoodRange;

            if (avgHunger > cfg.FlockGrazeHungerThreshold && foodNear)
            {
                Current = FlockAction.Graze;
                _grazeTarget = foodPos;
            }
            else
            {
                Current = FlockAction.Wander;
            }
        }

        // --- Move the anchor ---
        Vector3 vel;
        if (Current == FlockAction.Graze)
        {
            // Ease the whole circle onto the patch; settle within a radius so it doesn't overshoot.
            vel = Steering.Arrive(Anchor, _grazeTarget, cfg.FlockPace, Radius);
        }
        else
        {
            _wanderTimer -= delta;
            if (_wanderTimer <= 0 || _wanderDir.LengthSquared() < 1e-6f)
            {
                double angle = rng.NextDouble() * 2.0 * Math.PI;
                _wanderDir = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle));
                _wanderTimer = cfg.FlockWanderDwellMin
                    + rng.NextDouble() * (cfg.FlockWanderDwellMax - cfg.FlockWanderDwellMin);
            }
            vel = _wanderDir * cfg.FlockPace;
        }

        var next = Anchor + vel * (float)delta;
        next = arena.Clamp(next, Radius);            // keep the whole circle inside the arena
        Anchor = new Vector3(next.X, env.GroundFloor(next), next.Z);
    }

    private static float HorizDist(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
