using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vivarium.Core;

/// <summary>
/// The set of <see cref="BodyPlan"/> creature body plans loaded from data
/// (<c>assets/creatures.json</c>). The Godot layer reads a plan by id to assemble + animate
/// a creature, so new creatures and re-tuned proportions are a data change, not a code change.
///
/// Mirrors <see cref="FoodCatalog"/>'s two seams:
/// <list type="bullet">
/// <item><see cref="Parse"/> takes a JSON string — used by the Godot layer (reads bytes via
/// its own FileAccess, works inside an exported package).</item>
/// <item><see cref="Load"/> takes a filesystem path — used by offline tooling/tests.</item>
/// </list>
/// </summary>
public sealed class CreatureCatalog
{
    private readonly Dictionary<string, BodyPlan> _plans;

    private CreatureCatalog(Dictionary<string, BodyPlan> plans) => _plans = plans;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>
    /// Parse a catalog from a JSON array of creature body-plan objects. Entries without an
    /// <c>Id</c> are skipped (forgiving); missing fields fall back to defaults and unknown
    /// fields are ignored, so data files stay forward-compatible.
    /// </summary>
    public static CreatureCatalog Parse(string json)
    {
        var dtos = JsonSerializer.Deserialize<List<PlanDto>>(json, Options)
            ?? throw new InvalidDataException("Creature JSON deserialized to null.");

        var plans = new Dictionary<string, BodyPlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
                continue; // an id is required to reference the plan — skip rather than throw

            var parts = new List<BodyPart>();
            if (dto.Parts != null)
            {
                foreach (var p in dto.Parts)
                {
                    parts.Add(new BodyPart
                    {
                        Slot = p.Slot ?? PartSlot.Core,
                        Shape = p.Shape ?? ShapePrimitive.Sphere,
                        Size = ToVec(p.Size, Vector3.One),
                        Socket = ToVec(p.Socket, Vector3.Zero),
                        Tint = p.Tint ?? "#FFFFFF",
                        Role = p.Role ?? AnimRole.Static,
                        Phase = p.Phase ?? 0f,
                        Freq = p.Freq ?? 0f,
                    });
                }
            }

            plans[dto.Id] = new BodyPlan
            {
                Id = dto.Id,
                Name = dto.Name ?? dto.Id,
                BaseScale = dto.BaseScale ?? 1f,
                PrimaryHex = dto.PrimaryHex ?? "#FFFFFF",
                SecondaryHex = dto.SecondaryHex ?? "#DDDDDD",
                Parts = parts,
            };
        }

        return new CreatureCatalog(plans);
    }

    /// <summary>Load a catalog from a JSON file path.</summary>
    public static CreatureCatalog Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>A catalog with no entries — every <see cref="Get"/> returns null.</summary>
    public static CreatureCatalog Empty => new(new Dictionary<string, BodyPlan>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The ids of every plan in the catalog.</summary>
    public IReadOnlyCollection<string> Ids => _plans.Keys;

    /// <summary>
    /// Look up a body plan by id, or null if absent. Never throws — referencing a plan before
    /// its JSON entry exists is safe (the caller can fall back to a cube visual).
    /// </summary>
    public BodyPlan? Get(string id)
        => !string.IsNullOrEmpty(id) && _plans.TryGetValue(id, out var plan) ? plan : null;

    private static Vector3 ToVec(float[]? a, Vector3 fallback)
        => a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : fallback;

    // Nullable fields so "absent in JSON" is distinguishable from "present as default".
    private sealed class PlanDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public float? BaseScale { get; set; }
        public string? PrimaryHex { get; set; }
        public string? SecondaryHex { get; set; }
        public List<PartDto>? Parts { get; set; }
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
}
