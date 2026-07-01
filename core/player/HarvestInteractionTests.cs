using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// The harvest verb: gene drops from the nearest creature into the player's <see cref="GenePool"/>.
/// Mirrors <see cref="PlayerInteractionTests"/>'s style — a real <see cref="Simulator"/> tick, not a
/// bare verb call, so targeting/edge-triggering/gating are exercised together.
/// </summary>
public class HarvestInteractionTests
{
    private const string GeneJson = """
        [
          { "Id": "sheep.mat.core", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "sheep.mat.head", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "spec.sheep.rare1", "Kind": "Specialty", "Rarity": "Rare", "SourceSpecies": "sheep" }
        ]
        """;

    private static Drives Timid => new() { Fear = 0.9f, Curiosity = 0.5f, Sociability = 0.5f };

    private static Simulator NewSim(GeneCatalog? genes = null)
    {
        var sim = new Simulator(Arena.GroundArena(32, 32), seed: 1) { Genes = genes };
        return sim;
    }

    private static Creature SpawnSheep(Simulator sim, Vector3 pos)
    {
        var sheep = sim.SpawnBlob(pos, traits: null, drives: Timid);
        sheep.Body = new BodyPlan { Id = "sheep", Name = "Sheep" };
        return sheep;
    }

    [Fact]
    public void Harvest_AddsRolledGenes_ToPlayerPool()
    {
        var sim = NewSim(GeneCatalog.Parse(GeneJson));
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        SpawnSheep(sim, new Vector3(1f, 0, 0));

        input.QueueIntent("harvest");
        sim.Tick(0.1);

        Assert.NotEmpty(input.Pool.Collected);
        Assert.All(input.Pool.Collected, g => Assert.Equal("sheep", g.SourceSpecies));
    }

    [Fact]
    public void Harvest_IsEdgeTriggered_OneKeypressOneRoll()
    {
        var sim = NewSim(GeneCatalog.Parse(GeneJson));
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        SpawnSheep(sim, new Vector3(1f, 0, 0));

        input.QueueIntent("harvest");
        sim.Tick(0.1);
        var afterFirst = input.Pool.Collected.Count;

        sim.Tick(0.1); // no re-queue
        Assert.Equal(afterFirst, input.Pool.Collected.Count);
    }

    [Fact]
    public void Harvest_NoOp_WhenNoCatalog()
    {
        var sim = NewSim(genes: null);
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        SpawnSheep(sim, new Vector3(1f, 0, 0));

        input.QueueIntent("harvest");
        sim.Tick(0.1);

        Assert.Empty(input.Pool.Collected);
    }

    [Fact]
    public void Harvest_NoOp_WhenTargetSpeciesHasNoCatalogedGenes()
    {
        var sim = NewSim(GeneCatalog.Parse(GeneJson)); // catalog only knows "sheep"
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var wolf = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        wolf.Body = new BodyPlan { Id = "wolf", Name = "Wolf" };

        input.QueueIntent("harvest");
        sim.Tick(0.1);

        Assert.Empty(input.Pool.Collected);
    }

    [Fact]
    public void Harvest_NoOp_WhenNoTargetInReach()
    {
        var sim = NewSim(GeneCatalog.Parse(GeneJson));
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        SpawnSheep(sim, new Vector3(50f, 0, 0)); // far out of reach

        input.QueueIntent("harvest");
        sim.Tick(0.1);

        Assert.Empty(input.Pool.Collected);
    }

    [Fact]
    public void Harvest_IsDeterministic_ForSameSeed()
    {
        var genes = GeneCatalog.Parse(GeneJson);

        var simA = NewSim(genes);
        var (_, inputA) = simA.SpawnPlayer(new Vector3(0, 0, 0));
        SpawnSheep(simA, new Vector3(1f, 0, 0));
        inputA.QueueIntent("harvest");
        simA.Tick(0.1);

        var simB = NewSim(genes);
        var (_, inputB) = simB.SpawnPlayer(new Vector3(0, 0, 0));
        SpawnSheep(simB, new Vector3(1f, 0, 0));
        inputB.QueueIntent("harvest");
        simB.Tick(0.1);

        Assert.Equal(
            inputA.Pool.Collected.Select(g => g.Id).OrderBy(id => id),
            inputB.Pool.Collected.Select(g => g.Id).OrderBy(id => id));
    }

    [Fact]
    public void Harvest_UsesGenomeBaseSpecies_WhenNoBodyPlan()
    {
        var sim = NewSim(GeneCatalog.Parse(GeneJson));
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        var baseGene = new Gene
        {
            Id = "base.sheep",
            Kind = GeneKind.Base,
            Rarity = Rarity.Common,
            Tier = 0,
            Visible = false,
            SourceSpecies = "sheep",
        };
        sheep.Genome = Genome.Create(baseGene, Array.Empty<Gene>(), spliceBudget: 2);

        input.QueueIntent("harvest");
        sim.Tick(0.1);

        Assert.NotEmpty(input.Pool.Collected);
    }
}
