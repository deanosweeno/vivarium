# Plan: Radius-aware blob collision (sim layer push-apart)

## Goal
Add blob-to-blob and radius-aware wall collision in the headless sim. Blobs have radius 0.5f. Walls reflect when the edge touches. Blobs push apart on overlap (no velocity exchange).

## Design

### Blob.Radius = 0.5f
Hardcoded constant. 1×1 cube → circumscribed radius 0.5f. No per-blob variable radius yet.

### Arena radius overloads
New methods that accept a radius, checking against the boundary minus the radius:
- `Contains(Vector2 position, float radius)` — check if the circle fits within bounds
- `Clamp(Vector2 position, float radius)` — clamp so the edge sits on wall
- `Reflect(Vector2 position, Vector2 velocity, float radius)` — reflect + clamp with radius
Original parameterless methods remain (delegate to radius=0 for backward compat).

### Push-apart algorithm (Simulator)
After `blob.Tick()` moves all blobs, pairwise compare:
- `distance = |a.Position - b.Position|`
- `minDist = a.Radius + b.Radius` (= 1.0f for now)
- If `distance < minDist` and `distance > 0`:
  - `overlap = minDist - distance`
  - `axis = (a.Position - b.Position) / distance`
  - Move each blob outward by `overlap / 2` along axis
  - No velocity modification (simple push, not elastic)

Corner case: if `distance == 0` (spawned exactly on same spot), nudge by small random offset.

### Spawn overlap prevention
Before spawning, check distance to all existing blobs. If overlapping, try a few random positions. If all fail, clamp to arena + let next-tick push-apart handle it. (Phase 1: simple rejection loop, 10 attempts max.)

### Determinism
Push-apart is deterministic when RNG is seeded — it only uses position arithmetic and a deterministic loop over the blob list. No randomness in collision resolution.

## Tasks

### Task 0: Add Blob.Radius
- Add `public const float Radius = 0.5f;` to Blob.cs

### Task 1: Add radius-aware Arena overloads
- `Contains(Vector2 position, float radius)` — shrink bounds by radius
- `Clamp(Vector2 position, float radius)` — clamp so position ± radius stays inside
- `Reflect(Vector2 position, Vector2 velocity, float radius)` — use radius-aware Contains/Clamp
- Keep existing overloads, make them call radius-aware versions with radius=0

### Task 2: Update Blob.Tick for radius-aware walls
- Pass `Radius` to `arena.Contains` and `arena.Reflect`
- Wall reflect now triggers when the blob's edge touches, not its center

### Task 3: Add Simulator.ResolveBlobCollisions()
- Private method called at end of `Tick` after all blobs have moved
- Pairwise loop: check distance, push apart if overlapping
- Guard against distance=0 (same position)

### Task 4: Update SpawnBlob for overlap prevention
- After clamping to arena, check distance to all existing blobs
- If overlapping, try up to 10 random positions within arena
- On all failures, return the clamped position and let collisions resolve

### Task 5: Update existing tests for radius
- `BlobClampedToArena` — clamp now uses wall minus radius (max bounds = 5 - 0.5 = 4.5)
- `BlobBouncesOffWall` — expect reflect at edge-0.5, not edge
- `BlobMovesWhenSliding` — no wall collision in test, no change

### Task 6: Add collision tests
- `PushApart_OverlappingBlobsSeparated` — two blobs at distance < 1.0 are pushed to distance >= 1.0
- `PushApart_NoOverlapPreserved` — two blobs at distance > 1.0 unchanged
- `PushApart_DistanceZero_NoCrash` — same-position blobs get separated without NaN
- `DeterministicCollisions_SameSeedSameOutcome` — two sims with same seed + spawns + ticks produce identical state

### Task 7: Verification gate
- `dotnet build` — zero errors
- `dotnet test` — all tests pass (existing 13 + new collision tests)
- `godot --headless` — no C# errors

## Files touched
| File | Change |
|------|--------|
| `core/Blob.cs` | Add `Radius` constant |
| `core/Arena.cs` | Add radius-aware overloads |
| `core/Simulator.cs` | Add `ResolveBlobCollisions()`, update `SpawnBlob` |
| `core/BlobTests.cs` | Update wall-clamp and wall-bounce tests for radius |
| `core/SimulatorTests.cs` | Add push-apart + determinism tests |
