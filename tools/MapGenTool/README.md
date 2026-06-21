# MapGenTool — using & extending the map generator

This is the offline **generate-then-freeze** tool for Vivarium's terrain map. You run
it from a terminal; it builds a `MapData` grid from a seed, prints a preview, and
optionally writes the result to a JSON file. The shipped game **never** runs this tool —
it only loads the baked file (`assets/maps/default_map.json`) at startup.

> If you are an AI assistant picking up this task: this doc is written to be followed
> literally. Do exactly what is written. The authoritative design spec is
> `docs/map-architecture.md` — read it before changing generation rules. The hard rules
> below are not suggestions; the build fails or maps stop being reproducible if you break
> them.

---

## 1. Where everything lives

| Thing | File | Layer |
|-------|------|-------|
| The CLI entry point (arg parsing, preview, save) | `tools/MapGenTool/Program.cs` | tooling |
| Generation pipeline + passes | `core/MapGenerator.cs` | core (pure C#) |
| Tunable numbers (config) | `core/MapGenConfig.cs` | core |
| Grid data model + enums | `core/MapData.cs` | core |
| Save/load (JSON) | `core/MapStorage.cs` | core |
| The baked, shipped map | `assets/maps/default_map.json` | asset |

**The generation logic is in `core/`, not in this tool.** `Program.cs` is only a thin
command-line wrapper. When you "extend the generator" you almost always edit
`core/MapGenerator.cs` and `core/MapGenConfig.cs`, not `Program.cs`.

---

## 2. Using the tool

Run everything from the **repo root** (`/home/dean/vivarium`). The `--` is required: it
separates `dotnet run`'s own flags from the program's flags.

### Preview a seed (writes nothing)

```bash
dotnet run --project tools/MapGenTool -- --seed 42
```

Prints a downsampled ASCII map and terrain counts so you can judge a seed before
committing it:

```
Generated 128x128 map (seed 42, cellSize 1).
................................................................
..........................~~~~..................................
.........................~~~~~~.................................
...............#.........~~~~~~.................................
cells: grass=15915 water=441 rock=28  ('.'=grass '~'=water '#'=rock)
(no --out given; preview only, nothing written)
```

### Freeze a chosen seed to the shipped asset

```bash
dotnet run --project tools/MapGenTool -- --seed 42 --out assets/maps/default_map.json
```

This overwrites the asset the game loads. Re-running the same seed + flags produces a
**byte-identical** file (determinism — see §4).

### Filtering biomes

Restrict which biomes appear in the map with `--biome-names`. This limits the Voronoi
region assignment to only the listed biomes — all other biomes are excluded.

```bash
# Plains and Desert only
dotnet run --project tools/MapGenTool -- --biome-names "Plains,Desert" --seed 42

# Single biome (Forest everywhere)
dotnet run --project tools/MapGenTool -- --biome-names "Forest" --seed 7
```

When the flag is absent, all biomes in the `Biome` enum are used (current default).
The tool prints `Biome filter active: ...` when filtering. Invalid names cause an
error exit listing the valid biomes.

### Workflow

1. Try seeds with preview-only runs until the layout looks good.
2. Re-run that seed **with `--out assets/maps/default_map.json`** to bake it.
3. Launch the game; the new map renders via `scripts/MapView.cs`.

### All flags

| Flag | Default | Meaning |
|------|---------|---------|
| `--seed <int>` | 0 | RNG seed |
| `--out <path>` | *(none)* | output file; omit to preview only |
| `--width <int>` | 128 | grid width (cells) |
| `--depth <int>` | 128 | grid depth (cells) |
| `--cellsize <float>` | 1.0 | world units per cell |
| `--lakes <int>` | 1 | lake count |
| `--lakeradius <int>` | 12 | lake radius in cells |
| `--rocks <int>` | 8 | rock cluster count |
| `--rocksize <int>` | 5 | steps per rock cluster |
| `--biomeseeds <int>` | 6 | biome region seed points (Voronoi) |
| `--biome-names <csv>` | *(all)* | comma-separated biomes to include (e.g. `Plains,Desert`) |
| `--biomes <path>` | `assets/biomes.json` | biome rules JSON; neutral if missing |
| `--amplitude <float>` | 6 | peak terrain height (world units) |
| `--heightscale <float>` | 24 | noise feature size (cells between hilltops) |
| `--octaves <int>` | 4 | fBm height detail octaves |
| `--sealevel <float>` | 0 | water line; cells below it flood to Water |

Every flag maps 1:1 to a property on `MapGenConfig`. Adding a flag is step 3 of §5 below.

---

## 3. How generation works (the mental model)

`MapGenerator.Generate(config, seed)` is a **pipeline of passes**. It:

1. Creates one `Random` from the seed.
2. Builds an all-Grass `MapData`.
3. Runs each pass in a fixed order; each pass mutates the grid using that shared `Random`.

```
Generate(config, biomes, seed)
   var rng = new Random(seed)
   map = new MapData(all Grass, all Plains, Height 0)
   ├─ SculptHeight(map, config, rng)        write Cell.Height (value-noise + fBm)
   ├─ FloodWater(map, config)               Height < SeaLevel → Water (lakebed kept low)
   ├─ AssignBiomes(map, config, rng)        Voronoi regions → Cell.Biome
   ├─ CarveLakes(map, config, biomes, rng)  Grass → Water (biome-weighted ponds)
   └─ ScatterRocks(map, config, biomes, rng)Grass → Rock  (never overwrites Water)
   return map
```

A "pass" is just a `private static void` method taking `(MapData map, MapGenConfig config,
Random rng)`. Order matters: later passes see what earlier passes wrote (that is why
ScatterRocks can check "is this cell still Grass?" to avoid stomping water).

---

## 4. Hard rules — do not break these

1. **Determinism.** All randomness must come from the single `rng` passed into the pass.
   Never call `DateTime.Now`, `Guid.NewGuid()`, `Random.Shared`, `new Random()` (no seed),
   or anything thread/time based. Same `(config, seed)` must always give a byte-identical
   map. There is a test that fails if you break this (`core/MapGeneratorTests.cs`).

2. **Draw from `rng` in a fixed order.** Don't reorder existing `rng.Next(...)` calls or
   insert new ones *before* existing passes — that changes every seed's output and silently
   re-rolls the shipped map. Add new passes/draws **after** the existing ones when you can.

3. **No Godot in `core/`.** `core/` must never have `using Godot;`. Use
   `System.Numerics.Vector3`, not Godot's. (The map tool and `MapData` are engine-agnostic
   on purpose.)

