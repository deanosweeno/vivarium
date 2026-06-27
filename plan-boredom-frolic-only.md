# Implementation Plan: Boredom relief from Frolic action only

## 1. Change boredom update from speed-based to action-based
- **Files:** `core/Simulator.cs` (modify)
- **What:** In `UpdateNeeds`, replace the `speedFrac < BoredomActiveSpeedFrac` check with `entity.IsFrolicking`. Boredom relieves only during Frolic; builds during all other actions (including Wander, Flock, Forage, Approach, etc.). Keep the speedFrac variable for Fatigue (which remains speed-based).
- **Why:** Boredom was silently draining during any active movement (Flock jostling, Wander, Forage) because speedFrac easily exceeded 0.36. Only Frolic should relieve boredom — making it a true "need for play" meter.
- **Depends on:** none

## 2. Remove dead `BoredomActiveSpeedFrac` config
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Remove `BoredomActiveSpeedFrac` property (0.36f). Update `BoredomRelievePerSec` comment to clarify it only applies during Frolic now.
- **Why:** Dead config after speed threshold is removed.
- **Depends on:** 1

## 3. Update `BoredomRelievePerSec` to match new Frolic-only semantics
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Rename or keep `BoredomRelievePerSec` but update its XML doc to say "relieved per second during Frolic". Current value 0.12/s is fine — a 3-4s Frolic burst still drains ~36-48% boredom. Optionally rename to `FrolicBoredomReliefPerSec` for clarity.
- **Why:** Config name should reflect new semantics.
- **Depends on:** 2

## 4. Update comment in Frolic/Wander action configs
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Update the Wander comment (currently references "Moving relieves Boredom") and Frolic comment ("Frolic is movement, so BoredomRelievePerSec drains Boredom") to reflect that only Frolic relieves boredom now.
- **Why:** Comments should match reality.
- **Depends on:** 1

## 5. Update tests
- **Files:** `core/BehaviorTests.cs` (modify)
- **What:** No logic changes needed — existing tests set boredom explicitly in SenseContext. But remove any comment references to "moving relieves Boredom" / speed-based relief in test comments. The `FlocklessFrolic_ReleasesWhenBoredomDrops` test (line 383) tests scoring math, not needs update, so it's unaffected.
- **Why:** Keep comments accurate.
- **Depends on:** 1

## 6. Verify
- **Files:** `dotnet build`, `dotnet test`, Godot launch
- **What:** Build + full test suite (expect 236 pass, 2 skip), Godot headless launch clean.
- **Why:** Gate.
- **Depends on:** 1, 2, 3, 4, 5
