# Implementation Plan: Biome Pathing & Forage Targeting

## Exploration Summary

### Current foraging problem
Forage steering (`UtilityBrain.cs:147-154`) uses two branches:
- `HasFood` true → `Steering.Arrive()` toward the food
- `HasFood` false → `Wander()` (random drift)

`HasFood` is gated by `SensesRadius = 5` units — food at 5.1u is invisible.
The sheep walks right past food just outside its sensing radius while "foraging."

### Current biome awareness
`Simulator.ApplyBiomeEffects()` already applies happiness + speed changes per
biome, but there is zero *steering* to stay in preferred biomes. A sheep wanders
into Desert and the system only caps its speed — no push back toward Plains.

`SenseContext` carries no biome data at all. The brain can't score biomes.

---

## Plan

### 1. Add food-range sensing (separate from general SenseRadius)
- **Files:** `core/BehaviorConfig.cs` (modify), `core/Simulator.cs` (modify)
- **What:** Add `FoodSenseRadius` config (default 20f) and always populate
  `FoodPosition`/`FoodDistance` in SenseContext (not gated by the general
  `SenseRadius`). `HasFood` becomes `FoodDistance <= FoodSenseRadius`.
- **Why:** Sheep should see food from a realistic distance, not just 5 units.
  This makes Forage reliably target food instead of falling through to Wander.
  Separate radius keeps the general `SenseRadius=5` for neighbor/flee detection
  unchanged.
- **Depends on:** none

### 2. Fix Forage steering: always path to nearest known food
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** Replace the Forage steering's `senses.HasFood ? Arrive(...) : Wander(...)`
  with a single `Steering.Arrive()` toward `senses.FoodPosition`. When `HasFood`
  is false, the sheep is in the Forage state but out of food range — it still
  paths toward the last known food position rather than wandering aimlessly.
- **Why:** The user's direct request: "they should target any food they can see
  and move towards it." When the Forage action is selected (hunger is high),
  the sheep should always head for food, not drift. If the nearest food is
  extremely far, the steering just produces a max-speed line toward it — the
  brain will de-select Forage when hunger drops.
- **Depends on:** step 1

### 3. Define `IBiomeAffinity` interface (compositional contract)
- **Files:** `core/IBiomeAffinity.cs` (new)
- **What:** Interface with a single method `float Comfort(Biome biome)` returning
  [0,1] — 1 for preferred biomes, 0 for hostile ones. No implementation yet.
- **Why:** Keeps biome preference separate from steering, locomotion, and brain.
  Follows the project's composition-over-inheritance rule. The interface is a
  contract that future biome-aware systems can depend on.
- **Depends on:** none

### 4. Add `BiomeAffinity` data to sheep's creature definition
- **Files:** `assets/creatures.json` (modify), `core/CreatureTraits.cs` (modify)
- **What:** Add a `PreferredBiomes` string array to the sheep JSON entry
  (`["Plains"]`) and a matching `PreferredBiomes` property on `CreatureTraits`.
  Default empty = no preference.
- **Why:** Sheep data lives in the catalog and traits. `PreferredBiomes`
  is the data contract — the sim reads it, the brain doesn't need to know
  about it. Future creature types get their own preferences.
- **Depends on:** step 3

### 5. Compute biome comfort gradient in SenseContext
- **Files:** `core/SenseContext.cs` (modify), `core/Simulator.cs` (modify)
- **What:** Add `CurrentBiome` (Biome enum) and `BiomeComfort` (float [0,1])
  to SenseContext. In `BuildSenses`, look up the current cell's biome via
  `Map.BiomeAt()` and compare against `CreatureTraits.PreferredBiomes`. A
  creature in a preferred biome gets `BiomeComfort=1.0`; in a non-preferred
  biome it drops (currently binary: 0.0). Also add `BiomePush` (Vector3, XZ)
  — a unit vector toward the nearest cell of a preferred biome, or zero when
  already in one.
- **Why:** The brain needs to know whether it's in a good biome. `BiomePush`
  gives the locomotion layer a direction to bias movement toward. The Simulator
  owns the map and can compute the gradient.
- **Depends on:** step 4

### 6. Apply biome gradient as a velocity bias in the Simulator
- **Files:** `core/Simulator.cs` (modify)
- **What:** After the brain sets `DesiredVelocity`, blend in the biome gradient:
  when `BiomeComfort < 1.0`, add `BiomePush * maxSpeed * biomePushWeight` to
  `DesiredVelocity` before the locomotion tick. `biomePushWeight` is a new
  config value (default 0.3) in `BehaviorConfig`.
- **Why:** Compositional separation — the brain decides the action (Flock,
  Forage, Wander, etc.), and the sim adds a biome preference bias on top.
  A sheep that paths toward Plains while foraging will gently curve its path;
  a sheep already in Plains feels no push. Low weight ensures biome push
  never overpowers emergency behaviors (Flee, SeekFlock).
- **Depends on:** step 5

### 7. Add biome gradient config to BehaviorConfig
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Add `BiomeGradientWeight` (default 0.3f) — how strongly the biome
  push biases the creature's velocity when outside its preferred biome.
- **Why:** Tunable; keeps the push gentle so it never dominates Flock cohesion
  or emergency escape.
- **Depends on:** step 6

### 8. Update/add tests
- **Files:** `core/BehaviorTests.cs` (modify), `core/SimulatorTests.cs` (modify)
- **What:**
  - Test that Forage steering produces Arrive toward food even when HasFood=false
  - Test that `BiomeComfort=1` in preferred biome, 0 otherwise
  - Test that `BiomePush` points toward nearest preferred-biome cell
  - Test that a sheep in Desert gets a velocity bias toward Plains
- **Why:** Regression coverage for the new behavior.
- **Depends on:** steps 2, 5, 6
