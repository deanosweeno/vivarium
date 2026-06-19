namespace Vivarium.Core;

/// <summary>
/// Mutable configuration bag for creature physical traits.
/// Shared by reference — multiple systems can observe mutations (buffs, debuffs,
/// gene splicing) without polling. Use the copy constructor to create an
/// independent snapshot.
/// </summary>
public sealed class CreatureTraits
{
    /// <summary>
    /// Maximum horizontal speed in arena units per second.
    /// </summary>
    public float MaxSpeed { get; set; } = 0.6f;

    /// <summary>
    /// Vertical impulse applied when a ground creature jumps, in arena units.
    /// </summary>
    public float JumpHeight { get; set; } = 1.0f;

    /// <summary>
    /// How quickly the creature accelerates toward its desired velocity,
    /// in arena units per second².
    /// </summary>
    public float Acceleration { get; set; } = 2.0f;

    /// <summary>
    /// How quickly the creature can change direction, in radians per second.
    /// </summary>
    public float TurnRate { get; set; } = 3.0f;

    /// <summary>
    /// Collision sphere radius in arena units.
    /// </summary>
    public float Radius { get; set; } = 0.5f;

    /// <summary>
    /// Multiplier applied to the global gravity constant.
    /// 1.0 = normal gravity, 0.0 = no gravity (flying), negative = inverted.
    /// </summary>
    public float GravityScale { get; set; } = 1.0f;

    /// <summary>
    /// Whether the creature is capable of flight. When true, the movement mode
    /// may be swapped from WalkMode to FlyMode at runtime.
    /// </summary>
    public bool CanFly { get; set; }

    /// <summary>
    /// Ceiling for flying creatures in arena units (Y-axis).
    /// Ignored for ground creatures.
    /// </summary>
    public float MaxFlyHeight { get; set; } = float.MaxValue;

    /// <summary>
    /// Convenience accessor for a new traits instance with all defaults.
    /// </summary>
    public static CreatureTraits Default => new();

    /// <summary>
    /// Create a shallow copy of another traits instance. The copy is
    /// independent — mutating one does not affect the other.
    /// </summary>
    public CreatureTraits(CreatureTraits other)
    {
        MaxSpeed = other.MaxSpeed;
        JumpHeight = other.JumpHeight;
        Acceleration = other.Acceleration;
        TurnRate = other.TurnRate;
        Radius = other.Radius;
        GravityScale = other.GravityScale;
        CanFly = other.CanFly;
        MaxFlyHeight = other.MaxFlyHeight;
    }

    public CreatureTraits() { }
}
