using Xunit;

namespace Vivarium.Core.Tests;

public class GeneModelTests
{
    private static readonly StatKey[] AllStatKeys = Enum.GetValues<StatKey>();

    [Fact]
    public void StatRegistry_RoundTrips_AllKeys()
    {
        foreach (var key in AllStatKeys)
        {
            var traits = CreatureTraits.Default;
            var drives = Drives.Default;
            var (min, max) = StatRegistry.Range(key);
            var mid = min == float.MaxValue || max == float.MaxValue
                ? min + 1f
                : (min + max) / 2f;

            StatRegistry.Set(key, traits, drives, mid);
            Assert.Equal(mid, StatRegistry.Get(key, traits, drives), precision: 4);
        }
    }

    [Fact]
    public void StatRegistry_Set_ClampsOutOfRange()
    {
        var traits = CreatureTraits.Default;
        var drives = Drives.Default;
        var (min, max) = StatRegistry.Range(StatKey.Curiosity);

        StatRegistry.Set(StatKey.Curiosity, traits, drives, max + 100f);
        Assert.Equal(max, StatRegistry.Get(StatKey.Curiosity, traits, drives));

        StatRegistry.Set(StatKey.Curiosity, traits, drives, min - 100f);
        Assert.Equal(min, StatRegistry.Get(StatKey.Curiosity, traits, drives));
    }

    [Fact]
    public void Genome_Create_AcceptsValidBaseAndSpecialty()
    {
        var baseGene = MakeGene("base.sheep", GeneKind.Base);
        var specialty = new[] { MakeGene("spec.wool", GeneKind.Specialty) };

        var genome = Genome.Create(baseGene, specialty, spliceBudget: 1);

        Assert.Equal(baseGene, genome.Base);
        Assert.Equal(specialty, genome.Specialty);
    }

    [Fact]
    public void Genome_Create_RejectsNonBaseAsBase()
    {
        var notBase = MakeGene("spec.wool", GeneKind.Specialty);

        Assert.Throws<ArgumentException>(() => Genome.Create(notBase, [], spliceBudget: 1));
    }

    [Fact]
    public void Genome_Create_RejectsBaseGeneInSpecialtyList()
    {
        var baseGene = MakeGene("base.sheep", GeneKind.Base);
        var specialty = new[] { MakeGene("base.imposter", GeneKind.Base) };

        Assert.Throws<ArgumentException>(() => Genome.Create(baseGene, specialty, spliceBudget: 1));
    }

    [Fact]
    public void Genome_Create_RejectsSpecialtyCountOverBudget()
    {
        var baseGene = MakeGene("base.sheep", GeneKind.Base);
        var specialty = new[]
        {
            MakeGene("spec.wool", GeneKind.Specialty),
            MakeGene("spec.horns", GeneKind.Specialty),
        };

        Assert.Throws<ArgumentException>(() => Genome.Create(baseGene, specialty, spliceBudget: 1));
    }

    [Fact]
    public void Genome_Create_AcceptsSpecialtyCountEqualToBudget()
    {
        var baseGene = MakeGene("base.sheep", GeneKind.Base);
        var specialty = new[]
        {
            MakeGene("spec.wool", GeneKind.Specialty),
            MakeGene("spec.horns", GeneKind.Specialty),
        };

        var genome = Genome.Create(baseGene, specialty, spliceBudget: 2);
        Assert.Equal(2, genome.Specialty.Count);
    }

    [Fact]
    public void Gene_CanCarryBothPartsAndPins()
    {
        var gene = new Gene
        {
            Id = "spec.wool",
            Kind = GeneKind.Specialty,
            Rarity = Rarity.Rare,
            Tier = 1,
            Visible = true,
            SourceSpecies = "sheep",
            Parts = [new BodyPart { Slot = PartSlot.Surface }],
            Pins = [new StatPin { Key = StatKey.FatigueRecoverPerSec, Value = 0.5f }],
        };

        Assert.NotNull(gene.Parts);
        Assert.NotNull(gene.Pins);
        Assert.Single(gene.Parts);
        Assert.Single(gene.Pins);
    }

    [Fact]
    public void Locus_HasValueEquality()
    {
        var a = new Locus(PartSlot.Locomotion, "legs");
        var b = new Locus(PartSlot.Locomotion, "legs");
        var c = new Locus(PartSlot.Locomotion, "tail-fin");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    private static Gene MakeGene(string id, GeneKind kind) => new()
    {
        Id = id,
        Kind = kind,
        Rarity = Rarity.Common,
        Tier = 0,
        Visible = false,
        SourceSpecies = "sheep",
    };
}
