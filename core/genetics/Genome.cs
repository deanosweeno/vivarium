namespace Vivarium.Core;

/// <summary>
/// One creature's full gene set: exactly one Base gene plus zero or more Specialty genes,
/// bounded by a splice budget. Splice-budget progression (§7) is out of scope here — the
/// budget is just an int passed in by the caller.
/// </summary>
public sealed class Genome
{
    public Gene Base { get; }
    public IReadOnlyList<Gene> Specialty { get; }

    /// <summary>Whole-body metadata (scale/palette) for the base species, or null for defaults.</summary>
    public BodyEnvelope? Envelope { get; }

    private Genome(Gene baseGene, IReadOnlyList<Gene> specialty, BodyEnvelope? envelope)
    {
        Base = baseGene;
        Specialty = specialty;
        Envelope = envelope;
    }

    public static Genome Create(Gene baseGene, IReadOnlyList<Gene> specialty, int spliceBudget, BodyEnvelope? envelope = null)
    {
        if (baseGene.Kind != GeneKind.Base)
        {
            throw new ArgumentException($"Base gene must have Kind={GeneKind.Base}, got {baseGene.Kind}.", nameof(baseGene));
        }

        foreach (var gene in specialty)
        {
            if (gene.Kind != GeneKind.Specialty)
            {
                throw new ArgumentException($"Specialty gene '{gene.Id}' must have Kind={GeneKind.Specialty}, got {gene.Kind}.", nameof(specialty));
            }
        }

        if (specialty.Count > spliceBudget)
        {
            throw new ArgumentException($"Specialty count {specialty.Count} exceeds splice budget {spliceBudget}.", nameof(specialty));
        }

        return new Genome(baseGene, specialty, envelope);
    }
}
