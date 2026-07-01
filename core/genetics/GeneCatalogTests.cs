using Xunit;

namespace Vivarium.Core.Tests;

public class GeneCatalogTests
{
    private const string Json = """
        [
          { "Id": "sheep.mat.core", "Kind": "Specialty", "Rarity": "Common", "Tier": 0, "Visible": false, "SourceSpecies": "sheep" },
          { "Id": "sheep.mat.head", "Kind": "Specialty", "Rarity": "Common", "Tier": 0, "Visible": false, "SourceSpecies": "sheep" },
          {
            "Id": "spec.sheep.wool", "Kind": "Specialty", "Rarity": "Legendary", "Tier": 2, "Visible": true, "SourceSpecies": "sheep",
            "Parts": [{ "Slot": "Surface", "Shape": "Box", "Size": [0.9, 0.8, 1.0], "Socket": [0, 0.5, 0], "Tint": "#F4D35E" }],
            "Pins": [{ "Key": "Sociability", "Value": 1.0 }]
          },
          { "Id": "sprout.mat.core", "Kind": "Specialty", "Rarity": "Common", "Tier": 0, "Visible": false, "SourceSpecies": "sprout" }
        ]
        """;

    [Fact]
    public void GenesFor_ReturnsOnlyThatSpecies()
    {
        var catalog = GeneCatalog.Parse(Json);

        var sheep = catalog.GenesFor("sheep");
        var sprout = catalog.GenesFor("sprout");

        Assert.Equal(3, sheep.Count);
        Assert.Single(sprout);
    }

    [Fact]
    public void GenesFor_UnknownSpecies_ReturnsEmpty()
    {
        var catalog = GeneCatalog.Parse(Json);
        Assert.Empty(catalog.GenesFor("wolf"));
    }

    [Fact]
    public void RaritiesOwned_ReflectsDistinctRaritiesPresent()
    {
        var catalog = GeneCatalog.Parse(Json);

        var sheepRarities = catalog.RaritiesOwned("sheep");
        var sproutRarities = catalog.RaritiesOwned("sprout");

        Assert.Equal(new HashSet<Rarity> { Rarity.Common, Rarity.Legendary }, sheepRarities);
        Assert.Equal(new HashSet<Rarity> { Rarity.Common }, sproutRarities);
    }

    [Fact]
    public void Parses_PartsAndPins()
    {
        var catalog = GeneCatalog.Parse(Json);
        var wool = catalog.FindById("spec.sheep.wool");

        Assert.NotNull(wool);
        Assert.Single(wool!.Parts!);
        Assert.Equal(PartSlot.Surface, wool.Parts![0].Slot);
        Assert.Single(wool.Pins!);
        Assert.Equal(StatKey.Sociability, wool.Pins![0].Key);
    }

    [Fact]
    public void FindById_UnknownId_ReturnsNull()
    {
        var catalog = GeneCatalog.Parse(Json);
        Assert.Null(catalog.FindById("nonexistent"));
    }

    [Fact]
    public void EntriesWithoutIdOrSpecies_AreSkipped()
    {
        const string json = """
            [
              { "Kind": "Specialty", "Rarity": "Common", "SourceSpecies": "sheep" },
              { "Id": "orphan.gene", "Kind": "Specialty", "Rarity": "Common" }
            ]
            """;
        var catalog = GeneCatalog.Parse(json);

        Assert.Empty(catalog.GenesFor("sheep"));
        Assert.Null(catalog.FindById("orphan.gene"));
    }
}
