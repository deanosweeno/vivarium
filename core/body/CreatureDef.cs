namespace Vivarium.Core;

/// <summary>
/// The full data-driven definition of a creature *type*, loaded from <c>assets/creatures.json</c>
/// via <see cref="CreatureCatalog"/>. Bundles the visual <see cref="BodyPlan"/> with the optional
/// simulation rules — physical <see cref="Traits"/>, personality <see cref="Drives"/>, and group
/// <see cref="Herd"/> spawn config — so adding or retuning a creature (including its stats and how it
/// spawns) is a data change, not a code change. The optional sections are null when the JSON entry
/// omits them: a Sprout, for instance, supplies only a body plan and takes randomized temperament.
/// </summary>
public sealed class CreatureDef
{
    /// <summary>Stable identifier (matches <see cref="BodyPlan.Id"/>).</summary>
    public string Id { get; init; } = "";

    /// <summary>The visual body plan — always present.</summary>
    public BodyPlan Body { get; init; } = new();

    /// <summary>Physical traits, or null to use <see cref="CreatureTraits.Default"/>.</summary>
    public CreatureTraits? Traits { get; init; }

    /// <summary>Personality drives, or null (caller decides: default or randomized).</summary>
    public Drives? Drives { get; init; }

    /// <summary>Herd-spawn config, or null when this type does not spawn in herds.</summary>
    public HerdSpawnConfig? Herd { get; init; }

    /// <summary>
    /// Name of this type's flee-from-player policy, resolved via <see cref="FleeStrategyRegistry"/>
    /// (e.g. "sheep", "never", "always"). Null = use the spawner's default strategy.
    /// </summary>
    public string? FleeStrategy { get; init; }

    /// <summary>
    /// Name of this type's action table, resolved via <see cref="ActionSetCatalog"/>
    /// (e.g. "herbivore"). Null = use the brain config's default <see cref="BrainConfig.Actions"/>.
    /// </summary>
    public string? ActionSet { get; init; }
}
