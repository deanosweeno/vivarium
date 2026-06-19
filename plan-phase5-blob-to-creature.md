# Implementation Plan: Phase 5 — Blob → Creature Migration

## Goal
Refactor `Blob` to inherit from `Creature`, unifying the Simulator's dual entity
lists (`List<Blob>` + `List<Creature>`) into a single `List<Creature>`. The
blob's Idle/Slide wander state machine moves into a new `BlobWalkMode : IMovementMode`
— the blob's movement behavior becomes composable like WalkMode. Gravity and
ground clamping are still applied by the Simulator; blobs set `GravityScale = 0`
to opt out.

## Architecture change

```
Before:                               After:
Simulator                             Simulator
├── List<Blob> Blobs                  └── List<Creature> Entities
│   └── Blob (standalone)                 ├── Blob : Creature ← BlobWalkMode
│       ├── Position, Velocity                ├── Traits (GravityScale=0)
│       ├── Radius=0.5                        ├── R, G, B
│       └── WanderState + Idle/Slide          └── (color helpers)
│                                           
├── List<Creature> Creatures              └── Creature + WalkMode
│   └── Creature + WalkMode                   ├── Traits
│                                             └── WalkMode (continuous wander)
├── Tick():
│   ├── Creature: gravity→movement→ground     ├── Tick():
│   ├── Blob: Tick (separate)                 │   ALL entities: gravity→movement→ground
│   ├── ResolveEntityCollisions()             │   ResolveEntityCollisions() (1 loop)
│   │   ├── Blob-Blob                         │   Post-collision re-clamp
│   │   ├── Creature-Creature                 │
│   │   └── Blob-Creature                     │
│   └── Post-collision re-clamp
```

---

