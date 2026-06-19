using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Ground-based movement mode. Handles horizontal wander (random direction
/// changes on a timer) and jump initiation. Gravity and ground clamping are
/// applied externally by the Simulator as global rules.
///
/// Each creature should get its own WalkMode instance — the internal wander
/// state (direction, timer) is per-creature and must not be shared.
/// </summary>
public sealed class WalkMode : IMovementMode
{
    private Vector3 _direction;
    private double _directionTimer;

    private const double DirectionMin = 1.0;
    private const double DirectionMax = 4.0;

    /// <summary>
    /// Create a walk mode. The initial direction and timer are set such that
    /// the first Tick will immediately pick a random direction.
    /// </summary>
    public WalkMode()
    {
        _direction = new Vector3(1f, 0f, 0f);
        _directionTimer = 0;
    }

    /// <summary>
    /// Advance the creature by <paramref name="delta"/> seconds.
    /// Decrements the direction-change timer; when it expires, picks a new
    /// random XZ direction. Applies horizontal velocity, integrates position,
    /// and reflects off arena XZ walls.
    ///
    /// Does NOT touch Velocity.Y — gravity and ground clamping are the
    /// Simulator's responsibility.
    /// </summary>
    public void Tick(double delta, Creature creature, Arena arena, Random rng)
    {
        _directionTimer -= delta;

        // Pick a new random direction when the timer expires
        if (_directionTimer <= 0)
        {
            var angle = rng.NextDouble() * 2.0 * Math.PI;
            _direction = new Vector3(
                (float)Math.Cos(angle),
                0f,
                (float)Math.Sin(angle));
            _directionTimer = RandomRange(rng, DirectionMin, DirectionMax);
        }

        float speed = creature.Traits.MaxSpeed;

        // Set horizontal velocity; preserve Y (managed by Simulator gravity/ground)
        creature.Velocity = new Vector3(
            _direction.X * speed,
            creature.Velocity.Y,
            _direction.Z * speed);

        // Integrate position
        creature.Position += creature.Velocity * (float)delta;

        // Arena XZ wall reflection (Y is handled by Simulator gravity/ground)
        float radius = creature.Traits.Radius;
        var pos = creature.Position;
        var vel = creature.Velocity;
        bool hit = arena.ReflectXZ(ref pos, ref vel, radius);
        creature.Position = pos;
        creature.Velocity = vel;

        if (hit)
            _directionTimer = 0;
    }

    /// <summary>
    /// Initiate a jump by setting vertical velocity.
    /// Gravity (applied by Simulator) will pull the creature back down.
    /// <paramref name="jumpHeight"/> is the initial upward speed in arena units/sec.
    ///
    /// Not called automatically — the visual layer or player input will
    /// invoke this when a jump action is triggered.
    /// </summary>
    public void Jump(Creature creature, float jumpHeight)
    {
        creature.Velocity = new Vector3(
            creature.Velocity.X,
            jumpHeight,
            creature.Velocity.Z);
    }

    // ---------- helpers ----------

    private static double RandomRange(Random rng, double min, double max)
    {
        return min + rng.NextDouble() * (max - min);
    }
}
