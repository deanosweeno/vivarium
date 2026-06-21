using Vivarium.Core;

// Offline "generate-then-freeze" tool. Generates a map from a seed + config and
// writes it to a JSON file via MapStorage. Prints an ASCII preview so you can
// eyeball candidate seeds before committing one as the shipped asset.
//
// Usage:
//   dotnet run --project tools/MapGenTool -- --seed 42 --out assets/maps/default_map.json
//   dotnet run --project tools/MapGenTool -- --seed 42            (preview only, no write)
//
// Flags (all optional except as noted):
//   --seed <int>        RNG seed (default 0)
//   --out <path>        output file; if omitted, only previews
//   --width <int>       grid width  (default 128)
//   --depth <int>       grid depth  (default 128)
//   --cellsize <float>  world units per cell (default 1.0)
//   --lakes <int>       lake count (default 1)
//   --lakeradius <int>  lake radius in cells (default 12)
//   --rocks <int>       rock cluster count (default 0)
//   --rocksize <int>    steps per rock cluster (default 5)
//   --biomeseeds <int>  biome region seed points (default 6)
//   --biome-names <csv> comma-separated biome names to include (default all)
//   --biomes <path>     biome rules JSON (default assets/biomes.json; neutral if missing)
//   --amplitude <float> peak terrain height in world units (default 6)
//   --heightscale <float> noise feature size in cells (default 24)
//   --octaves <int>     fBm height detail octaves (default 4)
//   --sealevel <float>  water line; cells below it flood (default 0)
//   --waterdepth <float> depth water cells sink below dry neighbors (default 1.5)
//   --height-offsets <kv>     per-biome height offsets, e.g. "Plains=1.0,Desert=-1.0"
//   --height-variations <kv>  per-biome height variation, e.g. "Plains=0.3,Desert=0.5"

var args2 = ParseArgs(args);

int seed = GetInt(args2, "seed", 0);
string? outPath = args2.TryGetValue("out", out var o) ? o : null;

var config = new MapGenConfig
{
    Width = GetInt(args2, "width", 128),
    Depth = GetInt(args2, "depth", 128),
    CellSize = GetFloat(args2, "cellsize", 1.0f),
    LakeCount = GetInt(args2, "lakes", 1),
    LakeRadius = GetInt(args2, "lakeradius", 12),
    RockClusters = GetInt(args2, "rocks", 0),
    RockClusterSize = GetInt(args2, "rocksize", 5),
    BiomeSeedCount = GetInt(args2, "biomeseeds", 6),
    BiomeNames = ParseBiomeNames(args2),
    HeightAmplitude = GetFloat(args2, "amplitude", 6f),
    HeightScale = GetFloat(args2, "heightscale", 24f),
    HeightOctaves = GetInt(args2, "octaves", 4),
    SeaLevel = GetFloat(args2, "sealevel", 0f),
    WaterDepth = GetFloat(args2, "waterdepth", 1.5f),
};

string biomesPath = args2.TryGetValue("biomes", out var bp) ? bp : "assets/biomes.json";
BiomeCatalog biomes;
if (File.Exists(biomesPath))
{
    biomes = BiomeCatalog.Load(biomesPath);
    Console.WriteLine($"Loaded biome rules from {Path.GetFullPath(biomesPath)}.");
}
else
{
    biomes = BiomeCatalog.Empty;
    Console.WriteLine($"No biome file at '{biomesPath}'; using neutral biome weights.");
}

if (config.BiomeNames is { Count: > 0 } set)
{
    var names = string.Join(", ", set.OrderBy(b => (int)b).Select(b => b.ToString()));
    Console.WriteLine($"Biome filter active: {names}.");
}

var heightOffsets = ParseBiomeFloatDict(args2, "height-offsets");
var heightVariations = ParseBiomeFloatDict(args2, "height-variations");
if (heightOffsets is { Count: > 0 } || heightVariations is { Count: > 0 })
{
    biomes = biomes.WithOverrides(offsets: heightOffsets, variations: heightVariations);
    PrintHeightOverrides(heightOffsets, heightVariations);
}

