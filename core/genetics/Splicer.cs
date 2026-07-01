namespace Vivarium.Core;

/// <summary>
/// The §3 player-facing splice verb — a thin, named seam over <see cref="Genome.Create"/>, which
/// already enforces the one-base + budget invariants. Threads the (this-phase-hardcoded) splice
/// budget from <see cref="GeneticsConfig"/> rather than scattering the number at call sites.
/// </summary>
public static class Splicer
{
    public static Genome Splice(Gene baseGene, IReadOnlyList<Gene> specialty, GeneticsConfig cfg, BodyEnvelope? envelope = null)
        => Genome.Create(baseGene, specialty, cfg.DefaultSpliceBudget, envelope);
}
