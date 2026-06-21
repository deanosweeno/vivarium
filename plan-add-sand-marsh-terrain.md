# Implementation Plan: Add Sand & Marsh Terrain Types

## 1. Extend Terrain enum + update IsWalkable
- **Files:** `core/MapData.cs` (modify)
- **What:** Add `Sand = 3` and `Marsh = 4` to the `Terrain` enum (append-only, per serialization contract). Change `IsWalkable` from `Terrain == Terrain.Grass` to a set check — Grass, Sand, Marsh are walkable; Water, Rock are not. Update the constructor's initialization comment (cells still default to Grass until the new pass overwrites them).
- **Why:** Foundation — every other step depends on the new enum values existing and walkability being correct.
- **Depends on:** none

## 2. Add AssignDefaultTerrain pass to MapGenerator
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** New private method `AssignDefaultTerrain(MapData map)` that sets each cell's terrain from its biome: Plains→Grass, Desert→Sand, Forest→Grass, Wetland→Marsh. Insert it in the pipeline immediately after `AssignBiomes` (before `SculptHeight`). Update the doc comment listing pipeline order.
- **Why:** Biomes now control the default terrain. This is the mapping layer.
- **Depends on:** 1

## 3. Update ScatterRocks to place rocks on any walkable terrain
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** Change ScatterRocks' placement gate from `cell.Terrain == Terrain.Grass` to `MapData.IsWalkable(cx, cz)` (or an equivalent walkable-set check), so rocks can spawn on Sand and Marsh too.
- **Why:** Without this, Desert (RockChance 1.8, the highest) and Wetland would get zero rocks because their cells are Sand/Marsh instead of Grass.
- **Depends on:** 1, 2

## 4. Update MapView.CellColor for Sand and Marsh
- **Files:** `scripts/MapView.cs` (modify)
- **What:** Add `Terrain.Sand =>` and `Terrain.Marsh =>` cases to the `CellColor` switch. Sand → yellow (`#d4b463` or similar sandy beige), Marsh → dark green (`#3d5a3d`). Keep the `_ =>` fallback (Grass uses biome tint).
- **Why:** New terrains need distinct visual rendering.
- **Depends on:** 1

## 5. Update MapGenTool text preview
- **Files:** `tools/MapGenTool/Program.cs` (modify)
- **What:** Add Sand (`'s'`) and Marsh (`'m'`) to the `PrintPreview` terrain switch and update `PrintCounts` to track sand/marsh counts (currently the `default` case catches everything as grass).
- **Why:** Tooling must reflect the new terrain types.
- **Depends on:** 1

## 6. Update MapDataTests
- **Files:** `core/MapDataTests.cs` (modify)
- **What:** 
  - Update `IsWalkable_TrueOnlyForGrass` test — rename to `IsWalkable` and test that Grass, Sand, Marsh are walkable; Water, Rock are not.
  - `Constructor_FillsAllCellsWithGrass` still passes (constructor still defaults to Grass — the pass overwrites per biome later).
- **Why:** Walkability semantics changed — tests must match.
- **Depends on:** 1

## 7. Update MapGeneratorTests
- **Files:** `core/MapGeneratorTests.cs` (modify)
- **What:**
  - In the "all cells have valid terrain" assertion (the `Assert.True(t == Terrain.Grass || t == Terrain.Water || t == Terrain.Rock)` line), add Sand and Marsh to the allowed set.
  - In the `Generate_BiomeNamesAll_MatchesNull` test, the terrain comparison between two maps will still work (same seed = same terrain assignments).
  - Optionally add a test: `Generate_FourBiomes_ProducesAllTerrainTypes` that verifies all four walkable terrains (Grass, Sand, Marsh) + Water + Rock appear when all four biomes are used.
  - The `Generate_WithSinkWater_IsDeterministic` terrain equality assertion still holds.
  - `CountTerrain` helper and usages: the helper counts by exact match, and existing tests that assert Water > 0 / Rock > 0 still pass with the new default-mechanic interplay. May need to update a test that assumes Grass is the only walkable terrain.
- **Why:** Tests validate the new terrain types appear and don't break determinism.
- **Depends on:** 2, 3

## 8. Build + test + regenerate map
- **Files:** project-level
- **What:** 
  1. `dotnet build` — zero errors.
  2. `dotnet test` — all green.
  3. Regenerate `assets/maps/default_map.json` with the MapGenTool so the game loads a map with Sand and Marsh cells.
  4. Launch Godot and verify visual output.
- **Why:** Verification gate.
- **Depends on:** 1–7
