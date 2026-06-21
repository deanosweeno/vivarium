using System.Text.Json;

namespace Vivarium.Core;

/// <summary>
/// Saves and loads <see cref="MapData"/> as JSON. This is the only map I/O the
/// runtime touches (generate-then-freeze: the game loads a baked file, it never
/// runs <see cref="MapGenerator"/>).
///
/// Two seams are provided:
/// <list type="bullet">
/// <item><see cref="Save"/>/<see cref="Load"/> work with a filesystem path —
/// used by offline tooling.</item>
/// <item><see cref="Serialize"/>/<see cref="Deserialize"/> work with a JSON
/// string — used by the Godot presentation layer, which reads bytes via its own
/// FileAccess (so it works inside an exported package where res:// is not a real
/// filesystem path).</item>
/// </list>
/// Terrain and Resource are stored as their integer enum values, so the on-disk
/// format stays stable as long as the enum numbers do not change.
/// </summary>
public static class MapStorage
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    /// <summary>Serialize a map to a JSON string.</summary>
    public static string Serialize(MapData map)
    {
        var dto = new MapDto
        {
            Width = map.Width,
            Depth = map.Depth,
            CellSize = map.CellSize,
            SeaLevel = map.SeaLevel,
            Cells = new CellDto[map.Width * map.Depth],
        };

        for (int cz = 0; cz < map.Depth; cz++)
        for (int cx = 0; cx < map.Width; cx++)
        {
            var cell = map.GetCell(cx, cz);
            dto.Cells[cz * map.Width + cx] = new CellDto
            {
                Terrain = (int)cell.Terrain,
                Resource = (int)cell.Resource,
                Biome = (int)cell.Biome,
                Height = cell.Height,
            };
        }

        return JsonSerializer.Serialize(dto, Options);
    }

    /// <summary>Reconstruct a map from a JSON string produced by <see cref="Serialize"/>.</summary>
    public static MapData Deserialize(string json)
    {
        var dto = JsonSerializer.Deserialize<MapDto>(json, Options)
            ?? throw new InvalidDataException("Map JSON deserialized to null.");

        if (dto.Cells is null || dto.Cells.Length != dto.Width * dto.Depth)
            throw new InvalidDataException(
                $"Map JSON cell count {dto.Cells?.Length ?? 0} does not match " +
                $"{dto.Width}x{dto.Depth}.");

        var map = new MapData(dto.Width, dto.Depth, dto.CellSize)
        {
            SeaLevel = dto.SeaLevel,
        };
        for (int cz = 0; cz < dto.Depth; cz++)
        for (int cx = 0; cx < dto.Width; cx++)
        {
            var c = dto.Cells[cz * dto.Width + cx];
            map.SetCell(cx, cz, new Cell
            {
                Terrain = (Terrain)c.Terrain,
                Resource = (Resource)c.Resource,
                Biome = (Biome)c.Biome,
                Height = c.Height,
            });
        }

        return map;
    }

    /// <summary>Save a map to the given file path as JSON.</summary>
    public static void Save(MapData map, string path)
        => File.WriteAllText(path, Serialize(map));

    /// <summary>Load a map from the given JSON file path.</summary>
    public static MapData Load(string path)
        => Deserialize(File.ReadAllText(path));

    // Plain DTOs for System.Text.Json (properties, so default serialization works).
    private sealed class MapDto
    {
        public int Width { get; set; }
        public int Depth { get; set; }
        public float CellSize { get; set; }
        // Absent in pre-height maps → defaults to 0 (flat baseline), so old files still load.
        public float SeaLevel { get; set; }
        public CellDto[]? Cells { get; set; }
    }

    private sealed class CellDto
    {
        public int Terrain { get; set; }
        public int Resource { get; set; }
        // Absent in pre-biome maps → defaults to 0 (Biome.Plains), so old files still load.
        public int Biome { get; set; }
        // Absent in pre-height maps → defaults to 0 (flat floor), so old files still load.
        public float Height { get; set; }
    }
}
