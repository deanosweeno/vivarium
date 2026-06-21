# Implementation Plan: Per-Biome Height with Pipeline Reorder

## Design summary
- **Pipeline reorder:** `AssignBiomes` moves before `SculptHeight` — biomes are assigned first, then height sculpted per-biome.
- **New `BiomeDef` fields:** `HeightOffset` (world-unit baseline, default 0), `HeightVariation` (amplitude multiplier, 1=factory hills, 0=flat, default 1).
  Named `HeightVariation` to avoid collision with `MapGenConfig.HeightScale` (which means noise feature size).
- **Smooth boundaries:** during `SculptHeight`, each cell finds its two nearest Voronoi seeds and distance-blends their biome's offset/variation. No visible cliffs.
- **CLI overrides:** `--height-offsets "Plains=1.0,Desert=-1.0"` and `--height-variations "Plains=0.3,Wetland=0.15"` for one-run experimentation without editing JSON.
- **WithOverrides API:** `BiomeCatalog.WithOverrides(offsets?, variations?)` with two nullable dict params — callers pass only what they're overriding.

### Target biome heights
| Biome | Offset | Variation | Look |
|---|---|---|---|
| Plains | +1.0 | 0.3 | Plateau, gently rolling |
| Desert | −1.0 | 0.5 | Low basin, moderate dunes |
| Forest | +1.0 | 0.3 | Same as Plains for now |
| Wetland | −2.0 | 0.15 | Low, nearly flat → ~60:40 water:land at seaLevel −2 |

---

## 1. Add `HeightOffset` and `HeightVariation` to `BiomeDef`
- **Files:** `core/BiomeDef.cs` (modify)
- **What:** Add two `{ get; init; }` float properties:
  - `HeightOffset` (default `0f`) — world-unit baseline added to sculpted elevation. Positive = higher (plateau), negative = lower (basin).
  - `HeightVariation` (default `1f`) — multiplier on noise amplitude. 1.0 = full factory hills, 0.0 = perfectly flat.
- **Why:** Foundation for all per-biome height control. Defaults are identity (no change from current behavior). Named `Variation` not `Scale` to avoid collision with `MapGenConfig.HeightScale` (noise feature size).
- **Depends on:** none

## 2. Map new fields in `BiomeCatalog` + add `WithOverrides` method
- **Files:** `core/BiomeCatalog.cs` (modify)
- **What:**
  - Add `float? HeightOffset`, `float? HeightVariation` to private `BiomeDto` class
  - Map DTO fields → `BiomeDef` in `Parse()` (`dto.HeightOffset ?? fallback.HeightOffset`, etc.)
  - Add method:
    ```csharp
    public BiomeCatalog WithOverrides(
        Dictionary<Biome, float>? offsets = null,
        Dictionary<Biome, float>? variations = null)
    ```
    Creates a new catalog by cloning internal `_defs` dict. For each key in `offsets`/`variations`, replaces or creates a `BiomeDef` with the overridden field(s). Original catalog unchanged. Biome not already in `_defs` gets a `Neutral()` def with the override applied.
- **Why:** Overrides compose with JSON defaults. Immutable catalog pattern. Optional named params let callers pass only what they need (`WithOverrides(offsets: myDict)`).
- **Depends on:** 1

## 3. Test `BiomeCatalog.WithOverrides`
- **Files:** `core/BiomeCatalogTests.cs` (modify)
- **What:** Add tests:
  - `WithOverrides_SetsHeightOffset` — override Plains offset to 2.0, verify `Get(Plains).HeightOffset == 2.0`, other fields preserved from JSON
  - `WithOverrides_PreservesOriginal` — original catalog unchanged after calling `WithOverrides` on it
  - `WithOverrides_BiomeNotInCatalog` — override a Biome not in the JSON → synthesized def has the override
  - `WithOverrides_VariationOnly_DoesNotTouchOffset` — only pass variations dict → offsets stay at JSON values
  - `Parse_RoundTripsHeightFields` — JSON with `HeightOffset` and `HeightVariation` → round-trips correctly
- **Why:** Override composition is the backbone for CLI-driven tuning.
- **Depends on:** 2

