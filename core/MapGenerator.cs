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
    private record struct BiomeSeed(int X, int Z, Biome Biome);

    /// <summary>
    /// Find the two closest seed points to cell (cx, cz) by distance-squared.
    /// Used by both AssignBiomes (best index) and SculptHeight (best+second for boundary blend).
    /// </summary>
    private static (int best, int second) NearestSeeds(BiomeSeed[] seeds, int cx, int cz)
    {
        int best = 0;
        int second = 0;
        long bestDist = long.MaxValue;
        long secondDist = long.MaxValue;

        for (int i = 0; i < seeds.Length; i++)
        {
            long dx = cx - seeds[i].X;
            long dz = cz - seeds[i].Z;
            long dist = dx * dx + dz * dz;

            if (dist < bestDist)
            {
                second = best;
                secondDist = bestDist;
                best = i;
                bestDist = dist;
            }
            else if (dist < secondDist)
            {
                second = i;
                secondDist = dist;
            }
        }

        return (best, second);
    }
    /// <summary>
    /// Generate a map using a neutral (empty) biome catalog — every biome behaves
    /// at baseline weights. Convenience overload for callers that don't supply data
    /// (e.g. unit tests). Regions are still assigned; only the biases are neutral.
    /// </summary>
    public static MapData Generate(MapGenConfig config, int seed)
        => Generate(config, BiomeCatalog.Empty, seed);

    /// <summary>
    /// Generate a map from the given config, biome catalog, and seed. Pipeline order:
    /// AssignBiomes → SculptHeight → FloodWater → CarveLakes → SinkWater → ScatterRocks.
    /// </summary>
    public static MapData Generate(MapGenConfig config, BiomeCatalog biomes, int seed)
    {
        var rng = new Random(seed);
        var map = new MapData(config.Width, config.Depth, config.CellSize)
        {
            SeaLevel = config.SeaLevel,
        };

        // Draw biome seed points once — shared by AssignBiomes and SculptHeight.
        var biomePool = config.BiomeNames is { Count: > 0 } set
            ? [.. set]
            : Enum.GetValues<Biome>();
        int seedCount = Math.Max(1, config.BiomeSeedCount);
        var seeds = new BiomeSeed[seedCount];
        for (int i = 0; i < seedCount; i++)
            seeds[i] = new BiomeSeed(rng.Next(map.Width), rng.Next(map.Depth),
                biomePool[rng.Next(biomePool.Length)]);

        AssignBiomes(map, seeds);
        SculptHeight(map, config, biomes, seeds, rng);
        FloodWater(map, config);
        CarveLakes(map, config, biomes, rng);
        SinkWater(map, config);
        ScatterRocks(map, config, biomes, rng);

        return map;
    }

    /// <summary>
    /// Pass 1: sculpt terrain elevation with per-biome offset and variation.
    /// One continuous <see cref="HeightNoise"/> field runs across the whole map; per-cell
    /// the nearest two biome seeds' <see cref="BiomeDef.HeightOffset"/> and
    /// <see cref="BiomeDef.HeightVariation"/> are distance-blended so boundaries slope
    /// smoothly instead of forming cliffs.
    /// </summary>
    private static void SculptHeight(MapData map, MapGenConfig config, BiomeCatalog biomes,
        BiomeSeed[] seeds, Random rng)
    {
        var noise = new HeightNoise(rng.Next());
        float invScale = 1f / Math.Max(0.0001f, config.HeightScale);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            // Base noise sample — same continuous field everywhere.
            float n = noise.Fbm(cx * invScale, cz * invScale, config.HeightOctaves);

            // Blend the two nearest biomes' offset and variation.
            var (best, second) = NearestSeeds(seeds, cx, cz);
            var def1 = biomes.Get(seeds[best].Biome);
            var def2 = biomes.Get(seeds[second].Biome);

            long dx1 = cx - seeds[best].X;
            long dz1 = cz - seeds[best].Z;
            float d1 = MathF.Sqrt(dx1 * dx1 + dz1 * dz1);

            long dx2 = cx - seeds[second].X;
            long dz2 = cz - seeds[second].Z;
            float d2 = MathF.Sqrt(dx2 * dx2 + dz2 * dz2);

            float t = d1 + d2 > 0.0001f ? d1 / (d1 + d2) : 0f;
            float offset = Lerp(def1.HeightOffset, def2.HeightOffset, t);
            float variation = Lerp(def1.HeightVariation, def2.HeightVariation, t);

            float height = ((n * 2f - 1f) * config.HeightAmplitude * variation) + offset;

            var cell = map.GetCell(cx, cz);
            cell.Height = height;
            map.SetCell(cx, cz, cell);
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>
    /// Pass 2: flood low ground. Every cell whose <see cref="Cell.Height"/> is below
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
    private static void AssignBiomes(MapData map, BiomeSeed[] seeds)
    {
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var (best, _) = NearestSeeds(seeds, cx, cz);
            var cell = map.GetCell(cx, cz);
            cell.Biome = seeds[best].Biome;
            map.SetCell(cx, cz, cell);
        }
    }

    /// <summary>
    /// Pass 3: carve <see cref="MapGenConfig.LakeCount"/> lakes. Each lake picks a
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
    /// Pass 4: sink water cells into basins (pass 1 + convergent propagation).
    /// Pass 1 — every <see cref="Terrain.Water"/> cell is lowered to at most
    /// <c>WaterDepth</c> below its lowest adjacent non-water cell (shore). Interior
    /// cells with no dry neighbor sink relative to their own height. Heights are read
    /// from a pre-pass snapshot for order-independence.
    /// Pass 2+ — the shore depression is propagated inward iteratively. Each iteration
    /// snapshots the current heights, then every Water cell is lowered to at most its
    /// lowest neighbor height (water or land). The loop repeats until no cell changes,
    /// so the depression reaches every cell in the basin regardless of lake width.
    /// Heights only move downward and a 1e-7 epsilon guards float oscillation.
    /// Both passes read height only and draw no randomness; determinism is unaffected.
    /// </summary>
    private static void SinkWater(MapData map, MapGenConfig config)
    {
        if (config.WaterDepth <= 0f)
            return;

        // Pass 1: sink each water cell relative to its lowest dry neighbor (shore),
        // or relative to self for interior cells with no dry neighbor.
        var orig = SnapshotHeights(map);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            if (cell.Terrain != Terrain.Water)
                continue;

            // Lowest non-water neighbor (8-connected) is the shore reference.
            float shore = float.MaxValue;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;
                int nx = cx + dx;
                int nz = cz + dz;
                if (!map.InBounds(nx, nz))
                    continue;
                if (map.GetCell(nx, nz).Terrain == Terrain.Water)
                    continue;
                shore = MathF.Min(shore, orig[nz * map.Width + nx]);
            }

            float target = shore == float.MaxValue
                ? orig[cz * map.Width + cx] - config.WaterDepth  // interior: relative to self
                : shore - config.WaterDepth;                     // shore: relative to dry land
            cell.Height = MathF.Min(cell.Height, target);
            map.SetCell(cx, cz, cell);
        }

        // Pass 2+: propagate depression inward until no water cell changes.
        // Each iteration snapshots current heights so propagation is order-independent.
        bool changed;
        do
        {
            changed = false;
            var snap = SnapshotHeights(map);
            for (int cz = 0; cz < map.Depth; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                var cell = map.GetCell(cx, cz);
                if (cell.Terrain != Terrain.Water)
                    continue;

                float minNeighbor = float.MaxValue;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0)
                        continue;
                    int nx = cx + dx;
                    int nz = cz + dz;
                    if (!map.InBounds(nx, nz))
                        continue;
                    minNeighbor = MathF.Min(minNeighbor, snap[nz * map.Width + nx]);
                }
                float newHeight = MathF.Min(cell.Height, minNeighbor);
                // 1e-7 epsilon prevents float-oscillation infinite loops.
                if (newHeight < cell.Height - 1e-7f)
                {
                    cell.Height = newHeight;
                    map.SetCell(cx, cz, cell);
                    changed = true;
                }
            }
        } while (changed);
    }

    /// <summary>
    /// Take a height snapshot of every cell for order-independent processing.
    /// The returned array is indexed as [z * map.Width + x].
    /// </summary>
    private static float[] SnapshotHeights(MapData map)
    {
        var snap = new float[map.Width * map.Depth];
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            snap[cz * map.Width + cx] = map.GetCell(cx, cz).Height;
        return snap;
    }

    /// <summary>
    /// Pass 5: scatter <see cref="MapGenConfig.RockClusters"/> rock clusters. Each
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