## 1. Create `BlobWalkMode`
- **Files:** `core/BlobWalkMode.cs` (**new**)
- **What:**
  - New class `BlobWalkMode : IMovementMode` that ports the Idle/Slide wander
    state machine from Blob.Tick into an IMovementMode implementation.
  - Internal state: `_state` (WanderState), `_stateTimer`, `_direction`.
  - Constructor: `BlobWalkMode(Random rng)` — picks initial Idle duration.
  - `Tick(delta, creature, arena, rng)`:
    - Decrement state timer.
    - **Idle state:** velocity = 0. No position integration. When timer expires,
      transition to Slide (pick random XZ direction, set timer).
    - **Slide state:** set `creature.Velocity = direction * speed` (speed is
      random between 0.2–0.6, NOT from Traits.MaxSpeed). Integrate position.
      XZ wall bounce (same algorithm as WalkMode: clamp + reflect, reset timer).
      When timer expires, transition to Idle.
    - **Y velocity is explicitly set to zero** — blobs walk on the ground.
      Gravity is disabled via Traits.GravityScale=0, so Velocity.Y stays 0.
  - Public properties for testing: `State` (WanderState), `StateTimer`.
  - Move `WanderState` enum into this file (or keep in Blob.cs? → put in
    `BlobWalkMode.cs` since it's the only consumer).
  - Keep the Idle/Slide tempo constants (Idle: 0.5–3.0s, Slide: 1.0–4.0s).
    Keep speed ranges (SpeedMin=0.2, SpeedMax=0.6).
- **Why:** The Idle/Slide behavior is the blob's *movement mode*. Making it an
  IMovementMode keeps the architecture composable and lets the Simulator treat
  all entities uniformly through the creature pipeline.
- **Depends on:** none

## 2. Refactor `Blob` to inherit `Creature`
- **Files:** `core/Blob.cs` (major rewrite)
- **What:**
  - `Blob : Creature` (not sealed — follow Creature's pattern)
  - Constructor: `Blob(Vector3 position, float r, float g, float b, Random rng, CreatureTraits? traits = null)`
    - Calls `base(position, traits ?? DefaultBlobTraits, new BlobWalkMode(rng))`
  - Remove `Position`, `Velocity`, `Radius` — inherited from Creature.
  - Remove `Tick()`, `StartIdle()`, `StartSlide()` — replaced by BlobWalkMode.
  - Remove `WanderState` — enum moves to BlobWalkMode.cs.
  - Remove `State`, `StateTimer` — now on BlobWalkMode.
  - Remove Idle/Slide tempo constants — now in BlobWalkMode.
  - Keep: `R`, `G`, `B`, `RandomPastelColor()`, `HsvToRgb()`, `RandomRange()`.
  - Add `public static CreatureTraits DefaultBlobTraits`:
    ```
    new CreatureTraits { Radius = 0.5f, MaxSpeed = 0.6f, GravityScale = 0f }
    ```
    (GravityScale=0 so blobs don't fall — they're ground-only.)
  - Remove `public const float Radius = 0.5f` — use `DefaultBlobTraits.Radius`.
- **Why:** Blob becomes a thin Creature subclass — just adds color. All physics
  and AI are in the composable IMovementMode and creature pipeline.
- **Depends on:** task 1 (BlobWalkMode exists)

## 3. Unify Simulator entity system
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Replace two lists with one:
    ```csharp
    public List<Creature> Entities { get; } = new();
    ```
    Remove `Blobs`, `Creatures`.
  - Replace `BlobCount` + `CreatureCount` with `EntityCount => Entities.Count`.
  - Update `SpawnBlob`:
    - Use `Blob.DefaultBlobTraits.Radius` for clamping (was `Blob.Radius`).
    - Creates Blob, adds to `Entities`.
  - Update `SpawnCreature`:
    - Accept optional `IMovementMode? movement = null` parameter
      (defaults to `new WalkMode()`).
    - Creates Creature, adds to `Entities`.
  - Update `OverlapsAny`:
    - Single loop over `Entities`.
    - Match against `entity.Traits.Radius` or just use the passed `minDist`.
  - Update `Tick()`:
    - Single foreach over `Entities` — gravity → movement tick → ground clamp.
    - Remove the separate blob loop.
  - Update `ResolveEntityCollisions()`:
    - Single loop: `for i in 0..Entities.Count, j in i+1..Entities.Count`.
    - `minDist = Entities[i].Traits.Radius + Entities[j].Traits.Radius`.
    - Call `PushApart(ref Entities[i].Position, ref Entities[j].Position, minDist)`.
  - Update post-collision re-clamp:
    - Single foreach over `Entities`.
    - Clamp `Position.Y >= Arena.MinY + entity.Traits.Radius`.
  - Update doc comments on `Tick`, methods, and class-level summary.
- **Why:** Unifying the entity lists is the payoff of the migration. One
  pipeline, one collision loop, simpler code. Everything is a Creature.
- **Depends on:** task 2 (Blob : Creature exists, Blob.Radius removed)

## 4. Port blob tests → `BlobWalkModeTests`
- **Files:** `core/BlobWalkModeTests.cs` (**new**), `core/BlobTests.cs` (rewrite)
- **What:**
  - **`BlobWalkModeTests.cs`** — 8 tests ported from BlobTests:
    1. `StartsIdle` — verify `State == WanderState.Idle` and `StateTimer > 0`.
    2. `IdleHasZeroVelocity` — verify creature.Velocity == Vector3.Zero in Idle.
    3. `MovesWhenSliding` — force into Sliding with known direction, tick, assert Position changed.
    4. `TransitionsToIdleAfterSlideTimerExpires` — force Slide with tiny timer, tick, assert State == Idle.
    5. `TransitionsToSlidingAfterIdleExpires` — force Idle with tiny timer, tick, assert State == Sliding and velocity non-zero.
    6. `ClampedToArenaXZ` — start outside bounds, tick, verify Position within arena.
    7. `BouncesOffWall` — start near right wall heading right, tick, verify velocity reflected.
    8. `DeterministicWander` — same seed, tick many times, positions match.
    - Test pattern: create Creature with BlobWalkMode, call mode.Tick() directly.
      ```csharp
      var rng = new Random(42);
      var mode = new BlobWalkMode(rng);
      var traits = Blob.DefaultBlobTraits;
      var creature = new Creature(Vector3.Zero, traits, mode);
      mode.Tick(delta, creature, arena, rng);
      ```
    - Force state/time via reflection? No! The test manipulates `State` and `StateTimer`
      which are `internal set` on BlobWalkMode. Keep these writable for tests:
      ```csharp
      public WanderState State { get; internal set; }
      public double StateTimer { get; internal set; }
      ```
  - **`BlobTests.cs`** — rewrite to minimal color tests:
    - `Constructor_SetsColor` — verify R, G, B stored.
    - `InheritsFromCreature` — verify `blob is Creature`.
    - `DefaultTraits_HasZeroGravityScale` — verify GravityScale = 0.
    - `DefaultTraits_HasDefaultRadius` — verify Radius = 0.5.
    - (Remove all physics/wander tests — those are in BlobWalkModeTests.)
- **Why:** Tests must follow the code. Wander behavior now lives in BlobWalkMode;
  Blob itself is a thin data class with color.
- **Depends on:** tasks 1–2 (BlobWalkMode + Blob : Creature exist)

## 5. Update `SimulatorTests` for unified entity list
- **Files:** `core/SimulatorTests.cs` (modify)
- **What:** Systematic rename + merge:
  - `sim.Blobs` → `sim.Entities` (and `sim.Creatures` → `sim.Entities`).
  - `sim.BlobCount` → `sim.EntityCount` (and `sim.CreatureCount` → `sim.EntityCount`).
  - `sim.entities[0]` assertions — after unification, entities are `Creature`
    references; cast to `Blob` where needed: `((Blob)sim.Entities[0]).R` etc.
  - Specific test adjustments:
    - `TickAdvancesAllBlobs` — now tests Tick advances ALL entities. Create
      blobs (which are creatures), force their WalkMode into Slide state with
      known velocity, tick, verify EntityCount and positions.
      **Change approach:** use `sim.SpawnBlob()` (they get BlobWalkMode), then
      get the mode via `((Blob)entity).Movement` and force state.
      Actually, Blob.Movement is `IMovementMode`. Need to cast to `BlobWalkMode`
      to access State/StateTimer. Add `using Vivarium.Core;` and cast.
    - `DeterministicSim_SameSeedProducesIdenticalState` — iterate `Entities`.
    - `PushApart_OverlappingBlobsSeparated` — create blobs, add to `Entities`,
      force Idle (via mode.State/StateTimer), tick, check positions.
    - `BlobsFarApart_NoUnnecessaryPush` — same, uses `Entities`.
    - `PushApart_BlobsAtSamePosition_NudgedApart` — same.
    - `DeterministicCollisions_SameSeedSameOutcome_v2` — same.
    - Creature pipeline tests (`SpawnCreature_*`, `CreatureGravity_*`, etc.)
      — `sim.Creatures` → `sim.Entities`, `sim.CreatureCount` → `sim.EntityCount`.
      These spawn creatures with `sim.SpawnCreature` so they're plain Creatures +
      WalkMode. Entities list contains both. Count assertions change.
    - `BlobPipeline_Unaffected_ByCreatureChanges` — **REMOVE**. After
      unification, there's no separate pipeline to protect. The test's
      semantics (add blob + creature, tick, both alive) is now the normal
      mode of operation covered by other tests.
    - Phase 4 collision tests:
      - `CreatureCreature_PushApart` — still valid (two creatures, same list).
      - `CreatureCreature_PushApart3D` — still valid.
      - `CreatureCreature_PushApart_DistanceZero` — still valid.
      - `BlobCreature_PushApart` — **keep but simplify**. Both are Creatures
        now. Test becomes "two creatures push apart when overlapping". Rename
        to `Entities_PushApart_BlobVsCreature` or just fold into other tests
        (both entities now have Radius from Traits).
        → **MERGE** with `CreatureCreature_PushApart` tests — no blob-vs-creature
        distinction needed. Keep as two entities with different traits radii.
      - `PostCollision_GroundReclamp_Creature` — still valid.
      - `PostCollision_GroundReclamp_Blob` — **SIMPLIFY**: blob is a creature
        now. Create a Blob (which is Creature), add to Entities, test
        that post-collision re-clamp works on it same as any creature.
        But blobs have GravityScale=0, so they don't fall. Ground re-clamp
        for blobs only triggers if collision pushes them below floor. The
        test can still be: place two entities where one pushes the other below
        floor, verify re-clamp.
      - `Deterministic_Collisions_SameSeedSameOutcome` — simplify: all entities
        in one list, single collision loop.
      - `Collision_NoCrossContamination_BetweenLists` — **REMOVE**. No separate
        lists exist anymore. Entities far apart don't collide — this is covered
        by other tests implicitly.
  - Total tests: ~65-70 (62 existing − 2 removed + 2 BlobTests + 8 BlobWalkModeTests ≈ 70).
- **Why:** Tests must reflect the unified architecture. Many tests become
  simpler; cross-list tests become redundant.
- **Depends on:** task 3 (Simulator unified)

## 6. Update Godot scripts
- **Files:** `scripts/VivariumMain.cs`, `scripts/BlobVisual.cs` (modify)
- **What:**
  - **`VivariumMain.cs`:**
    - `foreach (var entity in _sim.Entities)` instead of `_sim.Blobs`.
    - Inside loop: `if (entity is Blob blob)` — only instantiate visuals for blobs.
    - `_visuals` dictionary stays `Dictionary<Blob, BlobVisual>` (key is Blob).
    - SpawnBlob calls unchanged.
  - **`BlobVisual.cs`:**
    - `Init(Blob model)` unchanged — `model.Position` and `model.R/G/B` still
      accessible since Blob inherits Creature that has Position.
    - `SyncFromModel()` — `_model.Position.X/Y/Z` works the same.
- **Why:** Minimal script changes. The visual layer only cares about blobs
  (for now). Non-blob creatures have no visual representation yet.
- **Depends on:** task 3 (Simulator has `Entities`)

## 7. Build and full test gate
- **What:**
  - `dotnet build` in `core/` — zero errors, zero warnings.
  - `dotnet test` in `core/` — all tests pass (~70 total).
  - `dotnet build` root — Godot project compiles clean.
  - `godot --headless --quit` — clean launch.
- **Why:** Verification gate per AGENTS.md.
- **Depends on:** tasks 1–6 complete

---

## Design decisions

### Why BlobWalkMode instead of reusing WalkMode?
WalkMode is a continuous wander (no idle pauses, always moving). Blob's original
behavior has Idle pauses (0.5–3s stationary, then 1–4s moving). The Idle/Slide
rhythm is the blob's identity. BlobWalkMode preserves it while fitting the
IMovementMode contract.

### Why GravityScale=0 for blobs?
Blobs are ground-only. Setting GravityScale=0 means the Simulator's gravity step
is a no-op for blobs (`Velocity.Y -= Gravity * 0 * delta` = 0 change). BlobWalkMode
explicitly sets `creature.Velocity.Y = 0` each tick. The ground clamp still runs
but finds Position.Y already at floor — no change needed.

### Why keep `WanderState` as a public enum?
Tests need to assert state transitions (Idle → Slide, Slide → Idle). Making
`BlobWalkMode.State` public with `internal set` lets tests verify state without
adding a separate observable API.

### Why `DefaultBlobTraits` instead of keeping `Blob.Radius`?
After unification, the source of truth for an entity's radius is
`creature.Traits.Radius`. Having both `Blob.Radius` (const) and `Traits.Radius`
(mutable) creates confusion. `DefaultBlobTraits.Radius = 0.5f` makes it clear
this is the *default* — individual blobs could have different radii in the future.

### `SpawnBlob` return type stays `Blob`
Returns concrete `Blob` type so callers get R, G, B. The blob is added to
`Entities` as `Creature` (via `Entities.Add(blob)` upcast).

### No `ICollidable` or shared interface
All entities are `Creature`. Position and radius come from Creature's properties.
No interface needed for collision — the single `PushApart` helper takes
`Vector3` positions directly.