4. **Numbers live in `MapGenConfig`, not in pass methods.** No magic numbers inside a pass.
   If a pass needs a tunable, add a config property for it.

5. **Enum values are serialized — append only.** `Terrain` / `Resource` numbers
   (`Grass=0, Water=1, Rock=2`, `None=0, Food=1`) are written to the JSON file. Only add new
   values by appending with a new number; never reorder or renumber, or old baked maps break.

6. **`TreatWarningsAsErrors` is on.** Unused usings/variables fail the build. Add `///`
   XML doc comments on new public types/methods (match the style in the existing files).

---

## 5. How to add a new generation pass (worked example)

Say you want a "ScatterFood" pass that places `Resource.Food` on some grass cells.

**Step 1 — add config knobs** in `core/MapGenConfig.cs`:

```csharp
/// <summary>Number of food items to scatter.</summary>
public int FoodCount { get; init; } = 20;
```

**Step 2 — add the pass** in `core/MapGenerator.cs`. Write a `private static` method with
the standard signature and call it from `Generate` **after** the existing passes (so you
don't change existing seeds' terrain):

```csharp
public static MapData Generate(MapGenConfig config, int seed)
{
    var rng = new Random(seed);
    var map = new MapData(config.Width, config.Depth, config.CellSize);

    CarveLakes(map, config, rng);
    ScatterRocks(map, config, rng);
    ScatterFood(map, config, rng);   // <-- new, appended last

    return map;
}

/// <summary>
/// Place Food on random Grass cells (never on Water/Rock).
/// </summary>
private static void ScatterFood(MapData map, MapGenConfig config, Random rng)
{
    for (int i = 0; i < config.FoodCount; i++)
    {
        int cx = rng.Next(map.Width);
        int cz = rng.Next(map.Depth);
        var cell = map.GetCell(cx, cz);
        if (cell.Terrain == Terrain.Grass)
        {
            cell.Resource = Resource.Food;
            map.SetCell(cx, cz, cell);
        }
    }
}
```

