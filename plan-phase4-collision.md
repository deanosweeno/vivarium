# Implementation Plan: Phase 4 — 3D Sphere-Sphere Collision

## Goal
Add creature-creature and blob-creature push-apart collision to the Simulator,
unify all collision into a single `ResolveEntityCollisions()` method, and add
a post-collision ground re-clamp to prevent entities from being pushed below
the floor.

## Architecture

```
Simulator.Tick(delta)
├── For each Creature:
│   1. Apply gravity
│   2. Movement.Tick (wander + XZ wall bounce)
│   3. Ground clamp (Y >= floor + radius, zero Y velocity if falling)
├── For each Blob:
│   blob.Tick(delta, arena, rng)  (unchanged)
├── ResolveEntityCollisions()
│   ├── Blob-Blob push-apart       (unchanged algorithm, extracted to helper)
│   ├── Creature-Creature push-apart  (NEW)
│   └── Blob-Creature push-apart     (NEW)
└── Post-collision ground re-clamp
    └── For each creature: if Y < floor + radius, clamp Y up
        (position-only, no velocity change — already corrected at step 3)
```

### Collision algorithm (shared)
All push-apart uses the same sphere-sphere overlap resolution:
1. `minDist = radiusA + radiusB`
2. If `distance >= minDist`, skip
3. If `distance ≈ 0`, nudge apart on fixed axis
4. `overlap = minDist - distance`
5. `push = normalized_axis * (overlap / 2)` (half each, equal mass assumption)
6. a.Position += push, b.Position -= push

---

## 1. Extract `PushApart` static helper
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Add `private static void PushApart(ref Vector3 a, ref Vector3 b, float minDist)`
    to `Simulator`.
  - Move the existing blob-blob push-apart algorithm into this helper
    (delta calculation, distance check, zero-distance nudge, overlap push).
  - Pure static method — no instance state, no mutation except the two ref positions.
  - Signature: `PushApart(ref Vector3 posA, ref Vector3 posB, float minDistance)`
- **Why:** The exact same algorithm is needed for 3 pair types
  (Blob-Blob, Creature-Creature, Blob-Creature). Extract once.
- **Depends on:** none

## 2. Replace `ResolveBlobCollisions()` with `ResolveEntityCollisions()`
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Delete the existing `ResolveBlobCollisions()` method.
  - Add `private void ResolveEntityCollisions()` with three nested loops:
    ```
    // Blob-Blob (unchanged radius = 2 * Blob.Radius = 1.0)
    for i in 0..Blobs.Count, j in i+1..Blobs.Count:
        PushApart(ref Blobs[i].Position, ref Blobs[j].Position, Blob.Radius * 2f)

    // Creature-Creature
    for i in 0..Creatures.Count, j in i+1..Creatures.Count:
        float minDist = Creatures[i].Traits.Radius + Creatures[j].Traits.Radius
        PushApart(ref Creatures[i].Position, ref Creatures[j].Position, minDist)

    // Blob-Creature
    for i in 0..Blobs.Count, j in 0..Creatures.Count:
        float minDist = Blob.Radius + Creatures[j].Traits.Radius
        PushApart(ref Blobs[i].Position, ref Creatures[j].Position, minDist)
    ```
  - No interface or base class needed — `Blob.Position` and `Creature.Position`
    are the same `Vector3` type; radii come from their respective sources.
- **Why:** Unifies all entity collision into one call. Prepares for Phase 5
  when Blob migrates to Creature (at which point the loops collapse to one).
- **Depends on:** task 1 (PushApart helper)

## 3. Update `Tick()` to call unified collision and post-re-clamp
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Replace `ResolveBlobCollisions()` call with `ResolveEntityCollisions()`
  - After collision, iterate all creatures and apply a Y-min clamp:
    ```
    foreach (var creature in Creatures)
    {
        float floor = Arena.MinY + creature.Traits.Radius;
        if (creature.Position.Y < floor)
            creature.Position = new Vector3(creature.Position.X, floor, creature.Position.Z);
    }
    ```
  - Similarly for blobs (collision push on Y axis could push them below floor):
    ```
    foreach (var blob in Blobs)
    {
        float floor = Arena.MinY + Blob.Radius;
        if (blob.Position.Y < floor)
            blob.Position = new Vector3(blob.Position.X, floor, blob.Position.Z);
    }
    ```
  - These re-clamps are **position-only** (no velocity zeroing). The
    velocity-zeroing only happens at step 3 (ground clamp during normal
    falling). A creature pushed below floor by collision was not falling —
    it was displaced horizontally.
  - Remove `// TODO Phase 4` comment.
  - Update the doc comment on `Tick()` to describe the full pipeline.
