# Implementation Plan: Phase 2 — Creature Base Class + Traits + IMovementMode

## Goal
Create the new type hierarchy in `core/` — no behavior, no wire-up. Just the type shells,
interface contract, and trait config bag. Nothing existing changes (Blob, Simulator, scripts
stay untouched). This lays the foundation for Phase 3 (WalkMode + gravity) and Phase 5
(migrate Blob to Creature).

### What gets created
| Type | Role |
|---|---|
| `IMovementMode` | Interface for movement strategies (Walk, Fly, Swim) |
| `CreatureTraits` | Mutable config bag (speed, jump, radius, etc.) |
| `Creature` | Base entity class — Position, Velocity, Traits, Movement composed |

### What does NOT change
- `Blob.cs` — untouched; will be migrated in Phase 5
- `Simulator.cs` — untouched; will gain gravity/ground in Phase 3, entity list refactor in Phase 5
- `scripts/` — untouched

---

## 1. Create `IMovementMode` interface
- **Files:** `core/IMovementMode.cs` (new)
- **What:**
  - Namespace `Vivarium.Core`
  - Single method: `void Tick(double delta, Creature creature, Arena arena)`
  - The movement mode receives the creature (for reading/mutating Position/Velocity) and
    the arena (for bounds checks). This follows the compositional pattern: movement is a
    swappable strategy, not baked into the entity.
  - Doc comment explaining the contract: movement strategies handle their own physics
    (gravity, ground clamping, etc.) within `Tick`.
- **Why:** Contracts first — IMovementMode is the key abstraction point for Phase 3+.
  Consumers (Simulator, Creature) can be written against the interface before any
  implementation exists.
- **Depends on:** none (new file in `core/`)

## 2. Create `CreatureTraits` config bag
- **Files:** `core/CreatureTraits.cs` (new)
- **What:**
  - Namespace `Vivarium.Core`
  - Mutable class (not struct — traits must be shared/mutated by reference for gene splicing,
    buffs/debuffs in future phases)
  - Properties with reasonable defaults for ground creatures:

    | Property | Type | Default | Purpose |
    |---|---|---|---|
    | `MaxSpeed` | `float` | `0.6f` | Max horizontal speed (units/sec) |
    | `JumpHeight` | `float` | `1.0f` | Jump impulse strength |
    | `Acceleration` | `float` | `2.0f` | How fast creature reaches MaxSpeed |
    | `TurnRate` | `float` | `3.0f` | How fast creature turns (rad/sec) |
    | `Radius` | `float` | `0.5f` | Collision sphere radius |
    | `GravityScale` | `float` | `1.0f` | Multiplier on global gravity |
    | `CanFly` | `bool` | `false` | Whether creature can switch to FlyMode |
    | `MaxFlyHeight` | `float` | `float.MaxValue` | Ceiling for flying creatures |

  - Static factory: `CreatureTraits Default => new()` (convenience accessor for the default config)
  - Copy constructor: `CreatureTraits(CreatureTraits other)` — shallow copy of all values,
    needed for gene splicing / mutation to create modified copies without mutating originals
  - All properties have `{ get; set; }` — mutable by design (Phase 5+ gene splicing, buffs)
  - Doc comments on each property explaining units and semantics
- **Why:** Traits decouple creature configuration from creature behavior. Different movement
  modes read different traits (WalkMode reads JumpHeight, FlyMode reads MaxFlyHeight).
  Mutable bag avoids "new GameObject" for every stat change.
- **Depends on:** none (new file in `core/`)

## 3. Create `Creature` base class
- **Files:** `core/Creature.cs` (new)
- **What:**
  - Namespace `Vivarium.Core`
  - Open class (not sealed — `Blob` will extend it in Phase 5)
  - Composition via constructor injection:

    ```csharp
    public class Creature
    {
        public Vector3 Position { get; internal set; }
        public Vector3 Velocity { get; internal set; }

        public CreatureTraits Traits { get; }
        public IMovementMode Movement { get; set; }

        public Creature(Vector3 position, CreatureTraits traits, IMovementMode movement)
        {
            Position = position;
            Velocity = Vector3.Zero;
            Traits = traits ?? CreatureTraits.Default;
            Movement = movement;
        }
    }
    ```

  - **No `Tick` method yet** — that's added in Phase 3 (when Creature gains a
    `Tick(delta, arena)` that delegates to `Movement.Tick(delta, this, arena)`). This
    phase is pure data + composition.
  - `Position` and `Velocity` have `internal set` — Simulator and movement modes can
    mutate them, but external consumers (scripts) only read.
  - `Movement` is a property with public get/set — swappable at runtime (e.g., WalkMode → FlyMode
    when `CanFly` toggles).
  - `Traits` is get-only (the reference is fixed, but the properties within it are mutable).
  - Constructor allows null `traits` (defaults to `CreatureTraits.Default`). Constructor
    requires `movement` — every creature must start with a movement mode.
  - Doc comments explaining the composition model.
