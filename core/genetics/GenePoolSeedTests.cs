using Xunit;

namespace Vivarium.Core.Tests;

public class GenePoolSeedTests
{
    private const string CreaturesJson = """
        [
          { "Id": "sprout" },
          { "Id": "sheep" }
        ]
        """;

    private const string GenesJson = """
        [
          { "Id": "spec.sprout.hardy", "Kind": "Specialty", "Rarity": "Rare", "SourceSpecies": "sprout" },
          { "Id": "spec.sheep.wool", "Kind": "Specialty", "Rarity": "Legendary", "SourceSpecies": "sheep" }
        ]
        """;

    [Fact]
    public void FillAll_AddsEveryBaseGeneAndEveryCatalogGene()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var genes = GeneCatalog.Parse(GenesJson);
        var pool = new GenePool();

        GenePoolSeed.FillAll(pool, creatures, genes);

        Assert.Contains(pool.Collected, g => g.Id == "base.sprout");
        Assert.Contains(pool.Collected, g => g.Id == "base.sheep");
        Assert.Contains(pool.Collected, g => g.Id == "spec.sprout.hardy");
        Assert.Contains(pool.Collected, g => g.Id == "spec.sheep.wool");
        Assert.Equal(4, pool.Collected.Count);
    }

    [Fact]
    public void FillAll_IsIdempotent()
    {
        var creatures = CreatureCatalog.Parse(CreaturesJson);
        var genes = GeneCatalog.Parse(GenesJson);
        var pool = new GenePool();

        GenePoolSeed.FillAll(pool, creatures, genes);
        GenePoolSeed.FillAll(pool, creatures, genes);

        Assert.Equal(4, pool.Collected.Count);
    }
}
