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
    /// <summary>How close the nearest available food is: 1 touching, 0 at/over sense radius (or none).</summary>
    FoodProximity,
    /// <summary>1 when the creature belongs to a non-empty flock, else 0. For Flock.</summary>
    HerdPresence,
    /// <summary>0→1: normalized seconds since last belonged to any flock.
    /// 0 = just left (or still in one), 1 = been alone for ≥SeekFlockDelay.</summary>
    SeparationTime,
    /// <summary>How close the player is: 1 touching, 0 at/over sense radius (or absent). For FleePlayer/FollowPlayer.</summary>
    PlayerProximity,
    /// <summary>The creature's bond with the player [0,1]. Pair with an Inverse curve for (1−affection).</summary>
    Affection,
    /// <summary>1 when the player holds food, else 0. Pair with an Inverse curve to suppress a term when food is out.</summary>
    PlayerHoldingFood,
    /// <summary>1 when the player is a threat to this creature (strategy-owned decision), else 0.
    /// Replaces the old three-consideration gate (Proximity × Affection × HoldingFood).</summary>
    PlayerThreat,
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
        InputKind.FoodProximity => ctx.FoodProximity,
        InputKind.HerdPresence => ctx.HasFlock ? 1f : 0f,
        InputKind.SeparationTime => ctx.SeparationTime,
        InputKind.PlayerProximity => ctx.PlayerProximity,
        InputKind.Affection => ctx.Affection,
        InputKind.PlayerHoldingFood => ctx.PlayerHoldingFood ? 1f : 0f,
        InputKind.PlayerThreat => ctx.IsPlayerThreat ? 1f : 0f,
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
