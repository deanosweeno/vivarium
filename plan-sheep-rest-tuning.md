# Plan: Sheep Rest Tuning (per-creature fatigue rates)

Move fatigue rates from global `BehaviorConfig` to per-creature `CreatureTraits` so sheep get
a 5-minute buildup / 45-second recovery cadence while sprouts keep current fast-recovery behavior.

## Step 1 — Add fatigue fields to CreatureTraits

**File:** `core/CreatureTraits.cs`

Add two properties with current values as defaults:
- `FatigueGainPerSec` (default `0.06f`) — fatigue accrued per second while moving (scaled by speed frac)
- `FatigueRecoverPerSec` (default `0.4f`) — fatigue recovered per second while nearly stopped

Include in copy constructor so `new CreatureTraits(other)` preserves them.

**Why:** Per-creature tuning. Defaults preserve all existing behavior; only sheep gets different values.

## Step 2 — Update CreatureTraitsTests for the new fields

**File:** `core/CreatureTraitsTests.cs`

- `Default_HasExpectedValues`: add assertions for `FatigueGainPerSec` (0.06f) and `FatigueRecoverPerSec` (0.4f)
- `CopyConstructor_ClonesAllValues`: add the new fields to the source object and their assertions
- `Property_Mutation_Persists`: add mutation + assertion for the new fields

## Step 3 — Switch Simulator.UpdateNeeds from Behavior to Traits

**File:** `core/Simulator.cs` (lines 730, 732)

Change:
- `Behavior.FatigueRecoverPerSec` → `entity.Traits.FatigueRecoverPerSec`
- `Behavior.FatigueGainPerSec` → `entity.Traits.FatigueGainPerSec`

Only 2 call sites. The Simulator already has `entity.Traits` in scope.

## Step 4 — Remove FatigueGainPerSec / FatigueRecoverPerSec from BehaviorConfig

**File:** `core/BehaviorConfig.cs` (lines 128, 131)

Delete the two property definitions. No other code references them (Step 3 already switched call sites).

## Step 5 — Set sheep-specific fatigue rates in VivariumMain

**File:** `scripts/VivariumMain.cs` (sheepTraits initializer, ~line 163)

Add to `sheepTraits`:
```csharp
FatigueGainPerSec = 1f / 300f,    // fills fatigue bar in ~5 min of activity
FatigueRecoverPerSec = 1f / 45f,  // drains from 1→0 in ~45 sec of rest
```

Sprouts get defaults via `CreatureTraits()` / `CreatureTraits.Default` — no change needed for them.

**Why these values:**
- At max speed (speedFrac=1), fatigue gain = 0.00333/s → 300 seconds to reach 1.0
- At rest, fatigue recovery = 0.0222/s → 45 seconds from 1.0 to 0
- Rest action uses Power(x⁸) curve → triggers only near fatigue=1.0, latch holds until drained to 0

## Step 6 — Verify gate

1. `dotnet build` — zero errors
2. `dotnet test` — all green (expect 239 passed, 2 skipped)
3. `godot --headless --quit` — launches clean with new sheep fatigue rates

## Notes

- **Frolic animation**: Already side-to-side rock (`CreatureVisual.cs` line 129 — `Mathf.Sin()` roll at RockFreq=3Hz, ~15°). No changes needed.
- **Rest latch**: Already implemented (hold until fatigue=0). Works with any fatigue rate values.
- **No test breakage**: Only 2 code sites reference the old config properties (both in `Simulator.cs`).
  Tests use `SenseContext.Fatigue` (the need value), not the per-second rate config.
- **Boredom rates** stay on `BehaviorConfig` (not moved in this change).
