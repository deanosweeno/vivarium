namespace Vivarium.Core;

/// <summary>
/// Offline tooling that fills a <see cref="MapData"/> by running an ordered
/// pipeline of small, deterministic, seeded passes. This is a generate-then-freeze
/// tool: the shipped game never runs it — it loads a baked file via
/// <see cref="MapStorage"/>.
///
/// Determinism is non-negotiable: all randomness comes from a single
/// <see cref="Random"/> built from the seed, and passes draw from it in a fixed
/// order. The same (config, biomes, seed) always produces a byte-identical map.
/// No DateTime, Guid, Random.Shared, or threads.
///
/// Per-biome generation biases come from a <see cref="BiomeCatalog"/> (data), so
/// regions can be re-tuned without code changes. The biome *regions themselves*
/// are assigned first; later terrain passes weight their placement by the biome
/// under each cell.
/// </summary>
public sealed class MapGenerator
{
    /// <summary>
    /// Generate a map using a neutral (empty) biome catalog — every biome behaves
    /// at baseline weights. Convenience overload for callers that don't supply data
    /// (e.g. unit tests). Regions are still assigned; only the biases are neutral.
    /// </summary>
    public static MapData Generate(MapGenConfig config, int seed)
        => Generate(config, BiomeCatalog.Empty, seed);

    /// <summary>
    /// Generate a map from the given config, biome catalog, and seed. Pipeline order:
    /// all-Grass/all-Plains map → AssignBiomes → CarveLake → ScatterRocks.
    /// </summary>
    public static MapData Generate(MapGenConfig config, BiomeCatalog biomes, int seed)
    {
        var rng = new Random(seed);
        var map = new MapData(config.Width, config.Depth, config.CellSize)
        {
            SeaLevel = config.SeaLevel,
        };

        SculptHeight(map, config, rng);
        FloodWater(map, config);
        AssignBiomes(map, config, rng);
        CarveLakes(map, config, biomes, rng);
        ScatterRocks(map, config, biomes, rng);

        return map;
    }

