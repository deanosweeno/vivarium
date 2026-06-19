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
/// A single blob entity. Pure data + behavior, no Godot dependency.
/// </summary>
public sealed class Blob
{
    /// <summary>Circumscribed radius of the 1×1 cube, in arena units.</summary>
    public const float Radius = 0.5f;

    // ---------- state ----------
    public Vector2 Position { get; internal set; }
    public Vector2 Velocity { get; internal set; }
    public float R { get; }
    public float G { get; }
    public float B { get; }
    public WanderState State { get; internal set; }
    public double StateTimer { get; internal set; }

    // tempo ranges (seconds)
    private const double IdleMin = 0.5;
    private const double IdleMax = 3.0;
    private const double SlideMin = 1.0;
    private const double SlideMax = 4.0;
    private const float SpeedMin = 0.2f;
    private const float SpeedMax = 0.6f;

    public Blob(Vector2 position, float r, float g, float b, Random rng)
    {
        Position = position;
        Velocity = Vector2.Zero;
        R = r;
        G = g;
        B = b;
        StartIdle(rng);
    }

    // ---------- public API ----------

    /// <summary>Advance simulation by <paramref name="delta"/> seconds.</summary>
    public void Tick(double delta, Arena arena, Random rng)
    {
        StateTimer -= delta;

        switch (State)
        {
            case WanderState.Idle:
                if (StateTimer <= 0)
                {
                    StartSlide(rng);
                }
                break;

            case WanderState.Sliding:
                // move
                Position += Velocity * (float)delta;

                // check bounds — reflect if we hit a wall
                if (!arena.Contains(Position, Radius))
                {
                    var (clamped, reflected) = arena.Reflect(Position, Velocity, Radius);
                    Position = clamped;
                    Velocity = reflected;
                }

                if (StateTimer <= 0)
                {
                    StartIdle(rng);
                }
                break;
        }
    }

    // ---------- state transitions ----------

    private void StartIdle(Random rng)
    {
        State = WanderState.Idle;
        Velocity = Vector2.Zero;
        StateTimer = RandomRange(rng, IdleMin, IdleMax);
    }

    private void StartSlide(Random rng)
    {
        State = WanderState.Sliding;

        // random direction (unit circle)
        var angle = rng.NextDouble() * 2.0 * Math.PI;
        var direction = new Vector2(
            (float)Math.Cos(angle),
            (float)Math.Sin(angle)
        );

        var speed = (float)RandomRange(rng, SpeedMin, SpeedMax);
        Velocity = direction * speed;
        StateTimer = RandomRange(rng, SlideMin, SlideMax);
    }

    // ---------- color generation ----------

    /// <summary>
    /// Generate a random pastel color (high value, moderate saturation in HSV).
    /// Returns RGB components in 0–1 range.
    /// </summary>
    public static (float R, float G, float B) RandomPastelColor(Random rng)
    {
        // pastels: hue any, saturation 0.3-0.6, value 0.8-1.0
        var h = (float)rng.NextDouble() * 360f;
        var s = (float)(0.3 + rng.NextDouble() * 0.3);  // 0.3–0.6
        var v = (float)(0.8 + rng.NextDouble() * 0.2);  // 0.8–1.0
        return HsvToRgb(h, s, v);
    }

    // ---------- helpers ----------

    private static double RandomRange(Random rng, double min, double max)
    {
        return min + rng.NextDouble() * (max - min);
    }

    /// <summary>HSV → RGB. h in [0,360], s,v in [0,1]. Returns RGB in [0,1].</summary>
    private static (float R, float G, float B) HsvToRgb(float h, float s, float v)
    {
        var c = v * s;
        var hp = h / 60f;
        var x = c * (1f - Math.Abs(hp % 2f - 1f));
        var m = v - c;

        float r1, g1, b1;
        if (hp < 1) { r1 = c; g1 = x; b1 = 0; }
        else if (hp < 2) { r1 = x; g1 = c; b1 = 0; }
        else if (hp < 3) { r1 = 0; g1 = c; b1 = x; }
        else if (hp < 4) { r1 = 0; g1 = x; b1 = c; }
        else if (hp < 5) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return (r1 + m, g1 + m, b1 + m);
    }
}
