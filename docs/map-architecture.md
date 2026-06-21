# Map Architecture — Ground Rules & Build Spec

> **Purpose of this document.** This is the authoritative spec for the Vivarium map
> system. It is written to be followed *literally*. If you are implementing this,
> do exactly what is written here. Do not invent extra features, do not "improve"
> the design, do not add classes that aren't listed. When something is unclear,
> stop and ask rather than guessing.

---

## 1. The big picture (read this first)

The map is split into **two completely separate layers**. This separation is the
single most important rule in this document. Never break it.

```
┌─────────────────────────────────────────────────────────────┐
│  LOGIC LAYER  (core/)   — pure C#, NO Godot, unit-tested     │
│                                                              │
│   MapData ............ the grid of cells (the "what")        │
│   MapGenerator ....... rules that fill a MapData (the "how") │
│   MapStorage ......... save/load MapData to/from a file      │
│                                                              │
│   Knows nothing about rendering, meshes, colors, or Godot.   │
└─────────────────────────────────────────────────────────────┘
                              │ reads state only
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  PRESENTATION LAYER  (scripts/, scenes/) — thin Godot layer  │
│                                                              │
│   Reads a MapData and draws it. Owns NO map rules.           │
└─────────────────────────────────────────────────────────────┘
```

**Hard rule:** `core/` must never reference Godot. No `using Godot;` in any map
logic file. The presentation layer reads `MapData` and draws it; it must never
contain map rules (what's walkable, where lakes go, etc.).

---

## 2. How the map relates to what already exists

The game already has an `Arena` (in `core/Arena.cs`). **Do not change `Arena`.**

- `Arena` = the outer box / walls of the world (continuous floats, X/Y/Z bounds).
- `MapData` = the terrain *inside* that box, described as a grid of cells.

`MapData` lays a grid over the **XZ plane** (the floor). Each cell also carries a
**height** (elevation in world units, Y up), so the surface rolls above and below
`y=0` — see the implemented §12. (The grid layout is still defined on XZ; height is a
per-cell scalar on top of it.)

---

## 3. Coordinate system & the grid

The grid covers the XZ floor. One cell = one square tile.

```
   world Z
     ▲
     │  ┌───┬───┬───┬───┐   each cell is CellSize × CellSize world units
     │  │0,0│1,0│2,0│3,0│   cell (col, row) = (cx, cz)
     │  ├───┼───┼───┼───┤   cx runs along world X  (0 .. Width-1)
     │  │0,1│1,1│2,1│3,1│   cz runs along world Z  (0 .. Depth-1)
     │  ├───┼───┼───┼───┤
     │  │0,2│1,2│2,2│3,2│
     │  └───┴───┴───┴───┘
     └────────────────────▶ world X
```

- Grid size: **Width × Depth** cells. Target default: **128 × 128**.
- `CellSize` (float, world units per cell) is stored on `MapData`.
- Cells are addressed by integer `(cx, cz)`, both zero-based.
- **World ↔ cell conversion lives in `MapData`** (methods `WorldToCell` /
  `CellToWorldCenter`). Nobody else computes this.

---

## 4. The data model — `MapData`

**File:** `core/MapData.cs`  **Namespace:** `Vivarium.Core`

### 4.1 The terrain enum

```csharp
public enum Terrain
{
    Grass = 0,   // default, walkable
    Water = 1,   // not walkable
    Rock  = 2,   // not walkable
}
```

> Add new terrain types ONLY by appending to this enum (keep existing numbers
> stable — they are written to save files). Do not reorder.

### 4.2 The cell

A cell is a small **struct** (value type), not a class. Keep it tiny.

```csharp
public struct Cell
{
    public Terrain Terrain;
    public Resource Resource;   // see §4.4; None for now is fine
}
```

`Walkable` is **derived**, never stored, so it can't get out of sync:

```csharp
// On MapData:
public bool IsWalkable(int cx, int cz)  =>  GetCell(cx, cz).Terrain == Terrain.Grass;
```

### 4.3 The MapData class — required surface

```csharp
public sealed class MapData
{
    public int   Width    { get; }      // cells along X
    public int   Depth    { get; }      // cells along Z
    public float CellSize { get; }      // world units per cell

    public MapData(int width, int depth, float cellSize);  // fills all cells with Grass

    // --- cell access (bounds-checked; throws on out-of-range) ---
    public Cell GetCell(int cx, int cz);
    public void SetCell(int cx, int cz, Cell cell);
    public bool InBounds(int cx, int cz);

    // --- derived ---
    public bool IsWalkable(int cx, int cz);

    // --- world <-> cell ---
    public (int cx, int cz) WorldToCell(System.Numerics.Vector3 world);
    public System.Numerics.Vector3 CellToWorldCenter(int cx, int cz);
}
```

**Storage:** a single flat array `Cell[]` of length `Width * Depth`, indexed
`index = cz * Width + cx`. Do NOT use a 2D array or a list of lists.

**Rules for MapData:**
- It is **pure data + accessors**. It contains NO generation rules and NO Godot.
- Out-of-bounds `GetCell`/`SetCell` throws `ArgumentOutOfRangeException`.
- The constructor fills every cell with `Terrain.Grass`, `Resource.None`.

### 4.4 Resource enum (stub for now)

```csharp
public enum Resource
{
    None = 0,
    Food = 1,
}
```

Define it now so the cell struct is stable, but generating/placing resources is a
**later pass** — don't implement food logic yet unless this doc's §6 lists it.

---

## 5. The generator — `MapGenerator`

**File:** `core/MapGenerator.cs`  **Namespace:** `Vivarium.Core`

The generator is a **pipeline of passes**. Each pass is a small method that takes
the `MapData` and a seeded `Random`, and mutates the grid. Passes run in a fixed
order.

```
MapGenerator.Generate(config, seed)
        │
        ▼
   new MapData(all Grass)
        │
        ├─►  Pass 1: CarveLake(s)      (Grass → Water)
        ├─►  Pass 2: ScatterRocks      (Grass → Rock)
        └─►  ... more passes added later, in order
        │
        ▼
   finished MapData
```

### 5.1 Required surface

```csharp
public sealed class MapGenerator
{
    public static MapData Generate(MapGenConfig config, int seed);
}
```

### 5.2 Determinism — NON-NEGOTIABLE

This is the most important rule for the generator. Get it wrong and everything
downstream breaks.

- **All randomness comes from one `System.Numerics`-free `System.Random` created
  from the `seed`:** `var rng = new Random(seed);`
- **Never** call `DateTime.Now`, `Guid.NewGuid()`, `Random.Shared`, thread stuff,
  or any other entropy source. Same `(config, seed)` MUST always produce a
  byte-identical map.
- Passes draw from the **same `rng` in the same order**. Do not create new
  `Random` instances inside passes.

(This matches how `Simulator` already takes a `seed` and builds `new Random(seed)`.)

### 5.3 Config is data, not hardcoded numbers

**File:** `core/MapGenConfig.cs`  **Namespace:** `Vivarium.Core`

Every tunable number lives here. Passes read from config — no magic numbers inside
pass methods.

```csharp
public sealed class MapGenConfig
{
    public int   Width        { get; init; } = 128;
    public int   Depth        { get; init; } = 128;
    public float CellSize     { get; init; } = 1.0f;

    public int   LakeCount    { get; init; } = 1;
    public int   LakeRadius   { get; init; } = 12;   // in cells

    public int   RockClusters { get; init; } = 8;
    public int   RockClusterSize { get; init; } = 5; // cells per cluster
}
```

### 5.4 The two passes to implement FIRST (and only these, to start)

Implement exactly these two. Stop and get them tested before adding more.

**Pass 1 — CarveLake:** Repeat `LakeCount` times: pick a random cell center, set
every cell within `LakeRadius` (Euclidean, in cell units) to `Terrain.Water`.

**Pass 2 — ScatterRocks:** Repeat `RockClusters` times: pick a random start cell;
do a tiny random walk of `RockClusterSize` steps, setting each visited cell to
`Terrain.Rock` — but **only if it is currently Grass** (never overwrite Water).

Do NOT implement resource placement, biomes, rivers, smoothing, or elevation yet.

---

## 6. Build order (do these in sequence, verify each before moving on)

> The point of this order: every step is independently testable and the project
> always builds. Never skip ahead.

```
STEP 1 ─ MapData.cs + Cell + Terrain + Resource enums
         └─ Test: construct 4×4 map, all cells Grass, Set/Get round-trips,
            WorldToCell/CellToWorldCenter are inverses, out-of-bounds throws.

STEP 2 ─ MapGenConfig.cs + MapGenerator.cs (Pass 1 + Pass 2)
         └─ Test: Generate(config, 42) twice → identical maps (determinism).
                  Generate produces at least one Water cell and one Rock cell.
                  No Rock cell sits on a Water cell.

STEP 3 ─ MapStorage.cs  (save MapData to file, load it back)   [see §7]
         └─ Test: save then load → identical MapData (round-trip).

STEP 4 ─ (LATER) creatures respect IsWalkable in the movement layer.
STEP 5 ─ (LATER) Godot rendering of the grid.
STEP 6 ─ (LATER) Resource pass, then elevation, then biomes.
```

**Steps 4–6 are not in scope for the first build. Do 1–3 only.**

---

## 7. Save / load & the authoring workflow ("generate-then-freeze")

**File:** `core/MapStorage.cs`  **Namespace:** `Vivarium.Core`

The game ships **one fixed, curated map** loaded from a file. The game does NOT
run the generator at startup. Workflow:

```
   [author offline]                          [shipped game]
   MapGenerator.Generate(seed)  ──► tweak ──►  map file  ──► MapStorage.Load() ──► MapData
        (find a seed you like)    (by hand)   (the asset)        (at startup)
```

So `MapGenerator` is a **tooling/offline** thing. The runtime only ever calls
`MapStorage.Load`. Keep that boundary clean.

> The offline tool that performs this generate-then-freeze step lives in
> `tools/MapGenTool`. See **`tools/MapGenTool/README.md`** for how to run it (flags,
> previewing seeds, baking the asset) and how to extend the generator (adding passes,
> terrain types, and config knobs).

**Required surface:**

```csharp
public static class MapStorage
{
    public static void Save(MapData map, string path);
    public static MapData Load(string path);
}
```

**Format rule:** use a simple, stable, human-diffable format — **JSON** via
`System.Text.Json` is fine (no Godot resource types in `core/`). Store
`Width`, `Depth`, `CellSize`, and the flat cell array. Terrain/Resource are
written as their integer enum values (that is why §4.1 says keep the numbers
stable).

---

## 8. Coding conventions (this project enforces them — build WILL fail otherwise)

- `TreatWarningsAsErrors=true` — **any warning breaks the build.** No unused
  usings, no unused variables.
- `Nullable=enable` — annotate nullability correctly; no `null` surprises.
- Namespace for logic: `Vivarium.Core`. Namespace for tests: `Vivarium.Core.Tests`.
- Tests use **xUnit** (`[Fact]`, `Assert.*`), one test file per class, named
  `MapDataTests.cs`, `MapGeneratorTests.cs`, `MapStorageTests.cs`, placed in
  `core/` next to the code (matches existing `ArenaTests.cs`).
- Vectors use `System.Numerics.Vector3` (NOT Godot's Vector3) in `core/`.
- XML `/// <summary>` doc comments on every public type and method, matching the
  style already in `Arena.cs` and `Simulator.cs`.
- Verify each step: run `dotnet test` from `core/` and read the output before
  declaring a step done.

---

## 9. Explicitly OUT OF SCOPE (do not build these now)

- **Multiple maps.** One arena, one map. (Biomes are now implemented — see §11;
  terrain height is now implemented — see §12.)
- **Runtime generation.** Game loads a file; it does not generate on startup.
- **Spatial-partition collision.** The grid could speed up collision later, but
  that is a future optimization — do not wire it into `Simulator` now.
- **Resource gameplay** (food seeking, etc.). The enum exists; the behavior does not.

---

## 11. Biomes (implemented)

Biomes are **regions** layered as a third independent axis on each cell —
`Cell = { Terrain, Resource, Biome }`. A biome is *not* a terrain subtype: a Grass cell in a
Desert and a Grass cell in a Forest share terrain but differ in biome. Biomes (a) **bias
generation** and (b) **affect creatures at runtime**.

The extensibility seam is a **data file** — `assets/biomes.json` → `core/BiomeCatalog` →
`BiomeDef` (one rule record per biome). Adding/tuning a biome is a data change:

- **Add a biome:** append to the `Biome` enum (`core/Biome.cs`, append-only/serialized like
  `Terrain`) + add one JSON object.
- **Add a rule/param:** add one `BiomeDef` property + one JSON field. Missing fields fall back to
  defaults; unknown fields are ignored (forward-compatible).

**Generation:** `MapGenerator` runs an `AssignBiomes` pass first (deterministic Voronoi /
nearest-seed, count from `MapGenConfig.BiomeSeedCount`), then the terrain passes weight their rng
rolls by the biome under each cell (`BiomeDef.WaterChance` / `RockChance`). `Generate` has two
overloads: `(config, seed)` (neutral catalog) and `(config, biomes, seed)`.

**Runtime:** `Simulator.Map` + `Simulator.Biomes` (optional; null = no effects) are sampled each
tick by `ApplyBiomeEffects`, applying `BiomeDef.HappinessRate` and `SpeedMultiplier` to each
creature. Position-only — no rng — so determinism holds.

**Presentation:** `scripts/MapView.cs` loads the catalog and tints grass per biome from
`BiomeDef.TintHex`. See `tools/MapGenTool/README.md` §6b for the full how-to.

If you think one of these is needed to finish Steps 1–3, you are mistaken — stop
and ask.

---

## 12. Terrain height (implemented)

Terrain is no longer flat. Each cell carries a **`Height`** (world units, Y up) as a
fourth independent axis — `Cell = { Terrain, Resource, Biome, Height }` — appended the
same append-only/back-compatible way as Biome (a map JSON without the field loads as
`Height = 0`, i.e. flat). The map also stores a single **`SeaLevel`** (the water-surface
elevation), so the renderer never drifts from the generated water line.

**Data + smoothness seam (`core/MapData.cs`):** `HeightAt(world)` returns the elevation at
any world position by **bilinear interpolation** of the four nearest cell centers (clamping
to the edge outside the grid). "What height is the ground here" is defined in exactly one
place, so the renderer and any future surface-walking sim read the same smooth surface.

**Generation (`core/MapGenerator.cs`, `core/HeightNoise.cs`):**
- `AssignBiomes` runs **first** — partitions the grid into Voronoi biome regions.
- `SculptHeight` then samples `HeightNoise` (a pure, deterministic value-noise + fBm
  helper, seeded from the shared `rng`; its own unit so the shape algorithm is
  testable/swappable). Per cell, the two nearest biome seeds' `HeightOffset` and
  `HeightVariation` (from `BiomeDef`, loaded via `BiomeCatalog`) are distance-blended,
  so boundaries slope smoothly. The final height is
  `((noise × 2 − 1) × HeightAmplitude × variation) + offset`.
- `FloodWater` then turns every cell below `SeaLevel` into `Terrain.Water`, leaving the cell
  height at its low value (the lakebed). This is the **primary** water source; the existing
  biome-weighted `CarveLakes` only adds extra ponds on remaining grass.
- Tunables live in `MapGenConfig` (`HeightAmplitude`, `HeightScale`, `HeightOctaves`,
  `SeaLevel`) for global hill shape, plus `BiomeDef.HeightOffset` and
  `BiomeDef.HeightVariation` for per-biome elevation and roughness. The biome values
  live in `assets/biomes.json` and can be tuned per-biome or overridden at the CLI
  (`--height-offsets`, `--height-variations`).

**Presentation (`scripts/MapView.cs`):** builds **one** smooth ground mesh (an `ArrayMesh`
via `SurfaceTool`): a vertex per cell center lifted to its height, joined into quads, with
`GenerateNormals()` for smooth shading and **per-vertex colors** (biome `TintHex` for grass,
grey for rock, a muted lakebed tone for water) so biomes blend across the surface instead of
forming hard tiles. A single translucent water plane is drawn at `Map.SeaLevel`; land rises
above it and basins show through. This replaced the old per-cell box tiles.

**Deferred:** creatures still clamp to `y=0` in `Simulator` — they don't yet follow the
surface. The follow-up is to replace that floor with `Map.HeightAt(position)` (the bilinear
seam already exists); position-only, so determinism holds.

---

## 10. One-paragraph summary (the whole thing in a nutshell)

A `MapData` is a flat grid of `Cell`s over the XZ floor; each cell has a `Terrain`
(Grass/Water/Rock) and walkability is derived from it. A `MapGenerator` fills a
`MapData` by running an ordered pipeline of small, deterministic, seeded passes
whose numbers come from a `MapGenConfig`. You generate offline, hand-tweak, and
freeze the result to a file via `MapStorage`; the shipped game only loads that
file. All of this lives in `core/` with zero Godot dependencies and full xUnit
test coverage. Build Steps 1–3 only; everything in §9 is deferred.
```
