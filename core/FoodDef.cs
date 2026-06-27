namespace Vivarium.Core;

/// <summary>
/// The data-driven rule record for a single *type* of food. One <see cref="FoodDef"/>
/// holds every tunable for a food type, independent of where it grows — biomes reference
/// a type by <see cref="Id"/> (see <see cref="BiomeDef.FoodType"/>), so a type is reusable
/// across biomes and the world can grow to many-types-per-biome without restructuring.
///
/// Loaded from <c>assets/foods.json</c> via <see cref="FoodCatalog"/>. Adding or retuning a
/// food type is a data change, not a code change — mirrors <see cref="BiomeDef"/>.
/// </summary>
public sealed class FoodDef
{
    /// <summary>Stable identifier referenced by <see cref="BiomeDef.FoodType"/> and save data.</summary>
    public string Id { get; init; } = "";

    /// <summary>Human-readable name (optional; for tooling/debug).</summary>
    public string Name { get; init; } = "";

    /// <summary>Hunger removed when a full item (Amount 1) is completely eaten. 0–1.</summary>
    public float Nutrition { get; init; } = 0.5f;

    /// <summary>
    /// Item Amount drained per second while a creature grazes. An item therefore lasts
    /// <c>1 / GrazeRate</c> seconds of contact, delivering <see cref="Nutrition"/> total.
    /// </summary>
    public float GrazeRate { get; init; } = 0.5f;

    /// <summary>Seconds an eaten item stays depleted before it regrows (Amount back to 1).</summary>
    public float RespawnSeconds { get; init; } = 300f;

    /// <summary>Display color as a "#rrggbb" hex string (read by the Godot FoodVisual).</summary>
    public string ColorHex { get; init; } = "#C24B3A";

    /// <summary>Whether the player can pick a grown item of this type up off the ground into hand.
    /// Data-driven so a type can be marked un-pickable (e.g. a fixed bush) without a code change.</summary>
    public bool Pickable { get; init; } = true;

    /// <summary>
    /// A neutral default for an unknown id — so a missing entry can never crash a lookup.
    /// </summary>
    public static FoodDef Neutral(string id) => new() { Id = id, Name = id };
}
