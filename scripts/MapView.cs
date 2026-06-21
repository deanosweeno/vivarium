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
    /// Build the single smooth ground surface. One vertex sits at each cell center,
    /// lifted to that cell's baked <see cref="Cell.Height"/>; adjacent cells are joined
    /// into quads (two triangles each). Each vertex is colored by its cell
    /// (biome <see cref="BiomeDef.TintHex"/> for grass, grey for rock, a muted lakebed
    /// tone for water), and <c>GenerateNormals</c> gives smooth shading — so biomes blend
    /// across the surface instead of forming hard squares.
    /// </summary>
    private void BuildTerrainMesh(MapData map)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        // Iterate over quads formed by four neighboring cell centers.
        for (int cz = 0; cz < map.Depth - 1; cz++)
        for (int cx = 0; cx < map.Width - 1; cx++)
        {
            // Corner cells of this quad.
            AddQuad(st, map, cx, cz);
        }

        st.GenerateNormals();

        var material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
        };

        var instance = new MeshInstance3D
        {
            Mesh = st.Commit(),
            MaterialOverride = material,
            Name = "TerrainSurface",
        };
        AddChild(instance);
    }

    /// <summary>Emit the two triangles of the quad whose lower-left corner is cell (cx, cz).</summary>
    private void AddQuad(SurfaceTool st, MapData map, int cx, int cz)
    {
        // Four corners (cell centers).
        EmitVertex(st, map, cx, cz);
        EmitVertex(st, map, cx + 1, cz);
        EmitVertex(st, map, cx, cz + 1);

        EmitVertex(st, map, cx + 1, cz);
        EmitVertex(st, map, cx + 1, cz + 1);
        EmitVertex(st, map, cx, cz + 1);
    }

    /// <summary>Add one mesh vertex at the given cell's center + baked height, colored by the cell.</summary>
    private void EmitVertex(SurfaceTool st, MapData map, int cx, int cz)
    {
        var cell = map.GetCell(cx, cz);
        SNVector3 c = map.CellToWorldCenter(cx, cz);
        st.SetColor(CellColor(cell));
        st.AddVertex(new Vector3(c.X, cell.Height, c.Z));
    }

    /// <summary>Surface color for a cell: biome tint for grass, grey for rock, muted lakebed for water.</summary>
    private Color CellColor(Cell cell)
    {
        return cell.Terrain switch
        {
            Terrain.Rock => new Color(0.5f, 0.48f, 0.45f),
            Terrain.Water => new Color(0.18f, 0.32f, 0.4f), // lakebed under the water plane
            _ => Color.FromHtml(Biomes.Get(cell.Biome).TintHex),
        };
    }

    /// <summary>
    /// One translucent flat plane at <see cref="_seaLevel"/> spanning the whole map. Land
    /// rises above it and occludes it; basins (below sea level) show through as water.
    /// </summary>
    private void BuildWaterPlane(MapData map)
    {
        var material = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.45f, 0.8f, 0.6f),
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
