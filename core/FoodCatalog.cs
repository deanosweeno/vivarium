using System.Text.Json;

namespace Vivarium.Core;

/// <summary>
/// The set of <see cref="FoodDef"/> food types loaded from data (<c>assets/foods.json</c>).
/// Both the simulator (spawning, nutrition) and the Godot layer (color) read their per-type
/// numbers from here, so food types can be added or re-tuned without touching code.
///
/// Mirrors <see cref="BiomeCatalog"/>'s two seams:
/// <list type="bullet">
/// <item><see cref="Parse"/> takes a JSON string — used by the Godot layer (reads bytes via
/// its own FileAccess, works inside an exported package).</item>
/// <item><see cref="Load"/> takes a filesystem path — used by offline tooling/tests.</item>
/// </list>
/// </summary>
public sealed class FoodCatalog
{
    private readonly Dictionary<string, FoodDef> _defs;

    private FoodCatalog(Dictionary<string, FoodDef> defs) => _defs = defs;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parse a catalog from a JSON array of food-type objects. Entries without an
    /// <c>Id</c> are skipped (forgiving); missing fields fall back to <see cref="FoodDef"/>
    /// defaults and unknown fields are ignored, so data files stay forward-compatible.
    /// </summary>
    public static FoodCatalog Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<FoodDto>>(json, Options)
            ?? throw new InvalidDataException("Food JSON deserialized to null.");

        var defs = new Dictionary<string, FoodDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
                continue; // an id is required to reference the type — skip rather than throw

            var fallback = FoodDef.Neutral(dto.Id);
            defs[dto.Id] = new FoodDef
            {
                Id = dto.Id,
                Name = dto.Name ?? fallback.Name,
                Nutrition = dto.Nutrition ?? fallback.Nutrition,
                GrazeRate = dto.GrazeRate ?? fallback.GrazeRate,
                RespawnSeconds = dto.RespawnSeconds ?? fallback.RespawnSeconds,
                ColorHex = dto.ColorHex ?? fallback.ColorHex,
            };
        }

        return new FoodCatalog(defs);
    }

    /// <summary>Load a catalog from a JSON file path.</summary>
    public static FoodCatalog Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// A catalog with no entries — every <see cref="Get"/> returns a neutral default.
    /// Lets the simulator run with no data file (e.g. in unit tests).
    /// </summary>
    public static FoodCatalog Empty => new(new Dictionary<string, FoodDef>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Look up a food type by id. Never throws: an unknown/empty id returns a neutral
    /// default, so referencing a type before its JSON entry exists is safe.
    /// </summary>
    public FoodDef Get(string id)
        => !string.IsNullOrEmpty(id) && _defs.TryGetValue(id, out var def) ? def : FoodDef.Neutral(id);

    // Nullable fields so "absent in JSON" is distinguishable from "present as 0",
    // letting missing fields fall back to FoodDef defaults (forward-compatible).
    private sealed class FoodDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public float? Nutrition { get; set; }
        public float? GrazeRate { get; set; }
        public float? RespawnSeconds { get; set; }
        public string? ColorHex { get; set; }
    }
}
