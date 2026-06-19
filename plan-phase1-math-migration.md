# Implementation Plan: Phase 1 — 3D Math Migration + Arena Upgrade

## Goal
Replace `System.Numerics.Vector2` with `System.Numerics.Vector3` in `core/`, extend
`Arena` from a 2D rectangle to a 3D volume with floor plane, and update all
consumers (`Blob`, `Simulator`, tests, scripts). The simulation remains XZ-ground-
bound during this phase (no gravity yet — that's Phase 3). The goal is purely to
switch the math types and arena bounds without changing behavior.

### Current axis mapping (2D)
| Arena | World meaning |
|---|---|
| `Vector2.X` | World X |
| `Vector2.Y` | World Z |

### Target axis mapping (3D)
| Arena | World meaning |
|---|---|
| `Vector3.X` | World X |
| `Vector3.Y` | World Y (vertical — up) |
| `Vector3.Z` | World Z |

---

## 1. Upgrade Arena from Vector2 to Vector3 with Y-axis bounds
- **Files:** `core/Arena.cs` (modify)
- **What:**
  - Change all `Vector2` → `Vector3` throughout the file
  - Add `MinY` and `MaxY` properties (default floor = 0, no ceiling = `float.MaxValue`)
  - Constructor changes: `Arena(Vector3 center, Vector3 size)` or keep existing with defaults
  - Rename `MinZ`/`MaxZ` to use `.Z` component directly (no longer stored in `.Y`)
  - `Contains(position, radius)` — add Y-axis check: `position.Y - radius >= MinY && position.Y + radius <= MaxY`
  - `Clamp(position, radius)` — add Y clamping: `Math.Clamp(position.Y, MinY + radius, MaxY - radius)`
  - `Reflect(position, velocity, radius)` — add Y-axis reflection (floor/ceiling bounce)
  - Keep the two-parameter overloads (position-only, position+radius) for each method
  - Keep the struct `readonly`
- **Why:** Arena is the geometry foundation. Everything else references its types.
- **Depends on:** none

## 2. Add `System.Numerics.Vector3` support to the Arena size model
- **Files:** `core/Arena.cs` (same file, separate logical step)
- **What:**
  - Arena currently stores `Size` as `Vector2` where `.Y` stores world-Z extent
  - Replace with direct `Vector3` storage: `float Width`, `float Height`, `float Depth` or keep `Vector3 Size`
  - Re-derive `MinX`, `MaxX`, `MinY`, `MaxY`, `MinZ`, `MaxZ` from `Center` + `Size`
  - Decide: keep `Center` as `Vector3` with `Center.Y` = midpoint of arena vertical space
  - Provide a convenient factory: `static Arena GroundArena(float width, float depth, float height = float.MaxValue)` where the arena sits on Y=0
- **Why:** Clean API so callers don't need to reason about which axis is stored where.
- **Depends on:** task 1

## 3. Update Blob to use Vector3
- **Files:** `core/Blob.cs` (modify)
- **What:**
  - `Position`: `Vector2` → `Vector3` (Y = 0 for ground creatures; set in constructor)
  - `Velocity`: `Vector2` → `Vector3`
  - Constructor: `Blob(Vector3 position, ...)` — sets `Position.Y = 0` explicitly
  - `Radius`: keep as `const float Radius = 0.5f` (unchanged — still a sphere radius)
  - `Tick(double delta, Arena arena, Random rng)` — no behavior change; wander direction stays in XZ plane:
    - `direction = new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle))`
  - `Position += Velocity * (float)delta` — now uses `Vector3` addition (Y stays 0 since velocity.Y is 0)
  - All `Vector2.Zero` → `Vector3.Zero`
  - All `new Vector2(...)` literals → `new Vector3(x, 0, z)` with explicit Y
  - `StartIdle`: `Velocity = Vector3.Zero`
- **Why:** Blob's state must match the new arena geometry. Wander stays XZ-plane-only this phase.
- **Depends on:** task 1 (Arena already switched)

## 4. Update Simulator to use Vector3 entities
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - `SpawnBlob(Vector3 position)` — accept Vector3, clamp via arena, spawn with Y=0
  - `OverlapsAny(Vector3 position, float minDist)` — use `Vector3` distance
  - `ResolveBlobCollisions()` — `delta = a.Position - b.Position` now computes 3D distance (all blobs at Y=0 so same result for now)
  - Same-position fallback: `delta = new Vector3(0.001f, 0f, 0f)` (was `Vector2`)
  - Arena construction in VivariumMain currently creates `new Arena(Vector2.Zero, new Vector2(10, 10))` — will need updating in task 7
  - Tick loop: `blob.Tick(delta, Arena, Rng)` — unchanged (delegates to blob)
- **Why:** Simulator is the orchestration layer; its types must align with Blob and Arena.
- **Depends on:** task 3 (Blob switched)

## 5. Update BlobVisual position mapping
- **Files:** `scripts/BlobVisual.cs` (modify)
- **What:**
  - Old: `Position = new Vector3(model.Position.X, 0.5f, model.Position.Y)`
  - New: `Position = new Vector3(model.Position.X, model.Position.Y + 0.5f, model.Position.Z)`
  - This places the cube visual half a unit above the model's Y position (so it sits on the model's origin)
  - For ground creatures (Y=0), the cube center sits at Y=0.5 — same visual result as before
