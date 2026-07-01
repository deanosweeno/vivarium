# Implementation Plan: Sprint ability (implicit signal via DesiredVelocity)

Remove hardcoded `FleeSpeedMultiplier`/`FlockFleeCap` from `IFleeStrategy`. Sprint
signal is implicit: `DesiredVelocity.Length() > MaxSpeed` means sprinting.
`ComputeSteering` uses `SprintSpeed` directly for flee actions; `SteeringLocomotion`
and `ApplyBiomeEffects` detect overspeed and switch to sprint stats. No flags
(`UsesSprint`/`IsSprinting`), no coupling between locomotion and brain — the signal
is the velocity vector itself.

## 1. Add SprintSpeed + SprintAcceleration to CreatureTraits
- **Files:** `core/CreatureTraits.cs` (modify)
- **What:**
  - `public float SprintSpeed { get; set; } = 0.6f;` (default = MaxSpeed default → no sprint benefit by default)
  - `public float SprintAcceleration { get; set; } = 2.0f;` (default = Acceleration default)
  - Wire into copy constructor `CreatureTraits(CreatureTraits other)`
- **Why:** Data fields the sprint system reads. Defaults match MaxSpeed/Acceleration so only creatures explicitly configured (e.g. sheep) get a sprint boost.
- **Depends on:** none

## 2. Add SprintSpeed + SprintAcceleration to StatKey
- **Files:** `core/genetics/StatKey.cs` (modify)
- **What:** Add `SprintSpeed` and `SprintAcceleration` enum members (alphabetical order, after `Radius` and before `Sociability` respectively).
- **Why:** Makes sprint stats inheritable through the genetics pipeline (StatRegistry → Expressor → Gene Pins).
- **Depends on:** 1

## 3. Wire SprintSpeed + SprintAcceleration in StatRegistry
- **Files:** `core/genetics/StatRegistry.cs` (modify)
- **What:** Add entries:
  - `[StatKey.SprintSpeed] = new((t, _) => t.SprintSpeed, (t, _, v) => t.SprintSpeed = v, 0f, 10f)`
  - `[StatKey.SprintAcceleration] = new((t, _) => t.SprintAcceleration, (t, _, v) => t.SprintAcceleration = v, 0f, 20f)`
- **Why:** StatKey members need getter/setter mappings. Ranges mirror MaxSpeed/Acceleration ranges. Tests that enumerate all StatKeys will fail without this.
- **Depends on:** 2

## 4. Add SprintSpeed/SprintAcceleration to CreatureCatalog JSON loading
- **Files:** `core/body/CreatureCatalog.cs` (modify)
- **What:**
  - Add `public float? SprintSpeed { get; set; }` and `public float? SprintAcceleration { get; set; }` to `TraitsDto`
  - Wire in `BuildTraits()`: `if (d.SprintSpeed is { } ss) t.SprintSpeed = ss;` (same for SprintAcceleration)
- **Why:** Sheep and future creature types define sprint stats in `creatures.json`.
- **Depends on:** 1

## 5. Modify SteeringLocomotion to detect sprint via DesiredVelocity
- **Files:** `core/SteeringLocomotion.cs` (modify)
- **What:** In `Tick()`, after reading `desired` (the XZ of `creature.DesiredVelocity`):
  ```csharp
  float maxSpeed = creature.Traits.MaxSpeed;
  float accel = creature.Traits.Acceleration;
  if (desired.Length() > maxSpeed + 1e-6f)
  {
      maxSpeed = creature.Traits.SprintSpeed;
      accel = creature.Traits.SprintAcceleration;
  }
  ```
  Then use `accel` for the per-tick acceleration delta and `maxSpeed` for the final cap (instead of hardcoded `creature.Traits.MaxSpeed`/`Acceleration`).
- **Why:** The implicit signal. All steering functions cap output at their caller-passed maxSpeed. A sprint action passes SprintSpeed → output > MaxSpeed → locomotion detects it. No flags, no coupling.
- **Depends on:** 1

## 6. Make ApplyBiomeEffects sprint-aware
- **Files:** `core/Simulator.cs` — `ApplyBiomeEffects` method (modify)
- **What:** Change the speed cap base from `entity.Traits.MaxSpeed` to:
  ```csharp
  float baseSpeed = entity.DesiredVelocity.Length() > entity.Traits.MaxSpeed + 1e-6f
      ? entity.Traits.SprintSpeed
      : entity.Traits.MaxSpeed;
  float maxSpeed = baseSpeed * def.SpeedMultiplier;
  ```
- **Why:** ApplyBiomeEffects caps Velocity AFTER SteeringLocomotion. If it caps sprint velocity back to MaxSpeed, sprint never manifests. Must use SprintSpeed as the base when sprinting.
- **Depends on:** 1

## 7. Update UtilityBrain.ComputeSteering — AvoidPlayer uses SprintSpeed
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `case SteeringKind.AvoidPlayer`:
  Replace `float speed = maxSpeed * fleeStrategy.FleeSpeedMultiplier;` with `float speed = self.Traits.SprintSpeed;`.
- **Why:** Sprint speed replaces the hardcoded multiplier. DesiredVelocity will have magnitude SprintSpeed > MaxSpeed, triggering the implicit signal in steps 5/6.
- **Depends on:** 1