- **Why:** Creature is the shared base for all entities. By composing`IMovementMode` and
  `CreatureTraits`, different creatures can have different movement + stats without
  inheritance hierarchies. This follows the "composition over inheritance" rule — one
  level of inheritance (Blob : Creature) is the limit.
- **Depends on:** task 1 (IMovementMode), task 2 (CreatureTraits)

## 4. Write `CreatureTraitsTests`
- **Files:** `core/CreatureTraitsTests.cs` (new)
- **What:**
  - Namespace `Vivarium.Core.Tests`
  - Tests:
    1. `Default_HasExpectedValues` — verify all 8 defaults match the spec
    2. `Property_Mutation_Persists` — set each property and read it back
    3. `CopyConstructor_ClonesAllValues` — create original, set non-default values, copy, assert copy matches
    4. `CopyConstructor_IndependentMutation` — copy a traits, mutate original, assert copy unchanged
    5. `Default_ReturnsNewInstance` — `CreatureTraits.Default != CreatureTraits.Default` (different objects, same values)
  - Pure unit tests — no arena, no RNG, no Creature needed
- **Why:** Traits is the only behavior-bearing config type in this phase. The copy constructor
  is critical for future gene splicing — must verify shallow-copy independence.
- **Depends on:** task 2 (CreatureTraits)

## 5. Write `CreatureTests`
- **Files:** `core/CreatureTests.cs` (new)
- **What:**
  - Namespace `Vivarium.Core.Tests`
  - Create a minimal stub/mock `IMovementMode` implementation for tests
    (private `StubMovement` class that records calls or does nothing)
  - Tests:
    1. `Constructor_SetsPosition` — position passed in matches `creature.Position`
    2. `Constructor_StartsWithZeroVelocity` — `creature.Velocity == Vector3.Zero`
    3. `Constructor_UsesProvidedTraits` — traits passed to constructor match `creature.Traits`
    4. `Constructor_NullTraitsDefaults` — passing null traits gives `CreatureTraits.Default` equivalent
    5. `Constructor_UsesProvidedMovement` — movement passed to constructor matches `creature.Movement`
    6. `Movement_IsSwappable` — assign a different IMovementMode, verify creature.Movement changed
    7. `Position_Velocity_AreMutable` — set Position and Velocity via internal accessor, verify
  - Use xUnit conventions matching the existing test files
- **Why:** Verify the composition wiring — Creature correctly stores and exposes its composed
  dependencies. The movement swappability test validates a core design requirement (mode
  switching at runtime).
- **Depends on:** task 3 (Creature), task 1 (IMovementMode)

## 6. Build and run full test suite
- **What:**
  - `dotnet build` in `core/` — zero errors, zero warnings
  - `dotnet test` in `core/` — all tests pass (existing 29 + new ~10-12 = ~40 total)
  - Verify no skipped tests
- **Why:** Gate before claiming Phase 2 done. The existing Blob/Simulator/Arena tests must
  still pass — no regressions from the new types.
- **Depends on:** tasks 1–5 complete

---

## Design notes

### Why no `ICreature` interface?
At this stage, `Creature` has only data (Position, Velocity) and composed types (Traits,
Movement). An `ICreature` interface would be premature — there's only one consumer of
Creature (IMovementMode.Tick takes `Creature creature`). If future types need a
substitutable creature (e.g., for testing movement modes with a lightweight stub), we
can extract `ICreature` in Phase 3 when the first real consumer exists.

### Why `Movement` is a property, not a readonly field?
The research doc explicitly calls for runtime-swappable movement modes. A property with
`get; set;` allows `creature.Movement = new FlyMode()` when `CanFly` toggles. The
setter is public because this swap may be triggered by external systems (gene splicing,
status effects).

### Why `CreatureTraits` is a class, not a struct?
Traits must be shared by reference — multiple systems may hold a reference to the same
traits object and observe mutations (buffs/debuffs) without polling. A struct would create
copies on assignment, breaking this model. The copy constructor handles the "I need my own
copy" case explicitly.

### What's deferred to Phase 3?
- `WalkMode` implementation
- Gravity application in Simulator
- Ground clamping in Simulator
- Creature.Tick() orchestration method
- All behavior is currently stubbed

### What's deferred to Phase 5?
- `Blob : Creature` migration
- WanderState preserved, but delegates movement to WalkMode
- Simulator entity list becomes `List<Creature>`
- BlobTests refactored to test Blob-as-Creature
