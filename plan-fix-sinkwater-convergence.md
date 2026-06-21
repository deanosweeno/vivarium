# Implementation Plan: Convergent SinkWater propagation (iterate to fixpoint)

## Problem
The 2-pass fix only propagates the shore depression 1-2 cells inward.
Lakes wider than ~3 cells still show terracing because interior cells
never reach the shore depression level.

Example with a wide lake (shore land at -0.5, WaterDepth 1.5):
```
Pass 1:  shore=-2.0  r1=-1.5  r2=-1.5  r3=-1.4  ...  center=-1.4
Pass 2:  shore=-2.0  r1=-2.0  r2=-1.5  r3=-1.5  ...  center=-1.4
                                          ↑ still higher than shore
```

## Fix
Replace the single pass-2 loop with a convergence loop: snapshot → propagate
→ repeat until no cell changes height. Each iteration propagates the
depression one more cell inward.

## 1. Replace pass-2 with convergence loop
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** Replace the current pass-2 block with:
  ```csharp
  // Pass 2+: propagate depression inward until no water cell changes.
  bool changed;
  do
  {
      changed = false;
      var snap = SnapshotHeights(map);
      for (int cz = 0; cz < map.Depth; cz++)
      for (int cx = 0; cx < map.Width; cx++)
      {
          var cell = map.GetCell(cx, cz);
          if (cell.Terrain != Terrain.Water)
              continue;
          float minNeighbor = float.MaxValue;
          for (int dz = -1; dz <= 1; dz++)
          for (int dx = -1; dx <= 1; dx++)
          {
              if (dx == 0 && dz == 0) continue;
              int nx = cx + dx, nz = cz + dz;
              if (!map.InBounds(nx, nz)) continue;
              minNeighbor = MathF.Min(minNeighbor, snap[nz * map.Width + nx]);
          }
          float newHeight = MathF.Min(cell.Height, minNeighbor);
          if (newHeight < cell.Height - 1e-7f)
          {
              cell.Height = newHeight;
              changed = true;
          }
          map.SetCell(cx, cz, cell);
      }
  } while (changed);
  ```
- **Why:** Passes iterate until the shore depression fully floods the basin.
  Heights only move downward → guaranteed termination. `1e-7f` epsilon
  prevents float-oscillation infinite loops.
- **Depends on:** none

## 2. Update doc comment
- **Files:** `core/MapGenerator.cs` (modify)
- **What:** Update SinkWater's doc comment to describe convergence loop
  instead of "two-pass".
- **Depends on:** 1

## 3. Verify tests pass
- **Files:** `core/MapGeneratorTests.cs` (no changes)
- **What:** `dotnet test` — all 145 tests pass. Existing tests only check:
  - Shore contract (`waterHeight ≤ shore - WaterDepth`) — preserved
  - Water cells never raise — convergence only lowers
  - Determinism — still fixed order, snapshot per pass
- **Depends on:** 1

## 4. Re-bake default map
- **Files:** `assets/maps/default_map.json` (modify)
- **What:** Regenerate with seed 21, sealevel -1
- **Why:** Verify terracing gone.
- **Depends on:** 1

## 5. Commit
- **Depends on:** 3, 4
