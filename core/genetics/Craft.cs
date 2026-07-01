namespace Vivarium.Core;

/// <summary>
/// Crafts a species' Base <see cref="Gene"/> once the player has collected the full Common set
/// for it (§3 gate — see <see cref="GenePool.HasFullSet"/>).
/// </summary>
public static class Craft
{
    public static Gene CraftBase(string species, GenePool pool, CreatureCatalog creatures, GeneCatalog genes)
    {
        if (!pool.HasFullSet(species, genes))
        {
            var missing = string.Join(", ", pool.Missing(species, genes));
            throw new InvalidOperationException($"Cannot craft base for '{species}' — missing genes: {missing}.");
        }

        var def = creatures.GetDef(species)
            ?? throw new ArgumentException($"Unknown species '{species}'.", nameof(species));

        // TODO §3: bundle the actual collected common genes into the base gene rather than
        // re-deriving from the CreatureDef. This phase keeps the def-derived MVP path
        // (see BaseGene.From) so a full collection always reproduces a stock, unmutated species.
        return BaseGene.From(def);
    }
}
