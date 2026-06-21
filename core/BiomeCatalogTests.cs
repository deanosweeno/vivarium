using Xunit;

namespace Vivarium.Core.Tests;

public class BiomeCatalogTests
{
    private const string SampleJson = """
    [
      { "Biome": "Plains", "WaterChance": 0.5, "HappinessRate": 0.2, "TintHex": "#5a8c4d" },
      { "Biome": "Desert", "WaterChance": 0.05, "HappinessRate": -0.3, "SpeedMultiplier": 1.1 }
    ]
    """;

    [Fact]
    public void Parse_ReadsKnownBiomes()
    {
        var catalog = BiomeCatalog.Parse(SampleJson);

        var plains = catalog.Get(Biome.Plains);
        Assert.Equal(Biome.Plains, plains.Biome);
        Assert.Equal(0.5f, plains.WaterChance, 4);
        Assert.Equal(0.2f, plains.HappinessRate, 4);
        Assert.Equal("#5a8c4d", plains.TintHex);

        var desert = catalog.Get(Biome.Desert);
        Assert.Equal(1.1f, desert.SpeedMultiplier, 4);
    }

    [Fact]
    public void Get_MissingBiome_ReturnsNeutralDefault()
    {
        var catalog = BiomeCatalog.Parse(SampleJson); // no Forest entry
        var forest = catalog.Get(Biome.Forest);

        Assert.Equal(Biome.Forest, forest.Biome);
        Assert.Equal(0f, forest.HappinessRate, 4);     // BiomeDef default
        Assert.Equal(1f, forest.SpeedMultiplier, 4);   // BiomeDef default
    }

    [Fact]
    public void Parse_MissingFields_FallBackToDefaults()
    {
        // Desert omits FoodChance and TintHex — should use BiomeDef defaults.
        var desert = BiomeCatalog.Parse(SampleJson).Get(Biome.Desert);
        Assert.Equal(0f, desert.FoodChance, 4);
        Assert.Equal("#5a8c4d", desert.TintHex);
    }

    [Fact]
    public void Parse_UnknownBiomeName_IsIgnored()
    {
        const string json = """[ { "Biome": "Volcano", "WaterChance": 9 } ]""";
        var catalog = BiomeCatalog.Parse(json);
        // Nothing blew up; known biome still returns a neutral default.
        Assert.Equal(1f, catalog.Get(Biome.Plains).SpeedMultiplier, 4);
    }

    [Fact]
    public void Empty_AlwaysReturnsNeutralDefaults()
    {
        var def = BiomeCatalog.Empty.Get(Biome.Wetland);
        Assert.Equal(Biome.Wetland, def.Biome);
        Assert.Equal(1f, def.WaterChance, 4);
    }
}
