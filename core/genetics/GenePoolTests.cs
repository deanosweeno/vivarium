using Xunit;

namespace Vivarium.Core.Tests;

public class GenePoolTests
{
    private const string Json = """
        [
          { "Id": "sheep.mat.core", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "sheep.mat.head", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "spec.sheep.wool", "Kind": "Specialty", "Rarity": "Legendary", "SourceSpecies": "sheep" }
        ]
        """;

    [Fact]
    public void Add_DedupesById()
    {
        var catalog = GeneCatalog.Parse(Json);
        var pool = new GenePool();
        var gene = catalog.FindById("sheep.mat.core")!;

        Assert.True(pool.Add(gene));
        Assert.False(pool.Add(gene));
        Assert.Single(pool.Collected);
    }

    [Fact]
    public void HasFullSet_FalseUntilAllCommonCollected_ThenTrue()
    {
        var catalog = GeneCatalog.Parse(Json);
        var pool = new GenePool();

        Assert.False(pool.HasFullSet("sheep", catalog));

        pool.Add(catalog.FindById("sheep.mat.core")!);
        Assert.False(pool.HasFullSet("sheep", catalog));

        pool.Add(catalog.FindById("sheep.mat.head")!);
        Assert.True(pool.HasFullSet("sheep", catalog)); // legendary not required
    }

    [Fact]
    public void Missing_ShrinksAsGenesAreCollected()
    {
        var catalog = GeneCatalog.Parse(Json);
        var pool = new GenePool();

        Assert.Equal(2, pool.Missing("sheep", catalog).Count);

        pool.Add(catalog.FindById("sheep.mat.core")!);
        Assert.Single(pool.Missing("sheep", catalog));

        pool.Add(catalog.FindById("sheep.mat.head")!);
        Assert.Empty(pool.Missing("sheep", catalog));
    }

    [Fact]
    public void SaveLoad_RoundTripsCollectedGenes()
    {
        var catalog = GeneCatalog.Parse(Json);
        var pool = new GenePool();
        pool.Add(catalog.FindById("sheep.mat.core")!);
        pool.Add(catalog.FindById("spec.sheep.wool")!);

        var json = pool.Save();
        var loaded = GenePool.Load(json, catalog);

        Assert.Equal(2, loaded.Collected.Count);
        Assert.True(loaded.HasFullSet("sheep", catalog) == false); // head still missing
        Assert.Contains(loaded.Collected, g => g.Id == "spec.sheep.wool");
    }

    [Fact]
    public void Load_IdsNoLongerInCatalog_AreDroppedForgivingly()
    {
        var catalog = GeneCatalog.Parse(Json);
        var json = """["sheep.mat.core", "gene.that.no.longer.exists"]""";

        var loaded = GenePool.Load(json, catalog);

        Assert.Single(loaded.Collected);
    }
}
