# Implementation Plan: Sprint ability for creatures

Remove hardcoded `FleeSpeedMultiplier` from `IFleeStrategy`, replace with data-driven
`CreatureTraits.SprintSpeed` + `CreatureTraits.SprintAcceleration`. Any action can
declare `UsesSprint = true`; the brain sets `Creature.IsSprinting`, and
`SteeringLocomotion` uses sprint stats instead of `MaxSpeed`/`Acceleration` while it's
active. Sheep flee uses sprint → sheep config gets `SprintSpeed = 1.2` (2× old MaxSpeed).

## 1. Add SprintSpeed + SprintAcceleration to CreatureTraits
- **Files:** `core/CreatureTraits.cs` (modify)
- **What:** Add `public float SprintSpeed { get; set; } = 0.6f;` (default = MaxSpeed default) and `public float SprintAcceleration { get; set; } = 2.0f;` (default = Acceleration default). Wire into copy constructor.
- **Why:** Foundation — these are the data fields the sprint system reads.
- **Depends on:** none

## 2. Add SprintSpeed + SprintAcceleration to StatKey enum
- **Files:** `core/genetics/StatKey.cs` (modify)
- **What:** Add `SprintSpeed` and `SprintAcceleration` enum members.
- **Why:** Makes sprint stats inheritable through the genetics pipeline (StatRegistry → Expressor → Gene Pins).
- **Depends on:** 1

## 3. Wire SprintSpeed + SprintAcceleration in StatRegistry
- **Files:** `core/genetics/StatRegistry.cs` (modify)
- **What:** Add entries mapping `StatKey.SprintSpeed → Traits.SprintSpeed` and `StatKey.SprintAcceleration → Traits.SprintAcceleration`. Range: SprintSpeed [0, 10], SprintAcceleration [0, 20].
- **Why:** StatKey enum members need concrete getter/setter mappings to CreatureTraits fields. Without this, genetics tests that enumerate all StatKeys will fail.
- **Depends on:** 2

## 4. Add SprintSpeed to Creatures JSON loading (CreatureCatalog)
- **Files:** `core/body/CreatureCatalog.cs` (modify)
- **What:** Add `SprintSpeed` and `SprintAcceleration` nullable float fields to `TraitsDto`. Wire them into `BuildTraits()`.
- **Why:** Sheep and future creature types can define sprint stats in `creatures.json` without code changes.
- **Depends on:** 1

## 5. Add UsesSprint to BehaviorAction
- **Files:** `core/BehaviorAction.cs` (modify)
- **What:** Add `public bool UsesSprint { get; init; }` (default false).
- **Why:** Each action declares whether it burns sprint speed. FleePlayer will set this true; Wander/Rest/Forage won't.
- **Depends on:** none

## 6. Add IsSprinting runtime flag to Creature
- **Files:** `core/Creature.cs` (modify)
- **What:** Add `public bool IsSprinting { get; internal set; }` (default false).
- **Why:** `SteeringLocomotion` reads this to pick effective speed/accel. Set by brain each tick from `Current?.UsesSprint`.
- **Depends on:** none

## 7. Modify SteeringLocomotion to use sprint stats when IsSprinting
- **Files:** `core/SteeringLocomotion.cs` (modify)
- **What:** In `Tick()`, read `creature.IsSprinting`; use `creature.Traits.SprintSpeed` as speed cap and `creature.Traits.SprintAcceleration` for the per-tick acceleration delta instead of `creature.Traits.MaxSpeed`/`Acceleration`.
- **Why:** The locomotion layer is the single point where speed caps and acceleration are enforced. This is the mechanical "how" of sprint.
- **Depends on:** 1, 6

## 8. Set IsSprinting in UtilityBrain each tick
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `Tick()`, after `Decide()` but before `ComputeSteering()`, set `self.IsSprinting = Current?.UsesSprint == true;`.
- **Why:** The brain owns the action choice → it owns whether the creature sprints. Future statuses can also set this flag externally.
- **Depends on:** 5, 6

## 9. Update UtilityBrain.ComputeSteering for AvoidPlayer to use SprintSpeed
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `case SteeringKind.AvoidPlayer`: replace `float speed = maxSpeed * fleeStrategy.FleeSpeedMultiplier;` with `float speed = self.Traits.SprintSpeed;`.
- **Why:** Sprint speed replaces the hardcoded multiplier. The locomotion already handles the sprint cap (task 7). DesiredVelocity direction vector should encode sprint speed so the locomotion accelerates toward it.
- **Depends on:** 1, 5

