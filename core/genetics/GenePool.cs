using System.Text.Json;

namespace Vivarium.Core;

/// <summary>
/// The player's collected genes, attributed to their source species. Duplicate drops don't stack
/// (§3 — de-duped by <see cref="Gene.Id"/>; counts are deferred). Gates <see cref="Craft"/> via
/// <see cref="HasFullSet"/>: a species' base unlocks once every Common-rarity gene cataloged for
/// it has been collected.
/// </summary>
public sealed class GenePool
{
    private readonly Dictionary<string, Gene> _byId = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<Gene> Collected => _byId.Values;

    /// <summary>Adds a gene, de-duping by id. Returns true if newly added, false if already collected.</summary>
    public bool Add(Gene gene) => _byId.TryAdd(gene.Id, gene);

    /// <summary>True once every Common-rarity gene cataloged for the species has been collected.</summary>
    public bool HasFullSet(string species, GeneCatalog catalog)
        => catalog.GenesFor(species).Where(g => g.Rarity == Rarity.Common).All(g => _byId.ContainsKey(g.Id));

    /// <summary>Ids of the species' Common-rarity genes not yet collected.</summary>
    public IReadOnlyList<string> Missing(string species, GeneCatalog catalog)
        => catalog.GenesFor(species)
            .Where(g => g.Rarity == Rarity.Common && !_byId.ContainsKey(g.Id))
            .Select(g => g.Id)
            .ToList();

    /// <summary>Serializes the collected gene ids. Rehydration needs the catalog (<see cref="Load"/>) to recover payload.</summary>
    public string Save() => JsonSerializer.Serialize(_byId.Keys.ToList());

    /// <summary>Rehydrates a pool from saved ids against a catalog. Ids no longer in the catalog are dropped (forgiving).</summary>
    public static GenePool Load(string json, GeneCatalog catalog)
    {
        var ids = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        var pool = new GenePool();
        foreach (var id in ids)
        {
            var gene = catalog.FindById(id);
            if (gene is not null)
            {
                pool.Add(gene);
            }
        }
        return pool;
    }
}
