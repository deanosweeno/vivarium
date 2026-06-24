using Xunit;

namespace Vivarium.Core.Tests;

public class BiomeCatalogTests
{
    private const string SampleJson = """
    [
      { "Biome": "Plains", "WaterChance": 0.5, "HappinessRate": 0.2, "GrassHex": "#5a8c4d" },
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
        Assert.Equal("#5a8c4d", plains.GrassHex);

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
        // Desert omits FoodChance and GrassHex — should use BiomeDef defaults.
        var desert = BiomeCatalog.Parse(SampleJson).Get(Biome.Desert);
        Assert.Equal(0f, desert.FoodChance, 4);
        Assert.Equal("#DCDBA8", desert.GrassHex);
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

    [Fact]
    public void Parse_RoundTripsHeightFields()
    {
        const string json = """
            [ { "Biome": "Plains", "HeightOffset": 1.5, "HeightVariation": 0.3 } ]
            """;
        var plains = BiomeCatalog.Parse(json).Get(Biome.Plains);
        Assert.Equal(1.5f, plains.HeightOffset, 4);
        Assert.Equal(0.3f, plains.HeightVariation, 4);
    }

    [Fact]
    public void Parse_MissingHeightFields_FallBackToDefault()
    {
        // SampleJson has no HeightOffset/HeightVariation — should use BiomeDef defaults.
        var plains = BiomeCatalog.Parse(SampleJson).Get(Biome.Plains);
        Assert.Equal(0f, plains.HeightOffset, 4);
        Assert.Equal(1f, plains.HeightVariation, 4);
    }

    [Fact]
    public void WithOverrides_SetsHeightOffset()
    {
        var catalog = BiomeCatalog.Parse(SampleJson);
        var overridden = catalog.WithOverrides(offsets: new() { [Biome.Plains] = 2.0f });

        var plains = overridden.Get(Biome.Plains);
        Assert.Equal(2.0f, plains.HeightOffset, 4);
        Assert.Equal(0.5f, plains.WaterChance, 4); // other fields preserved from JSON
    }

    [Fact]
    public void WithOverrides_PreservesOriginal()
    {
        var catalog = BiomeCatalog.Parse(SampleJson);
        _ = catalog.WithOverrides(offsets: new() { [Biome.Plains] = 2.0f });

        // Original unchanged.
        Assert.Equal(0f, catalog.Get(Biome.Plains).HeightOffset, 4);
    }

    [Fact]
    public void WithOverrides_BiomeNotInCatalog()
    {
        var catalog = BiomeCatalog.Empty;
        var overridden = catalog.WithOverrides(offsets: new() { [Biome.Wetland] = -2.0f });

        Assert.Equal(-2.0f, overridden.Get(Biome.Wetland).HeightOffset, 4);
    }

    [Fact]
    public void WithOverrides_VariationOnly_DoesNotTouchOffset()
    {
        const string json = """[ { "Biome": "Plains", "HeightOffset": 1.5 } ]""";
        var catalog = BiomeCatalog.Parse(json);
        var overridden = catalog.WithOverrides(variations: new() { [Biome.Plains] = 0.3f });

        Assert.Equal(1.5f, overridden.Get(Biome.Plains).HeightOffset, 4); // from JSON, not overridden
        Assert.Equal(0.3f, overridden.Get(Biome.Plains).HeightVariation, 4);
    }

    [Fact]
    public void WithOverrides_BothOffsetAndVariation()
    {
        var catalog = BiomeCatalog.Empty;
        var overridden = catalog.WithOverrides(
            offsets: new() { [Biome.Desert] = -1.0f },
            variations: new() { [Biome.Desert] = 0.5f });

        var desert = overridden.Get(Biome.Desert);
        Assert.Equal(-1.0f, desert.HeightOffset, 4);
        Assert.Equal(0.5f, desert.HeightVariation, 4);
    }
}
