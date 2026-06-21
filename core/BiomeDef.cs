namespace Vivarium.Core;

/// <summary>
/// The data-driven rule record for a single <see cref="Biome"/>. One
/// <see cref="BiomeDef"/> holds every tunable for a biome, grouped by the system
/// that reads it. Instances are loaded from <c>assets/biomes.json</c> via
/// <see cref="BiomeCatalog"/> — extending a biome is a data change, not a code change.
///
/// Extensibility contract:
/// <list type="bullet">
/// <item>Add a new biome: append to the <see cref="Biome"/> enum + add one JSON object.</item>
/// <item>Add a new rule/param: add one property here + one JSON field. Missing fields
/// fall back to the defaults below; unknown JSON fields are ignored. So old data files
/// keep loading and new fields are opt-in.</item>
/// </list>
/// </summary>
public sealed class BiomeDef
{
    /// <summary>Which biome these rules apply to.</summary>
    public Biome Biome { get; init; }

    /// <summary>Human-readable name (optional; for tooling/debug output).</summary>
    public string Name { get; init; } = "";

    // --- generation biases (read by MapGenerator) ---

    /// <summary>Relative weight for placing Water in this region. 0 = never, 1 = baseline.</summary>
    public float WaterChance { get; init; } = 1f;

    /// <summary>Relative weight for placing Rock in this region. 0 = never, 1 = baseline.</summary>
    public float RockChance { get; init; } = 1f;

    /// <summary>Relative weight for placing Food in this region. 0 = never, 1 = baseline.</summary>
    public float FoodChance { get; init; } = 0f;

    // --- runtime effects (read by Simulator) ---

    /// <summary>Happiness gained (or lost, if negative) per second a creature spends here.</summary>
    public float HappinessRate { get; init; } = 0f;

    /// <summary>Multiplier applied to a creature's effective speed while in this biome.</summary>
    public float SpeedMultiplier { get; init; } = 1f;

    // --- presentation hint (read by MapView) ---

    /// <summary>Grass tint for this biome as a "#rrggbb" hex string.</summary>
    public string TintHex { get; init; } = "#5a8c4d";

    /// <summary>
    /// A neutral default definition for the given biome — used when a biome is not
    /// present in the loaded data file, so a missing entry can never crash a lookup.
    /// </summary>
    public static BiomeDef Neutral(Biome biome) => new() { Biome = biome, Name = biome.ToString() };
}