## 4. Extract shared seed-point struct + nearest-2 helper
- **Files:** `core/MapGenerator.cs` (modify)
- **What:**
  - Add private record struct at the top of the class:
    ```csharp
    private record struct BiomeSeed(int X, int Z, Biome Biome);
    ```
  - Add private static helper:
    ```csharp
    private static (int best, int second) NearestSeeds(BiomeSeed[] seeds, int cx, int cz)
    ```
    Iterates seeds once, tracks the two closest by distance-squared. Returns indices into the seeds array. `second` is initialized to the same as `best` (so single-seed case degrades gracefully — blend weight = 0.5 each, both point to same biome).
  - No rng draws in these — purely geometric. Determinism unaffected.
- **Why:** `AssignBiomes` needs nearest-1; `SculptHeight` needs nearest-2 for boundary blending. One loop, shared, no copy-paste.
- **Depends on:** none (pure refactor of helpers, doesn't touch existing passes yet)

## 5. Reorder pipeline in `Generate` + generate seed points at top
- **Files:** `core/MapGenerator.cs` (modify)
- **What:**
  - Move `AssignBiomes` call before `SculptHeight` and `FloodWater`
  - New order: `AssignBiomes → SculptHeight → FloodWater → CarveLakes → ScatterRocks`
  - At the top of `Generate()`, after creating `rng` and `map`, draw seed points:
    ```csharp
    var seeds = new BiomeSeed[seedCount];
    for (int i = 0; i < seedCount; i++)
        seeds[i] = new BiomeSeed(rng.Next(map.Width), rng.Next(map.Depth),
            biomePool[rng.Next(biomePool.Length)]);
    ```
    Pass `seeds` to both `AssignBiomes` and `SculptHeight`.
  - Update `AssignBiomes` signature: `AssignBiomes(MapData, BiomeSeed[])` — no rng parameter, no internal seed generation. Just the nearest-seed loop over cells using `NearestSeeds(seeds, cx, cz).best`.
  - Update the doc comment on `Generate` to reflect new pipeline order.
- **Why:** `SculptHeight` needs to know which biome each cell belongs to. Seed points generated once, consumed by both passes. All rng draws remain at the top of `Generate()` in deterministic order. This re-rolls every existing seed — expected per architecture doc.
- **Depends on:** 4

## 6. Rewrite `SculptHeight` with per-biome offset/variation + boundary blending
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** New signature: `SculptHeight(MapData, MapGenConfig, BiomeCatalog, BiomeSeed[], Random)`
  - One continuous `HeightNoise` pass as before, but per cell:
    1. Call `NearestSeeds(seeds, cx, cz)` → `(best, second)` indices
    2. Get both biomes' defs: `def1 = biomes.Get(seeds[best].Biome)`, `def2 = biomes.Get(seeds[second].Biome)`
    3. Compute distances `d1`, `d2`; blend weight `t = d1 / (d1 + d2)` (when d1+d2=0 → t=0, use best)
    4. Lerp: `offset = Lerp(def1.HeightOffset, def2.HeightOffset, t)`, `variation = Lerp(def1.HeightVariation, def2.HeightVariation, t)`
    5. Base noise: `n = noise.Fbm(cx * invScale, cz * invScale, config.HeightOctaves)` (unchanged from today)
    6. Final height: `h = ((n * 2f - 1f) * config.HeightAmplitude * variation) + offset`
  - Note: `FloodWater` will run after this, converting cells below `SeaLevel` to Water. The blend means boundary zones between high and low biomes will slope — water may form "shorelines" where the slope dips below sea level. Expected and may be visually desirable. Preview seeds to confirm.
- **Why:** Smooth biome-aware terrain. Same noise field = no seams in the base shape. Two-nearest blend = no height cliffs at Voronoi boundaries.
- **Depends on:** 5

## 7. Update `assets/biomes.json` with per-biome height fields
- **Files:** `assets/biomes.json` (modify)
- **What:** Add `HeightOffset` and `HeightVariation` to each entry:
  - Plains: `"HeightOffset": 1.0, "HeightVariation": 0.3`
  - Desert: `"HeightOffset": -1.0, "HeightVariation": 0.5`
  - Forest: `"HeightOffset": 1.0, "HeightVariation": 0.3`
  - Wetland: `"HeightOffset": -2.0, "HeightVariation": 0.15`
- **Why:** Data drives biome look. Tuning these later is just editing this JSON + re-baking.
- **Depends on:** 2

## 8. Add `--height-offsets` and `--height-variations` CLI flags
- **Files:** `tools/MapGenTool/Program.cs` (modify)
- **What:**
  - Add to flags comment header:
    ```
    //   --height-offsets <kv>     per-biome height offsets, e.g. "Plains=1.0,Desert=-1.0"
    //   --height-variations <kv>  per-biome height variation, e.g. "Plains=0.3,Desert=0.5"
    ```
  - Add `ParseBiomeFloatDict(args2, "height-offsets")` helper (and same for `"height-variations"`):
    - Returns `Dictionary<Biome, float>?` (null if flag absent)
    - Split on `','`, split each part on `'='`, `Enum.TryParse<Biome>` + `float.TryParse`
    - Unknown biome name → error exit with valid-names list. Invalid float → error exit.
  - After loading `BiomeCatalog biomes` from JSON, apply:
    ```csharp
    biomes = biomes.WithOverrides(offsets: ParseBiomeFloatDict(args2, "height-offsets"),
                                   variations: ParseBiomeFloatDict(args2, "height-variations"));
    ```
  - Print override summary when flags active (e.g. `"Height overrides active: offsets=Plains+1.00, variations=Plains×0.30"`).
- **Why:** CLI experimentation without touching JSON. Follows `--biome-names` pattern.
- **Depends on:** 2, 7

## 9. Update `MapGeneratorTests` for reordered pipeline + per-biome height
- **Files:** `core/MapGeneratorTests.cs` (modify)
- **What:**
  - Existing tests still pass (they test invariants, not specific seed outputs). Verify after reorder.
  - Add new tests:
    - `Generate_RespectsBiomeHeightOffset` — Plains +3 offset, Desert −3 offset. Assert mean Plains height > mean Desert height by a meaningful margin.
    - `Generate_HeightVariationZero_IsFlat` — single-biome catalog with `HeightVariation = 0` → all cell heights equal (tolerance: 0.001).
    - `Generate_BoundaryBlend_NoCliffs` — adjacent cells across biome boundaries have height delta within some tolerance (the blend prevents the full offset jump). Verify at least one pair exists where the delta is less than `|offset1 - offset2|`.
    - `Generate_WithNeutralCatalog_HeightsStillSpanBothSigns` — default catalog (offset=0, variation=1) still produces heights above and below sea level.
- **Why:** New behavior demands tests. Offset application, flat-variation edge case, and boundary-blend correctness.
- **Depends on:** 6

## 10. Update `docs/map-architecture.md` §12
- **Files:** `docs/map-architecture.md` (modify)
- **What:**
  - Update the pipeline documentation to reflect new order: `AssignBiomes → SculptHeight → FloodWater → ...`
  - Update `SculptHeight` description: now reads per-cell biome from `BiomeCatalog` via the `BiomeSeed[]` layout, applies `HeightOffset` and `HeightVariation`, and blends two nearest seeds at boundaries.
  - Remove sentence "(A per-biome amplitude could later move into BiomeDef... but is not implemented.)" — it is now implemented.
  - Update the tunables list to mention biome-specific `HeightOffset` / `HeightVariation` in addition to the global `MapGenConfig` knobs.
- **Why:** Authoritative architecture doc must match code. §12 is the spec.
- **Depends on:** 6

## 11. Update `tools/MapGenTool/README.md`
- **Files:** `tools/MapGenTool/README.md` (modify)
- **What:**
  - Add `--height-offsets` and `--height-variations` to flags table
  - Update pipeline diagram in §3 to reflect `AssignBiomes → SculptHeight → FloodWater`
  - Add brief note in §6b about per-biome height tuning via JSON or CLI
- **Why:** Tool usage doc matches implementation.
- **Depends on:** 8

## 12. Re-bake default map + final verification
- **What:**
  1. Preview several seeds with all biomes (no `--biome-names` filter):
     ```bash
     dotnet run --project tools/MapGenTool -- --sealevel -2 --seed 21
     dotnet run --project tools/MapGenTool -- --sealevel -2 --seed 42
     ```
  2. While previewing, watch for excessive water "moats" along biome boundaries. If the two-nearest blend leaves sharp water rings, consider reducing offset differences (e.g. Desert −0.5 instead of −1.0).
  3. Pick the best-looking seed, bake:
     ```bash
     dotnet run --project tools/MapGenTool -- --sealevel -2 --seed <chosen> --out assets/maps/default_map.json
     ```
  4. Run `dotnet test core/core.csproj` — all green.
  5. Run `dotnet build Vivarium.csproj` — zero warnings.
  6. Commit.
- **Depends on:** 1–11
