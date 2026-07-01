# Implementation Plan: Fix creatures dipping below water surface at lake edges

## Root cause

After terrain collision (`SlideAgainstTerrain`) keeps creatures horizontally on walkable cells,
`PlaceOnGround` snaps Y via `MapData.HeightAt()`, which does **bilinear interpolation** across
the four nearest cell centers. When a walkable cell (Grass/Sand/Marsh) borders a water cell
whose height is sunk below `SeaLevel` by the `SinkWater` pass, the interpolation pulls the
creature's Y toward the water cell's lower height — placing it below the visible water plane.

```
Grass cell (height=0.0)       Water cell (height=-1.5, sunk)
         │                             │
    🐑───●─────────────────────────────●───
         │    interpolated Y ≈ -0.3    │
         │    ↑ below SeaLevel (0.0)   │
```

## Fix strategy

In `Simulator.GroundFloor`, clamp the height to `Map.SeaLevel` when the creature is standing on a
walkable cell. This is a one-line change in the single seam all ground-placement goes through
(`PlaceOnGround`, flock anchor formation, flock anchor drift). `HeightAt` itself stays unchanged
so the terrain mesh continues to render smooth basins.

Why this is safe: after `FloodWater` in the generator, every cell below `SeaLevel` is marked
`Terrain.Water` — walkable cells are always at or above `SeaLevel`. So clamping to `SeaLevel`
only affects the interpolation bleed case, never legitimate terrain.

## 1. Fix `GroundFloor` clamp
- **Files:** `core/Simulator.cs` (modify)
- **What:** In `GroundFloor`, after calling `Map.HeightAt(pos)`, if `Map.IsWalkableWorld(pos)` is
  true and the height is below `Map.SeaLevel`, clamp to `SeaLevel`.
- **Why:** Prevents bilinear interpolation from pulling walkable-cell Y below the water surface.
  The single seam fixes creatures, flock anchors, and herd-spawner placement.
- **Depends on:** none

## 2. Update `Surface_Blob_FollowsTerrainDown` test
- **Files:** `core/SimulatorTests.cs` (modify)
- **What:** Add `map.SeaLevel = -3f` to the `UniformHeightMap` helper (or inline in the test)
  so the -2.0-height Grass cells are above `SeaLevel`. The test asserts creatures snap DOWN
  onto below-zero walkable terrain, and that must still work — the clamp only kicks in when the
  interpolated height dips below the water line, not below zero.
- **Why:** The `UniformHeightMap` helper creates walkable Grass at an arbitrary height but leaves
  `SeaLevel` at the default 0. With the new clamp, a walkable cell at -2.0 with `SeaLevel=0`
  would be raised to 0 — the blob would float. Setting `SeaLevel` below -2.0 restores the
  original intent: the creature follows the terrain surface down to -2.0.
- **Depends on:** 1

## 3. Add test for water-edge interpolation bleed
- **Files:** `core/SimulatorTests.cs` (modify)
- **What:** Add a test `Surface_Blob_NextToWater_StaysAboveSeaLevel`. Create a map with two
  adjacent cells: walkable Grass (height 0.0, `SeaLevel=0`) next to Water (height -1.5, sunk).
  Spawn a blob on the Grass cell, tick several times, and assert the blob's Y is never below
  `SeaLevel + radius`.
- **Why:** Regression test for the exact scenario: a creature near a water cell should not dip
  below the visible water plane due to bilinear interpolation bleeding.
- **Depends on:** 1

## 4. Verify
- **What:** Run the full gate — `dotnet build`, `dotnet test`, and confirm all tests pass
  (especially `Surface_Blob_FollowsTerrainDown` and the new test).
- **Depends on:** 1, 2, 3
