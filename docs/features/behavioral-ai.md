# System: Behavioral AI (Pillar 1)

**Status:** Built · decoupled + per-species variety + liveliness (this doc) ·
**Design:** [../GAME-DESIGN.md](../GAME-DESIGN.md)

How a creature decides what to do and how that decision becomes movement — and how a
creature *type* (sheep vs. a future deer, predator, sloth) can carry its own behavior
without every creature sharing one global brain. Lives entirely in `core/` (Godot-free,
deterministic); `scripts/CreatureVisual.cs` only reads the result to animate.

## Pipeline: perceive → score → steer

```
PerceptionBuilder.Build(self, world) -> SenseContext   (pure fn: nearest neighbor, flock
                                                          anchor, nearest food, biome
                                                          comfort, player channel, …)
UtilityBrain.Decide(self, senses)     -> BehaviorAction  (argmax over Actions, with
                                                          anti-dither stickiness + latches)
UtilityBrain.ComputeSteering(...)     -> DesiredVelocity (per-SteeringKind movement math)
Movement.Tick(...)                    -> Position/Velocity (WalkMode/FlyMode/PlayerInputMode)
```

Every tick, `Simulator.Tick` builds a fresh `SenseContext` for each creature with a
brain, ticks its `UtilityBrain`, then hands the resulting `DesiredVelocity` to the
creature's `IMovementMode`. `NeedSystem.Resolve` and `GrazingSystem.Resolve` run
alongside as pure per-tick systems (need dynamics, food consumption).

A `BehaviorAction` scores as `BaseWeight × ∏ Considerations`, where each
`Consideration` reads a normalized `[0,1]` `InputKind` off the `SenseContext`, bends it
through a `ResponseCurve`, and scales by a `Drives` weight. Any near-zero consideration
kills the action — "don't flee when nothing's near" falls out of the math, not an
if-statement.

## Config: one aggregate, four concerns

