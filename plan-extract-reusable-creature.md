# Plan: Extract Sheep-Specific Code → Reusable Patterns

Extract hardcoded sheep spawning/configuration logic from `VivariumMain.cs` into reusable
types so adding the next creature type (wolf, rabbit, etc.) doesn't require copy-paste.

## 1. Add `CreatureNeeds.Randomize(Random)` — jitter helper
- **Files:** `core/CreatureNeeds.cs` (modify), `scripts/VivariumMain.cs` (modify)
- **What:**
  - Add `public void Randomize(Random rng)` method: sets Hunger, Fatigue, Boredom to `(float)rng.NextDouble()`
  - In `VivariumMain.cs`, replace the 3-line manual jitter with `sheep.Needs.Randomize(_sim.Rng)`
- **Why:** Currently copy-pasted for each creature type. A method on `CreatureNeeds` is composable and self-documenting.
- **Depends on:** none

## 2. Add `Diet` to `CreatureTraits` — diet flows through factory pipeline
- **Files:** `core/CreatureTraits.cs` (modify), `core/BlobFactory.cs` (modify), `scripts/VivariumMain.cs` (modify)
- **What:**
  - Add `IReadOnlySet<string>? Diet` property to `CreatureTraits` (nullable, default null = no diet restriction)
  - Add to copy constructor
  - In `BlobFactory.Create`, copy `traits.Diet` to `blob.Diet` if non-null
  - In `VivariumMain.cs`, set `Diet = ["berries"]` on `sheepTraits` init, remove `sheep.Diet = sheepDiet` post-spawn line
- **Why:** Currently diet is set post-spawn via `Blob.Diet` mutable property — fragile, sheep-specific, easy to miss for new creatures. Flowing through traits makes it part of the creature definition.
- **Depends on:** none

## 3. Extract `HerdSpawner` — reusable group spawn logic
- **Files:** `core/HerdSpawner.cs` (new), `scripts/VivariumMain.cs` (modify)
- **What:**
  - Create `HerdSpawnConfig` record:
    ```csharp
    public sealed record HerdSpawnConfig
    {
        public int HerdCount { get; init; } = 3;
        public float MinHerdSeparation { get; init; } = 18f;
        public float HerdJitter { get; init; } = 2f;
        public int HerdSizeMin { get; init; } = 4;
        public int HerdSizeMax { get; init; } = 5;
        public Biome Biome { get; init; } = Biome.Plains;
        public bool JitterNeeds { get; init; } = true;
    }
    ```
  - Add static method `HerdSpawner.SpawnHerds(Simulator, ICreatureFactory, IPlacementStrategy, CreatureTraits, Drives, MapData, HerdSpawnConfig, Random, BodyPlan?)` that runs the full herd-spawn loop
  - Move `PickSeparatedBiomeCenters` and `PickBiomeCenter` into `HerdSpawner` (currently static methods on `VivariumMain`)
  - In `VivariumMain.cs`, replace ~35 lines of herd spawning with a single `HerdSpawner.SpawnHerds(...)` call
- **Why:** Herd spawning is a general pattern (biome-bound group with separation, jitter, needs randomization). Currently ~35 lines of sheep-specific code. Extracting lets any future creature type use the same spawn logic.
- **Depends on:** 1, 2

## 4. Add `CreatureTraitsTests` updates for Diet
- **Files:** `core/CreatureTraitsTests.cs` (modify)
- **What:**
  - `Default_HasExpectedValues`: add assertion that `Diet` is null
  - `CopyConstructor_ClonesAllValues`: add Diet to source and assertion
  - `Property_Mutation_Persists`: add Diet mutation + assertion
- **Why:** New field must be covered by existing trait tests.
- **Depends on:** 2

## 5. Verify gate
- `dotnet build` — zero errors
- `dotnet test` — all green
- `godot --headless --quit` — launches clean
- **Depends on:** 4

## Notes

- The sheep-specific values (5-min fatigue, 45-s recovery, berries diet, Plains biome) stay as
  *data* — what changes is WHERE they're set. After this plan, they're set in a `HerdSpawnConfig`
  and `CreatureTraits`/`Drives` initializers, not scattered through spawning code.
- Data-driven creature definitions (JSON loading of traits+drives+herd config) is the next
  logical step but is intentionally out of scope — bigger change, needs design discussion.
- `BiomeFilteredPlacement` composition is already reusable decorator pattern — no extraction needed.