- **Why:** Collision can push entities below the floor (especially
  creature-creature pushes along a diagonal that has a Y component).
  The re-clamp guarantees entities never sink below the floor.
- **Depends on:** task 2

## 4. Write collision tests
- **Files:** `core/SimulatorTests.cs` (modify — append)
- **What:** Add tests after the existing CreatureGroundClamp_ZeroesVerticalVelocity test:
  1. `CreatureCreature_PushApart` — spawn two creatures with overlapping positions
     (add directly to list), set traits.Radius for known overlap, tick, verify
     they are separated (`distance >= sum of radii`)
  2. `CreatureCreature_PushApart3D` — creatures at different Y heights
     (e.g., Y=2 and Y=3 with radius 1.0, overlapping), verify they're pushed
     apart in 3D
  3. `CreatureCreature_PushApart_DistanceZero` — two creatures at exact same
     position, verify no NaN/infinity, positions differ
  4. `BlobCreature_PushApart` — blob and creature overlapping, verify both
     positions changed, distance >= Blob.Radius + creature.Traits.Radius
  5. `BlobBlobCollision_StillWorks` — existing blob-blob push-apart behavior
     unchanged (use the same pattern as existing `PushApart_OverlappingBlobsSeparated` test)
  6. `PostCollision_GroundReclamp_Creature` — set up two creatures where
     collision forces one below Y=floor, tick, verify Y >= floor
  7. `PostCollision_GroundReclamp_Blob` — set up blob + creature where
     collision pushes blob below floor, tick, verify blob Y >= floor
  8. `Deterministic_Collisions_SameSeedSameOutcome` — two sims same seed,
     spawn overlapping entities, tick many frames, verify identical positions
  9. `Collision_EntitiesInDifferentLists_DontBreakEachOther` — spawn 1 blob
     and 1 creature far apart, tick many frames, both still in list and alive
  - Follow existing test patterns: create entities with `new Blob(...)` /
    `new Creature(...)`, add directly to `sim.Blobs` / `sim.Creatures`,
    force Idle state for blobs, use `sim.Tick()` to trigger collision.
- **Why:** All three collision pair types must be tested, plus edge cases
  (zero-distance, ground re-clamp, determinism, cross-list isolation).
- **Depends on:** task 3 (unified collision in Tick)

## 5. Build and run full test suite
- **What:**
  - `dotnet build` in `core/` — zero errors, zero warnings
  - `dotnet test` in `core/` — all tests pass (existing 62 + ~9 new ≥ 71 total)
  - `dotnet build` root — Godot project compiles clean
  - `godot --headless --quit` — clean launch
- **Why:** Verification gate per AGENTS.md. Must confirm existing tests
  still pass and new collision behavior works.
- **Depends on:** tasks 1–4 complete

---

## Design notes

### Why no `ICollidable` interface?
Not needed yet. Both `Blob` and `Creature` have `Vector3 Position` (public field /
property with internal set) and a way to get radius. Specialized entity access
is localized to 3 short loops in one method. When Phase 5 migrates Blob to
inherit Creature, the cross-list loops collapse to a single loop. Introducing an
interface now would create abstraction debt without reuse gain.

### Why post-collision re-clamp is position-only?
The initial ground clamp (step 3 in Tick) zeroes `Velocity.Y` when an entity
falls through the floor. Post-collision re-clamp handles a different case:
horizontal/diagonal collision push displacing an entity below floor. The entity
wasn't falling — it was shoved. Zeroing velocity here would incorrectly halt
a creature that's legitimately sliding along the ground. The next tick's normal
ground clamp will handle any residual falling.

### Why `PushApart` takes `ref Vector3` instead of mutating entity references?
`Blob.Position` is a `Vector3` field, `Creature.Position` is a property with
`get; internal set;`. Passing the field/property directly keeps the helper
type-agnostic. At the call site, the position is already read for radius
calculation; the `ref` avoids a second read.

### What about blob Y? Blobs stay at Y=0.5
Blobs are always spawned at ground level (Y=0.5 = Arena.MinY + Blob.Radius)
and their `Tick()` only moves them in XZ. Creature collision could push them in
Y (e.g., a creature above them pushes down). The post-collision re-clamp
handles this — it pushes blobs back to Y=0.5.