`BehaviorConfig` is a thin aggregate over four focused sub-configs, each owning one
concern (split in the decoupling pass so a 50-field bag didn't keep growing):

| Sub-config | Owns |
|---|---|
| `BrainConfig` | decision cadence, anti-dither, satiation, perception radii, wander/frolic dwell, the action table |
| `FlockConfig` | flock circle sizing, join/leave/merge radii, anchor pace, peer-alignment weight |
| `NeedConfig` | need gain/relief rates, broadcast thresholds |
| `InteractionConfig` | feed/soothe/play bonds, reach, flavor-mismatch floor, bond thresholds |

The original flat properties (`Behavior.SenseRadius`, `Behavior.PartialBondThreshold`,
…) still work — they delegate to the sub-config — so existing call sites and data
files are unaffected. New code should prefer the sub-config directly
(`Behavior.Brain.SenseRadius`).

## Per-type seams: how a species carries its own behavior

The proven pattern is `IFleeStrategy` injection (a creature's flee-from-player policy
is a swappable strategy object, not a hardcoded branch). Three more axes follow the
same shape, each resolved from a `CreatureDef` (loaded from `assets/creatures.json`)
and stored on the `Creature` instance, falling back to a shared default when a type
doesn't override it:

- **Flee strategy** — `CreatureDef.FleeStrategy` (a name, e.g. `"sheep"`) resolves via
  `FleeStrategyRegistry` to an `IFleeStrategy` (`SheepFleeStrategy`,
  `NeverFleeStrategy`, `AlwaysFleeStrategy`). Read by `UtilityBrain`,
  `PerceptionBuilder`, and the flock-flee loop as `self.FleeStrategy ?? <default>`.
- **Action set** — `CreatureDef.ActionSet` (a name, e.g. `"herbivore"`) resolves via
  `ActionSetCatalog` to a candidate action table, fed into the brain's `BehaviorConfig`
  at construction (`HerdSpawner` builds a per-def config when an override is present).
- **Perception range** — `CreatureTraits.SenseRadius` / `FoodSenseRadius` (nullable)
  override the shared `BrainConfig` defaults per-creature, so a keen-eyed vs. dull type
  perceives differently. `PerceptionBuilder` reads `self.Traits.SenseRadius ?? behavior.SenseRadius`.

Adding a new creature type's behavior is a `creatures.json` edit, not a code change —
mirrors how `CreatureTraits` already carried per-type `FatigueGainPerSec`/`Diet`.

## Anti-dither latches: `HoldWhile`

Some actions need to hold once committed — a forager shouldn't be yanked off food
after one bite, a fleeing creature shouldn't un-panic mid-stride. `BehaviorAction.HoldWhile`
declares this as data: `new HoldWhile(InputKind.Hunger, Threshold: 0.15f)` means "stay
committed while Hunger reads above 0.15." `UtilityBrain.Decide` evaluates the *current*
action's `HoldWhile` generically — no `SteeringKind` switch, so a new action type gets
latch behavior for free by setting the property. `SenseContext.PlayerPanic` (a computed
`IsPlayerThreat && HasPlayer && !HasFlock`) unifies the flee-onset check across the
brain's unconditional override, the `FleePlayer` latch, and `ReactionSystem`.

## Liveliness: reactions and light peer alignment

`ReactionKind` (Happy/Dislike/Startled/Curious/Content) is the feedback channel that
teaches the player a creature's temperament and makes the world read as alive:

- **Happy/Dislike** — set directly by the interaction verbs (`FeedInteraction`,
  `PlayInteraction`, `SootheInteraction`) based on `FlavorMatch` — a well-matched
  interaction (a lively creature played with) reads bigger; a mismatch still helps
  (floored, never punished) but reads as Dislike instead of Happy.
- **Startled/Curious/Content** — resolved by the pure `ReactionSystem.ResolveTransition`
  from one tick's action/sense transition: player-panic onset → Startled; committing to
  Approach/SeekFlock → Curious; a `HoldWhile` latch releasing (a held need was just
  satisfied) → Content.

`scripts/CreatureVisual.cs` reads `Creature.LastReaction` and branches its tell per
`Kind` (pop/hop for Happy, shrink/recoil for Startled/Dislike, a small roll for
Curious, a gentle settle for Content) — cosmetic only, never feeds back into the sim.

`SenseContext.NeighborHeading` (average unit heading of moving neighbors within sense
radius, computed in `PerceptionBuilder`'s existing neighbor scan) blends a light
peer-alignment term into `Flock` steering (`FlockConfig.FlockAlignmentWeight`, default
small) so a herd reads as members loosely following each other's direction, not just
independently orbiting the anchor — the anchor stays the leader.

## Determinism

`core/` never references Godot and never draws non-seeded randomness. Every system
above is either a pure function of `(state, config)` (`NeedSystem`, `GrazingSystem`,
`ReactionSystem`, `PerceptionBuilder.Build`) or reads only the seeded `Random` passed
down from `Simulator.Rng` (`UtilityBrain`'s wander re-rolls, `FlockManager`). Same
seed ⇒ same simulation — a test assertion (`NeedSystemTests`, `CreatureVarietyTests`),
not just a convention.

## Key files

```
core/
  UtilityBrain.cs          — decide (score/latch/commit) + steer, per creature
  BehaviorConfig.cs         — BrainConfig/FlockConfig/NeedConfig/InteractionConfig + action table
  BehaviorAction.cs         — action data + HoldWhile latch
  Consideration.cs          — InputKind/DriveKind/ResponseCurve scoring primitives
  PerceptionBuilder.cs      — self+world -> SenseContext (pure)
  SenseContext.cs           — perception snapshot (PlayerPanic, NeighborHeading, …)
  NeedSystem.cs             — need dynamics (pure)
  GrazingSystem.cs          — food consumption (pure)
  ReactionSystem.cs         — Startled/Curious/Content transitions (pure)
  SimPhysics.cs             — ground placement + collision resolution
  IFleeStrategy.cs / FleeStrategyRegistry.cs / SheepFleeStrategy.cs
  ActionSetCatalog.cs
  body/CreatureDef.cs       — per-type FleeStrategy/ActionSet/Traits/Drives/Herd
  player/CreatureReaction.cs — ReactionKind + factory helpers
  player/interactions/*.cs  — Feed/Soothe/Play verbs, FlavorMatch

scripts/
  CreatureVisual.cs         — reads LastReaction/IsFrolicking/FocusPosition; no rules
```
