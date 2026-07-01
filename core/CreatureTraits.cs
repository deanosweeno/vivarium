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
    /// Horizontal speed when sprinting (fleeing, charging, etc.) in arena units
    /// per second. Default equals <see cref="MaxSpeed"/> — no sprint benefit unless
    /// explicitly configured higher. The sprint signal is implicit: when
    /// <see cref="Creature.DesiredVelocity"/> exceeds <see cref="MaxSpeed"/>, the
    /// locomotion layer reads this value as the speed cap instead.
    /// </summary>
    public float SprintSpeed { get; set; } = 0.6f;

    /// <summary>
    /// How quickly the creature accelerates while sprinting, in arena units per
    /// second^2. Default equals <see cref="Acceleration"/>.
    /// </summary>
    public float SprintAcceleration { get; set; } = 2.0f;

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
    /// Which biomes this creature prefers (by Id string, resolved via BiomeCatalog).
    /// Empty list or null = no biome preference. Data-driven: set in creatures.json.
    /// </summary>
    public IReadOnlyList<string> PreferredBiomes { get; set; } = [];

    /// <summary>
    /// Fatigue gained per second while moving (scaled by speed fraction).
    /// Default matches the old global BehaviorConfig value.
    /// </summary>
    public float FatigueGainPerSec { get; set; } = 0.06f;

    /// <summary>
    /// Fatigue recovered per second while resting (nearly stopped).
    /// Default matches the old global BehaviorConfig value.
    /// </summary>
    public float FatigueRecoverPerSec { get; set; } = 0.4f;

    /// <summary>
    /// Restricted diet for this creature type. null = no restriction (eats anything).
    /// When set, the creature will only eat foods whose Id matches one of these strings.
    /// </summary>
    public IReadOnlySet<string>? Diet { get; set; }

    /// <summary>
    /// Minimum hunger level before the creature will passively graze while in the Wander
    /// action. Below this threshold, a wandering creature walks past food without eating.
    /// Default 0.3 — start nibbling at 30% hunger.
    /// </summary>
    public float GrazeHungerThreshold { get; set; } = 0.3f;

    /// <summary>
    /// How far this creature senses neighbors/terrain, in arena units, or null to use the
    /// shared <see cref="BrainConfig.SenseRadius"/>. Lets a keen-eyed vs. dull creature type
    /// perceive differently without a global config change.
    /// </summary>
    public float? SenseRadius { get; set; }

    /// <summary>
    /// How far this creature senses food, in arena units, or null to use the shared
    /// <see cref="BrainConfig.FoodSenseRadius"/>.
    /// </summary>
    public float? FoodSenseRadius { get; set; }

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
        SprintSpeed = other.SprintSpeed;
        SprintAcceleration = other.SprintAcceleration;
        PreferredBiomes = other.PreferredBiomes;
        FatigueGainPerSec = other.FatigueGainPerSec;
        FatigueRecoverPerSec = other.FatigueRecoverPerSec;
        Diet = other.Diet;
        GrazeHungerThreshold = other.GrazeHungerThreshold;
        SenseRadius = other.SenseRadius;
        FoodSenseRadius = other.FoodSenseRadius;
    }

    public CreatureTraits() { }
}
