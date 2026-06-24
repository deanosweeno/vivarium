using Godot;
using Vivarium.Core;
using SNVector3 = System.Numerics.Vector3;
using FileAccess = Godot.FileAccess;

namespace Vivarium.Scripts;

/// <summary>
/// Presentation for the baked terrain map. Loads a frozen <see cref="MapData"/>
/// (generate-then-freeze: the game never runs the generator) and draws it.
///
/// This is a thin view: it reads <see cref="MapData"/> and renders it, owning no
/// map rules. Rendering builds a single smooth, deformed ground mesh from the baked
/// per-cell heights (vertex-colored by biome/terrain) plus one translucent water
/// plane at sea level — so terrain reads as continuous rolling hills, not square tiles.
/// </summary>
public partial class MapView : Node3D
{
    /// <summary>res:// path to the baked map JSON asset.</summary>
    [Export] private string _mapPath = "res://assets/maps/default_map.json";

    /// <summary>res:// path to the biome rules JSON asset.</summary>
    [Export] private string _biomesPath = "res://assets/biomes.json";

    /// <summary>The loaded map, or null if loading failed. Read after _Ready.</summary>
    public MapData? Map { get; private set; }

    /// <summary>The loaded biome catalog (never null; empty if loading failed).</summary>
    public BiomeCatalog Biomes { get; private set; } = BiomeCatalog.Empty;

    /// <summary>World width of the map (Width * CellSize), 0 if not loaded.</summary>
    public float WorldWidth => Map is null ? 0f : Map.Width * Map.CellSize;

    /// <summary>World depth of the map (Depth * CellSize), 0 if not loaded.</summary>
    public float WorldDepth => Map is null ? 0f : Map.Depth * Map.CellSize;

    public override void _Ready()
    {
        Biomes = LoadBiomes(_biomesPath);
        Map = LoadMap(_mapPath);
        if (Map is null)
            return;

        BuildTerrainMesh(Map);
        BuildWaterPlane(Map);
    }

    /// <summary>
    /// Load the biome catalog through Godot's FileAccess (string seam). Falls back
    /// to an empty catalog (neutral colors) if the file is missing or invalid.
    /// </summary>
    private static BiomeCatalog LoadBiomes(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PrintErr($"MapView: could not open biomes at '{path}' ({FileAccess.GetOpenError()}); using neutral biomes.");
            return BiomeCatalog.Empty;
        }

