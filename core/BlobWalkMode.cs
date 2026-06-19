using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Wander behavior state machine.
/// </summary>
public enum WanderState
{
    /// <summary>Blob is stationary, waiting to pick a new direction.</summary>
    Idle,
    /// <summary>Blob is moving in a direction.</summary>
    Sliding
}

/// <summary>
/// Ground-based wander movement with Idle/Slide rhythm.
/// When Idle: stationary, no velocity. When Slide: pick a random XZ
/// direction and move at a speed derived from <see cref="CreatureTraits.MaxSpeed"/>
/// (random in 0.33× to 1.0× MaxSpeed).
/// Transitions happen via configurable timer ranges.
///
/// Y velocity is always set to zero — gravity is handled externally
/// by the Simulator (blobs set GravityScale=0 to opt out).
/// </summary>
public sealed class BlobWalkMode : IMovementMode
{
    /// <summary>
    /// Current wander state. May be set externally for testing.
    /// </summary>
    public WanderState State { get; internal set; }

    /// <summary>
    /// Seconds remaining in the current state. May be set externally for testing.
    /// </summary>
    public double StateTimer { get; internal set; }

    private Vector3 _direction;
    private float _cachedSpeed;

    // tempo ranges (seconds)
    private const double IdleMin = 0.5;
    private const double IdleMax = 3.0;
    private const double SlideMin = 1.0;
    private const double SlideMax = 4.0;

    // Speed range: 0.33×...1.0× MaxSpeed (preserves original 0.2–0.6 for default traits)
    private const float SpeedFractionMin = 0.33f;

    /// <summary>
    /// Create a BlobWalkMode starting in the Idle state.
    /// </summary>
    public BlobWalkMode(Random rng)
    {
        _direction = Vector3.Zero;
        _cachedSpeed = 0f;
        StartIdle(rng);
    }

    /// <summary>
    /// Advance the wander state machine by <paramref name="delta"/> seconds.
    /// Does NOT touch Velocity.Y — it is set to zero. Gravity is expected
    /// to be disabled via Traits.GravityScale = 0.
    /// </summary>
    public void Tick(double delta, Creature creature, Arena arena, Random rng)
    {
        StateTimer -= delta;

        switch (State)
        {
            case WanderState.Idle:
                if (StateTimer <= 0)
                {
                    StartSlide(rng, creature);
                    // Set velocity for observability (original Blob.StartSlide did this)
                    creature.Velocity = _direction * _cachedSpeed;
                    // No position integration this tick (original behavior)
                }
                else
                {
                    creature.Velocity = Vector3.Zero;
                }
                break;

            case WanderState.Sliding:
                creature.Velocity = _direction * _cachedSpeed;
                creature.Position += creature.Velocity * (float)delta;
                ApplyWallBounce(creature, arena);

                if (StateTimer <= 0)
                {
                    StartIdle(rng);
                    creature.Velocity = Vector3.Zero;
                }
                break;
        }
    }

    // ---------- state transitions ----------

    /// <summary>
    /// Force the mode into a specific slide for testing purposes.
    /// Sets direction, speed, and slide timer.
    /// </summary>
    internal void ForceSlide(Vector3 direction, float speed, double duration)
    {
        State = WanderState.Sliding;
        _direction = direction;
        _cachedSpeed = speed;
        StateTimer = duration;
    }

    private void StartIdle(Random rng)
    {
        State = WanderState.Idle;
        StateTimer = RandomRange(rng, IdleMin, IdleMax);
    }

    private void StartSlide(Random rng, Creature creature)
    {
        State = WanderState.Sliding;

        var angle = rng.NextDouble() * 2.0 * Math.PI;
        _direction = new Vector3(
            (float)Math.Cos(angle),
            0f,
            (float)Math.Sin(angle));

        float maxSpeed = creature.Traits.MaxSpeed;
        _cachedSpeed = (float)RandomRange(rng, maxSpeed * SpeedFractionMin, maxSpeed);
        StateTimer = RandomRange(rng, SlideMin, SlideMax);
    }

    // ---------- bounds ----------

    private static void ApplyWallBounce(Creature creature, Arena arena)
    {
        float radius = creature.Traits.Radius;
        var pos = creature.Position;
        var vel = creature.Velocity;
        // XZ-only reflection — Y is handled by Simulator gravity/ground
        arena.ReflectXZ(ref pos, ref vel, radius);
        creature.Position = pos;
        creature.Velocity = vel;
    }

    // ---------- helpers ----------

    private static double RandomRange(Random rng, double min, double max)
    {
        return min + rng.NextDouble() * (max - min);
    }
}
