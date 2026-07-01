namespace Vivarium.Core;

/// <summary>
/// Rolls harvest drops for a species: 1–3 genes (per <see cref="GeneticsConfig"/>) picked from
/// the <see cref="GeneCatalog"/>, rarity-weighted. Pure and deterministic for a given seed — the
/// caller supplies the <see cref="Random"/> instance.
/// </summary>
public static class HarvestTable
{
    public static IReadOnlyList<Gene> Roll(string species, GeneCatalog catalog, GeneticsConfig cfg, Random rng)
    {
        var pool = catalog.GenesFor(species);
        if (pool.Count == 0)
        {
            return Array.Empty<Gene>();
        }

        var owned = catalog.RaritiesOwned(species);
        var dropCount = rng.Next(cfg.MinDrops, cfg.MaxDrops + 1);
        var drops = new List<Gene>(dropCount);
        for (var i = 0; i < dropCount; i++)
        {
            var rarity = PickRarity(owned, cfg, rng);
            var candidates = pool.Where(g => g.Rarity == rarity).ToList();
            drops.Add(candidates[rng.Next(candidates.Count)]);
        }

        return drops;
    }

    /// <summary>
    /// Weighted pick over only the rarities the species actually owns — a missing tier (e.g. no
    /// Legendary genes cataloged yet) redistributes its weight over the remaining tiers rather
    /// than ever being picked and coming up empty (§3 rule).
    /// </summary>
    private static Rarity PickRarity(IReadOnlySet<Rarity> owned, GeneticsConfig cfg, Random rng)
    {
        var total = 0f;
        foreach (var rarity in owned)
        {
            total += cfg.RarityOdds.TryGetValue(rarity, out var w) ? w : 0f;
        }

        var roll = rng.NextDouble() * total;
        var cumulative = 0f;
        foreach (var rarity in owned.OrderBy(r => r)) // stable iteration order for determinism
        {
            cumulative += cfg.RarityOdds.TryGetValue(rarity, out var w) ? w : 0f;
            if (roll < cumulative)
            {
                return rarity;
            }
        }

        return owned.OrderBy(r => r).Last(); // floating-point edge case: roll landed exactly on total
    }
}
