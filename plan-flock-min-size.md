# Implementation Plan: sheep minimum flock size ≥ 3

Raise the minimum cluster size for flock formation from 2 to 3 so
sheep must gather in threes before a herd coalesces.  Since the kin
gate already isolates species, this is effectively sheep-specific
even though the threshold lives in the shared config.

---

## 1. Add `FlockMinSize` to `BehaviorConfig`
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Add a new init property:
  ```csharp
  /// <summary>Minimum number of nearby kin required to seed a new flock.  Below this
  /// threshold unflocked kin remain solitary.  Default 3 (sheep: herd of three).</summary>
  public int FlockMinSize { get; init; } = 3;
  ```
- **Why:** Single configurable threshold.  Species isolation is already
  handled by the genetics kin gate — only same-species creatures can form
  a flock together, so the count applies per-species naturally.
- **Depends on:** none

## 2. Gate flock formation on `FlockMinSize`
- **Files:** `core/FlockManager.cs` (modify)
- **What:** In the Form step, between building `group` and creating the
  `Flock`, add:
  ```csharp
  if (group.Count < behavior.FlockMinSize) continue;
  ```
- **Why:** Silently skip clusters too small to form a herd.  Join (step 2)
  still works for singles — a lone sheep can still join an existing flock
  of 2+.  Only *new* flock creation requires the minimum.
- **Depends on:** 1

## 3. Update the 2-sheep test
- **Files:** `core/FlockTests.cs` (modify)
- **What:** In `FlockManager_FormsAndDropsMembers_InIsolation`, change the
  test to spawn 3 sheep (not 2) so the minimum is met.
  ```csharp
  var a = Sheep(sim, new Vector3(0, 0, 0));
  var b = Sheep(sim, new Vector3(1.5f, 0, 0));
  var c = Sheep(sim, new Vector3(0, 0, 1.5f));   // third sheep
  // Assert.Equal(3, flock.Members.Count);
  ```
  Keep the Leave test (stray one member) but adjust the count assertion.
- **Why:** Default `FlockMinSize = 3` means 2-sheep test would produce
  zero flocks → `Assert.Single(flocks)` fails.
- **Depends on:** 1, 2

## 4. Verify with tests
- **Files:** all `.cs` under `core/`
- **What:** `dotnet build && dotnet test` — all 272 tests must pass.
  (The only test that forms a flock from exactly 2 sheep is the one
  updated in step 3.)
- **Why:** Gate requirement.
- **Depends on:** 1–3
