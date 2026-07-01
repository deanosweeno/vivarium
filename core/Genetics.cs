using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Heritable kinship between two creatures — the proto-genome used for herd grouping. A real
/// genome / <c>Expressor</c> (genotype→phenotype) does not exist yet; this blends the two
/// heritable slices that DO exist today — body lineage (<see cref="BodyPlan"/>) and personality
/// <see cref="Drives"/> ("the first slice of the genotype") — into a single similarity score in
/// [0,1]. When the genome lands, this is the one place that learns to read it.
///
/// Used by <see cref="Simulator"/>.BuildSenses so a creature coheres only with genetically
/// similar herd-mates: identical sheep score ~1.0, a Sprout scores low, and a future ~90%
/// hybrid stays above the herd kin threshold so it can hang with its parent herd.
/// Pure and deterministic (no RNG, no Godot).
/// </summary>
public static class Genetics
{
    private const float BodyWeight = 0.5f;
    private const float DrivesWeight = 0.5f;

    // Six drives, each in [0,1] ⇒ max Euclidean distance is sqrt(6).
    private static readonly float MaxDriveDistance = MathF.Sqrt(6f);

    // Genome path: shared base species dominates so a spliced hybrid coheres with its parent
    // herd; specialty overlap refines within a species.
    private const float BaseWeight = 0.7f;
    private const float SpecialtyWeight = 0.3f;

    /// <summary>Blended similarity of two creatures, in [0,1].</summary>
    /// <remarks>
    /// When both creatures carry a <see cref="Genome"/> (i.e. they were spliced/expressed, §8),
    /// similarity reads lineage directly — shared base species + specialty-gene overlap — so an
    /// engineered hybrid groups with its kin. Otherwise it falls back to the structural
    /// Body + Drives blend used by stock creatures.
    /// </remarks>
    public static float Similarity(Creature a, Creature b)
    {
        if (a.Genome is { } ga && b.Genome is { } gb)
            return GenomeSimilarity(ga, gb);

        return BodyWeight * BodySimilarity(a.Body, b.Body)
             + DrivesWeight * DrivesSimilarity(a.Drives, b.Drives);
    }

    /// <summary>
    /// Lineage similarity of two genomes in [0,1]: shared base gene (same species) weighted
    /// heavily, plus Jaccard overlap of their specialty-gene ids. Deterministic, no RNG.
    /// </summary>
    public static float GenomeSimilarity(Genome a, Genome b)
    {
        float baseSim = a.Base.Id == b.Base.Id ? 1f : 0f;
        float specialtySim = SpecialtyOverlap(a.Specialty, b.Specialty);
        return BaseWeight * baseSim + SpecialtyWeight * specialtySim;
    }

    /// <summary>Jaccard overlap (|A∩B| / |A∪B|) of two specialty-gene id sets. Both empty → 1.</summary>
    private static float SpecialtyOverlap(IReadOnlyList<Gene> a, IReadOnlyList<Gene> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1f;

        var idsA = new HashSet<string>();
        foreach (var g in a) idsA.Add(g.Id);
        var idsB = new HashSet<string>();
        foreach (var g in b) idsB.Add(g.Id);

        int intersection = 0;
        foreach (var id in idsA)
            if (idsB.Contains(id)) intersection++;

        int union = idsA.Count + idsB.Count - intersection;
        return union == 0 ? 1f : (float)intersection / union;
    }

    /// <summary>
    /// Body-lineage similarity in [0,1]. Same plan (reference-equal or matching <see cref="BodyPlan.Id"/>)
    /// → 1; otherwise a structural blend of matching part-slots and overall scale. Two missing
    /// bodies count as identical; one missing → 0.
    /// </summary>
    public static float BodySimilarity(BodyPlan? a, BodyPlan? b)
    {
        if (ReferenceEquals(a, b)) return 1f;       // same instance, or both null
        if (a is null || b is null) return 0f;
        if (!string.IsNullOrEmpty(a.Id) && a.Id == b.Id) return 1f;

        // Structural fallback for distinct (e.g. hybrid) plans: how much of the part-slot makeup
        // they share, plus how close their overall scale is.
        float slotShare = SlotOverlap(a.Parts, b.Parts);
        float maxScale = MathF.Max(a.BaseScale, b.BaseScale);
        float scaleSim = maxScale > 1e-4f ? 1f - MathF.Abs(a.BaseScale - b.BaseScale) / maxScale : 1f;
        return 0.75f * slotShare + 0.25f * Math.Clamp(scaleSim, 0f, 1f);
    }

    /// <summary>Drives similarity in [0,1]: 1 − normalized Euclidean distance over the 6 drives.</summary>
    public static float DrivesSimilarity(Drives a, Drives b)
    {
        var da = new Vector3(a.Curiosity, a.Fear, a.Sociability);
        var db = new Vector3(b.Curiosity, b.Fear, b.Sociability);
        var ea = new Vector3(a.PlayCuddle, a.Appetite, a.Aggression);
        var eb = new Vector3(b.PlayCuddle, b.Appetite, b.Aggression);
        float dist = MathF.Sqrt((da - db).LengthSquared() + (ea - eb).LengthSquared());
        return 1f - Math.Clamp(dist / MaxDriveDistance, 0f, 1f);
    }

    /// <summary>
    /// Fraction of part-slots shared between two bodies (multiset overlap / larger part count).
    /// </summary>
    private static float SlotOverlap(IReadOnlyList<BodyPart> a, IReadOnlyList<BodyPart> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1f;
        if (a.Count == 0 || b.Count == 0) return 0f;

        var counts = new Dictionary<PartSlot, int>();
        foreach (var p in a)
            counts[p.Slot] = counts.GetValueOrDefault(p.Slot) + 1;

        int shared = 0;
        foreach (var p in b)
        {
            if (counts.TryGetValue(p.Slot, out int n) && n > 0)
            {
                counts[p.Slot] = n - 1;
                shared++;
            }
        }
        return (float)shared / Math.Max(a.Count, b.Count);
    }
}
