# Research: Vivarium Physics Foundation

## Summary

We're building a custom 3D physics foundation in `core/` — extending the existing
Simulator/Arena/entity architecture. No Godot physics engine involvement; Godot is
the renderer only. Physics rules exist at two layers: global sim rules (gravity,
ground, walls, entity-entity collision) and creature-specific rules (movement mode,
configurable traits). The entity model uses composition over inheritance: a base
`Creature` class composes an `IMovementMode` and a mutable `CreatureTraits` config
bag. Blob becomes the first concrete creature type.

## Key decisions

| Decision | Choice | Rationale |
|---|---|---|
| Physics engine | Custom in `core/` | Keeps architecture coherent; Godot physics is non-deterministic and requires scene tree |
| Coordinate system | `System.Numerics.Vector3` | Full 3D movement (ground + flying + swimming creatures) |
| Entity model | Base `Creature` + composed `IMovementMode` | Composition over inheritance per AGENTS.md; traits configurable per creature |
| Traits mutability | Mutable at runtime | Enables gene splicing, status effects, dynamic gameplay |
| Collision | Sphere-sphere push-apart + ground plane | O(n²) fine for ~100 entities; add spatial partitioning later if needed |
| Determinism | Soft goal (seeded RNG kept, not a hard gate) | Unblocks forward progress; can tighten later |

## Architecture overview

```
Simulator (global rules)
  ├── Gravity: Velocity.Y -= 9.8 * delta  (configurable)
  ├── Ground plane: Y >= 0
  ├── Arena walls: XZ bounds, velocity reflect
  └── Entity collision: sphere-sphere push-apart

Creature (base, per-entity)
  ├── Vector3 Position, Velocity
  ├── float Radius
  ├── CreatureTraits Traits        ← mutable: maxSpeed, jumpHeight, canFly, etc.
  └── IMovementMode Movement       ← Walk / Fly / Swim (swappable)
```

### Type sketch

```
IMovementMode     ← interface: Tick(delta, creature, arena)
WalkMode          ← gravity + ground clamp + jump
FlyMode           ← free 3D, no gravity, configurable drag
SwimMode          ← gravity + buoyancy + fluid drag (future)

CreatureTraits    ← mutable config bag
  MaxSpeed, JumpHeight, Acceleration, TurnRate,
  Radius, GravityScale, CanFly, MaxFlyHeight

Creature          ← base class
  Position, Velocity, Radius, Traits, Movement

Blob : Creature   ← WanderState + pastel color + wander cycle
```

## Implementation phases

Each phase is self-contained and produces its own `plan-phaseN-<slug>.md`.

---

### Phase 1: 3D math migration + Arena upgrade

**Scope:** Replace `Vector2` with `Vector3` in Arena.cs and all consumers.
Extend Arena from 2D rectangle to 3D volume with floor plane.

**Files:** `core/Arena.cs`, `core/ArenaTests.cs` (new)

**Key changes:**
- `Arena` gets `MinY`, `MaxY` (floor at 0, optional ceiling)
- `Contains(position, radius)` checks X, Y, Z bounds
- `Clamp(position, radius)` clamps all 3 axes
- `Reflect(position, velocity, radius)` adds floor bounce on Y
- Existing 2D tests updated; new 3D tests added

**Depends on:** nothing (current codebase)

---

### Phase 2: Creature base class + CreatureTraits + IMovementMode

**Scope:** Create the new type hierarchy in `core/`. No behavior yet — just the
type shells, interfaces, and the trait config bag.

**Files (new):**
- `core/IMovementMode.cs`
- `core/CreatureTraits.cs`
- `core/Creature.cs`
- `core/CreatureTests.cs`

**Key changes:**
- `IMovementMode` interface with `void Tick(double delta, Creature creature, Arena arena)`
- `CreatureTraits` — mutable properties: MaxSpeed, JumpHeight, Acceleration, TurnRate, Radius, GravityScale, CanFly (defaults for ground creatures)
- `Creature` — Position (Vector3), Velocity (Vector3), Radius, Traits, Movement

**Depends on:** Phase 1 (Vector3 types)

---

### Phase 3: WalkMode + gravity in Simulator

**Scope:** Implement gravity as a global sim rule. Implement WalkMode as the first
movement mode. Wire both into Simulator.Tick().

**Files:**
- `core/WalkMode.cs` (new)
- `core/Simulator.cs` (modify)
- `core/WalkModeTests.cs` (new)
- `core/SimulatorTests.cs` (modify)

