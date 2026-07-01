using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class GenomeSimilarityTests
{
    private static Gene Base(string species)
        => new() { Id = $"base.{species}", Kind = GeneKind.Base, Rarity = Rarity.Common, Tier = 0, Visible = true, SourceSpecies = species };

    private static Gene Spec(string id, string species)
        => new() { Id = id, Kind = GeneKind.Specialty, Rarity = Rarity.Rare, Tier = 0, Visible = false, SourceSpecies = species };

    private static Genome Genome(Gene baseGene, params Gene[] specialty)
        => Vivarium.Core.Genome.Create(baseGene, specialty, spliceBudget: 8);

    private static Creature Spawn(Genome genome)
        => new(Vector3.Zero, CreatureTraits.Default, new SteeringLocomotion()) { Genome = genome };

    [Fact]
    public void SameBase_NoSpecialty_IsIdentical()
    {
        var a = Genome(Base("sheep"));
        var b = Genome(Base("sheep"));
        Assert.Equal(1f, Genetics.GenomeSimilarity(a, b), 3);
    }

    [Fact]
    public void DifferentBase_ScoresLow()
    {
        var sheep = Genome(Base("sheep"), Spec("sheep.wool", "sheep"));
        var sprout = Genome(Base("sprout"), Spec("sprout.leaf", "sprout"));
        // No shared base and no shared specialty ids ⇒ 0.
        Assert.Equal(0f, Genetics.GenomeSimilarity(sheep, sprout), 3);
    }

    [Fact]
    public void SameBase_PartialSpecialtyOverlap_StaysHigh()
    {
        var a = Genome(Base("sheep"), Spec("wool", "sheep"), Spec("horn", "sheep"));
        var b = Genome(Base("sheep"), Spec("wool", "sheep"));
        // base 0.7 + specialty Jaccard (1/2 = 0.5)*0.3 = 0.85 — well above any kin threshold.
        Assert.Equal(0.85f, Genetics.GenomeSimilarity(a, b), 3);
    }

    [Fact]
    public void Similarity_UsesGenomePath_WhenBothHaveGenome()
    {
        var a = Spawn(Genome(Base("sheep"), Spec("wool", "sheep")));
        var b = Spawn(Genome(Base("sheep"), Spec("wool", "sheep")));
        Assert.Equal(1f, Genetics.Similarity(a, b), 3);
    }

    [Fact]
    public void Similarity_FallsBackToStructural_WhenGenomeMissing()
    {
        // No genome on either ⇒ must not throw; falls back to Body+Drives blend.
        var a = new Creature(Vector3.Zero, CreatureTraits.Default, new SteeringLocomotion());
        var b = new Creature(Vector3.Zero, CreatureTraits.Default, new SteeringLocomotion());
        float sim = Genetics.Similarity(a, b);
        Assert.InRange(sim, 0f, 1f);
    }
}
