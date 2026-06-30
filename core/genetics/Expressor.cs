namespace Vivarium.Core;

/// <summary>
/// Reads a <see cref="Genome"/> into a <see cref="Phenotype"/> (genotype → phenotype). Pure and
/// deterministic for a given seed. This is the MVP linear path of §2: lay down the base, let a
/// specialty gene replace the base part at any locus it occupies, override stats with specialty
/// pins, and clamp. The hard multi-gene-same-target cases are deferred:
/// <list type="bullet">
/// <item>§4 multi-pin stat conflict (rarity-weighted random pick) — here last pin wins.</item>
/// <item>§5/§6 stack &amp; part multiplicity (N instances split across genes) — here parts stack.</item>
/// <item>§6 mutation / fusion and §2 seeded jitter — not applied.</item>
/// </list>
/// </summary>
public static class Expressor
{
    public static Phenotype Express(Genome genome, Random rng)
    {
        // rng reserved for §2 jitter and §4/§6 weighted picks (deferred); unused in the MVP path.
        _ = rng;

        var baseGene = genome.Base;
        if (baseGene.Kind != GeneKind.Base)
        {
            throw new ArgumentException($"Genome base gene must have Kind={GeneKind.Base}.", nameof(genome));
        }

        // Stats: seed from base pins, then specialty pins override (last-wins; TODO §4 conflict).
        var traits = CreatureTraits.Default;
        var drives = Drives.Default;
        ApplyPins(baseGene.Pins, traits, drives);
        foreach (var gene in genome.Specialty)
        {
            ApplyPins(gene.Pins, traits, drives);
        }

        // Parts: keep base parts except at slots a specialty gene occupies (first-replaces, §5).
        // Multiple specialty genes at one slot stack here; multiplicity split is TODO §6.
        var specialtySlots = genome.Specialty
            .Where(g => g.Parts is not null)
            .SelectMany(g => g.Parts!)
            .Select(p => p.Slot)
            .ToHashSet();

        var parts = new List<BodyPart>();
        if (baseGene.Parts is not null)
        {
            foreach (var part in baseGene.Parts)
            {
                if (!specialtySlots.Contains(part.Slot))
                {
                    parts.Add(part);
                }
            }
        }
        foreach (var gene in genome.Specialty)
        {
            if (gene.Parts is null)
            {
                continue;
            }
            foreach (var part in gene.Parts)
            {
                parts.Add(part);
            }
        }

        // Whole-body scale/palette come from the sidecar envelope (BodyEnvelope); fall back to
        // BodyPlan defaults + the base species name when a genome carries no envelope.
        var env = genome.Envelope;
        var body = new BodyPlan
        {
            Id = env?.Id ?? baseGene.SourceSpecies,
            Name = env?.Name ?? baseGene.SourceSpecies,
            BaseScale = env?.BaseScale ?? 1f,
            PrimaryHex = env?.PrimaryHex ?? "#FFFFFF",
            SecondaryHex = env?.SecondaryHex ?? "#DDDDDD",
            Parts = parts,
        };

        return new Phenotype(body, traits, drives);
    }

    private static void ApplyPins(IReadOnlyList<StatPin>? pins, CreatureTraits traits, Drives drives)
    {
        if (pins is null)
        {
            return;
        }
        foreach (var pin in pins)
        {
            StatRegistry.Set(pin.Key, traits, drives, pin.Value);
        }
    }
}
