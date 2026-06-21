using System.Numerics;

namespace Vivarium.Core;

/// <summary>
/// Terrain type of a single map cell. Values are stable and serialized to
/// save files — add new types only by appending; never reorder or renumber.
/// </summary>
public enum Terrain
{
    /// <summary>Default, walkable ground.</summary>
    Grass = 0,
    /// <summary>Water — not walkable.</summary>
    Water = 1,
    /// <summary>Rock / obstacle — not walkable.</summary>
    Rock = 2,
    /// <summary>Sand — walkable, default terrain of Desert biomes.</summary>
    Sand = 3,
    /// <summary>Marsh — walkable, default terrain of Wetland biomes.</summary>
    Marsh = 4,
}

/// <summary>
/// Resource occupying a cell. Stubbed for now (placement is a later pass).
/// Values are stable and serialized — append only.
/// </summary>
public enum Resource
{
    /// <summary>No resource.</summary>
    None = 0,
    /// <summary>Food resource.</summary>
    Food = 1,
}

/// <summary>
/// A single map cell: a value type holding its terrain and any resource.
/// Walkability is NOT stored here — it is derived from <see cref="Terrain"/>
/// on <see cref="MapData.IsWalkable"/> so it can never drift out of sync.
/// </summary>
public struct Cell
{
    /// <summary>The terrain type of this cell.</summary>
    public Terrain Terrain;
    /// <summary>The resource occupying this cell (None for now).</summary>
    public Resource Resource;
    /// <summary>The biome region this cell belongs to.</summary>
    public Biome Biome;
    /// <summary>
    /// Terrain elevation at this cell's center, in world units (Y up). 0 = the
    /// original flat floor; positive = hills, negative = basins/below sea level.
    /// Defaults to 0, so maps without height data read as flat.
    /// </summary>
    public float Height;
}

/// <summary>
/// The terrain grid laid over the XZ floor of the world. Pure data plus
/// accessors — contains NO generation rules and NO Godot dependency.
///
/// The grid is centered on the world XZ origin so it aligns with the
/// origin-centered <see cref="Arena"/>. Y (up) is not part of the grid;
/// the world is flat.
///
/// Cells are stored in a single flat array indexed <c>cz * Width + cx</c>.
/// </summary>
public sealed class MapData
{
    private readonly Cell[] _cells;

    /// <summary>Number of cells along world X.</summary>
    public int Width { get; }

    /// <summary>Number of cells along world Z.</summary>
    public int Depth { get; }

    /// <summary>World units per cell (each cell is CellSize × CellSize).</summary>
    public float CellSize { get; }

    /// <summary>
    /// The water-surface elevation in world units (Y up). Cells below it were flooded
    /// at generation; the presentation layer draws the water plane at this height.
    /// Baked with the map so the renderer can't drift from the generated water line.
    /// Defaults to 0 (the original flat-floor baseline).
    /// </summary>
    public float SeaLevel { get; set; }