## 8. Update UtilityBrain.ComputeSteering — Flock flee cap uses SprintSpeed
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `case SteeringKind.Flock`, replace:
  ```csharp
  float cap = self.Flock?.Current == FlockAction.FleePlayer
      ? fleeStrategy.FlockFleeCap(maxSpeed)
      : maxSpeed;
  ```
  with:
  ```csharp
  float cap = self.Flock?.Current == FlockAction.FleePlayer
      ? self.Traits.SprintSpeed
      : maxSpeed;
  ```
  Remove the comment block above this that references the strategy's ownership of the speed formula.
- **Why:** Sprint speed is the flee cap for flock members converging on a fleeing anchor. DesiredVelocity > MaxSpeed triggers sprint detection in locomotion.
- **Depends on:** 1

## 9. Update Flock.AdvanceAnchor — flee pace uses member SprintSpeed
- **Files:** `core/Flock.cs` (modify)
- **What:** In `AdvanceAnchor()`, when `fleePlayer == true`, replace:
  ```csharp
  float avgMax = 0f;
  foreach (var m in Members) avgMax += m.Traits.MaxSpeed;
  avgMax /= Members.Count;
  float fleeSpeed = avgMax * strategy.FleeSpeedMultiplier;
  ```
  with:
  ```csharp
  float avgSprint = 0f;
  foreach (var m in Members) avgSprint += m.Traits.SprintSpeed;
  avgSprint /= Members.Count;
  float fleeSpeed = avgSprint;
  ```
- **Why:** Anchor panic-flee pace matches members' sprint speed. Anchor moves directly (no SteeringLocomotion), so read SprintSpeed directly.
- **Depends on:** 1

## 10. Remove FleeSpeedMultiplier from IFleeStrategy
- **Files:** `core/IFleeStrategy.cs` (modify)
- **What:** Remove:
  - `float FleeSpeedMultiplier { get; }` property
  - `float FlockFleeCap(float maxSpeed)` default method
  - Their xml-doc comments
- **Why:** Sprint speed + implicit signal replaces both. IFleeStrategy now only owns threat detection, safe distance, flee direction, and flock-flee group policy.
- **Depends on:** 7, 8, 9

## 11. Remove FleeSpeedMultiplier from all IFleeStrategy implementations
- **Files:** `core/SheepFleeStrategy.cs`, `core/FleeStrategyRegistry.cs` (modify)
- **What:** Remove `FleeSpeedMultiplier` property from `SheepFleeStrategy`, `NeverFleeStrategy`, `AlwaysFleeStrategy`.
- **Why:** Interface member removed — implementations must comply.
- **Depends on:** 10

## 12. Update sheep config with SprintSpeed in creatures.json
- **Files:** `assets/creatures.json` (modify)
- **What:** In sheep's `Traits` block, add:
  ```json
  "SprintSpeed": 1.2,
  "SprintAcceleration": 3.2
  ```
  (MaxSpeed is 0.6, SprintSpeed=1.2 = 2× old FleeSpeedMultiplier. SprintAcceleration=3.2 = 2× normal Acceleration=1.6 → snappier flee.)
- **Why:** Data-driver replacing the removed hardcoded multiplier.
- **Depends on:** 4

## 13. Update tests
- **Files:** multiple test files (modify)
- **What:**
  - `core/CreatureTraitsTests.cs`: Add test for SprintSpeed/SprintAcceleration defaults + copy-constructor round-trip.
  - `core/genetics/ExpressorTests.cs`: `AllStatKeys` iteration now includes new keys. Verify `StockBase_RoundTrips_AllStatsAndParts` still passes (sheep def doesn't override sprint → uses defaults matching MaxSpeed/Acceleration).
  - `core/genetics/GeneModelTests.cs`: Add test that `StatRegistry.Set(StatKey.SprintSpeed, ...)` clamps to [0,10] and `StatRegistry.Get` reads back.
  - `core/CreatureVarietyTests.cs`: Assert `SheepFleeStrategy` no longer has `FleeSpeedMultiplier`. Remove/update any test referencing `FleeSpeedMultiplier` or `FlockFleeCap`.
  - `core/BehaviorTests.cs`: Test that AvoidPlayer DesiredVelocity magnitude ≈ SprintSpeed (not MaxSpeed). Test SteeringLocomotion with DesiredVelocity > MaxSpeed caps at SprintSpeed.
  - `core/PlayerInteractionTests.cs`: In `StrangerSheep_PicksFleePlayer_NearPlayer`, assert DesiredVelocity magnitude exceeds MaxSpeed (sheep SprintSpeed=1.2 from creatures.json).
  - `core/SimulatorTests.cs`: Test ApplyBiomeEffects uses SprintSpeed as base for biome cap when sprinting.
- **Why:** Removed API members referenced in tests. New sprint behavior needs coverage.
- **Depends on:** 1, 2, 3, 5, 6, 7, 8, 9, 10, 11, 12

## 14. Verification gate
- **What:** `dotnet build` → zero errors. `dotnet test` → all green. Run project, screenshot fleeing sheep, check game logs.
- **Depends on:** 1–13
