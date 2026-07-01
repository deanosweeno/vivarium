using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vivarium.Core;

/// <summary>
/// The set of harvestable <see cref="Gene"/>s loaded from data (<c>assets/genes.json</c>) — the
/// §3 authoring source for both the harvest drop-table and the craft "full common set" gate.
/// Every catalog entry is <see cref="GeneKind.Specialty"/> (a <see cref="GeneKind.Base"/> gene is
/// only ever produced by <see cref="Craft"/>, never authored directly). Common-rarity entries are
/// the "raw material" a player must collect one of each to unlock crafting that species' base;
/// Rare/Legendary entries carry the interesting splice payload (parts/pins).
///
/// Mirrors <see cref="CreatureCatalog"/>'s two seams: <see cref="Parse"/> for the Godot layer,
/// <see cref="Load"/> for offline tooling/tests.
/// </summary>
public sealed class GeneCatalog
{
    private readonly Dictionary<string, List<Gene>> _bySpecies;

    private GeneCatalog(Dictionary<string, List<Gene>> bySpecies) => _bySpecies = bySpecies;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>
    /// Parse a catalog from a flat JSON array of gene objects. Entries without an <c>Id</c> or
    /// <c>SourceSpecies</c> are skipped (forgiving); unknown fields are ignored.
    /// </summary>
    public static GeneCatalog Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<GeneDto>>(json, Options)
            ?? throw new InvalidDataException("Gene JSON deserialized to null.");

        var bySpecies = new Dictionary<string, List<Gene>>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.SourceSpecies))
                continue; // both are required to attribute + reference the entry — skip rather than throw

            var gene = new Gene
            {
                Id = dto.Id,
                Kind = dto.Kind ?? GeneKind.Specialty,
                Rarity = dto.Rarity ?? Rarity.Common,
                Tier = dto.Tier ?? 0,
                Visible = dto.Visible ?? false,
                SourceSpecies = dto.SourceSpecies,
                Parts = dto.Parts?.Select(ToPart).ToList(),
                Pins = dto.Pins?.Select(ToPin).ToList(),
            };

            if (!bySpecies.TryGetValue(dto.SourceSpecies, out var list))
            {
                list = new List<Gene>();
                bySpecies[dto.SourceSpecies] = list;
            }
            list.Add(gene);
        }

        return new GeneCatalog(bySpecies);
    }

    /// <summary>Load a catalog from a JSON file path.</summary>
    public static GeneCatalog Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>A catalog with no entries — every lookup returns empty.</summary>
    public static GeneCatalog Empty => new(new Dictionary<string, List<Gene>>(StringComparer.OrdinalIgnoreCase));

    /// <summary>Every harvestable gene cataloged for a species, or empty if none.</summary>
    public IReadOnlyList<Gene> GenesFor(string species)
        => _bySpecies.TryGetValue(species, out var list) ? list : Array.Empty<Gene>();

    /// <summary>The distinct rarities a species actually owns entries for (drives harvest-odds normalization).</summary>
    public IReadOnlySet<Rarity> RaritiesOwned(string species)
        => GenesFor(species).Select(g => g.Rarity).ToHashSet();

    /// <summary>Look up a single catalog gene by id across all species, or null if absent.</summary>
    public Gene? FindById(string id)
        => _bySpecies.Values.SelectMany(g => g).FirstOrDefault(g => g.Id == id);

    private static BodyPart ToPart(PartDto p) => new()
    {
        Slot = p.Slot ?? PartSlot.Core,
        Shape = p.Shape ?? ShapePrimitive.Sphere,
        Size = ToVec(p.Size, Vector3.One),
        Socket = ToVec(p.Socket, Vector3.Zero),
        Tint = p.Tint ?? "#FFFFFF",
        Role = p.Role ?? AnimRole.Static,
        Phase = p.Phase ?? 0f,
        Freq = p.Freq ?? 0f,
    };

    private static StatPin ToPin(PinDto p) => new() { Key = p.Key ?? StatKey.MaxSpeed, Value = p.Value ?? 0f };

    private static Vector3 ToVec(float[]? a, Vector3 fallback)
        => a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : fallback;

    private sealed class GeneDto
    {
        public string? Id { get; set; }
        public GeneKind? Kind { get; set; }
        public Rarity? Rarity { get; set; }
        public int? Tier { get; set; }
        public bool? Visible { get; set; }
        public string? SourceSpecies { get; set; }
        public List<PartDto>? Parts { get; set; }
        public List<PinDto>? Pins { get; set; }
    }

    private sealed class PartDto
    {
        public PartSlot? Slot { get; set; }
        public ShapePrimitive? Shape { get; set; }
        public float[]? Size { get; set; }
        public float[]? Socket { get; set; }
        public string? Tint { get; set; }
        public AnimRole? Role { get; set; }
        public float? Phase { get; set; }
        public float? Freq { get; set; }
    }

    private sealed class PinDto
    {
        public StatKey? Key { get; set; }
        public float? Value { get; set; }
    }
}
