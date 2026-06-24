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

    /// <summary>Number of nearest already-placed seeds checked for adjacency constraints.</summary>
    private const int AdjacencyNeighbors = 3;

    /// <summary>
    /// A domain-warp field: displaces a cell's (x, z) by the sum of a broad low-frequency
    /// warp and a fine high-frequency jitter, both fBm noise. Applied before the nearest-seed
    /// lookup so biome boundaries read as organic curves instead of straight Voronoi edges.
    /// Sampling at decorrelated coordinates keeps the x and z displacements independent.
    /// </summary>
    private sealed class BiomeWarp
    {
        private readonly HeightNoise _warp;
        private readonly HeightNoise _jitter;
        private readonly float _warpAmp, _warpInvScale, _jitterAmp, _jitterInvScale;
        private readonly int _octaves;

        public BiomeWarp(MapGenConfig config, HeightNoise warp, HeightNoise jitter)
        {
            _warp = warp;
            _jitter = jitter;
            _warpAmp = config.BiomeWarpAmp;
            _warpInvScale = 1f / Math.Max(0.0001f, config.BiomeWarpScale);
            _jitterAmp = config.BiomeJitterAmp;
            _jitterInvScale = 1f / Math.Max(0.0001f, config.BiomeJitterScale);
            _octaves = config.BiomeWarpOctaves;
        }

        public (float x, float z) Apply(int cx, int cz)
        {
            // Offset the z-samples into a different region of the field so dx ≠ dz.
            float dx = _warpAmp * (_warp.Fbm(cx * _warpInvScale, cz * _warpInvScale, _octaves) - 0.5f)
                     + _jitterAmp * (_jitter.Fbm(cx * _jitterInvScale, cz * _jitterInvScale, _octaves) - 0.5f);
            float dz = _warpAmp * (_warp.Fbm((cx + 137) * _warpInvScale, (cz - 91) * _warpInvScale, _octaves) - 0.5f)
                     + _jitterAmp * (_jitter.Fbm((cx + 137) * _jitterInvScale, (cz - 91) * _jitterInvScale, _octaves) - 0.5f);
            return (cx + dx, cz + dz);
        }
    }

    /// <summary>
    /// Pick a biome for seed index <paramref name="i"/> by rejection sampling: prefer
    /// biomes compatible (per <see cref="BiomeCatalog.AreCompatible"/>) with the nearest
    /// few already-placed seeds — an approximation of Voronoi adjacency that is ample for
    /// the small seed counts used. If no candidate survives, fall back to the full pool so
    /// generation never deadlocks. Exactly one rng draw is made regardless, keeping the
    /// seed progression deterministic.
    /// </summary>
    private static Biome PickBiome(BiomeSeed[] seeds, int i, Biome[] pool, BiomeCatalog biomes, Random rng)
    {
        // Indices of the nearest already-placed seeds (0..i-1).
        var neighbors = new List<int>();
        for (int j = 0; j < i; j++)
            neighbors.Add(j);
        neighbors.Sort((a, b) =>
        {
            long da = Sq(seeds[a].X - seeds[i].X) + Sq(seeds[a].Z - seeds[i].Z);
            long db = Sq(seeds[b].X - seeds[i].X) + Sq(seeds[b].Z - seeds[i].Z);
            return da.CompareTo(db);
        });

        var candidates = new List<Biome>();
        foreach (var candidate in pool)
        {
            bool ok = true;
            for (int n = 0; n < neighbors.Count && n < AdjacencyNeighbors; n++)
                if (!biomes.AreCompatible(candidate, seeds[neighbors[n]].Biome))
                {
                    ok = false;
                    break;
                }
            if (ok)
                candidates.Add(candidate);
        }

        var choices = candidates.Count > 0 ? candidates : new List<Biome>(pool);
        return choices[rng.Next(choices.Count)];
    }

    private static long Sq(long v) => v * v;

    /// <summary>
    /// Find the two closest seed points to warped position (wx, wz) by distance-squared.
    /// Used by both AssignBiomes (best index) and SculptHeight (best+second for boundary blend).
    /// </summary>
    private static (int best, int second) NearestSeeds(BiomeSeed[] seeds, float wx, float wz)
    {
        int best = 0;
        int second = 0;
        float bestDist = float.MaxValue;
        float secondDist = float.MaxValue;

        for (int i = 0; i < seeds.Length; i++)
        {
            float dx = wx - seeds[i].X;
            float dz = wz - seeds[i].Z;
            float dist = dx * dx + dz * dz;

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
    /// AssignBiomes → AssignDefaultTerrain → SculptHeight → FloodWater → CarveLakes → FillLakeIslands → SinkWater → ScatterRocks.
    /// </summary>
    public static MapData Generate(MapGenConfig config, BiomeCatalog biomes, int seed)
    {
        var rng = new Random(seed);
        var map = new MapData(config.Width, config.Depth, config.CellSize)
        {
            SeaLevel = config.SeaLevel,
        };

        // Draw biome seed points once — shared by AssignBiomes and SculptHeight.
        // Forest is excluded from the default pool (not needed yet).
        var biomePool = config.BiomeNames is { Count: > 0 } set
            ? [.. set]
            : new Biome[] { Biome.Plains, Biome.Desert, Biome.Wetland };
        int seedCount = Math.Max(1, config.BiomeSeedCount);
        var seeds = new BiomeSeed[seedCount];
        for (int i = 0; i < seedCount; i++)
        {
            int sx = rng.Next(map.Width);
            int sz = rng.Next(map.Depth);
            seeds[i] = new BiomeSeed(sx, sz, PickBiome(seeds, i, biomePool, biomes, rng));
        }

        // Domain-warp field bends the straight Voronoi boundaries into organic shapes.
        // Drawn here (before SculptHeight's height noise) so the rng order stays fixed.
        var warp = new BiomeWarp(config, new HeightNoise(rng.Next()), new HeightNoise(rng.Next()));

        AssignBiomes(map, seeds, warp);
        SeparateIncompatibleBiomes(map, biomes, biomePool);
        AssignDefaultTerrain(map);
        SculptHeight(map, config, biomes, seeds, warp, rng);
        FloodWater(map, config);
        CarveLakes(map, config, biomes, rng);
        FillLakeIslands(map);
        SinkWater(map, config);
        ScatterRocks(map, config, biomes, rng);

        return map;
    }

    /// <summary>
    /// Pass 0b: enforce biome adjacency rules at the cell level. The seed-level
    /// rejection sampling in <see cref="PickBiome"/> only *biases* incompatible biomes
    /// apart; domain warping can still push non-neighbor regions into contact, so this
    /// pass provides the hard guarantee. For every cell that is 4-adjacent to an
    /// incompatible biome (per <see cref="BiomeCatalog.AreCompatible"/>), the cell is
    /// rewritten to a neutral "buffer" biome — the lowest-enum biome in the pool that is
    /// compatible with every biome present. Both sides of a contested border convert in
    /// the same snapshot pass, so a thin buffer strip is inserted and no incompatible
    /// 4-adjacency can survive. If no neutral biome exists, the pass is a no-op. Reads
    /// from a snapshot and draws no randomness, so determinism is unaffected.
    /// </summary>
    private static void SeparateIncompatibleBiomes(MapData map, BiomeCatalog biomes, Biome[] pool)
    {
        // Pick a buffer biome compatible with every biome in the pool (deterministic: lowest enum).
        Biome? buffer = null;
        foreach (var b in pool.OrderBy(x => (int)x))
            if (pool.All(other => biomes.AreCompatible(b, other)))
            {
                buffer = b;
                break;
            }
        if (buffer is null)
            return; // no neutral biome — cannot enforce without creating a new conflict

        // Snapshot biomes so both sides of a border see the original assignment.
        var snap = new Biome[map.Width * map.Depth];
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
            snap[cz * map.Width + cx] = map.GetCell(cx, cz).Biome;

        int[] dx4 = { -1, 1, 0, 0 };
        int[] dz4 = { 0, 0, -1, 1 };
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var b = snap[cz * map.Width + cx];
            bool violates = false;
            for (int d = 0; d < 4 && !violates; d++)
            {
                int nx = cx + dx4[d], nz = cz + dz4[d];
                if (map.InBounds(nx, nz) && !biomes.AreCompatible(b, snap[nz * map.Width + nx]))
                    violates = true;
            }
            if (violates)
            {
                var cell = map.GetCell(cx, cz);
                cell.Biome = buffer.Value;
                map.SetCell(cx, cz, cell);
            }
        }
    }

    /// <summary>
    /// Pass 1: set each cell's default terrain from its biome.
    /// Plains → Grass, Desert → Sand, Forest → Grass, Wetland → Marsh.
    /// Later passes (FloodWater, CarveLakes, ScatterRocks) may overwrite
    /// individual cells with Water or Rock.
    /// </summary>
    private static void AssignDefaultTerrain(MapData map)
    {
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            cell.Terrain = cell.Biome switch
            {
                Biome.Desert => Terrain.Sand,
                Biome.Wetland => Terrain.Marsh,
                _ => Terrain.Grass,
            };
            map.SetCell(cx, cz, cell);
        }
    }

    /// <summary>
    /// Pass 2: sculpt terrain elevation with per-biome offset and variation.
    /// One continuous <see cref="HeightNoise"/> field runs across the whole map; per-cell
    /// the nearest two biome seeds' <see cref="BiomeDef.HeightOffset"/> and
    /// <see cref="BiomeDef.HeightVariation"/> are distance-blended so boundaries slope
    /// smoothly instead of forming cliffs.
    /// </summary>
    private static void SculptHeight(MapData map, MapGenConfig config, BiomeCatalog biomes,
        BiomeSeed[] seeds, BiomeWarp warp, Random rng)
    {
        var noise = new HeightNoise(rng.Next());
        float invScale = 1f / Math.Max(0.0001f, config.HeightScale);

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            // Base noise sample — same continuous field everywhere.
            float n = noise.Fbm(cx * invScale, cz * invScale, config.HeightOctaves);

            // Blend the two nearest biomes' offset and variation, using the same warped
            // coordinates as AssignBiomes so height bias tracks the warped region edges.
            var (wx, wz) = warp.Apply(cx, cz);
            var (best, second) = NearestSeeds(seeds, wx, wz);
            var def1 = biomes.Get(seeds[best].Biome);
            var def2 = biomes.Get(seeds[second].Biome);

            float dx1 = wx - seeds[best].X;
            float dz1 = wz - seeds[best].Z;
            float d1 = MathF.Sqrt(dx1 * dx1 + dz1 * dz1);

            float dx2 = wx - seeds[second].X;
            float dz2 = wz - seeds[second].Z;
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
    /// Pass 3: flood low ground. Every cell whose <see cref="Cell.Height"/> is below
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
    private static void AssignBiomes(MapData map, BiomeSeed[] seeds, BiomeWarp warp)
    {
        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var (wx, wz) = warp.Apply(cx, cz);
            var (best, _) = NearestSeeds(seeds, wx, wz);
            var cell = map.GetCell(cx, cz);
            cell.Biome = seeds[best].Biome;
            map.SetCell(cx, cz, cell);
        }
    }

    /// <summary>
    /// Pass 4: carve <see cref="MapGenConfig.LakeCount"/> lakes. Each lake picks a
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
                // Inner core is always water; outer ring uses biome probability.
                // Always draw the RNG so seed progression stays deterministic.
                float distFrac = radiusSq == 0 ? 0f : (float)(dx * dx + dz * dz) / radiusSq;
                const float CoreFrac = 0.5f;
                double roll = rng.NextDouble();
                if (distFrac < CoreFrac * CoreFrac || roll < biomes.Get(cell.Biome).WaterChance)
                {
                    cell.Terrain = Terrain.Water;
                    map.SetCell(cx, cz, cell);
                }
            }
        }
    }

    /// <summary>
    /// Pass 5: flood-fill from all map-border cells (4-connected BFS) to identify every
    /// non-water cell reachable from the border without crossing water. Any non-water cell
    /// that is NOT reachable is completely enclosed by water (an interior island) and is
    /// converted to <see cref="Terrain.Water"/> so it cannot protrude through the water
    /// plane. Shoreline shape is unaffected; only fully enclosed holes are filled.
    /// </summary>
    private static void FillLakeIslands(MapData map)
    {
        bool[] reachable = new bool[map.Width * map.Depth];
        var queue = new Queue<(int x, int z)>();

        void Enqueue(int x, int z)
        {
            int idx = z * map.Width + x;
            if (!reachable[idx] && map.GetCell(x, z).Terrain != Terrain.Water)
            {
                reachable[idx] = true;
                queue.Enqueue((x, z));
            }
        }

        for (int x = 0; x < map.Width; x++) { Enqueue(x, 0); Enqueue(x, map.Depth - 1); }
        for (int z = 1; z < map.Depth - 1; z++) { Enqueue(0, z); Enqueue(map.Width - 1, z); }

        int[] dx4 = { -1, 1,  0, 0 };
        int[] dz4 = {  0, 0, -1, 1 };
        while (queue.Count > 0)
        {
            var (cx, cz) = queue.Dequeue();
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + dx4[d], nz = cz + dz4[d];
                if (map.InBounds(nx, nz)) Enqueue(nx, nz);
            }
        }

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            if (!reachable[cz * map.Width + cx])
            {
                var cell = map.GetCell(cx, cz);
                if (cell.Terrain != Terrain.Water)
                {
                    cell.Terrain = Terrain.Water;
                    map.SetCell(cx, cz, cell);
                }
            }
        }
    }

    /// <summary>
    /// Pass 6: sink water cells into basins (pass 1 + convergent propagation).
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
    /// Pass 7: scatter <see cref="MapGenConfig.RockClusters"/> rock clusters. Each
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
                    if ((cell.Terrain is Terrain.Grass or Terrain.Sand or Terrain.Marsh)
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
