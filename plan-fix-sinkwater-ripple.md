# Implementation Plan: Fix SinkWater ripple artifact (two-pass propagation)

## Problem
`SinkWater` creates a depth reversal where shore-adjacent water cells end up
DEEPER than their interior neighbors, producing visible ripple/step artifacts
in the terrain mesh.

**Root cause:** pass uses a pre-sink snapshot. Shore cells sink correctly
(`shoreLand - WaterDepth`), but interior cells sink from their OWN original
height (`selfOrig - WaterDepth`), unaware that shore cells have been lowered.

Example with shore land at -0.5 and interior at 0.0:
```
Pass 1:  land=-0.5  shore=-2.0  interior=-1.5   ← -2.0 < -1.5 = reversal
```

## Fix: Two-pass propagation
Pass 1 stays unchanged. Pass 2 re-snapshots and propagates depression inward
so no water cell sits higher than any of its water neighbors.

## 1. Extract a snapshot helper (shared by both passes)
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** Extract the current `orig` snapshot allocation into a
  `static float[] SnapshotHeights(MapData map)` helper.
  Call it from `SinkWater` for both passes. No behavior change.
- **Depends on:** none

## 2. Add pass-2 propagation loop
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** After the current loop (pass 1), add:
  ```csharp
  // Pass 2: propagate so no water cell is higher than any water neighbor.
  var pass1 = SnapshotHeights(map);
  for (int cz = 0; cz < map.Depth; cz++)
  for (int cx = 0; cx < map.Width; cx++)
  {
      if (map.GetCell(cx, cz).Terrain != Terrain.Water) continue;
      float minNeighbor = float.MaxValue;
      for (int dz = -1; dz <= 1; dz++)
      for (int dx = -1; dx <= 1; dx++)
      {
          if (dx == 0 && dz == 0) continue;
          int nx = cx + dx, nz = cz + dz;
          if (!map.InBounds(nx, nz)) continue;
          minNeighbor = MathF.Min(minNeighbor, pass1[nz * map.Width + nx]);
      }
      var cell = map.GetCell(cx, cz);
      cell.Height = MathF.Min(cell.Height, minNeighbor);
      map.SetCell(cx, cz, cell);
  }
  ```
- **Why:** Ensures `∀ water cell: height ≤ min(neighbor heights)`, eliminating
  the reverse slope. Distributions that would need >1 propagation step still
  improve (no reversal, just possibly incomplete convergence for very wide lakes).
- **Depends on:** 1

## 3. Run existing tests — must pass unchanged
- **Files:** `core/MapGeneratorTests.cs` (no changes needed)
- **What:** `dotnet test` — all 145 tests pass.
  - `Generate_SinkWater_DropsWaterBelowDryNeighbors` — shore contracts still hold
  - `Generate_WaterDepthZero_LeavesHeightsUnchanged` — no-op for zero depth
  - `Generate_WithSinkWater_IsDeterministic` — still byte-identical with same seed
- **Why:** Two-pass is a refinement. Shore contract (`≤ shore - WaterDepth`)
  is never violated (pass 1 upholds it, pass 2 only lowers further).
  Determinism is preserved (two fixed snapshots, order-independent).
- **Depends on:** 2

## 4. Re-bake default map with fix
- **Files:** `assets/maps/default_map.json` (modify)
- **What:** Regenerate with seed 21, sealevel -1 using MapGenTool
- **Why:** Verify the ripple is gone in the shipped map.
- **Depends on:** 2

## 5. Commit
- **What:** Commit with message explaining the two-pass propagation fix.
- **Depends on:** 3, 4
