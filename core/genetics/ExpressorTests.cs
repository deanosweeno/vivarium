using Xunit;

namespace Vivarium.Core.Tests;

public class ExpressorTests
{
    private static readonly StatKey[] AllStatKeys = Enum.GetValues<StatKey>();

    /// <summary>A def with a few non-default stats and a 7-part body (core/head/4 legs/tail).</summary>
    private static CreatureDef SheepDef() => new()
    {
        Id = "sheep",
        Body = new BodyPlan
        {
            Id = "sheep",
            Name = "Sheep",
            BaseScale = 2f,
            PrimaryHex = "#AABBCC",
            SecondaryHex = "#112233",
            Parts = new List<BodyPart>
            {
                new() { Slot = PartSlot.Core },
                new() { Slot = PartSlot.Head },
                new() { Slot = PartSlot.Locomotion },
                new() { Slot = PartSlot.Locomotion },
                new() { Slot = PartSlot.Locomotion },
                new() { Slot = PartSlot.Locomotion },
                new() { Slot = PartSlot.Tail },
            },
        },
        Traits = new CreatureTraits { MaxSpeed = 0.8f, JumpHeight = 1.5f },
        Drives = new Drives { Curiosity = 0.3f, Sociability = 0.7f },
    };

    private static Gene Specialty(string id, IReadOnlyList<BodyPart>? parts = null, IReadOnlyList<StatPin>? pins = null) => new()
    {
        Id = id,
        Kind = GeneKind.Specialty,
        Rarity = Rarity.Rare,
        Tier = 1,
        Visible = true,
        SourceSpecies = "wolf",
        Parts = parts,
        Pins = pins,
    };

    [Fact]
    public void StockBase_RoundTrips_AllStatsAndParts()
    {
        var def = SheepDef();
        var genome = Genome.Create(BaseGene.From(def), [], spliceBudget: 2, BodyEnvelope.From(def.Body));

        var ph = Expressor.Express(genome, new Random(1));

        foreach (var key in AllStatKeys)
        {
            var expected = StatRegistry.Get(key, def.Traits!, def.Drives!);
            Assert.Equal(expected, StatRegistry.Get(key, ph.Traits, ph.Drives), precision: 4);
        }
        Assert.Equal(7, ph.Body.Parts.Count);
        Assert.Equal("sheep", ph.Body.Id);
        Assert.Equal(2f, ph.Body.BaseScale, precision: 4);   // envelope round-trips scale + palette
        Assert.Equal("#AABBCC", ph.Body.PrimaryHex);
        Assert.Equal("#112233", ph.Body.SecondaryHex);
    }

    [Fact]
    public void SpecialtyPin_OverridesBaseline()
    {
        var def = SheepDef();
        var spec = Specialty("spec.fast", pins: [new StatPin { Key = StatKey.MaxSpeed, Value = 2f }]);
        var genome = Genome.Create(BaseGene.From(def), [spec], spliceBudget: 2, BodyEnvelope.From(def.Body));

        var ph = Expressor.Express(genome, new Random(1));

        Assert.Equal(2f, ph.Traits.MaxSpeed, precision: 4);
        // Untouched stat stays at baseline.
        Assert.Equal(1.5f, ph.Traits.JumpHeight, precision: 4);
    }

    [Fact]
    public void SpecialtyPart_ReplacesBaseAtSameLocus()
    {
        var def = SheepDef();
        var spec = Specialty("spec.legs", parts: [new BodyPart { Slot = PartSlot.Locomotion, Tint = "#FF0000" }]);
        var genome = Genome.Create(BaseGene.From(def), [spec], spliceBudget: 2, BodyEnvelope.From(def.Body));

        var ph = Expressor.Express(genome, new Random(1));

        var legs = ph.Body.Parts.Where(p => p.Slot == PartSlot.Locomotion).ToList();
        Assert.Single(legs);                       // 4 base legs replaced by 1 specialty leg
        Assert.Equal("#FF0000", legs[0].Tint);
        Assert.Equal(4, ph.Body.Parts.Count);      // 7 - 4 + 1
        Assert.Contains(ph.Body.Parts, p => p.Slot == PartSlot.Head);   // other slots intact
    }

    [Fact]
    public void SpecialtyPart_AtNewLocus_AddsWithoutDisplacingBase()
    {
        var def = SheepDef();
        var spec = Specialty("spec.wool", parts: [new BodyPart { Slot = PartSlot.Surface }]);
        var genome = Genome.Create(BaseGene.From(def), [spec], spliceBudget: 2, BodyEnvelope.From(def.Body));

        var ph = Expressor.Express(genome, new Random(1));

        Assert.Equal(8, ph.Body.Parts.Count);      // all 7 base + 1 new
        Assert.Contains(ph.Body.Parts, p => p.Slot == PartSlot.Surface);
    }

    [Fact]
    public void OutOfRangePin_ClampsToRegistryRange()
    {
        var def = SheepDef();
        var spec = Specialty("spec.hyper", pins: [new StatPin { Key = StatKey.MaxSpeed, Value = 999f }]);
        var genome = Genome.Create(BaseGene.From(def), [spec], spliceBudget: 2, BodyEnvelope.From(def.Body));

        var ph = Expressor.Express(genome, new Random(1));

        var (_, max) = StatRegistry.Range(StatKey.MaxSpeed);
        Assert.Equal(max, ph.Traits.MaxSpeed, precision: 4);
    }

    [Fact]
    public void Express_IsDeterministic_ForSameSeed()
    {
        var def = SheepDef();
        var spec = Specialty("spec.fast", pins: [new StatPin { Key = StatKey.MaxSpeed, Value = 2f }]);
        var genome = Genome.Create(BaseGene.From(def), [spec], spliceBudget: 2, BodyEnvelope.From(def.Body));

        var a = Expressor.Express(genome, new Random(7));
        var b = Expressor.Express(genome, new Random(7));

        foreach (var key in AllStatKeys)
        {
            Assert.Equal(StatRegistry.Get(key, a.Traits, a.Drives), StatRegistry.Get(key, b.Traits, b.Drives), precision: 4);
        }
        Assert.Equal(a.Body.Parts.Count, b.Body.Parts.Count);
    }
}
