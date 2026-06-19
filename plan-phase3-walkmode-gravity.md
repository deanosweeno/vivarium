# Implementation Plan: Phase 3 — WalkMode + Gravity in Simulator

## Goal
Implement gravity as a global sim rule and WalkMode as the first `IMovementMode`
implementation. Wire both into the Simulator's new creature pipeline while leaving
the existing Blob pipeline untouched.

### Architecture
```
Simulator.Tick(delta)
├── For each Creature:
│   1. Apply gravity  →  Velocity.Y -= Gravity * GravityScale * delta
│   2. Movement.Tick   →  horizontal wander + wall bounce
│   3. Ground clamp    →  Y >= floor + Radius, zero Velocity.Y if clamped
├── For each Blob (unchanged):
│   blob.Tick(delta, Arena, Rng)
└── ResolveBlobCollisions (unchanged)
```

### What gets wired vs deferred
| Wired now (Phase 3) | Deferred to Phase 4-5 |
|---|---|
| Gravity on creatures | Creature-creature collision |
| Ground clamp on creatures | Blob → Creature migration |
| WalkMode simple wander | Blob's Idle/Slide state machine |
| Creature list + spawn | Smooth acceleration / turn rate in WalkMode |
| Jump capability (tested) | Jump trigger from Blob behavior |

---

## 1. Update `IMovementMode` to accept RNG
- **Files:** `core/IMovementMode.cs` (modify), `core/CreatureTests.cs` (modify)
- **What:**
  - Change signature from `void Tick(double delta, Creature creature, Arena arena)`
    to `void Tick(double delta, Creature creature, Arena arena, Random rng)`
  - WalkMode needs `Random` for picking wander directions; the Simulator passes its
    master RNG to maintain determinism
  - Update `StubMovement` in `CreatureTests.cs` to match the new signature
  - No other consumers exist yet (WalkMode is created in task 3)
- **Why:** Movement modes that use randomness (wander direction, erratic flight, etc.)
  must pull from the master RNG for determinism. Adding it now avoids a second
  interface change later.
- **Depends on:** none (no implementations to break)

## 2. Add `Gravity` field to Simulator
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Add `public float Gravity { get; set; } = 9.8f;` (arena units / s²)
  - Doc comment explaining this is the global gravity constant applied to all creatures
  - No behavior change yet — just the field exists for task 4
  - No test changes needed (field is observable via tests in task 6)
- **Why:** Gravity is a global sim rule per the research doc. Configurable as a
  property so it can be changed for testing (e.g., set to 0 for no-gravity tests).
- **Depends on:** none