var map = MapGenerator.Generate(config, biomes, seed);

Console.WriteLine($"Generated {map.Width}x{map.Depth} map (seed {seed}, cellSize {map.CellSize}).");
PrintPreview(map);
PrintBiomePreview(map);
PrintCounts(map);
PrintBiomeCounts(map);
PrintHeightSummary(map, config.SeaLevel);

if (outPath is not null)
{
    var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
    if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
    MapStorage.Save(map, outPath);
    Console.WriteLine($"Saved to {Path.GetFullPath(outPath)}");
}
else
{
    Console.WriteLine("(no --out given; preview only, nothing written)");
}

return 0;

// --- helpers ---

static Dictionary<string, string> ParseArgs(string[] argv)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (int i = 0; i < argv.Length; i++)
    {
        if (!argv[i].StartsWith("--", StringComparison.Ordinal))
            continue;
        string key = argv[i][2..];
        string value = (i + 1 < argv.Length && !argv[i + 1].StartsWith("--", StringComparison.Ordinal))
            ? argv[++i]
            : "true";
        result[key] = value;
    }
    return result;
}

static int GetInt(Dictionary<string, string> a, string key, int fallback)
    => a.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

static float GetFloat(Dictionary<string, string> a, string key, float fallback)
    => a.TryGetValue(key, out var v)
       && float.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var n)
        ? n : fallback;

/// <summary>
/// Parse the --biome-names comma-separated list into a set of <see cref="Biome"/>.
/// Returns null when the flag is absent (meaning "use all biomes").
/// Exits with an error message when a name doesn't match any known <see cref="Biome"/>.
/// </summary>
static HashSet<Biome>? ParseBiomeNames(Dictionary<string, string> args)
{
    if (!args.TryGetValue("biome-names", out var raw) || string.IsNullOrWhiteSpace(raw))
        return null;

    var set = new HashSet<Biome>();
    var allNames = string.Join(", ", Enum.GetNames<Biome>());
    foreach (var part in raw.Split(','))
    {
        var trimmed = part.Trim();
        if (!Enum.TryParse<Biome>(trimmed, ignoreCase: true, out var b))
        {
            Console.Error.WriteLine($"Unknown biome '{trimmed}'. Valid biomes: {allNames}.");
            Environment.Exit(1);
        }
        set.Add(b);
    }

    return set.Count > 0 ? set : null;
}

