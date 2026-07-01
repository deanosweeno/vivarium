namespace Vivarium.Core;

/// <summary>
/// Data-over-code tunables for the §3 harvest/craft/splice loop. A single record so callers
/// thread one config rather than scattering magic numbers through <see cref="HarvestTable"/>
/// and <see cref="Splicer"/>.
/// </summary>
public sealed record GeneticsConfig(
    IReadOnlyDictionary<Rarity, float> RarityOdds,
    int MinDrops,
    int MaxDrops,
    float OverrideChance,
    int DefaultSpliceBudget)
{
    public static GeneticsConfig Default { get; } = new(
        RarityOdds: new Dictionary<Rarity, float>
        {
            [Rarity.Common] = 65f,
            [Rarity.Rare] = 30f,
            [Rarity.Legendary] = 5f,
        },
        MinDrops: 1,
        MaxDrops: 3,
        OverrideChance: 1f, // §4 stat-conflict override chance; always-override until conflict resolution lands
        DefaultSpliceBudget: 2 // §7 progression source; flat until a leveling system exists
    );
}
