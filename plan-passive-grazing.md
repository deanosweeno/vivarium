# Plan: Passive Grazing During Wander

Add opportunistic grazing: while a creature is Wandering and hungry enough,
it eats nearby food without switching to Forage. Sheep-specific tuning lives on
`CreatureTraits`, but the mechanism is general.

## 1. Add `GrazeHungerThreshold` to `CreatureTraits`
- **Files:** `core/CreatureTraits.cs` (modify)
- **What:**
  - Add `public float GrazeHungerThreshold { get; set; } = 0.3f;`
  - Add to copy constructor: `GrazeHungerThreshold = other.GrazeHungerThreshold;`
  - Explanation: hunger must be ≥ this value for passive grazing to kick in during Wander.
    Default 0.3 keeps well-fed creatures from nibbling.
- **Why:** Per-creature tunable — sheep get species-appropriate threshold, sprouts leave default.
- **Depends on:** none

## 2. Wire passive grazing into `ResolveGrazing`
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Change the steering gate at the top of `ResolveGrazing` from:
    ```csharp
    if (entity.Brain?.Current?.Steering != SteeringKind.Forage) continue;
    ```
    to:
    ```csharp
    var steering = entity.Brain?.Current?.Steering;
    bool isForage = steering == SteeringKind.Forage;
    bool isPassiveGraze = steering == SteeringKind.Wander
        && entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold;
    if (!isForage && !isPassiveGraze) continue;
    ```
  - Everything else in the method (NearestFood, eatRange, Bite) stays the same — the
    only change is WHICH steering modes can eat.
- **Why:** Minimal change — one condition becomes two. Same eat range, same diet
  filtering, same `FoodItem.Bite()` consumption. No new methods needed.
- **Depends on:** 1

## 3. Set sheep-specific `GrazeHungerThreshold`
- **Files:** `scripts/VivariumMain.cs` (modify)
- **What:**
  - Add `GrazeHungerThreshold = 0.2f,` to the `sheepTraits` initializer
  - Suggested value: 0.2 (sheep are grazers — start nibbling at 20% hunger)
- **Why:** Sheep graze more readily than the general default. Tune freely.
- **Depends on:** 1

## 4. Add `CreatureTraits` test for `GrazeHungerThreshold`
- **Files:** `core/CreatureTraitsTests.cs` (modify)
- **What:**
  - `Default_HasExpectedValues`: assert `GrazeHungerThreshold == 0.3f`
  - `CopyConstructor_ClonesAllValues`: add Mutation + assert
  - `Property_Mutation_Persists`: add `GrazeHungerThreshold` mutation + assert
- **Depends on:** 1

## 5. Add `Simulator` test for passive grazing
- **Files:** `core/SimulatorTests.cs` (modify)
- **What:**
  - `PassiveGraze_WanderEatsFoodWhenHungry`:
    - Spawn blob on top of food, set brain to Wander, set Hunger ≥ threshold
    - Tick a few frames, assert Hunger decreased
  - `PassiveGraze_WanderDoesNotEatWhenNotHungry`:
    - Spawn blob on food, Wander, Hunger < threshold
    - Tick a few frames, assert Hunger unchanged or increased (only gains from HungerGainPerSec)
- **Depends on:** 2

## 6. Verify gate
- `dotnet build` — zero errors
- `dotnet test` — all green
- `godot --headless --quit` — launches clean
- **Depends on:** 5

## Notes

- Passive grazing only fires during Wander — not Flock or any other action.
  Flock could be added later (one OR) but is intentionally out of scope.
- `EatRange` from `FoodConfig` is reused — no separate graze range needed.
- Diet filtering already works through `NearestFood(entity.Position, entity.Diet)`.
- The `NearestFood` call costs O(food) per creature per tick. This already runs for all
  Forage creatures; adding Wander creatures increases the cost proportionally. Fine for
  the current arena scale.
- If the brain decides to Forage (hunger high enough to out-score Wander), the creature
  switches to active hunting and grazing is already covered. The passive grazing is the
  *opportunistic* nibble between dedicated Forage runs.
