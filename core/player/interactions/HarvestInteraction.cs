namespace Vivarium.Core;

/// <summary>
/// Harvest genes from the nearest creature: a non-lethal sample, not a kill. Rolls
/// <see cref="HarvestTable.Roll"/> for the target's species (<see cref="Creature.Body"/>'s id,
/// falling back to its <see cref="Creature.Genome"/> base — spliced hybrids still sample as their
/// base species) and deposits the drops into the player's <see cref="GenePool"/>. The §3
/// harvest-half of the harvest/pool/craft/splice loop; craft/splice happen later, off the pool.
/// </summary>
public sealed class HarvestInteraction : IPlayerInteraction
{
    public string Id => "harvest";

    public bool CanApply(in InteractionContext ctx)
    {
        if (ctx.Target is null || ctx.Genes is null) return false;
        var species = SpeciesOf(ctx.Target);
        return species is not null && ctx.Genes.GenesFor(species).Count > 0;
    }

    public void Apply(in InteractionContext ctx)
    {
        var target = ctx.Target!;
        var species = SpeciesOf(target)!;
        var drops = HarvestTable.Roll(species, ctx.Genes!, ctx.GeneticsCfg, ctx.Rng);
        foreach (var gene in drops)
        {
            ctx.Input.Pool.Add(gene);
        }

        // Non-lethal sample: a mild startle/curiosity tell, not a bond hit — sampling isn't play.
        target.LastReaction = CreatureReaction.Startled(target.LastReaction);
    }

    private static string? SpeciesOf(Creature target) => target.Body?.Id ?? target.Genome?.Base.SourceSpecies;
}