**Key changes:**
- Simulator: apply gravity to each creature's velocity each tick
- Simulator: ground-clamp each creature (Y >= floor)
- WalkMode: handles horizontal movement + jump initiation
- Jump: sets Velocity.Y = jumpImpulse, gravity pulls back down

**Depends on:** Phase 2 (Creature, CreatureTraits, IMovementMode)

---

### Phase 4: 3D sphere-sphere collision + ground resolution

**Scope:** Adapt existing push-apart from 2D circles to 3D spheres. Add ground
collision resolution (penetration push-out).

**Files:**
- `core/Simulator.cs` (modify — update collision methods)
- `core/SimulatorTests.cs` (modify)

**Key changes:**
- `ResolveEntityCollisions()` — sphere-sphere (Vector3 distance, same algorithm)
- Ground penetration: if `Position.Y - Radius < 0`, push `Position.Y = Radius`, zero or reflect `Velocity.Y`
- Arena wall collision with full 3D bounds

**Depends on:** Phase 3 (gravity + creatures in simulator)

---

### Phase 5: Migrate Blob to Creature

**Scope:** Refactor `Blob` to extend `Creature` with `WalkMode`. Preserve
existing wander behavior, pastel color, idle/slide state machine. Remove
standalone physics from Blob — it delegates to Creature base.

**Files:**
- `core/Blob.cs` (major refactor)
- `core/BlobTests.cs` (update)
- `core/Simulator.cs` (update — operates on Creature, not raw Blob)

**Key changes:**
- `Blob : Creature` with WanderState, R, G, B, color methods
- Constructor: `new Blob(position, r, g, b, rng, traits)` — passes `WalkMode` + traits to base
- `Blob.Tick()` calls base tick (gravity + movement) then runs wander state machine
- Simulator no longer applies gravity/ground directly to Blob — Creature base handles it
- Simulator's entity list becomes `List<Creature>`

**Depends on:** Phase 4 (3D collision), Phase 3 (WalkMode + gravity)

---

### Phase 6: Scripts layer — 3D visual sync

**Scope:** Update `BlobVisual` and `VivariumMain` to work with 3D positions.
Y-axis is now meaningful (creatures float, jump, fly).

**Files:**
- `scripts/BlobVisual.cs` (modify)
- `scripts/VivariumMain.cs` (modify)
- `scenes/Blob.tscn` (optional — adjust camera/floor if needed)

**Key changes:**
- `BlobVisual.Position` = `Vector3(model.X, model.Y + 0.5f, model.Z)` — Y offset for visual center
- Spawning: click projects ray to Y=0 plane, spawn at Vector3(x, 0, z)
- Camera: maybe add vertical orbit for observing jumping/flying creatures

**Depends on:** Phase 5 (Blob → Creature migration complete)

---

### Phase 7: Additional movement modes (FlyMode, SwimMode)

**Scope:** Add `FlyMode` and optionally `SwimMode` implementations. Update
creature spawning to allow flight.

**Files (new):**
- `core/FlyMode.cs`
- `core/SwimMode.cs`
- `core/FlyModeTests.cs`

**Key changes:**
- `FlyMode`: no gravity, configurable drag, optional max height ceiling
- `SwimMode`: reduced gravity (buoyancy), fluid drag, optional surface clamping
- Traits: `CanFly` toggles between WalkMode/FlyMode
- Spawner can create flying creatures for testing

**Depends on:** Phase 5 (creature architecture stable)

---

## Trade-offs discussed

- **Custom vs Godot physics**: Custom keeps architecture coherent, deterministic,
  unit-testable. Godot physics is non-deterministic and couples to scene tree.
  Writing ~100 lines of collision + gravity math is well worth the architectural
  purity.
- **Mutable vs immutable traits**: Mutable enables dynamic gameplay (gene splicing,
  buffs/debuffs) at the cost of slightly more complex state management. Accepted.
- **Sphere-only collision**: Simple, fast, sufficient for first-pass creature shapes.
  Can add capsule/box later without changing the architecture.
- **O(n²) collision**: Fine for 100 entities (~5K pairwise checks/frame). Broad-phase
  spatial partitioning can be added later if scaling past ~300 entities.

## Sources

No external sources — this is architecture design based on:
- Existing Vivarium codebase (`core/Arena.cs`, `core/Simulator.cs`, `core/Blob.cs`)
- Godot 4.5 documentation (physics body types, PhysicsServer3D)
- Project AGENTS.md conventions (composition over inheritance, headless core)
