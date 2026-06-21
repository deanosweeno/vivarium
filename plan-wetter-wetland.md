# Implementation Plan: Wetter Wetland (+0.5 variation)

## Design summary
- Change Wetland `HeightVariation` from `0.15` to `0.5` in `assets/biomes.json`
- Re-bake `default_map.json` with seed 8 (same seed) and same `--sealevel -2`
- No code changes — pure data tuning

---

## 1. Update Wetland HeightVariation in biomes.json
- **Files:** `assets/biomes.json` (modify)
- **What:** Change `"HeightVariation": 0.15` → `"HeightVariation": 0.5` in the Wetland entry
- **Why:** 0.5 matches Desert dune-level hills instead of the current near-flat 0.15
- **Depends on:** none

## 2. Re-bake default map
- **What:**
  ```bash
  dotnet run --project tools/MapGenTool -- --sealevel -2 --seed 8 --out assets/maps/default_map.json
  ```
  Verify water percentage stayed reasonable. If Wetland now dips too much water below seaLevel -2, consider raising seaLevel slightly.
- **Depends on:** 1

## 3. Verify
- **What:**
  - `dotnet test core/core.csproj` — all 137 pass
  - `dotnet build Vivarium.csproj` — zero warnings
  - Commit
- **Depends on:** 2
