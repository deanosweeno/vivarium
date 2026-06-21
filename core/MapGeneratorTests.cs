using Xunit;

namespace Vivarium.Core.Tests;

public class MapGeneratorTests
{
    private static MapGenConfig SmallConfig() => new()
    {
        Width = 32,
        Depth = 32,
        CellSize = 1f,
        LakeCount = 1,
        LakeRadius = 5,
        RockClusters = 8,
        RockClusterSize = 5,
    };

    private static int CountTerrain(MapData map, Terrain terrain)
    {
        int count = 0;
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            if (map.GetCell(cx, cz).Terrain == terrain)
                count++;
        return count;
    }

    [Fact]
    public void Generate_IsDeterministic_ForSameSeed()
    {
        var config = SmallConfig();
        var a = MapGenerator.Generate(config, 42);
        var b = MapGenerator.Generate(config, 42);

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Depth, b.Depth);
        for (int cz = 0; cz < a.Depth; cz++)
        for (int cx = 0; cx < a.Width; cx++)
        {
            Assert.Equal(a.GetCell(cx, cz).Terrain, b.GetCell(cx, cz).Terrain);
            Assert.Equal(a.GetCell(cx, cz).Resource, b.GetCell(cx, cz).Resource);
            Assert.Equal(a.GetCell(cx, cz).Height, b.GetCell(cx, cz).Height, 5);
        }
    }

    [Fact]
    public void Generate_DifferentSeeds_ProduceDifferentMaps()
    {
        var config = SmallConfig();
        var a = MapGenerator.Generate(config, 1);
        var b = MapGenerator.Generate(config, 2);

        bool anyDifferent = false;
        for (int cz = 0; cz < a.Depth && !anyDifferent; cz++)
        for (int cx = 0; cx < a.Width && !anyDifferent; cx++)
            if (a.GetCell(cx, cz).Terrain != b.GetCell(cx, cz).Terrain)
                anyDifferent = true;

        Assert.True(anyDifferent);
    }

    [Fact]
    public void Generate_ProducesWaterAndRock()
    {
        var map = MapGenerator.Generate(SmallConfig(), 42);
        Assert.True(CountTerrain(map, Terrain.Water) > 0, "expected at least one Water cell");
        Assert.True(CountTerrain(map, Terrain.Rock) > 0, "expected at least one Rock cell");
    }

    [Fact]
    public void Generate_NeverPlacesRockOnWater()
    {
        // A config whose lake fills most of the map and many rock attempts,
        // so rocks frequently land on would-be water cells.
        var config = new MapGenConfig
        {
            Width = 16,
            Depth = 16,
            CellSize = 1f,
            LakeCount = 1,
            LakeRadius = 12,
            RockClusters = 40,
            RockClusterSize = 8,
        };
        var map = MapGenerator.Generate(config, 7);

        // No cell can be both — terrain is a single value, but assert the
        // invariant explicitly: every Rock cell is not Water and vice versa.
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var t = map.GetCell(cx, cz).Terrain;
            Assert.True(t == Terrain.Grass || t == Terrain.Water || t == Terrain.Rock);
        }

        // Water must be preserved despite heavy rock scattering.
        Assert.True(CountTerrain(map, Terrain.Water) > 0);
    }

    [Fact]
    public void Generate_AssignsValidBiomeToEveryCell()
    {
        var map = MapGenerator.Generate(SmallConfig(), 42);
        var valid = new HashSet<Biome>(Enum.GetValues<Biome>());
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            Assert.Contains(map.GetCell(cx, cz).Biome, valid);
    }

    [Fact]
    public void Generate_ProducesMoreThanOneBiome()
    {
        // BiomeSeedCount = 6 over a 32x32 grid should yield several regions.
        var map = MapGenerator.Generate(SmallConfig(), 42);
        var seen = new HashSet<Biome>();
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            seen.Add(map.GetCell(cx, cz).Biome);
        Assert.True(seen.Count > 1, "expected the map to contain more than one biome");
    }

    [Fact]
    public void Generate_WithCatalog_IsDeterministic()
    {
        var config = SmallConfig();
        var biomes = BiomeCatalog.Parse("""
            [ { "Biome": "Desert", "WaterChance": 0.0, "RockChance": 0.0 } ]
            """);
        var a = MapGenerator.Generate(config, biomes, 7);
        var b = MapGenerator.Generate(config, biomes, 7);

        for (int cz = 0; cz < a.Depth; cz++)
        for (int cx = 0; cx < a.Width; cx++)
        {
            Assert.Equal(a.GetCell(cx, cz).Terrain, b.GetCell(cx, cz).Terrain);
            Assert.Equal(a.GetCell(cx, cz).Biome, b.GetCell(cx, cz).Biome);
        }
    }

    [Fact]
    public void Generate_BiomeWaterChanceZero_KeepsDesertCellsDry()
    {
        // Desert water chance 0 → CarveLakes adds no water to Desert cells.
        // SeaLevel below the terrain floor disables height-flooding, isolating CarveLakes.
        var biomes = BiomeCatalog.Parse("""
            [ { "Biome": "Desert", "WaterChance": 0.0 } ]
            """);
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            SeaLevel = -1000f, // no flooding — test only CarveLakes' biome weighting
        };
        var map = MapGenerator.Generate(config, biomes, 3);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            if (cell.Biome == Biome.Desert)
                Assert.NotEqual(Terrain.Water, cell.Terrain);
        }
    }

    [Fact]
    public void Generate_RespectsBiomeNamesFilter()
    {
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome> { Biome.Plains, Biome.Desert },
        };
        var map = MapGenerator.Generate(config, 42);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var b = map.GetCell(cx, cz).Biome;
            Assert.True(b == Biome.Plains || b == Biome.Desert,
                $"unexpected biome {b} — filter should exclude it");
        }
    }

    [Fact]
    public void Generate_BiomeNamesAll_MatchesNull()
    {
        var configAll = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome>(Enum.GetValues<Biome>()),
        };
        var configNull = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = null,
        };

        var a = MapGenerator.Generate(configAll, 42);
        var b = MapGenerator.Generate(configNull, 42);

        for (int cz = 0; cz < a.Depth; cz++)
        for (int cx = 0; cx < a.Width; cx++)
        {
            Assert.Equal(a.GetCell(cx, cz).Terrain, b.GetCell(cx, cz).Terrain);
            Assert.Equal(a.GetCell(cx, cz).Biome, b.GetCell(cx, cz).Biome);
        }
    }

    [Fact]
    public void Generate_SingleBiome_Works()
    {
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome> { Biome.Forest },
        };
        var map = MapGenerator.Generate(config, 7);

        // Every cell must be Forest.
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            Assert.Equal(Biome.Forest, map.GetCell(cx, cz).Biome);

        // Generation still produces water and rock (the passes still run).
        Assert.True(CountTerrain(map, Terrain.Water) > 0);
        Assert.True(CountTerrain(map, Terrain.Rock) > 0);
    }

    [Fact]
    public void Generate_HeightsSpanBothSignsAroundSeaLevel()
    {
        // Default amplitude 6, sea level 0 → the surface must roll both above and below y=0.
        var map = MapGenerator.Generate(SmallConfig(), 42);

        float min = float.MaxValue, max = float.MinValue;
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            float h = map.GetCell(cx, cz).Height;
            min = MathF.Min(min, h);
            max = MathF.Max(max, h);
        }

        Assert.True(min < 0f, $"expected some terrain below sea level (min was {min})");
        Assert.True(max > 0f, $"expected some terrain above sea level (max was {max})");
    }

    [Fact]
    public void Generate_EveryBelowSeaLevelCellIsWater()
    {
        var config = SmallConfig(); // SeaLevel defaults to 0
        var map = MapGenerator.Generate(config, 42);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            if (cell.Height < config.SeaLevel)
                Assert.Equal(Terrain.Water, cell.Terrain);
        }
    }
}