## 3. Create `WalkMode : IMovementMode`
- **Files:** `core/WalkMode.cs` (new)
- **What:**
  - Namespace `Vivarium.Core`
  - Implements `IMovementMode`
  - Internal state:
    - `Vector3 _direction` — current wander direction (XZ plane only, Y=0, unit length)
    - `double _directionTimer` — seconds until next direction change
  - Direction change intervals: 1.0–4.0 seconds
  - `Tick(double delta, Creature creature, Arena arena, Random rng)`:
    1. Decrement `_directionTimer`
    2. If `_directionTimer <= 0`: pick new random XZ direction (`cos(θ), 0, sin(θ)`)
       using `rng`, reset timer
    3. Set horizontal velocity: `creature.Velocity = new Vector3(_direction.X * MaxSpeed, creature.Velocity.Y, _direction.Z * MaxSpeed)`
    4. Integrate position: `creature.Position += creature.Velocity * (float)delta`
    5. Arena wall check: `arena.Reflect(creature.Position, creature.Velocity, creature.Traits.Radius)` →
       clamp position, reflect X/Z velocity; if wall was hit, set `_directionTimer = 0`
       to force a direction change next tick
  - `Jump()` method (not called automatically — invoked by external behavior):
    - Sets `creature.Velocity = new Vector3(creature.Velocity.X, JumpHeight, creature.Velocity.Z)`
    - `JumpHeight` default = 1.0f (placeholder; will use `creature.Traits.JumpHeight`
      when Blob's behavior triggers it in Phase 5)
  - Constructor initializes a random initial direction using a throwaway Random or
    takes an initial direction
  - The first tick will immediately set up direction via the timer=0 path
  - Doc comments explaining the wander cycle and that gravity/ground are handled
    externally by Simulator
- **Why:** WalkMode is the baseline movement strategy for ground creatures. Simple
  random wander is enough to prove the IMovementMode → Simulator pipeline works.
  The wander is intentionally simpler than Blob's Idle/Slide state machine
  (no idle pauses) — Blob's richer behavior is migrated in Phase 5 and can
  layer on top of WalkMode.
- **Depends on:** task 1 (updated IMovementMode)

## 4. Wire creature pipeline into Simulator
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Add `public List<Creature> Creatures { get; } = new();`
  - Add `public Creature SpawnCreature(Vector3 position, CreatureTraits? traits = null)`:
    - Creates `new WalkMode()` as the movement strategy
    - Creates `new Creature(position, traits, walkMode)`
    - Clamps spawn position to arena (using `traits?.Radius ?? CreatureTraits.Default.Radius`)
    - Avoids overlap with existing creatures and blobs (up to 10 retries)
    - Adds to `Creatures` list, returns the creature
  - Add helper `private bool OverlapsAnyCreature(Vector3 pos, float minDist)` —
    checks against both `Creatures` and `Blobs` lists
  - Modify `Tick(double delta)`:
    ```csharp
    // New: tick creatures with gravity + ground + movement
    foreach (var creature in Creatures)
    {
        // 1. Apply gravity
        creature.Velocity = new Vector3(
            creature.Velocity.X,
            creature.Velocity.Y - Gravity * creature.Traits.GravityScale * (float)delta,
            creature.Velocity.Z);

        // 2. Movement tick
        creature.Movement.Tick(delta, creature, Arena, Rng);

        // 3. Ground clamp
        float floor = Arena.MinY + creature.Traits.Radius;
        if (creature.Position.Y < floor)
        {
            creature.Position = new Vector3(
                creature.Position.X, floor, creature.Position.Z);
            creature.Velocity = new Vector3(
                creature.Velocity.X, 0f, creature.Velocity.Z);
        }
    }

    // Existing: tick blobs
    foreach (var blob in Blobs)
        blob.Tick(delta, Arena, Rng);

    // Existing: resolve blob collisions
    ResolveBlobCollisions();
    ```
  - `SpawnCreature` retries: picks random XZ positions within arena bounds if
    overlap detected
  - Blob pipeline (`Blobs`, `SpawnBlob`, `BlobCount`) is **unchanged**
- **Why:** This adds the creature physics pipeline alongside the existing blob
  pipeline. The order (gravity → movement → ground clamp) follows standard
  physics integration: apply forces, integrate, resolve constraints.
- **Depends on:** tasks 2 (Gravity field), 3 (WalkMode exists)

## 5. Write `WalkModeTests`
- **Files:** `core/WalkModeTests.cs` (new)
- **What:**
  - Namespace `Vivarium.Core.Tests`
  - Tests:
    1. `Wander_ChangesDirectionOverTime` — tick a WalkMode creature many times,
       verify the direction changes at least once (the creature moves to
       different XZ positions)
    2. `Speed_ClampedToMaxSpeed` — set `Traits.MaxSpeed = 1.0f`, tick, verify
       horizontal speed ≤ 1.0f
    3. `WallBounce_ReflectsVelocity` — place creature near wall heading toward
       it, tick, verify direction reversed or changed
    4. `Jump_SetsVerticalVelocity` — create WalkMode, call `Jump()`, verify
       `creature.Velocity.Y > 0` and X/Z unchanged
    5. `Deterministic_SameSeedSamePath` — two WalkMode creatures with same seed,
       tick many frames, verify same positions
    6. `Y_Velocity_PreservedAcrossWalkTicks` — set a Y velocity manually
       (simulating gravity), tick, verify Y velocity is unchanged by WalkMode
       (WalkMode only touches X/Z)
    7. `Direction_Timer_Expires_PicksNewDirection` — set very short direction
       change window, verify direction rotates
  - Use `Arena.GroundArena(10, 10)` for arena, `new Creature(pos, traits, new WalkMode())`
  - Seed `Random` deterministically
  - Follow xUnit patterns from existing test files
- **Why:** WalkMode is the first behavior-bearing implementation. Tests verify
  wander cycle, speed clamping, wall response, jump capability, and Y-preservation
  (critical that WalkMode doesn't stomp on Y velocity that Simulator manages).
- **Depends on:** tasks 3 (WalkMode), 4 (Simulator pipeline for integration context)

## 6. Add creature simulation tests to `SimulatorTests`
- **Files:** `core/SimulatorTests.cs` (modify)
- **What:**
  - Add tests (appended to existing file, all existing tests preserved):
    1. `SpawnCreature_PlacesAtPosition` — spawn at (2, 1, 3), verify position
    2. `SpawnCreature_OutsideBounds_Clamped` — spawn at (100, -100, 100), verify clamped
    3. `SpawnedCreature_InList` — spawn, verify `Creatures.Count == 1` and reference matches
    4. `Gravity_PullsCreatureDown` — spawn creature at Y=2 with `GravityScale=1`,
       tick several frames, verify `Position.Y` decreases
    5. `GroundClamp_PreventsFallingThroughFloor` — spawn at Y=0, `Gravity=999`, tick,
       verify `Position.Y >= floor + Radius`
    6. `Creature_WithWalkMode_Wanders` — spawn creature, tick many frames, verify
       XZ position changes (creature moves horizontally)
    7. `GravityScale_Zero_NoGravity` — spawn with `GravityScale=0`, tick,
       verify Y position unchanged
    8. `CreatureGroundClamp_ZeroesVerticalVelocity` — spawn at floor level, tick
       with gravity, verify `creature.Velocity.Y == 0`
    9. `Deterministic_CreatureSim_SameSeedSameState` — two sims, same seed,
       spawn creature at same position, tick many frames, verify identical state
    10. `BlobPipeline_Unaffected_ByCreatureChanges` — spawn both blobs and
        creatures, verify existing blob behavior is unchanged
  - `SpawnCreature` uses `CreatureTraits.Default` or custom traits as needed
- **Why:** These tests verify the full creature physics pipeline works
  end-to-end: spawn → gravity → movement → ground clamp. The blob regression
  test proves existing functionality isn't broken.
- **Depends on:** tasks 4 (Simulator creature pipeline), 5 (WalkMode working)

## 7. Build and run full test suite
- **What:**
  - `dotnet build` in `core/` — zero errors, zero warnings
  - `dotnet test` in `core/` — all tests pass (existing 42 + new ~15-17 ≥ 57 total)
  - `dotnet build` root — Godot project compiles clean
  - `godot --headless --quit` — clean launch
  - Verify no skipped tests, no warnings
- **Why:** Verification gate per AGENTS.md. Must confirm all existing blob behavior
  is preserved and new creature pipeline works.
- **Depends on:** tasks 1–6 complete

---

## Design notes

### Why gravity BEFORE movement tick?
Physics integration order: forces → velocity → position → constraints.
Gravity is a force that modifies velocity. Movement.Tick then uses that
velocity for position integration. If we reversed the order, gravity would
lag by one frame.

### Why WalkMode doesn't handle gravity or ground?
The research doc places gravity and ground as global sim rules (Simulator).
WalkMode handles only horizontal movement + jump initiation. This separation
lets FlyMode (Phase 7) ignore gravity entirely without code duplication.

### Why WalkMode's wander is simpler than Blob's?
WalkMode provides a continuous wander (no idle pauses) as a baseline.
Blob's richer Idle/Slide state machine is creature-specific behavior that
will be layered on top in Phase 5. Keeping WalkMode simple makes it reusable
for other creature types that might want different wander patterns.

### Why `Random rng` on IMovementMode rather than WalkMode's own field?
WalkMode is composed into Creature; multiple creatures must not share RNG
state. Passing the master RNG from Simulator preserves determinism and avoids
each WalkMode needing its own seeded RNG instance.

### What happens to the existing Blob tests?
Nothing. Blob.cs, BlobTests.cs, and the Blob path in Simulator are untouched.
All 29 existing tests (8 Blob + 10 Simulator + 11 Arena) must still pass.
