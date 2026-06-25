namespace Vivarium.Core;

/// <summary>
/// Tunables for how food is seeded into and replenished across the world. Data, like
/// <see cref="BehaviorConfig"/> / <see cref="MapGenConfig"/> — the simulator reads these,
/// never hard-codes them. One shared instance lives on the <see cref="Simulator"/>.
/// </summary>
public sealed class FoodConfig
{
    /// <summary>
    /// Target food items per 100 square arena units, before per-cell <see cref="BiomeDef.FoodChance"/>
    /// weighting. The simulator turns this into an attempt count over the arena area.
    /// </summary>
    public float DensityPer100SqUnits { get; init; } = 4f;

    /// <summary>
    /// How close (arena units) a creature must be to an item to graze it, added to its radius.
    /// Also the radius at which the Forage steering switches from Arrive to grazing in place.
    /// </summary>
    public float EatRange { get; init; } = 0.4f;
}
