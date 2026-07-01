using Xunit;

namespace Vivarium.Core.Tests;

public class HarvestTableTests
{
    private const string Json = """
        [
          { "Id": "sheep.mat.core", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "sheep.mat.head", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
          { "Id": "spec.sheep.rare1", "Kind": "Specialty", "Rarity": "Rare", "SourceSpecies": "sheep" },
          { "Id": "spec.sheep.legendary1", "Kind": "Specialty", "Rarity": "Legendary", "SourceSpecies": "sheep" },
          { "Id": "sprout.mat.core", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sprout" },
          { "Id": "sprout.mat.head", "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sprout" }
        ]
        """;

    [Fact]
    public void Roll_DropCount_IsWithinConfiguredRange()
    {
        var catalog = GeneCatalog.Parse(Json);
        var cfg = GeneticsConfig.Default;
        var rng = new Random(42);

        for (var i = 0; i < 50; i++)
        {
            var drops = HarvestTable.Roll("sheep", catalog, cfg, rng);
            Assert.InRange(drops.Count, cfg.MinDrops, cfg.MaxDrops);
        }
    }

    [Fact]
    public void Roll_UnknownSpecies_ReturnsEmpty()
    {
        var catalog = GeneCatalog.Parse(Json);
        var drops = HarvestTable.Roll("wolf", catalog, GeneticsConfig.Default, new Random(1));
        Assert.Empty(drops);
    }

    [Fact]
    public void Roll_IsDeterministic_ForSameSeed()
    {
        var catalog = GeneCatalog.Parse(Json);
        var cfg = GeneticsConfig.Default;

        var a = HarvestTable.Roll("sheep", catalog, cfg, new Random(7));
        var b = HarvestTable.Roll("sheep", catalog, cfg, new Random(7));

        Assert.Equal(a.Select(g => g.Id), b.Select(g => g.Id));
    }

    [Fact]
    public void Roll_SpeciesMissingLegendary_RedistributesWeight_NeverDrawsLegendary()
    {
        var catalog = GeneCatalog.Parse(Json);
        var cfg = GeneticsConfig.Default;
        var rng = new Random(99);

        for (var i = 0; i < 200; i++)
        {
            var drops = HarvestTable.Roll("sprout", catalog, cfg, rng);
            Assert.All(drops, g => Assert.Equal(Rarity.Common, g.Rarity));
        }
    }

    [Fact]
    public void Roll_OnlyDrawsGenesOwnedBySpecies()
    {
        var catalog = GeneCatalog.Parse(Json);
        var cfg = GeneticsConfig.Default;
        var rng = new Random(5);

        for (var i = 0; i < 50; i++)
        {
            var drops = HarvestTable.Roll("sheep", catalog, cfg, rng);
            Assert.All(drops, g => Assert.Equal("sheep", g.SourceSpecies));
        }
    }
}
