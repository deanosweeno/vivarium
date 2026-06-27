# Plan: Frolic+Rest fire at 100%, relief to 0%, latch until drained

## 1. Change Frolic consideration to fire only near 100%
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Replace Frolic logistic curve (midpoint 0.7, steepness 10) with a Power curve Exponent=10. At boredom 0.99, score ≈ 0.90 which beats Wander(0.81)+SwitchMargin. At 0.95, score ≈ 0.60 which is below Wander(0.73) — doesn't fire. Sharp cliff in last 5%.
- **Why:** User wants Frolic only at 100% play.
- **Depends on:** none

## 2. Change Rest consideration to fire only near 100%
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Change Rest curve from Power(x³) to Power(x⁸). At fatigue 0.99, Rest=0.92 — beats Wander+SwitchMargin. At 0.90, Rest=0.43 — below Wander+SwitchMargin.
- **Why:** User wants same "only at 100%" behavior for Rest.
- **Depends on:** none

## 3. Increase Frolic boredom relief rate
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** `BoredomRelievePerSec` 0.12 → 0.4. Drains 1.0→0 in ~2.5s.
- **Why:** User wants faster relief during Frolic so the play meter visibly drops.
- **Depends on:** none

## 4. Increase Rest fatigue recovery rate
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Increase `FatigueRecoverPerSec` — check current value, set to ~0.4 so 1.0→0 in ~2.5s.
- **Why:** Mirror Frolic relief rate.
- **Depends on:** none

## 5. Add Frolic latch — hold until boredom hits 0
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** Add a latch to the Decide method (like the existing Forage satiation latch): when Current is Frolic && Boredom > 0, hold. Release when Boredom ≤ 0 (or a small epsilon threshold).
- **Why:** Frolic must stick until boredom fully drained, not release early when commitment decays.
- **Depends on:** 1

## 6. Add Rest latch — hold until fatigue hits 0
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** Same latch pattern for Rest: when Current is Rest && Fatigue > 0, hold. Release when Fatigue ≤ 0.
- **Why:** Mirror Frolic latch for consistency.
- **Depends on:** 2, 4

## 7. Update/add tests
- **Files:** `core/BehaviorTests.cs` (modify)
- **What:** Update `HighBoredom_BeatsFlockHold_AndFrolics` and `LowBoredom_DoesNotFrolic` to use 1.0/0.9 boredom respectively (since curve is now Power x¹⁰). Add tests for Frolic latch and Rest latch. Update `FlocklessFrolic_ReleasesWhenBoredomDrops` — now releases at 0, not just "less than wander".
- **Why:** Updated curve changes the scoring thresholds.
- **Depends on:** 1, 2, 5, 6

## 8. Verify
- **Files:** `dotnet build`, `dotnet test`, Godot launch
- **What:** Build + full test suite, Godot headless launch.
- **Why:** Gate.
- **Depends on:** 1-7
