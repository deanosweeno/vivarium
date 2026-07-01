namespace Vivarium.Core;

/// <summary>
/// Debug/dev seeding: fills a <see cref="GenePool"/> with every species' base gene and every
/// catalog specialty gene, bypassing the real harvest/craft gate. Used to bootstrap the splice UI
/// (both the real game and the devtools harness) until a real harvest-driven pool is wired up.
/// </summary>
public static class GenePoolSeed
{
    public static void FillAll(GenePool pool, CreatureCatalog creatures, GeneCatalog genes)
    {
        foreach (var id in creatures.Ids)
        {
            if (creatures.GetDef(id) is not { } def) continue;
            pool.Add(BaseGene.From(def));
            foreach (var gene in genes.GenesFor(id))
                pool.Add(gene);
        }
    }
}