/// <summary>
/// Parse a comma-separated "Biome=value" list (e.g. "Plains=1.0,Desert=-1.0").
/// Returns null when the flag is absent. Exits with error on unknown biome or invalid float.
/// </summary>
static Dictionary<Biome, float>? ParseBiomeFloatDict(Dictionary<string, string> args, string key)
{
    if (!args.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        return null;

    var dict = new Dictionary<Biome, float>();
    var allNames = string.Join(", ", Enum.GetNames<Biome>());
    foreach (var part in raw.Split(','))
    {
        var eq = part.IndexOf('=');
        if (eq < 0)
        {
            Console.Error.WriteLine($"Expected 'Biome=value' format for --{key}, got '{part.Trim()}'.");
            Environment.Exit(1);
        }
        var name = part[..eq].Trim();
        var valStr = part[(eq + 1)..].Trim();

        if (!Enum.TryParse<Biome>(name, ignoreCase: true, out var b))
        {
            Console.Error.WriteLine($"Unknown biome '{name}' in --{key}. Valid biomes: {allNames}.");
            Environment.Exit(1);
        }
        if (!float.TryParse(valStr, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            Console.Error.WriteLine($"Invalid float '{valStr}' for {name} in --{key}.");
            Environment.Exit(1);
        }
        dict[b] = v;
    }

    return dict.Count > 0 ? dict : null;
}

static void PrintHeightOverrides(Dictionary<Biome, float>? offsets, Dictionary<Biome, float>? variations)
{
    var parts = new List<string>();
    if (offsets is { Count: > 0 })
        foreach (var (b, v) in offsets.OrderBy(kv => (int)kv.Key))
            parts.Add($"{b}+{v:0.00}");
    if (variations is { Count: > 0 })
        foreach (var (b, v) in variations.OrderBy(kv => (int)kv.Key))
            parts.Add($"{b}×{v:0.00}");
    if (parts.Count > 0)
        Console.WriteLine($"Height overrides active: {string.Join(", ", parts)}.");
}

// Downsampled ASCII preview so large maps fit the terminal.
static void PrintPreview(MapData map)
{
    const int maxCols = 64;
    const int maxRows = 32;
    int stepX = Math.Max(1, map.Width / maxCols);
    int stepZ = Math.Max(1, map.Depth / maxRows);

    for (int cz = 0; cz < map.Depth; cz += stepZ)
    {
        var line = new char[(map.Width + stepX - 1) / stepX];
        int col = 0;
        for (int cx = 0; cx < map.Width; cx += stepX)
        {
            line[col++] = map.GetCell(cx, cz).Terrain switch
            {
                Terrain.Water => '~',
                Terrain.Rock => '#',
                Terrain.Sand => 's',
                Terrain.Marsh => 'm',
                _ => '.',
            };
        }
        Console.WriteLine(new string(line));
    }
}

// Downsampled biome view: one letter per biome (P/D/F/W…).
static void PrintBiomePreview(MapData map)
{
    const int maxCols = 64;
    const int maxRows = 32;
    int stepX = Math.Max(1, map.Width / maxCols);
    int stepZ = Math.Max(1, map.Depth / maxRows);

    Console.WriteLine("biomes:");
    for (int cz = 0; cz < map.Depth; cz += stepZ)
    {
        var line = new char[(map.Width + stepX - 1) / stepX];
        int col = 0;
        for (int cx = 0; cx < map.Width; cx += stepX)
            line[col++] = BiomeLetter(map.GetCell(cx, cz).Biome);
        Console.WriteLine(new string(line));
    }
}

// First letter of the biome name (handles future biomes without code changes).
static char BiomeLetter(Biome biome)
{
    string name = biome.ToString();
    return name.Length > 0 ? name[0] : '?';
}

static void PrintBiomeCounts(MapData map)
{
    var counts = new Dictionary<Biome, int>();
    for (int cz = 0; cz < map.Depth; cz++)
    for (int cx = 0; cx < map.Width; cx++)
    {
        var b = map.GetCell(cx, cz).Biome;
        counts[b] = counts.TryGetValue(b, out var n) ? n + 1 : 1;
    }

    var parts = counts.OrderBy(kv => kv.Key)
        .Select(kv => $"{kv.Key}={kv.Value}");
    Console.WriteLine("biome cells: " + string.Join(' ', parts));
}

static void PrintHeightSummary(MapData map, float seaLevel)
{
    float min = float.MaxValue, max = float.MinValue, sum = 0f;
    int belowSea = 0;
    for (int cz = 0; cz < map.Depth; cz++)
    for (int cx = 0; cx < map.Width; cx++)
    {
        float h = map.GetCell(cx, cz).Height;
        min = Math.Min(min, h);
        max = Math.Max(max, h);
        sum += h;
        if (h < seaLevel) belowSea++;
    }
    float mean = sum / (map.Width * map.Depth);
    Console.WriteLine(
        $"height: min={min:0.00} max={max:0.00} mean={mean:0.00} " +
        $"seaLevel={seaLevel:0.00} belowSea={belowSea}");
}

static void PrintCounts(MapData map)
{
    int grass = 0, water = 0, rock = 0, sand = 0, marsh = 0;
    for (int cz = 0; cz < map.Depth; cz++)
    for (int cx = 0; cx < map.Width; cx++)
    {
        switch (map.GetCell(cx, cz).Terrain)
        {
            case Terrain.Water: water++; break;
            case Terrain.Rock: rock++; break;
            case Terrain.Sand: sand++; break;
            case Terrain.Marsh: marsh++; break;
            default: grass++; break;
        }
    }
    Console.WriteLine($"cells: grass={grass} water={water} rock={rock} sand={sand} marsh={marsh}  ('.'=grass '~'=water '#'=rock 's'=sand 'm'=marsh)");
}