- **Why:** Model Y is now vertical; must map to Godot's Y-up correctly.
- **Depends on:** task 4 (Simulator produces Vector3 positions)

## 6. Update VivariumMain arena construction and spawn logic
- **Files:** `scripts/VivariumMain.cs` (modify)
- **What:**
  - Old: `new Arena(SNVector2.Zero, new SNVector2(10, 10))`
  - New: use the factory from task 2, e.g. `Arena.GroundArena(10, 10)` or `new Arena(Vector3.Zero, new Vector3(10, float.MaxValue, 10))`
  - Remove `using SNVector2 = System.Numerics.Vector2;` (or change to Vector3)
  - Spawn site from click: `Vector3(point.X, 0f, point.Z)` — Y=0 since floor is at 0
  - Random initial spawns: `new Vector3(x, 0f, z)`
  - `_sim.Arena.Contains(worldPos)` → now checks Vector3 (need to update Contains signatures in Arena)
- **Why:** Bridges the core/ changes to the Godot runtime.
- **Depends on:** task 2 (Arena factory available)

## 7. Update Blob tests for Vector3
- **Files:** `core/BlobTests.cs` (modify)
- **What:**
  - All `Vector2.Zero` → `Vector3.Zero`
  - All `new Vector2(x, z)` → `new Vector3(x, 0, z)` with explicit Y=0
  - `Arena` construction updates to match new factory/signature
  - Assertions with `Position.X` and `Position.Y` → `.X` and `.Z`; add `.Y` assertions where appropriate
  - Example: `Assert.Equal(2.25f, blob.Position.X, 3)` + new `Assert.Equal(0f, blob.Position.Y, 3)`
  - `DeterministicWander_SameSeedProducesSamePath` — should still pass; path is in XZ plane
- **Why:** Tests must compile and pass after the type migration.
- **Depends on:** task 3 (Blob.cs updated)

## 8. Update Simulator tests for Vector3
- **Files:** `core/SimulatorTests.cs` (modify)
- **What:**
  - All `Vector2.Zero` → `Vector3.Zero`
  - All `new Vector2(x, z)` → `new Vector3(x, 0, z)`
  - Arena construction updated
  - `SpawnBlob` calls updated to pass `Vector3` positions
  - In `PushApart_OverlappingBlobsSeparated`: overlapping blobs at `Vector3(1, 0, 0)` and `Vector3(1.6f, 0, 0)`
  - In `PushApart_DistanceZero_NoCrash`: both at `Vector3.Zero`, fallback delta becomes `Vector3(0.001f, 0, 0)`
  - `DeterministicCollisions_SameSeedSameOutcome` — update spawn positions
