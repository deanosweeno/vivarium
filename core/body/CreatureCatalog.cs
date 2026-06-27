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
    private readonly Dictionary<string, CreatureDef> _defs;

    private CreatureCatalog(Dictionary<string, CreatureDef> defs) => _defs = defs;

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

        var defs = new Dictionary<string, CreatureDef>(StringComparer.OrdinalIgnoreCase);
        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Id))
                continue; // an id is required to reference the entry — skip rather than throw

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

            var body = new BodyPlan
            {
                Id = dto.Id,
                Name = dto.Name ?? dto.Id,
                BaseScale = dto.BaseScale ?? 1f,
                PrimaryHex = dto.PrimaryHex ?? "#FFFFFF",
                SecondaryHex = dto.SecondaryHex ?? "#DDDDDD",
                Parts = parts,
            };

            defs[dto.Id] = new CreatureDef
            {
                Id = dto.Id,
                Body = body,
                Traits = BuildTraits(dto.Traits),
                Drives = BuildDrives(dto.Drives),
                Herd = BuildHerd(dto.Herd),
            };
        }

        return new CreatureCatalog(defs);
    }

    /// <summary>Load a catalog from a JSON file path.</summary>
    public static CreatureCatalog Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>A catalog with no entries — every lookup returns null.</summary>
    public static CreatureCatalog Empty => new(new Dictionary<string, CreatureDef>(StringComparer.OrdinalIgnoreCase));

    /// <summary>The ids of every entry in the catalog.</summary>
    public IReadOnlyCollection<string> Ids => _defs.Keys;

    /// <summary>
    /// Look up a creature type's body plan by id, or null if absent. Never throws — referencing
    /// a plan before its JSON entry exists is safe (the caller can fall back to a cube visual).
    /// </summary>
    public BodyPlan? Get(string id) => GetDef(id)?.Body;

    /// <summary>
    /// Look up the full creature definition (body + optional sim rules) by id, or null if absent.
    /// </summary>
    public CreatureDef? GetDef(string id)
        => !string.IsNullOrEmpty(id) && _defs.TryGetValue(id, out var def) ? def : null;

    private static Vector3 ToVec(float[]? a, Vector3 fallback)
        => a is { Length: 3 } ? new Vector3(a[0], a[1], a[2]) : fallback;

    // Each section is optional; absent → null (caller uses defaults). Within a present section,
    // individual fields fall back to the type default, so a JSON entry only states what it overrides.
    private static CreatureTraits? BuildTraits(TraitsDto? d)
    {
        if (d is null) return null;
        var t = new CreatureTraits();
        if (d.MaxSpeed is { } ms) t.MaxSpeed = ms;
        if (d.JumpHeight is { } jh) t.JumpHeight = jh;
        if (d.Acceleration is { } ac) t.Acceleration = ac;
        if (d.TurnRate is { } tr) t.TurnRate = tr;
        if (d.Radius is { } r) t.Radius = r;
        if (d.GravityScale is { } gs) t.GravityScale = gs;
        if (d.CanFly is { } cf) t.CanFly = cf;
        if (d.MaxFlyHeight is { } mfh) t.MaxFlyHeight = mfh;
        if (d.PreferredBiomes is { } pb) t.PreferredBiomes = pb;
        if (d.FatigueGainPerSec is { } fg) t.FatigueGainPerSec = fg;
        if (d.FatigueRecoverPerSec is { } fr) t.FatigueRecoverPerSec = fr;
        if (d.Diet is { } diet) t.Diet = new HashSet<string>(diet, StringComparer.OrdinalIgnoreCase);
        if (d.GrazeHungerThreshold is { } gh) t.GrazeHungerThreshold = gh;
        return t;
    }

    private static Drives? BuildDrives(DrivesDto? d)
    {
        if (d is null) return null;
        var dr = new Drives();
        if (d.Curiosity is { } c) dr.Curiosity = c;
        if (d.Fear is { } f) dr.Fear = f;
        if (d.Sociability is { } s) dr.Sociability = s;
        if (d.PlayCuddle is { } pc) dr.PlayCuddle = pc;
        if (d.Appetite is { } a) dr.Appetite = a;
        if (d.Aggression is { } ag) dr.Aggression = ag;
        return dr;
    }

    private static HerdSpawnConfig? BuildHerd(HerdDto? d)
    {
        if (d is null) return null;
        return new HerdSpawnConfig
        {
            HerdCount = d.HerdCount ?? 3,
            MinHerdSeparation = d.MinHerdSeparation ?? 18f,
            HerdJitter = d.HerdJitter ?? 2f,
            HerdSizeMin = d.HerdSizeMin ?? 4,
            HerdSizeMax = d.HerdSizeMax ?? 6,
            Biome = d.Biome ?? Biome.Plains,
            JitterNeeds = d.JitterNeeds ?? true,
        };
    }

    // Nullable fields so "absent in JSON" is distinguishable from "present as default".
    private sealed class PlanDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public float? BaseScale { get; set; }
        public string? PrimaryHex { get; set; }
        public string? SecondaryHex { get; set; }
        public List<PartDto>? Parts { get; set; }
        public TraitsDto? Traits { get; set; }
        public DrivesDto? Drives { get; set; }
        public HerdDto? Herd { get; set; }
    }

    private sealed class TraitsDto
    {
        public float? MaxSpeed { get; set; }
        public float? JumpHeight { get; set; }
        public float? Acceleration { get; set; }
        public float? TurnRate { get; set; }
        public float? Radius { get; set; }
        public float? GravityScale { get; set; }
        public bool? CanFly { get; set; }
        public float? MaxFlyHeight { get; set; }
        public List<string>? PreferredBiomes { get; set; }
        public float? FatigueGainPerSec { get; set; }
        public float? FatigueRecoverPerSec { get; set; }
        public List<string>? Diet { get; set; }
        public float? GrazeHungerThreshold { get; set; }
    }

    private sealed class DrivesDto
    {
        public float? Curiosity { get; set; }
        public float? Fear { get; set; }
        public float? Sociability { get; set; }
        public float? PlayCuddle { get; set; }
        public float? Appetite { get; set; }
        public float? Aggression { get; set; }
    }

    private sealed class HerdDto
    {
        public int? HerdCount { get; set; }
        public float? MinHerdSeparation { get; set; }
        public float? HerdJitter { get; set; }
        public int? HerdSizeMin { get; set; }
        public int? HerdSizeMax { get; set; }
        public Biome? Biome { get; set; }
        public bool? JitterNeeds { get; set; }
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