Note the read-modify-write pattern: `Cell` is a **struct** (value type), so you must
`GetCell` → mutate the local copy → `SetCell`. Mutating the result of `GetCell` in place
does nothing.

**Step 3 — (optional) expose a CLI flag** in `tools/MapGenTool/Program.cs`, inside the
`new MapGenConfig { ... }` block:

```csharp
FoodCount = GetInt(args2, "food", 20),
```

and add a line to the flags comment header at the top of `Program.cs`.

**Step 4 — add a test** in `core/MapGeneratorTests.cs` (xUnit, `[Fact]`). At minimum assert
determinism still holds and that the new pass did something (e.g. at least one Food cell, no
Food on Water). Then:

```bash
dotnet test core/core.csproj
```

**Step 5 — re-bake** the shipped map so it reflects the new pass:

```bash
dotnet run --project tools/MapGenTool -- --seed 42 --out assets/maps/default_map.json
```

---

## 6. Adding a new terrain type

1. **Append** to the `Terrain` enum in `core/MapData.cs` with the next free number
   (e.g. `Sand = 3`). Never renumber existing values (rule §4.5).
2. Add/adjust a pass in `MapGenerator.cs` that writes it.
3. Render it in `scripts/MapView.cs`: add another `BuildTerrainLayer(...)` call with a color
   and height. (`MapView` draws one GPU-instanced `MultiMeshInstance3D` per non-Grass
   terrain over a single grass ground plane.)
4. Update the ASCII preview legend in `Program.cs` (`PrintPreview` / `PrintCounts`) if you
   want the new terrain visible in previews.

---

## 6b. Biomes — adding & extending

Biomes are **regions** (Plains, Desert, Forest, Wetland, …) — a separate axis from `Terrain`.
A cell stores its `Biome`; the biome's *rules* live entirely in data at `assets/biomes.json`,
loaded by `core/BiomeCatalog`. Biomes do two things: **bias generation** (water/rock/food
weighting) and **affect creatures at runtime** (happiness, movement speed). The whole point is
that adding or tuning a biome is a **data change**, not a code change.

**Files:** `core/Biome.cs` (the enum), `core/BiomeDef.cs` (the rule record), `core/BiomeCatalog.cs`
(loads/looks up), `assets/biomes.json` (the data you edit).

### Tune an existing biome — zero code

Edit the numbers in `assets/biomes.json` and re-bake. Each field maps 1:1 to a `BiomeDef`
property. Missing fields fall back to `BiomeDef` defaults; unknown fields are ignored (so old
files keep loading).

### Add a new biome — one enum value + one JSON object

1. **Append** to `core/Biome.cs` with the next free number (e.g. `Tundra = 4`). Never renumber
   existing values — biome numbers are serialized into baked maps (same rule as `Terrain`).
2. **Add one object** to `assets/biomes.json` with `"Biome": "Tundra"` and whatever fields you
   want; omitted fields use defaults.