- **Why:** Tests must compile and pass after the type migration.
- **Depends on:** task 4 (Simulator.cs updated)

## 9. Add Arena 3D-specific tests
- **Files:** `core/ArenaTests.cs` (new)
- **What:**
  - `Contains_3D`: a sphere at `(1, 1, 1)` with radius 0.5 in an arena `GroundArena(10, 10, 5)` should be within bounds
  - `Contains_Above_Floor`: a sphere at `(1, 10, 1)` should be outside if MaxY < 10.5
  - `Contains_Below_Floor`: `(1, -0.3, 1)` radius 0.5 → outside (penetrates floor)
  - `Clamp_To_Floor`: `(1, -2, 1)` radius 0.5 → clamped to `(1, 0.5, 1)` (MinY + radius)
  - `Reflect_Floor`: velocity `(0, -5, 0)` at floor → reflected to `(0, 5, 0)`
  - `Reflect_Wall`: XZ wall reflection unchanged behavior (existing test in BlobTests covers)
  - `Deterministic_Arena`: two arenas with same config produce same `Clamp`/`Reflect` results
- **Why:** Arena is the only component gaining new geometry behavior in this phase.
- **Depends on:** task 2 (Arena API finalized)

## 10. Build and run full test suite
- **What:**
  - `dotnet build` in `core/` — zero errors
  - `dotnet test` in `core/` — all tests pass (updated existing + new ArenaTests)
  - `dotnet build` in project root — Godot project compiles (scripts bridge updated)
- **Why:** Gate before claiming Phase 1 done.
- **Depends on:** tasks 1–9 complete

---

## Migration cheat-sheet: Vector2 → Vector3

| Old (core/) | New (core/) |
|---|---|
| `Vector2 position` | `Vector3 position` |
| `Vector2.Zero` | `Vector3.Zero` |
| `new Vector2(x, z)` | `new Vector3(x, 0, z)` |
| `position.X` (world X) | `position.X` (unchanged) |
| `position.Y` (world Z) | `position.Z` (was .Y in Vector2) |
| — | `position.Y` (world vertical) |
| `Arena(center, size)` | `Arena(Vector3 center, Vector3 size)` or `Arena.GroundArena(w, d)` |
| `MinZ` / `MaxZ` (from .Y) | `MinZ` / `MaxZ` (from .Z directly) |
| `Contains(pos, radius)` — 2D | `Contains(pos, radius)` — 3D (adds Y check) |
| `Clamp(pos, radius)` — 2D | `Clamp(pos, radius)` — 3D (adds Y clamp) |
| `Reflect(pos, vel)` — 2D | `Reflect(pos, vel)` — 3D (adds Y reflect) |

| Old (scripts/) | New (scripts/) |
|---|---|
| `SNVector2 pos` | `Vector3 pos` or use `System.Numerics.Vector3` directly |
| `new SNVector2(x, z)` | `new Vector3(x, 0, z)` |
| `model.Position.Y` (was Z) | `model.Position.Z` |
| `Position=new Vector3(model.X, 0.5f, model.Y)` | `Position=new Vector3(model.X, model.Y+0.5f, model.Z)` |

---

## Notes

- **No behavior change expected** in the XZ plane. Blobs wander exactly as before;
  they just carry a `Vector3` with `Y=0` and `Velocity.Y=0`.
- **Collision math** transitions from 2D to 3D distance automatically via
  `Vector3.Length()`. Since all blobs share Y=0, the 3D distance equals the
  old 2D distance. We verify this with the existing deterministic tests.
- **Arena Y bounds** default to `MinY=0, MaxY=float.MaxValue` (no ceiling).
  This keeps current behavior — the floor exists but isn't reachable since
  nothing applies gravity yet.
- **`Radius` stays at 0.5** — it's now a sphere radius rather than a circle
  radius, but the numeric value and collision behavior are identical in the
  XZ plane.
