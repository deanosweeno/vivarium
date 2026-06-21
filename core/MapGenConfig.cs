namespace Vivarium.Core;

/// <summary>
/// All tunable numbers for <see cref="MapGenerator"/>. Generation passes read
/// from here — there are no magic numbers inside the pass methods, so the map
/// can be re-tuned without changing code.
/// </summary>
public sealed class MapGenConfig
{
    /// <summary>Grid width in cells.</summary>
    public int Width { get; init; } = 128;

    /// <summary>Grid depth in cells.</summary>
    public int Depth { get; init; } = 128;

    /// <summary>World units per cell.</summary>
    public float CellSize { get; init; } = 1.0f;

    /// <summary>Number of lakes to carve.</summary>
    public int LakeCount { get; init; } = 1;

    /// <summary>Radius of each lake, in cells (Euclidean).</summary>
    public int LakeRadius { get; init; } = 12;

    /// <summary>Number of rock clusters to scatter.</summary>
    public int RockClusters { get; init; } = 8;

    /// <summary>Steps in each rock cluster's random walk (cells per cluster).</summary>
    public int RockClusterSize { get; init; } = 5;

    /// <summary>
    /// Number of biome seed points scattered for the Voronoi region assignment.
    /// More seeds → smaller, more numerous biome patches. Per-biome rules live in
    /// the <see cref="BiomeCatalog"/> data file, not here.
    /// </summary>
    public int BiomeSeedCount { get; init; } = 6;

    /// <summary>
    /// Optional subset of biomes to choose from during region assignment.
    /// <c>null</c> (default) uses all <see cref="Biome"/> values. When set, only
    /// the listed biomes appear in the generated map.
    /// </summary>
    public IReadOnlySet<Biome>? BiomeNames { get; init; }

    // --- terrain height (SculptHeight pass) ---
    // Global hill-shape knobs. (Per-biome amplitude could later move into BiomeDef
    // for a "Mountains" biome — data-only — but is not implemented yet.)

    /// <summary>
    /// Peak elevation in world units. Heights range roughly [-Amplitude, +Amplitude],
    /// so some terrain sits below y=0 (and, after <c>FloodWater</c>, fills with water).
    /// </summary>
    public float HeightAmplitude { get; init; } = 6f;

    /// <summary>
    /// Noise feature size in cells — the approximate spacing between hilltops. Larger
    /// = broader, gentler hills; smaller = tighter, busier terrain.
    /// </summary>
    public float HeightScale { get; init; } = 24f;

    /// <summary>
    /// Number of fBm octaves layered for height detail. More octaves add finer bumps
    /// on top of the broad shape (diminishing returns past ~5).
    /// </summary>
    public int HeightOctaves { get; init; } = 4;

    /// <summary>
    /// Water line in world units. Cells whose <see cref="Cell.Height"/> is below this
    /// become <see cref="Terrain.Water"/> (basins/ponds) in the <c>FloodWater</c> pass.
    /// </summary>
    public float SeaLevel { get; init; } = 0f;
}
