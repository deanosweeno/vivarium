# Implementation Plan: Fix Frolic Stuck Loop + Tune Play Cadence

## Root Cause Summary

Four issues combine to create the "stuck in continual frolicking" problem:

1. **Frolic can strand sheep from their flock.** Flavor 3 (solo zoomies) has no anchor
   tether. Flavor 1 (play-chase) also lacks one. Once a sheep is out of its flock,
   `HasFlock = false` → Flock score = 0 → only Wander can challenge Frolic, but…
2. **Wander floor can't beat Frolic + SwitchMargin.** Wander at zero boredom scores
   0.059. Frolic at zero boredom scores ~0.001. SwitchMargin (0.15) requires any
   challenger to beat `FrolicScore + 0.15 = 0.151` — Wander's floor (0.059) never
   clears this, so once flockless the sheep is stuck until SeekFlock (60s delay).
3. **`FrolicPlayRange` is dead config.** Defined as 4.0 in `BehaviorConfig` but
   never read. The play-chase flavor gates on `senses.NeighborProximity > 0`, which
   uses `SenseRadius` (5.0) instead.
4. **High frolic duty cycle (~37.5%).** Boredom builds at 0.03/s during herd milling
   (speedFrac=0.35 < threshold 0.5), relieves at 0.05/s during frolic. Cycle:
   ~5s calm / ~3s play. All herd members sync up (same rates), making it look like
   perpetual bouncing.

---

## 1. Add universal flock-anchor tether to all Frolic flavors

- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `ComputeSteering`, `case SteeringKind.Frolic`: after computing the
  darty term but before the flavor branches, compute an anchor-tether steering
  vector. When `senses.HasFlock` is true, every flavor includes a soft tether
  (`Steering.Standoff` toward the flock anchor with `band = FlockLeaveRadius`).
  - Flavor 1 (play-chase): add tether at 30% weight to the existing blend so the
    chase still dominates but the sheep can't drift away.
  - Flavor 2 (frolic-in-herd): already has a tether — increase its authority by
    removing the `darty` term's solo strength (use `darty * 0.5f` like flavor 1).
  - Flavor 3 (solo zoomies): when `HasFlock` is true, replace pure darty with
    `tether + darty * 0.7f` so the sheep expresses playfulness while staying near
    the herd.
  - When `HasFlock` is false, flavors 2 and 3 fall through to pure darty (current
    behavior for flockless creatures).
- **Why:** This is the primary fix for the stuck loop — prevents flock separation
  during Frolic, so Flock score always remains available to retake control.
- **Depends on:** none

## 2. Wire `FrolicPlayRange` into play-chase gate

- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In the play-chase branch (`if (senses.HasNeighbor && ...)`), replace
  the implicit `senses.NeighborProximity > 0` gate (which uses SenseRadius=5.0)
  with an explicit distance check using `_config.FrolicPlayRange`. Compute the
  neighbor's horizontal distance to self, and only trigger play-chase when the
  distance ≤ `FrolicPlayRange`.
- **Why:** Makes the dead config value functional; gates play-chase at the intended
  4-unit range instead of the broader 5-unit sense radius.
- **Depends on:** none (can be done alongside Step 1)

## 3. Tune boredom cadence for a healthier duty cycle

- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Three config changes:
  - `BoredomGainPerSec`: 0.03 → **0.015** (half the buildup rate)
  - `BoredomRelievePerSec`: 0.05 → **0.06** (slightly faster relief)
  - `SwitchMargin`: 0.15 → **0.10** (narrower — commitment bonus already provides
    stickiness; makes action switching feel more responsive)
- **Why:** New cycle: ~10s calm / ~2s play = ~16.7% duty cycle. Bursts feel like
  distinct playful moments rather than a constant state. Lower SwitchMargin makes
  Wander slightly more competitive as a second-line defense, and makes the
  Frolic→Flock release happen faster.
- **Depends on:** Step 1 (universal tether ensures Flock is always available to
  retake control, so lower SwitchMargin is safe)

## 4. Update existing tests for new cadence constants

- **Files:** `core/BehaviorTests.cs` (modify)
- **What:** Review all tests that depend on explicit Boredom thresholds for Frolic.
  The logistic midpoint (0.7) stays the same, but the timing of boredom relief
  changes. Tests that use `Boredom = 1f` to force Frolic selection should still
  pass. Tests that assert specific timing or score values may need updated expected
  values. Run `dotnet test` and fix any broken assertions.
- **Why:** Keep the test suite green after config changes.
- **Depends on:** Step 3

## 5. Add regression tests

- **Files:** `core/BehaviorTests.cs` (modify)
- **What:** Three new tests:
  - **`FlocklessFrolic_ReleasesWhenBoredomDrops`** — Create a flockless creature
    with high boredom; assert it picks Frolic. Then feed it a `SenseContext` with
    `Boredom = 0f`; assert it switches away from Frolic (to Wander, since no
    flock). This validates the Wander-floor + SwitchMargin fix.
  - **`Frolic_PlayChaseGatesOnPlayRange`** — Create a frolicking creature with a
    neighbor at 4.5 units (beyond `FrolicPlayRange=4` but within `SenseRadius=5`).
    Assert it does NOT use play-chase (steer should not point toward neighbor).
    Then move neighbor to 3.5 units; assert it DOES use play-chase.
  - **`Frolic_InFlock_StaysTetheredToAnchor`** — A frolicking creature with
    `HasFlock = true` and no neighbor should produce desired velocity that
    includes a component toward the anchor (not pure darty). Check that
    `DesiredVelocity` has a non-zero projection toward the anchor.
- **Why:** Prevents regression of the three root causes.
- **Depends on:** Steps 1, 2, 3

## 6. Run full verification gate

- **Files:** none (verification only)
- **What:** `dotnet build` → `dotnet test` → `run_project` → `game_get_errors` →
  `game_get_logs` → `game_screenshot`
- **Why:** Per AGENTS.md verification contract.
- **Depends on:** Steps 1–5
