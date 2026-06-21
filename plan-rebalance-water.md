# Implementation Plan: Rebalance Desert & Wetland to reduce water coverage

## 1. Update biomes.json — Desert and Wetland tuning
- **Files:** `assets/biomes.json` (modify)
- **What:** Three field changes:
  - `Desert.HeightOffset`: `-1.0` → `-0.5`
  - `Wetland.HeightOffset`: `-2.0` → `-1.0`
  - `Wetland.WaterChance`: `2.0` → `0.8`
- **Why:** Less negative HeightOffset keeps terrain above sea level more often (fewer cells hit `FloodWater`). Lower WaterChance from always (2.0) to 0.8 means `CarveLakes` leaves some Marsh cells walkable instead of converting all to Water. Together these reduce over-saturation while keeping Wetland the wettest biome.
- **Depends on:** none

## 2. Regenerate default map
- **Files:** `assets/maps/default_map.json` (regenerate)
- **What:** Run `MapGenTool --seed 17 --out assets/maps/default_map.json` to bake the rebalanced data.
- **Why:** Map must reflect the new biome rules.
- **Depends on:** 1

## 3. Build + test
- **Files:** project-level
- **What:** `dotnet build` + `dotnet test` — confirm no regressions (this is pure data, but the gate runs).
- **Why:** Verification gate.
- **Depends on:** none
