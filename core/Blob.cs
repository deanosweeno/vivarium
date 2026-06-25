using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// A single blob entity that extends <see cref="Creature"/> with a pastel color
/// and the <see cref="BlobWalkMode"/> wander rhythm (Idle/Slide state machine).
///
/// All physics, gravity, ground clamping, and collision are handled by the
/// Simulator through the Creature base and the composed IMovementMode.
/// </summary>
public class Blob : Creature
{
    // ---------- color ----------

    /// <summary>Red component in 0–1 range.</summary>
    public float R { get; }

    /// <summary>Green component in 0–1 range.</summary>
    public float G { get; }

    /// <summary>Blue component in 0–1 range.</summary>
    public float B { get; }

    // ---------- defaults ----------

    /// <summary>
    /// Default traits for a blob: Radius 0.5, GravityScale 0 (ground-only).
    /// </summary>
    public static CreatureTraits DefaultBlobTraits => new()
    {
        Radius = 0.5f,
        MaxSpeed = 0.6f,
        GravityScale = 0f,
    };

    // ---------- construction ----------

    /// <summary>
    /// Create a blob at the given position with the specified pastel color.
    /// Uses <see cref="SteeringLocomotion"/> driven by a <see cref="UtilityBrain"/>
    /// (attached by the Simulator). If <paramref name="traits"/> is null,
    /// <see cref="DefaultBlobTraits"/> is used. If <paramref name="drives"/> is null, a
    /// neutral temperament is used. <paramref name="rng"/> is accepted for API symmetry.
    /// </summary>
    public Blob(Vector3 position, float r, float g, float b, Random rng, CreatureTraits? traits = null, Drives? drives = null)
        : base(position, traits ?? DefaultBlobTraits, new SteeringLocomotion(), drives)
    {
        _ = rng;
        R = r;
        G = g;
        B = b;
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
