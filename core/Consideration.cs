namespace Vivarium.Core;

/// <summary>A normalized [0,1] input a <see cref="Consideration"/> can read from the world.</summary>
public enum InputKind
{
    /// <summary>Always 1 — for considerations that are purely a drive factor (e.g. ×(1−fear)).</summary>
    Constant,
    /// <summary>How close the nearest neighbor is: 1 touching, 0 at/over sense radius (or none).</summary>
    NeighborProximity,
    /// <summary>The creature's Fatigue need.</summary>
    Fatigue,
    /// <summary>The creature's Hunger need.</summary>
    Hunger,
    /// <summary>The creature's Boredom need.</summary>
    Boredom,
    /// <summary>How uncomfortable the terrain under the creature is (1 = unpleasant, 0 = pleasant).</summary>
    TerrainDiscomfort,
}

/// <summary>Which <see cref="Drives"/> weight scales a consideration (or none).</summary>
public enum DriveKind
{
    None, Curiosity, Fear, Sociability, Appetite, Aggression,
}

/// <summary>
/// One scored input of an action: read an <see cref="InputKind"/>, bend it through a
/// <see cref="ResponseCurve"/>, then scale by a <see cref="Drives"/> weight (optionally
/// inverted). An action multiplies its considerations together, so any near-zero one
/// kills the action — "don't flee when nothing's near" falls out for free.
///
/// <c>value = curve(input) × driveFactor</c>, where driveFactor is the drive, (1−drive),
/// or 1 when <see cref="Drive"/> is <see cref="DriveKind.None"/>.
/// </summary>
public sealed class Consideration
{
    public InputKind Input { get; init; } = InputKind.Constant;
    public ResponseCurve Curve { get; init; } = ResponseCurve.Identity;
    public DriveKind Drive { get; init; } = DriveKind.None;

    /// <summary>When true, scale by (1 − drive) instead of the drive (e.g. Approach ×(1−fear)).</summary>
    public bool InvertDrive { get; init; }

    /// <summary>Evaluate this consideration against a perceived context and the creature's drives.</summary>
    public float Evaluate(in SenseContext ctx, Drives drives)
        => Curve.Evaluate(ReadInput(ctx)) * DriveFactor(drives);

    private float ReadInput(in SenseContext ctx) => Input switch
    {
        InputKind.Constant => 1f,
        InputKind.NeighborProximity => ctx.NeighborProximity,
        InputKind.Fatigue => ctx.Fatigue,
        InputKind.Hunger => ctx.Hunger,
        InputKind.Boredom => ctx.Boredom,
        InputKind.TerrainDiscomfort => ctx.TerrainDiscomfort,
        _ => 0f,
    };

    private float DriveFactor(Drives d)
    {
        if (Drive == DriveKind.None) return 1f;
        float v = Drive switch
        {
            DriveKind.Curiosity => d.Curiosity,
            DriveKind.Fear => d.Fear,
            DriveKind.Sociability => d.Sociability,
            DriveKind.Appetite => d.Appetite,
            DriveKind.Aggression => d.Aggression,
            _ => 1f,
        };
        return InvertDrive ? 1f - v : v;
    }
}