## 10. Update UtilityBrain.ComputeSteering Flock flee cap to use SprintSpeed
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `case SteeringKind.Flock`: replace `fleeStrategy.FlockFleeCap(maxSpeed)` in the flock-flee cap with `self.Traits.SprintSpeed`. Remove the `var fleeStrategy = self.FleeStrategy ?? _fleeStrategy;` line in this switch if it was only used for FlockFleeCap.
- **Why:** Same as above: sprint speed is the flock-flee speed cap, not maxSpeed × multiplier.
- **Depends on:** 1

## 11. Update Flock.AdvanceAnchor flee speed to use member SprintSpeed
- **Files:** `core/Flock.cs` (modify)
- **What:** In `AdvanceAnchor()`, when `fleePlayer == true`: compute `float avgSprint` from member `Traits.SprintSpeed` instead of `avgMax * strategy.FleeSpeedMultiplier`.
- **Why:** The anchor's panic-flee pace should match the members' sprint speed, not maxSpeed × a strategy multiplier.
- **Depends on:** 1

## 12. Remove FleeSpeedMultiplier from IFleeStrategy
- **Files:** `core/IFleeStrategy.cs` (modify)
- **What:** Remove `float FleeSpeedMultiplier { get; }` property and the default `FlockFleeCap()` method.
- **Why:** Both members are now obsolete — sprint speed replaces the multiplier, and the locomotion layer handles the cap.
- **Depends on:** 9, 10, 11

## 13. Remove FleeSpeedMultiplier from all IFleeStrategy implementations
- **Files:** `core/SheepFleeStrategy.cs`, `core/FleeStrategyRegistry.cs` (modify)
- **What:** Remove `FleeSpeedMultiplier` property from `SheepFleeStrategy`, `NeverFleeStrategy`, `AlwaysFleeStrategy`.
- **Why:** Interface member removed — implementations must drop it.
- **Depends on:** 12

## 14. Set UsesSprint = true on FleePlayer action in DefaultActions
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** In `DefaultActions()`, add `UsesSprint = true` to the `FleePlayer` action.
- **Why:** When a creature flees the player, it should sprint. This is the declarative config that wires sprint to flee.
- **Depends on:** 5

## 15. Update sheep config with SprintSpeed in creatures.json
- **Files:** `assets/creatures.json` (modify)
- **What:** Add `"SprintSpeed": 1.2` and `"SprintAcceleration": 3.2` to the sheep's `Traits` block.
- **Why:** Sheep should sprint at 2× their normal MaxSpeed (0.6 → 1.2) when fleeing. Acceleration stays proportional. This is the data-driver that replaces the old hardcoded `FleeSpeedMultiplier = 2.0f`.
- **Depends on:** 4

## 16. Update tests
- **Files:** multiple test files (modify)
- **What:**
  - `core/CreatureTraitsTests.cs`: add test asserting SprintSpeed/SprintAcceleration defaults and copy constructor round-trip.
  - `core/genetics/ExpressorTests.cs`: update the `AllStatKeys` iteration — SprintSpeed + SprintAcceleration now enumerated, so `StockBase_RoundTrips_AllStatsAndParts` should still pass (new keys default to same as MaxSpeed/Acceleration, which the sheep def doesn't override → uses defaults → round-trips).
  - `core/genetics/GeneModelTests.cs`: add test that `StatRegistry.Set(StatKey.SprintSpeed, ...)` clamps to range.
  - `core/CreatureVarietyTests.cs`: add assertion that `SheepFleeStrategy` no longer has `FleeSpeedMultiplier`. Remove/update tests referencing `FleeSpeedMultiplier` or `FlockFleeCap`.
  - `core/BehaviorTests.cs`: add test that `FleePlayer` action has `UsesSprint = true`. Add test that during AvoidPlayer, `Creature.IsSprinting` is true after brain tick, and `SteeringLocomotion` uses SprintSpeed as cap.
  - `core/PlayerInteractionTests.cs`: add test asserting sprint speed is used when fleeing (check `DesiredVelocity` magnitude ≈ SprintSpeed).
- **Why:** Existing tests reference removed API. New sprint behavior needs coverage.
- **Depends on:** 1, 5, 6, 7, 12

## 17. Verification gate
- **What:** `dotnet build` → zero errors. `dotnet test` → all green. Run project, screenshot, check game logs.
- **Depends on:** 1–16