    /// <summary>
    /// Pass 0a: sculpt terrain elevation. Samples a deterministic <see cref="HeightNoise"/>
    /// (seeded from the shared <paramref name="rng"/>) per cell and writes
    /// <see cref="Cell.Height"/> mapped to roughly <c>[-HeightAmplitude, +HeightAmplitude]</c>,
    /// so the surface rolls above and below <c>y=0</c>. Runs first; later passes read it.
    /// </summary>
    private static void SculptHeight(MapData map, MapGenConfig config, Random rng)
    {
        // Draw the noise seed from the shared rng so the whole map stays one deterministic stream.
        var noise = new HeightNoise(rng.Next());
        float invScale = 1f / Math.Max(0.0001f, config.HeightScale);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            // fBm in [0,1] → centered to [-1,1] → scaled to amplitude.
            float n = noise.Fbm(cx * invScale, cz * invScale, config.HeightOctaves);
            float height = (n * 2f - 1f) * config.HeightAmplitude;

            var cell = map.GetCell(cx, cz);
            cell.Height = height;
            map.SetCell(cx, cz, cell);
        }
    }

    /// <summary>
    /// Pass 0b: flood low ground. Every cell whose <see cref="Cell.Height"/> is below
    /// <see cref="MapGenConfig.SeaLevel"/> becomes <see cref="Terrain.Water"/>. The cell's
    /// height is left at its low value — that is the lakebed below the surface; the renderer
    /// draws a flat water plane at sea level on top, so basins read as genuinely below y=0.
    /// This is the primary water source; later biome-weighted <c>CarveLakes</c> only adds
    /// extra ponds. Reads height only — draws no randomness — so determinism is unaffected.
    /// </summary>
    private static void FloodWater(MapData map, MapGenConfig config)
    {
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            if (cell.Height < config.SeaLevel)
            {
                cell.Terrain = Terrain.Water;
                map.SetCell(cx, cz, cell);
            }
        }
    }

    /// <summary>
    /// Pass 0: partition the grid into biome regions via deterministic nearest-seed
    /// (Voronoi). Scatter <see cref="MapGenConfig.BiomeSeedCount"/> seed points, each
    /// assigned a random biome; every cell takes the biome of its nearest seed.
    /// </summary>
    private static void AssignBiomes(MapData map, MapGenConfig config, Random rng)
    {
        var biomePool = config.BiomeNames is { Count: > 0 } set
            ? [.. set]
            : Enum.GetValues<Biome>();
        int seedCount = Math.Max(1, config.BiomeSeedCount);

        var seedX = new int[seedCount];
        var seedZ = new int[seedCount];
        var seedBiome = new Biome[seedCount];
        for (int i = 0; i < seedCount; i++)
        {
            seedX[i] = rng.Next(map.Width);
            seedZ[i] = rng.Next(map.Depth);
            seedBiome[i] = biomePool[rng.Next(biomePool.Length)];
        }

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            int best = 0;
            long bestDistSq = long.MaxValue;
            for (int i = 0; i < seedCount; i++)
            {
                long dx = cx - seedX[i];
                long dz = cz - seedZ[i];
                long distSq = dx * dx + dz * dz;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = i;
                }
            }

            var cell = map.GetCell(cx, cz);
            cell.Biome = seedBiome[best];
            map.SetCell(cx, cz, cell);
        }
    }

    /// <summary>
    /// Pass 1: carve <see cref="MapGenConfig.LakeCount"/> lakes. Each lake picks a
    /// random center cell; cells within <see cref="MapGenConfig.LakeRadius"/>
    /// (Euclidean, in cell units) become Water with probability equal to the cell's
    /// biome <see cref="BiomeDef.WaterChance"/> — so wet biomes flood and arid ones stay dry.
    /// </summary>
    private static void CarveLakes(MapData map, MapGenConfig config, BiomeCatalog biomes, Random rng)
    {
        int radius = config.LakeRadius;
        int radiusSq = radius * radius;

        for (int i = 0; i < config.LakeCount; i++)
        {
            int centerX = rng.Next(map.Width);
            int centerZ = rng.Next(map.Depth);

            for (int cz = centerZ - radius; cz <= centerZ + radius; cz++)
            for (int cx = centerX - radius; cx <= centerX + radius; cx++)
            {
                if (!map.InBounds(cx, cz))
                    continue;

                int dx = cx - centerX;
                int dz = cz - centerZ;
                if (dx * dx + dz * dz > radiusSq)
                    continue;

                var cell = map.GetCell(cx, cz);
                // Draw once per candidate cell (fixed order) so determinism holds.
                if (rng.NextDouble() < biomes.Get(cell.Biome).WaterChance)
                {
                    cell.Terrain = Terrain.Water;
                    map.SetCell(cx, cz, cell);
                }
            }
        }
    }

    /// <summary>
    /// Pass 2: scatter <see cref="MapGenConfig.RockClusters"/> rock clusters. Each
    /// cluster starts at a random cell and does a random walk of
    /// <see cref="MapGenConfig.RockClusterSize"/> steps. A visited cell becomes Rock
    /// only if it is currently Grass (never overwriting Water) and a per-cell roll
    /// against the biome's <see cref="BiomeDef.RockChance"/> succeeds.
    /// </summary>
    private static void ScatterRocks(MapData map, MapGenConfig config, BiomeCatalog biomes, Random rng)
    {
        for (int i = 0; i < config.RockClusters; i++)
        {
            int cx = rng.Next(map.Width);
            int cz = rng.Next(map.Depth);

            for (int step = 0; step < config.RockClusterSize; step++)
            {
                if (map.InBounds(cx, cz))
                {
                    var cell = map.GetCell(cx, cz);
                    if (cell.Terrain == Terrain.Grass
                        && rng.NextDouble() < biomes.Get(cell.Biome).RockChance)
                    {
                        cell.Terrain = Terrain.Rock;
                        map.SetCell(cx, cz, cell);
                    }
                }

                // Step one cell in a random cardinal direction.
                switch (rng.Next(4))
                {
                    case 0: cx++; break;
                    case 1: cx--; break;
                    case 2: cz++; break;
                    default: cz--; break;
                }
            }
        }
    }
}
