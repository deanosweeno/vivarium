using System.Numerics;
using Vivarium.Core;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the proto-genome kinship metric (<see cref="Genetics"/>). Pure and deterministic.
/// </summary>
public class GeneticsTests
{
    private static BodyPlan SheepBody() => new()
    {
        Id = "sheep",
        BaseScale = 1f,
        Parts = new List<BodyPart>
        {
            new() { Slot = PartSlot.Core },
            new() { Slot = PartSlot.Head },
            new() { Slot = PartSlot.Locomotion },
            new() { Slot = PartSlot.Locomotion },
        },
    };

    private static Drives SheepDrives() => new()
    {
        Curiosity = 0.3f, Fear = 0.15f, Sociability = 0.9f,
        Appetite = 0.6f, Aggression = 0.1f, PlayCuddle = 0.3f,
    };

    private static Creature Make(BodyPlan? body, Drives drives)
        => new(Vector3.Zero, traits: null, new WalkMode(), drives) { Body = body };

    [Fact]
    public void Similarity_IdenticalSheep_IsOne()
    {
        var body = SheepBody();
        var a = Make(body, SheepDrives());
        var b = Make(body, SheepDrives());
        Assert.Equal(1f, Genetics.Similarity(a, b), 4);
    }

    [Fact]
    public void Similarity_SheepVsSprout_IsLow()
    {
        var sheep = Make(SheepBody(), SheepDrives());
        var sprout = Make(
            new BodyPlan { Id = "sprout", Parts = new List<BodyPart> { new() { Slot = PartSlot.Tail } } },
            new Drives { Curiosity = 1f, Fear = 1f, Sociability = 0f, Appetite = 1f, Aggression = 1f, PlayCuddle = 1f });

        Assert.True(Genetics.Similarity(sheep, sprout) < 0.5f);
    }

    [Fact]
    public void Similarity_SheepBodiedHybrid_StaysAboveKinThreshold()
    {
        // Same body lineage, drives perturbed by ~0.1 each → still well above the 0.85 default.
        var sheep = Make(SheepBody(), SheepDrives());
        var d = SheepDrives();
        d.Curiosity += 0.1f; d.Sociability -= 0.1f; d.Appetite += 0.1f;
        var hybrid = Make(SheepBody(), d);

        Assert.True(Genetics.Similarity(sheep, hybrid) >= 0.85f,
            $"near-kin hybrid should stay herd-eligible, got {Genetics.Similarity(sheep, hybrid):F3}");
    }

    [Fact]
    public void Similarity_DifferentBodyIdSameStructure_ScoresHighOnStructure()
    {
        // Distinct plan Ids but identical part-slots + scale + drives → structural body sim = 1.
        var a = Make(new BodyPlan { Id = "a", Parts = SheepBody().Parts }, SheepDrives());
        var b = Make(new BodyPlan { Id = "b", Parts = SheepBody().Parts }, SheepDrives());
        Assert.Equal(1f, Genetics.Similarity(a, b), 4);
    }
}
