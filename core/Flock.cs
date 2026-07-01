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
    /// <summary>Flee the player as a group — the anchor bolts away at increased speed and
    /// members cohere around it. Triggered when any member detects a player threat.</summary>
    FleePlayer,
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

    /// <summary>The terrain grid, or null when the sim runs without a map. Lets the anchor path
    /// around obstacles and stay off non-walkable cells so it doesn't drag the herd into a lake.</summary>
    MapData? Map { get; }
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

    /// <summary>Anchor navigation state — the herd's shared A* path when grazing toward a patch.
    /// The flock is the herd's nav agent; members follow the anchor reactively (no per-member path).</summary>
    public NavState Nav { get; } = new();

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
    ///
    /// When <paramref name="fleePlayer"/> is true the flock enters <see cref="FlockAction.FleePlayer"/>
    /// immediately — the anchor bolts away from the player at strategy-driven speed, overriding any
    /// Wander/Graze decision. Members cohere to the fleeing anchor via their normal Flock steering.
    /// </summary>
    public void AdvanceAnchor(double delta, Arena arena, Random rng, IFlockEnv env,
        BehaviorConfig cfg, IFleeStrategy strategy, bool fleePlayer, Vector3 playerPos)
    {
        if (Members.Count == 0) return;

        Radius = cfg.FlockBaseRadius + cfg.FlockRadiusPerMember * MathF.Sqrt(Members.Count);

        Vector3 vel;

        if (fleePlayer)
        {
            // Flock flee: bolt away from the player at member sprint speed.
            // The anchor's pace is the average member SprintSpeed,
            // so the herd moves in perfect lock-step — members cohere at the same cap.
            Current = FlockAction.FleePlayer;
            float avgSprint = 0f;
            foreach (var m in Members) avgSprint += m.Traits.SprintSpeed;
            avgSprint /= Members.Count;
            float fleeSpeed = avgSprint;
            vel = Steering.Flee(Anchor, playerPos, fleeSpeed);
        }
        else
        {
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
                bool foodNear = hasFood && Vec.HorizDist(Anchor, foodPos)
                    <= cfg.SenseRadius * cfg.FlockGrazeFoodRange;

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
            if (Current == FlockAction.Graze)
            {
                // Ease the whole circle onto the patch; settle within a radius.
                vel = Steering.Arrive(Anchor, _grazeTarget, cfg.FlockPace, Radius);

                // Grazing is goal-seeking: route the anchor around obstacles via A* so the whole
                // herd flows around a rock/lake between it and the patch. Falls back to the straight
                // Arrive above when there's no path (or no map).
                if (env.Map is { } navMap)
                {
                    var navVel = NavSystem.Steer(Anchor, _grazeTarget, Nav, navMap,
                        cfg.Nav, (float)delta, cfg.FlockPace);
                    if (navVel is { } v) vel = v;
                }
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
        }

        var next = Anchor + vel * (float)delta;
        next = arena.Clamp(next, Radius);            // keep the whole circle inside the arena
        if (env.Map is { } terrain)                  // don't let the anchor drift onto rock/water
            next = SimPhysics.SlideAgainstTerrain(Anchor, next, terrain);
        Anchor = new Vector3(next.X, env.GroundFloor(next), next.Z);
    }
}