    /// <summary>
    /// Create a map of the given dimensions with every cell initialized to
    /// <see cref="Terrain.Grass"/> and <see cref="Resource.None"/>.
    /// </summary>
    public MapData(int width, int depth, float cellSize)
    {
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth));
        if (cellSize <= 0f)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        Width = width;
        Depth = depth;
        CellSize = cellSize;
        _cells = new Cell[width * depth];
        // new Cell[] is already Grass(0)/None(0)/Plains(0); left explicit for clarity.
        for (int i = 0; i < _cells.Length; i++)
            _cells[i] = new Cell
            {
                Terrain = Terrain.Grass,
                Resource = Resource.None,
                Biome = Biome.Plains,
                Height = 0f,
            };
    }

    /// <summary>True if the cell coordinate is inside the grid.</summary>
    public bool InBounds(int cx, int cz)
        => cx >= 0 && cx < Width && cz >= 0 && cz < Depth;

    /// <summary>Get the cell at (cx, cz). Throws if out of bounds.</summary>
    public Cell GetCell(int cx, int cz)
    {
        if (!InBounds(cx, cz))
            throw new ArgumentOutOfRangeException(
                nameof(cx), $"Cell ({cx},{cz}) is outside {Width}x{Depth}.");
        return _cells[cz * Width + cx];
    }

    /// <summary>Set the cell at (cx, cz). Throws if out of bounds.</summary>
    public void SetCell(int cx, int cz, Cell cell)
    {
        if (!InBounds(cx, cz))
            throw new ArgumentOutOfRangeException(
                nameof(cx), $"Cell ({cx},{cz}) is outside {Width}x{Depth}.");
        _cells[cz * Width + cx] = cell;
    }

    /// <summary>
    /// Derived walkability: a cell is walkable iff its terrain is Grass, Sand,
    /// or Marsh. Water and Rock are not. Never stored — always computed from
    /// the cell's terrain, so a terrain enum addition is the single place to
    /// declare walkability.
    /// </summary>
    public bool IsWalkable(int cx, int cz)
        => GetCell(cx, cz).Terrain is Terrain.Grass or Terrain.Sand or Terrain.Marsh;

    /// <summary>The biome of the cell at (cx, cz). Throws if out of bounds.</summary>
    public Biome GetBiome(int cx, int cz) => GetCell(cx, cz).Biome;

    /// <summary>The elevation of the cell at (cx, cz), in world units. Throws if out of bounds.</summary>
    public float GetHeight(int cx, int cz) => GetCell(cx, cz).Height;

    /// <summary>
    /// Terrain elevation at a world position, <b>bilinearly interpolated</b> between
    /// the four nearest cell centers so the surface reads smooth rather than stepped.
    /// This is the single seam both the renderer and (later) the simulator read, so
    /// "what height is the ground here" is defined in exactly one place.
    ///
    /// Y of <paramref name="world"/> is ignored. Positions outside the grid clamp to the
    /// nearest edge cell's height (the surface extends flat past the border rather than
    /// dropping to a cliff), so callers need not bounds-check. Sampling at a cell center
    /// returns that cell's <see cref="Cell.Height"/> exactly.
    /// </summary>
    public float HeightAt(Vector3 world)
    {
        // Position in "cell-center space": integer coordinate i maps to cell i's center.
        float fx = world.X / CellSize + Width / 2f - 0.5f;
        float fz = world.Z / CellSize + Depth / 2f - 0.5f;

        int x0 = (int)MathF.Floor(fx);
        int z0 = (int)MathF.Floor(fz);
        float tx = fx - x0;
        float tz = fz - z0;

        // Sample the four surrounding centers, clamping to the edge so the surface
        // extends flat to the border instead of falling off.
        float h00 = SampleClamped(x0, z0);
        float h10 = SampleClamped(x0 + 1, z0);
        float h01 = SampleClamped(x0, z0 + 1);
        float h11 = SampleClamped(x0 + 1, z0 + 1);

        float top = h00 + (h10 - h00) * tx;
        float bottom = h01 + (h11 - h01) * tx;
        return top + (bottom - top) * tz;
    }

    /// <summary>Cell height with the coordinate clamped to the grid edge (0 if the grid is empty).</summary>
    private float SampleClamped(int cx, int cz)
    {
        cx = Math.Clamp(cx, 0, Width - 1);
        cz = Math.Clamp(cz, 0, Depth - 1);
        return _cells[cz * Width + cx].Height;
    }

    /// <summary>
    /// The biome at a world position. Returns <see cref="Biome.Plains"/> for
    /// positions outside the grid (so callers need not bounds-check).
    /// </summary>
    public Biome BiomeAt(Vector3 world)
    {
        var (cx, cz) = WorldToCell(world);
        return InBounds(cx, cz) ? GetCell(cx, cz).Biome : Biome.Plains;
    }

    /// <summary>
    /// Convert a world position to the cell coordinate that contains it.
    /// The grid is centered on the XZ origin. Y is ignored. The result is
    /// not bounds-checked — callers use <see cref="InBounds"/> if needed.
    /// </summary>
    public (int cx, int cz) WorldToCell(Vector3 world)
    {
        int cx = (int)MathF.Floor(world.X / CellSize + Width / 2f);
        int cz = (int)MathF.Floor(world.Z / CellSize + Depth / 2f);
        return (cx, cz);
    }

    /// <summary>
    /// Convert a cell coordinate to the world-space position of its center
    /// (Y = 0). Inverse of <see cref="WorldToCell"/> for in-bounds cells.
    /// </summary>
    public Vector3 CellToWorldCenter(int cx, int cz)
    {
        float x = (cx - Width / 2f + 0.5f) * CellSize;
        float z = (cz - Depth / 2f + 0.5f) * CellSize;
        return new Vector3(x, 0f, z);
    }
}
