using System.Text.Json;

namespace Vivarium.Core;

/// <summary>
/// The set of <see cref="BiomeDef"/> rules loaded from data (<c>assets/biomes.json</c>).
/// Both the generator and the runtime read their per-biome numbers from here, so
/// biomes can be added or re-tuned without touching code.
///
/// Mirrors <see cref="MapStorage"/>'s two seams:
/// <list type="bullet">
/// <item><see cref="Parse"/> takes a JSON string — used by the Godot layer, which reads
/// bytes via its own FileAccess (works inside an exported package).</item>
/// <item><see cref="Load"/> takes a filesystem path — used by offline tooling.</item>
/// </list>
/// </summary>
public sealed class BiomeCatalog
{
    private readonly Dictionary<Biome, BiomeDef> _defs;

    private BiomeCatalog(Dictionary<Biome, BiomeDef> defs) => _defs = defs;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parse a catalog from a JSON array of biome objects. Entries whose
    /// <c>Biome</c> name is not a known <see cref="Biome"/> value are skipped
    /// (forgiving: lets the data file stay ahead of or behind the enum).
    /// </summary>
    public static BiomeCatalog Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<BiomeDto>>(json, Options)
            ?? throw new InvalidDataException("Biome JSON deserialized to null.");

        var defs = new Dictionary<Biome, BiomeDef>();
        foreach (var dto in dtos)
        {
            if (dto.Biome is null || !Enum.TryParse<Biome>(dto.Biome, ignoreCase: true, out var biome))
                continue; // unknown biome name — ignore rather than throw

            var fallback = BiomeDef.Neutral(biome);
            defs[biome] = new BiomeDef
            {
                Biome = biome,
                Name = dto.Name ?? biome.ToString(),
                WaterChance = dto.WaterChance ?? fallback.WaterChance,
                RockChance = dto.RockChance ?? fallback.RockChance,
                FoodChance = dto.FoodChance ?? fallback.FoodChance,
                HappinessRate = dto.HappinessRate ?? fallback.HappinessRate,
                SpeedMultiplier = dto.SpeedMultiplier ?? fallback.SpeedMultiplier,
                TintHex = dto.TintHex ?? fallback.TintHex,
                HeightOffset = dto.HeightOffset ?? fallback.HeightOffset,
                HeightVariation = dto.HeightVariation ?? fallback.HeightVariation,
            };
        }

        return new BiomeCatalog(defs);
    }

    /// <summary>Load a catalog from a JSON file path.</summary>
    public static BiomeCatalog Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>
    /// A catalog with no entries — every <see cref="Get"/> returns a neutral default.
    /// Lets the generator/simulator run with no data file (e.g. in unit tests).
    /// </summary>
    public static BiomeCatalog Empty => new(new Dictionary<Biome, BiomeDef>());

    /// <summary>
    /// Look up the rules for a biome. Never throws: a biome missing from the data
    /// returns a neutral default, so adding an enum value before its JSON entry is safe.
    /// </summary>
    public BiomeDef Get(Biome biome)
        => _defs.TryGetValue(biome, out var def) ? def : BiomeDef.Neutral(biome);

    /// <summary>
    /// Create a new catalog with per-biome height overrides applied on top of the
    /// current definitions. Both parameters are optional — pass only the ones you
    /// need. The original catalog is unchanged (immutable pattern).
    /// </summary>
    public BiomeCatalog WithOverrides(
        Dictionary<Biome, float>? offsets = null,
        Dictionary<Biome, float>? variations = null)
    {
        var newDefs = new Dictionary<Biome, BiomeDef>(_defs);

        if (offsets is { Count: > 0 })
        {
            foreach (var (biome, offset) in offsets)
            {
                var existing = Get(biome);
                newDefs[biome] = new BiomeDef
                {
                    Biome = biome,
                    Name = existing.Name,
                    WaterChance = existing.WaterChance,
                    RockChance = existing.RockChance,
                    FoodChance = existing.FoodChance,
                    HappinessRate = existing.HappinessRate,
                    SpeedMultiplier = existing.SpeedMultiplier,
                    TintHex = existing.TintHex,
                    HeightOffset = offset,
                    HeightVariation = existing.HeightVariation,
                };
            }
        }

        if (variations is { Count: > 0 })
        {
            foreach (var (biome, variation) in variations)
            {
                var existing = Get(biome);
                newDefs[biome] = new BiomeDef
                {
                    Biome = biome,
                    Name = existing.Name,
                    WaterChance = existing.WaterChance,
                    RockChance = existing.RockChance,
                    FoodChance = existing.FoodChance,
                    HappinessRate = existing.HappinessRate,
                    SpeedMultiplier = existing.SpeedMultiplier,
                    TintHex = existing.TintHex,
                    HeightOffset = offsets is { Count: > 0 } && offsets.TryGetValue(biome, out var o)
                        ? o : existing.HeightOffset,
                    HeightVariation = variation,
                };
            }
        }

        return new BiomeCatalog(newDefs);
    }

    // Nullable fields so "absent in JSON" is distinguishable from "present as 0",
    // letting missing fields fall back to BiomeDef defaults (forward-compatible).
    private sealed class BiomeDto
    {
        public string? Biome { get; set; }
        public string? Name { get; set; }
        public float? WaterChance { get; set; }
        public float? RockChance { get; set; }
        public float? FoodChance { get; set; }
        public float? HappinessRate { get; set; }
        public float? SpeedMultiplier { get; set; }
        public string? TintHex { get; set; }
        public float? HeightOffset { get; set; }
        public float? HeightVariation { get; set; }
    }
}
