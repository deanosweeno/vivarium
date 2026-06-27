# Plan: Decouple Grazing from SteeringKind — Compositional Refactor

Extract the hardcoded `SteeringKind.Forage` / `Wander` / `Flock` checks from
`Simulator.ResolveGrazing` into a `GrazingMode` flag on `BehaviorAction`. The
Simulator no longer needs to know WHICH actions graze — each action declares
its own grazing policy.

Follows the existing `EmergencyCapable`/`EmergencyThreshold` pattern on
`BehaviorAction`.

## 1. Add `GrazingMode` enum + `Grazing` property to `BehaviorAction`
- **Files:** `core/BehaviorAction.cs` (modify)
- **What:**
  - Add enum (after `SteeringKind`, before `BehaviorAction`):
    ```csharp
    public enum GrazingMode
    {
        /// <summary>Never graze (default).</summary>
        None,
        /// <summary>Graze nearby food unconditionally.</summary>
        Always,
        /// <summary>Graze when hunger ≥ CreatureTraits.GrazeHungerThreshold.</summary>
        WhenHungry,
    }
    ```
  - Add property to `BehaviorAction`:
    ```csharp
    public GrazingMode Grazing { get; init; } = GrazingMode.None;
    ```
- **Why:** Action declares its own grazing eligibility. Simulator respects the
  contract without coupling to specific `SteeringKind` values.
- **Depends on:** none

## 2. Set `Grazing` on actions in `BehaviorConfig.DefaultActions()`
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:**
  - Forage: add `Grazing = GrazingMode.Always,`
  - Wander: add `Grazing = GrazingMode.WhenHungry,`
  - Flock: add `Grazing = GrazingMode.WhenHungry,`
  - All others: omit (defaults to `None`)
- **Why:** This is where action-level policy belongs. Adding a new grazer later
  is a one-line change in the config, not a Simulator edit.
- **Depends on:** 1

## 3. Rewrite `Simulator.ResolveGrazing` gate using `GrazingMode`
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Replace:
    ```csharp
    var steering = entity.Brain?.Current?.Steering;
    bool isForage = steering == SteeringKind.Forage;
    bool isPassiveGraze = (steering == SteeringKind.Wander || steering == SteeringKind.Flock)
        && entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold;
    if (!isForage && !isPassiveGraze) continue;
    ```
    with:
    ```csharp
    var action = entity.Brain?.Current;
    bool canGraze = action?.Grazing switch
    {
        GrazingMode.Always => true,
        GrazingMode.WhenHungry => entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold,
        _ => false
    };
    if (!canGraze) continue;
    ```
  - Update method doc comment — action-agnostic wording.
- **Depends on:** 1, 2

## 4. Remove unused steering-kind checks in Simulator (if any left)
- **Files:** `core/Simulator.cs` (verify)
- **What:**
  - Verify no other `SteeringKind.Forage` / `Wander` / `Flock` references in
    grazing-related code remain. (The sense building and focus resolution are
    separate concerns — left untouched.)
- **Depends on:** 3

## 5. Verify gate
- `dotnet build` — zero errors
- `dotnet test` — 243 total, 2 skipped, 0 failed
- `godot --headless --quit` — launches clean
- **Depends on:** 4

## Notes

- `GrazeHungerThreshold` stays on `CreatureTraits` — it's species-level data
  (sheep=0.2, sprouts=0.3). The action declares WHEN to check the threshold
  (`WhenHungry` vs `Always` vs `None`), not what the threshold value is.
- `BehaviorAction` already uses `EmergencyCapable`/`EmergencyThreshold` for
  action-level simulator behavior — `GrazingMode` follows the same pattern.
- Existing tests (`PassiveGraze_*`, `Diet_*`) should pass without changes —
  behavior unchanged, only mechanism changes.
