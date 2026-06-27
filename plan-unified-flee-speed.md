# Implementation Plan: unified flock flee speed + composition cleanup

Fix the flock flee speed split (anchor vs members use different formulas),
make `FleeSpeedMultiplier` the single knob, and address the composition smell
where `ComputeSteering` reads `self.Flock?.Current` — a Flock internal.

---

## 1. Remove `FlockFleeSpeedMultiplier` from `IFleeStrategy`
- **Files:** `core/IFleeStrategy.cs`, `core/SheepFleeStrategy.cs` (modify)
- **What:** Delete `float FlockFleeSpeedMultiplier { get; }` from the interface
  and remove the property from `SheepFleeStrategy`.  Everything now flows
  through `FleeSpeedMultiplier` (the single knob).
- **Why:** After step 2 the anchor and members both derive their flee speed
  from `FleeSpeedMultiplier × <appropriate base>`.  The separate multiplier
  is no longer needed and only invited drift between the two paths.
- **Depends on:** none

## 2. Make `Flock.AdvanceAnchor` use `FleeSpeedMultiplier` (not `FlockPace`)
- **Files:** `core/Flock.cs` (modify)
- **What:** Compute the anchor's flee speed as the **average member MaxSpeed
  × strategy.FleeSpeedMultiplier** so it matches member capability exactly.
  ```csharp
  // Replace line ~90:
  //   float fleeSpeed = cfg.FlockPace * strategy.FlockFleeSpeedMultiplier;
  // With:
  float avgMax = 0f;
  foreach (var m in Members) avgMax += m.Traits.MaxSpeed;
  avgMax /= Members.Count;
  float fleeSpeed = avgMax * strategy.FleeSpeedMultiplier;
  ```
  Wander/Graze anchor speed still uses `FlockPace` — flee is the only
  override that runs at creature-scale speed.
- **Why:** Anchor and members now derive from the same formula:
  `baseSpeed × FleeSpeedMultiplier`.  The anchor's base is average member
  MaxSpeed; each member's base is its own MaxSpeed.  One multiplier controls
  all flee speed.  Herd stays a tight ball at any multiplier value.
- **Depends on:** 1

## 3. Composition: replace `self.Flock?.Current` check with a strategy method
- **Files:** `core/IFleeStrategy.cs`, `core/SheepFleeStrategy.cs`,
  `core/UtilityBrain.cs` (modify)
- **What:** Add a query method to `IFleeStrategy`:
  ```csharp
  /// <summary>
  /// Speed cap for this creature's Flock steering during a flee.  Receives the
  /// creature's own MaxSpeed; returns the boosted cap.  Default of
  /// maxSpeed × FleeSpeedMultiplier keeps the herd in lock-step with the anchor.
  /// </summary>
  float FlockFleeCap(float maxSpeed) => maxSpeed * FleeSpeedMultiplier;
  ```
  (C# default interface method — no change needed in `SheepFleeStrategy`.)
  Then in `ComputeSteering.Flock`:
  ```csharp
  // Replace:
  //   float cap = self.Flock?.Current == FlockAction.FleePlayer
  //       ? maxSpeed * _fleeStrategy.FleeSpeedMultiplier
  //       : maxSpeed;
  // With:
  float cap = self.Flock?.Current == FlockAction.FleePlayer
      ? _fleeStrategy.FlockFleeCap(maxSpeed)
      : maxSpeed;
  ```
- **Why:** `ComputeSteering.Flock` still reads `self.Flock?.Current` to
  decide *whether* the flock is fleeing (that's a state question the
  Flock owns), but the *speed cap* during flee is now delegated to the
  strategy.  The strategy can vary the formula per-creature (sheep:
  linear multiplier; deer: different curve) without the brain
  hardcoding the math.
- **Depends on:** 1, 2

## 4. Verify with tests
- **Files:** all `.cs` under `core/`
- **What:** `dotnet build && dotnet test` — all 272 tests must pass.
  Pay special attention to `FlockTests` and any test touching
  `FlockFleeSpeedMultiplier` (compilation will catch stray references).
- **Why:** Gate requirement.
- **Depends on:** 1–3