        try
        {
            return BiomeCatalog.Parse(file.GetAsText());
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"MapView: failed to parse biomes '{path}': {e.Message}");
            return BiomeCatalog.Empty;
        }
    }

    /// <summary>
    /// Load and deserialize the baked map. Reads bytes through Godot's FileAccess
    /// (works inside an exported .pck where res:// is not a real filesystem path)
    /// and hands the JSON string to the engine-agnostic <see cref="MapStorage"/>.
    /// </summary>
    private static MapData? LoadMap(string path)
    {
        using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PrintErr($"MapView: could not open map at '{path}' ({FileAccess.GetOpenError()})");
            return null;
        }

        string json = file.GetAsText();
        try
        {
            return MapStorage.Deserialize(json);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"MapView: failed to parse map '{path}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Build the single smooth ground surface. Each cell quad is subdivided
    /// <see cref="SubdivPerCell"/>× per axis; sub-vertices sample the smooth
    /// <see cref="MapData.HeightAt"/> field for elevation and a bilinearly-blended
    /// <see cref="ColorAt"/> for color (biome <see cref="BiomeDef.GrassHex"/> for grass,
    /// grey for rock, a muted lakebed tone for water). <c>GenerateNormals</c> gives smooth
    /// shading — so hills and biome borders read as continuous gradients, not coarse facets.
    /// </summary>
    /// <summary>
    /// Sub-quads emitted per cell along each axis. Each cell quad is divided into
    /// SubdivPerCell × SubdivPerCell smaller quads whose vertices are sampled from the
    /// smooth <see cref="MapData.HeightAt"/> field and a bilinearly-blended color, so
    /// hill silhouettes and biome color borders read smoothly instead of stairstepping
    /// along the coarse cell grid. Purely cosmetic — the simulation grid is untouched.
    /// </summary>
    private const int SubdivPerCell = 4;

    private void BuildTerrainMesh(MapData map)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Fractional cell-center coordinates run 0 .. Width-1 (the span between the
        // first and last cell centers), stepped SubdivPerCell times per cell.
        int stepsX = (map.Width - 1) * SubdivPerCell;
        int stepsZ = (map.Depth - 1) * SubdivPerCell;
        float inv = 1f / SubdivPerCell;

        for (int j = 0; j < stepsZ; j++)
        for (int i = 0; i < stepsX; i++)
        {
            float fx0 = i * inv, fx1 = (i + 1) * inv;
            float fz0 = j * inv, fz1 = (j + 1) * inv;

            EmitSubVertex(st, map, fx0, fz0);
            EmitSubVertex(st, map, fx1, fz0);
            EmitSubVertex(st, map, fx0, fz1);

            EmitSubVertex(st, map, fx1, fz0);
            EmitSubVertex(st, map, fx1, fz1);
            EmitSubVertex(st, map, fx0, fz1);
        }

        st.GenerateNormals();

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
        };

        var instance = new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = material,
            Name = "TerrainSurface",
        };
        AddChild(instance);
    }

    /// <summary>
    /// Emit one mesh vertex at fractional cell-center coordinate (fx, fz), where integer
    /// values land exactly on cell centers. Height comes from the smooth
    /// <see cref="MapData.HeightAt"/> field and color from the bilinear <see cref="ColorAt"/>,
    /// so sub-vertices interpolate between cell centers with no seams.
    /// </summary>
    private void EmitSubVertex(SurfaceTool st, MapData map, float fx, float fz)
    {
        // Inverse of HeightAt's cell-center mapping: world = (frac - N/2 + 0.5) * CellSize.
        float wx = (fx - map.Width / 2f + 0.5f) * map.CellSize;
        float wz = (fz - map.Depth / 2f + 0.5f) * map.CellSize;

        st.SetColor(ColorAt(map, fx, fz));
        st.AddVertex(new Vector3(wx, map.HeightAt(new SNVector3(wx, 0f, wz)), wz));
    }

    /// <summary>
    /// Bilinearly blend the per-cell <see cref="CellColor"/> of the four cell centers
    /// surrounding fractional coordinate (fx, fz), clamping to the grid edge — the color
    /// analogue of <see cref="MapData.HeightAt"/>. Blending fades biome tints (and the
    /// rock/water tones at their edges) instead of stairstepping at cell boundaries.
    /// </summary>
    private Color ColorAt(MapData map, float fx, float fz)
    {
        int x0 = (int)Mathf.Floor(fx);
        int z0 = (int)Mathf.Floor(fz);
        float tx = fx - x0;
        float tz = fz - z0;

        Color c00 = ClampedCellColor(map, x0, z0);
        Color c10 = ClampedCellColor(map, x0 + 1, z0);
        Color c01 = ClampedCellColor(map, x0, z0 + 1);
        Color c11 = ClampedCellColor(map, x0 + 1, z0 + 1);

        Color top = c00.Lerp(c10, tx);
        Color bottom = c01.Lerp(c11, tx);
        return top.Lerp(bottom, tz);
    }

    /// <summary>Cell color with the coordinate clamped to the grid edge.</summary>
    private Color ClampedCellColor(MapData map, int cx, int cz)
    {
        cx = Mathf.Clamp(cx, 0, map.Width - 1);
        cz = Mathf.Clamp(cz, 0, map.Depth - 1);
        return CellColor(map.GetCell(cx, cz));
    }

    /// <summary>Surface color for a cell — all terrain colors come from the biome definition.</summary>
    private Color CellColor(Cell cell)
    {
        var b = Biomes.Get(cell.Biome);
        return cell.Terrain switch
        {
            Terrain.Rock => Color.FromHtml(b.RockHex),
            Terrain.Water => Color.FromHtml(b.WaterHex),
            Terrain.Sand => Color.FromHtml(b.SandHex),
            Terrain.Marsh => Color.FromHtml(b.MarshHex),
            _ => Color.FromHtml(b.GrassHex),
        };
    }

    /// <summary>
    /// One translucent flat plane at <see cref="_seaLevel"/> spanning the whole map. Land
    /// rises above it and occludes it; basins (below sea level) show through as water.
    /// </summary>
    private void BuildWaterPlane(MapData map)
    {
        var waterColor = Color.FromHtml(Biomes.Get(Biome.Plains).WaterHex);
        waterColor.A = 0.6f;
        var material = new StandardMaterial3D
        {
            AlbedoColor = waterColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.1f,
            Metallic = 0.2f,
        };

        var plane = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(WorldWidth, WorldDepth) },
            MaterialOverride = material,
            Position = new Vector3(0f, map.SeaLevel, 0f),
            Name = "WaterSurface",
        };
        AddChild(plane);
    }
}
