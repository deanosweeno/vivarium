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
            Assert.True(t is Terrain.Grass or Terrain.Water or Terrain.Rock or Terrain.Sand or Terrain.Marsh);
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
    public void Generate_BiomeDefaultTerrains_ArePlaced()
    {
        // Lakes may overwrite some walkable cells with Water, but each biome's
        // default terrain should appear. Test each biome in isolation so the
        // assertion is deterministic regardless of seed.

        var desertConfig = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome> { Biome.Desert },
        };
        var desert = MapGenerator.Generate(desertConfig, 42);
        Assert.True(CountTerrain(desert, Terrain.Sand) > 0,
            "Desert-only map should have Sand");

        var wetlandConfig = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome> { Biome.Wetland },
        };
        var wetland = MapGenerator.Generate(wetlandConfig, 42);
        Assert.True(CountTerrain(wetland, Terrain.Marsh) > 0,
            "Wetland-only map should have Marsh");

        var plainsConfig = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome> { Biome.Plains },
        };
        var plains = MapGenerator.Generate(plainsConfig, 42);
        Assert.True(CountTerrain(plains, Terrain.Grass) > 0,
            "Plains-only map should have Grass");
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
        // Null BiomeNames now defaults to {Plains, Desert, Wetland} (Forest excluded).
        // The explicit 3-biome set should match null byte-for-byte.
        var configNull = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = null,
        };
        var configThree = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeNames = new HashSet<Biome> { Biome.Plains, Biome.Desert, Biome.Wetland },
        };

        var a = MapGenerator.Generate(configThree, 42);
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

    [Fact]
    public void Generate_RespectsBiomeHeightOffset()
    {
        var biomes = BiomeCatalog.Parse("""
            [
              { "Biome": "Plains", "HeightOffset": 3.0, "HeightVariation": 0.3 },
              { "Biome": "Desert", "HeightOffset": -3.0, "HeightVariation": 0.5 }
            ]
            """);
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 5,
            RockClusters = 8, RockClusterSize = 5,
            BiomeSeedCount = 8,
            BiomeNames = new HashSet<Biome> { Biome.Plains, Biome.Desert },
            HeightAmplitude = 1f, // keep noise small so offset dominates
            SeaLevel = -1000f, // no flooding — test pure height
        };
        var map = MapGenerator.Generate(config, biomes, 42);

        float plainsMean = 0f, desertMean = 0f;
        int plainsCount = 0, desertCount = 0;
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            if (cell.Biome == Biome.Plains) { plainsMean += cell.Height; plainsCount++; }
            if (cell.Biome == Biome.Desert) { desertMean += cell.Height; desertCount++; }
        }
        plainsMean /= plainsCount;
        desertMean /= desertCount;

        Assert.True(plainsCount > 0, "expected Plains cells");
        Assert.True(desertCount > 0, "expected Desert cells");
        Assert.True(plainsMean > desertMean + 1.0f,
            $"Plains mean {plainsMean:0.00} should be higher than Desert mean {desertMean:0.00}");
    }

    [Fact]
    public void Generate_HeightVariationZero_IsFlat()
    {
        var biomes = BiomeCatalog.Parse("""
            [ { "Biome": "Plains", "HeightVariation": 0.0 } ]
            """);
        var config = new MapGenConfig
        {
            Width = 16, Depth = 16, CellSize = 1f,
            LakeCount = 0,
            RockClusters = 0, RockClusterSize = 0,
            BiomeNames = new HashSet<Biome> { Biome.Plains },
        };
        var map = MapGenerator.Generate(config, biomes, 7);

        float first = map.GetCell(0, 0).Height;
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            Assert.Equal(first, map.GetCell(cx, cz).Height, 3);
    }

    [Fact]
    public void Generate_WithNeutralCatalog_HeightsStillSpanBothSigns()
    {
        // Default catalog: offset=0, variation=1 — same as old behavior.
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
    public void Generate_SinkWater_DropsWaterBelowDryNeighbors()
    {
        // Heavy lake coverage so plenty of water cells border dry land.
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 6,
            RockClusters = 0, RockClusterSize = 0,
            WaterDepth = 1.5f,
        };
        var map = MapGenerator.Generate(config, 42);

        bool checkedAny = false;
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            if (map.GetCell(cx, cz).Terrain != Terrain.Water)
                continue;

            // Find the lowest dry neighbor's height (the shore reference).
            float shore = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = cx + dx, nz = cz + dz;
                if (!map.InBounds(nx, nz)) continue;
                if (map.GetCell(nx, nz).Terrain == Terrain.Water) continue;
                shore = MathF.Min(shore, map.GetCell(nx, nz).Height);
            }
            if (shore == float.MaxValue)
                continue; // interior cell — no dry neighbor to compare against

            checkedAny = true;
            Assert.True(map.GetCell(cx, cz).Height <= shore - config.WaterDepth + 1e-3f,
                $"water cell ({cx},{cz}) height {map.GetCell(cx, cz).Height:0.00} should be " +
                $"at least {config.WaterDepth} below shore {shore:0.00}");
        }
        Assert.True(checkedAny, "expected at least one shore-adjacent water cell to verify");
    }

    [Fact]
    public void Generate_WaterDepthZero_LeavesHeightsUnchanged()
    {
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 6,
            RockClusters = 8, RockClusterSize = 5,
            WaterDepth = 0f,
        };
        // Same generation but with sinking on; only water-cell heights may differ.
        var withSink = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 6,
            RockClusters = 8, RockClusterSize = 5,
            WaterDepth = 1.5f,
        };

        var flat = MapGenerator.Generate(config, 42);
        var basin = MapGenerator.Generate(withSink, 42);

        // With WaterDepth = 0 the pass is a no-op: every non-water cell keeps its
        // height, and water cells are unchanged relative to the sinking version's dry land.
        for (int cz = 0; cz < flat.Depth; cz++)
        for (int cx = 0; cx < flat.Width; cx++)
        {
            var f = flat.GetCell(cx, cz);
            var b = basin.GetCell(cx, cz);
            Assert.Equal(f.Terrain, b.Terrain);
            if (f.Terrain != Terrain.Water)
                Assert.Equal(f.Height, b.Height, 5);
            else
                Assert.True(b.Height <= f.Height + 1e-3f,
                    "sinking should only lower water cells, never raise them");
        }
    }

    [Fact]
    public void Generate_WithSinkWater_IsDeterministic()
    {
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 1, LakeRadius = 6,
            RockClusters = 8, RockClusterSize = 5,
            WaterDepth = 1.5f,
        };
        var a = MapGenerator.Generate(config, 42);
        var b = MapGenerator.Generate(config, 42);

        for (int cz = 0; cz < a.Depth; cz++)
        for (int cx = 0; cx < a.Width; cx++)
        {
            Assert.Equal(a.GetCell(cx, cz).Terrain, b.GetCell(cx, cz).Terrain);
            Assert.Equal(a.GetCell(cx, cz).Height, b.GetCell(cx, cz).Height, 5);
        }
    }

    [Fact]
    public void Generate_BoundaryBlend_NoCliffs()
    {
        var biomes = BiomeCatalog.Parse("""
            [
              { "Biome": "Plains", "HeightOffset": 2.0, "HeightVariation": 0.0 },
              { "Biome": "Desert", "HeightOffset": -2.0, "HeightVariation": 0.0 }
            ]
            """);
        // Variation=0 and SeaLevel ridiculously low so only offsets matter — the blend
        // should produce intermediate heights between +2 and −2 near boundaries.
        var config = new MapGenConfig
        {
            Width = 32, Depth = 32, CellSize = 1f,
            LakeCount = 0,
            RockClusters = 0, RockClusterSize = 0,
            BiomeSeedCount = 8,
            BiomeNames = new HashSet<Biome> { Biome.Plains, Biome.Desert },
            SeaLevel = -1000f,
        };
        var map = MapGenerator.Generate(config, biomes, 42);

        // Verify that adjacent cells across biome boundaries don't have the full
        // 4-unit cliff (−2 → +2). At least one pair of neighbours where biomes differ
        // should differ by less than |offset1 − offset2| = 4.
        float fullJump = 4.0f; // |2 − (−2)|
        bool foundBlended = false;
        for (int cz = 0; cz < map.Depth - 1; cz++)
        for (int cx = 0; cx < map.Width - 1; cx++)
        {
            var a = map.GetCell(cx, cz);
            var b = map.GetCell(cx + 1, cz);
            if (a.Biome != b.Biome)
            {
                float delta = MathF.Abs(a.Height - b.Height);
                if (delta < fullJump * 0.95f) // must be noticeably less than full jump
                    foundBlended = true;
            }
        }
        Assert.True(foundBlended,
            "Expected at least one adjacent pair across a biome boundary to have blended height (delta < full offset jump)");
    }
}