3. Re-bake: `dotnet run --project tools/MapGenTool -- --seed 21 --out assets/maps/default_map.json`.
   The new biome shows up in the tool's biome preview (`biomes:` block) with **no other code
   change** — `AssignBiomes` chooses from `Enum.GetValues<Biome>()` automatically.

### Add a new rule/param/variable to biomes — one property + one field

Want biomes to control something new (e.g. a `FoodRegrowthRate`)?

1. Add one `init` property to `BiomeDef` (with a sensible default), and a matching nullable
   field to the private `BiomeDto` in `BiomeCatalog` (so missing values fall back to the default).
2. Read it where it matters: a generation pass in `MapGenerator` (via `biomes.Get(cell.Biome)`),
   or the runtime in `Simulator.ApplyBiomeEffects`, or `MapView` for presentation.
3. Add the field to the biomes you want it on in `assets/biomes.json`. Biomes that omit it keep
   the default. This is forward-compatible: old data files still load.

### How biomes plug into the pipeline

`AssignBiomes` runs **first** (deterministic Voronoi / nearest-seed, `--biomeseeds` controls how
many regions). Later passes weight their rng rolls by the biome under each cell — e.g. a Desert
cell rolls against `BiomeDef.WaterChance = 0.05`, so lakes barely form there. **Note:** because
`AssignBiomes` draws from the rng before the terrain passes, it shifts the rng stream — adding/
removing it re-rolls every seed's output. That's expected; just re-bake and pick a seed you like.

The runtime side: `Simulator.Map` + `Simulator.Biomes` are set by `scripts/VivariumMain.cs` from
the loaded `MapView`. Each tick, `Simulator.ApplyBiomeEffects` samples the biome under each
creature and applies `HappinessRate` and `SpeedMultiplier`. It reads position only (no rng), so
the sim stays deterministic.

---

## 6c. Terrain height — tuning the hills

Terrain has elevation: a `SculptHeight` pass (deterministic value-noise + fBm, in
`core/HeightNoise.cs`) writes each cell's `Height`, then `FloodWater` turns everything below
sea level into water. The renderer builds one smooth deformed mesh from those heights. The
height summary line in the preview reports `min/max/mean` height and how many cells flooded:

```
height: min=-4.76 max=4.82 mean=-0.36 seaLevel=-2.00 belowSea=3957
```

**Tune the look with flags** (all map to `MapGenConfig`, no code change):

- `--amplitude` — how tall the hills are. Bigger = more dramatic relief.
- `--heightscale` — feature size. Bigger = broader, gentler hills; smaller = busier terrain.
- `--octaves` — fine detail layered on the broad shape.
- `--sealevel` — the water line. **This is the main land/water dial.** At `0` (the noise
  midpoint) roughly half the map floods like an ocean; lower it (e.g. `-2`) and only the
  basins fill, giving lakes with more land. `SeaLevel` is baked into the map, so the renderer
  draws the water plane at exactly the height you generated with.

```bash
# more land, lakes instead of ocean:
dotnet run --project tools/MapGenTool -- --seed 3 --sealevel -2 --out assets/maps/default_map.json
```

Height is baked per cell and is **append-only/back-compatible** like the other axes — a map
file written before height existed loads as flat (`Height = 0`).

To add a *new* height-related rule/param, follow the same pattern as §6b: add a knob to
`MapGenConfig` (global) or a property to `BiomeDef` (per-biome), read it in the relevant pass,
and re-bake.

---

## 7. Verifying your change

- `dotnet test core/core.csproj` — all green, including the determinism test.
- `dotnet build Vivarium.csproj` — 0 warnings (warnings are errors here).
- `dotnet run --project tools/MapGenTool -- --seed 42` — preview looks right.
- Re-run the same command twice with `--out` to a temp path and `diff` the two files — they
  must be identical (determinism sanity check).
- Launch the game and confirm the map renders (`scripts/MapView.cs` loads the baked asset).
</content>
</invoke>
