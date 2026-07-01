namespace Vivarium.Core;

/// <summary>
/// Everything an <see cref="IPlayerInteraction"/> needs to decide and apply its effect, bundled so
/// verbs stay self-contained and the dispatcher (<see cref="PlayerController"/>) does the lookups
/// once. Deterministic: carries the seeded <see cref="Rng"/> rather than letting verbs reach for
/// global entropy.
/// </summary>
public readonly struct InteractionContext
{
    /// <summary>The player avatar performing the interaction.</summary>
    public Creature Player { get; init; }

    /// <summary>Nearest valid creature in reach, or null if none — verbs gate on this in CanApply.</summary>
    public Creature? Target { get; init; }

    /// <summary>Nearest pickable food item in reach, or null — the pick-up verb gates on this.</summary>
    public FoodItem? FoodTarget { get; init; }

    /// <summary>The live player input/inventory — the write seam a verb uses to put food in or out of
    /// hand (the struct's <see cref="HoldingFood"/> copy is read-only).</summary>
    public PlayerInputMode Input { get; init; }

    /// <summary>Tunable magnitudes (relief/bond amounts, thresholds). The data layer.</summary>
    public BehaviorConfig Config { get; init; }

    /// <summary>Whether the player currently has food in hand (gates Feed and the lure).</summary>
    public bool HoldingFood { get; init; }

    /// <summary>Seeded RNG for any randomized verb outcomes. Keeps interactions reproducible.</summary>
    public Random Rng { get; init; }

    /// <summary>The harvestable-gene catalog, or null if genetics isn't wired up (e.g. older tests) —
    /// <see cref="HarvestInteraction"/> gates on this.</summary>
    public GeneCatalog? Genes { get; init; }

    /// <summary>Harvest drop-rate tunables. Defaults to <see cref="GeneticsConfig.Default"/> so callers
    /// that don't care about genetics don't need to set it.</summary>
    public GeneticsConfig GeneticsCfg { get; init; }
}
