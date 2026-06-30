namespace Vivarium.Core;

/// <summary>
/// Derives a creature type's Base <see cref="Gene"/> from its data-driven
/// <see cref="CreatureDef"/> — the species' common parts plus a baseline stat pin per
/// <see cref="StatKey"/>. Expressed alone, the result reproduces a plain "stock" animal of
/// that species (§1 spec). This stands in for the §3 gameplay craft path (collect-all → craft):
/// here the base is computed straight from the def so the Expressor has real input.
/// </summary>
public static class BaseGene
{
    public static Gene From(CreatureDef def)
    {
        var traits = def.Traits ?? CreatureTraits.Default;
        var drives = def.Drives ?? Drives.Default;

        var pins = new List<StatPin>();
        foreach (var key in Enum.GetValues<StatKey>())
        {
            pins.Add(new StatPin { Key = key, Value = StatRegistry.Get(key, traits, drives) });
        }

        // TODO: non-scalar traits are NOT carried — StatPin.Value is float, so StatKey covers only
        // scalar stats. CreatureTraits.CanFly (bool), Diet (enum) and PreferredBiomes (list) are
        // dropped here, so an expressed flier/carnivore/biome-specialist resets to defaults. When we
        // add new creature abilities (flight, diet, habitat as spliceable genes), give Gene a
        // non-scalar payload (flags/tags) and capture them here + apply them in Expressor.

        return new Gene
        {
            Id = $"base.{def.Id}",
            Kind = GeneKind.Base,
            Rarity = Rarity.Common,
            Tier = 0,
            Visible = true,
            SourceSpecies = def.Id,
            Parts = def.Body.Parts.ToList(),
            Pins = pins,
        };
    }
}
