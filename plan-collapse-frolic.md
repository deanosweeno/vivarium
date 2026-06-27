# Implementation Plan: collapse Frolic to single steering + side-to-side animation

## 1. Collapse Frolic steering to one mode
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** Replace the three-flavor fall-through in the `SteeringKind.Frolic` case with a single steering path: darty zig-zag (`FrolicWander`) plus soft anchor tether when `HasFlock`. Remove play-chase neighbor targeting and the three-branch structure.
- **Why:** User wants one Frolic behavior, not three flavors. Simplifies steering logic.
- **Depends on:** none

## 2. Remove dead `FrolicPlayRange` config
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Remove the `FrolicPlayRange` property (only used by removed play-chase flavor).
- **Why:** Dead config after play-chase flavor is removed.
- **Depends on:** 1

## 3. Update/remove play-chase tests
- **Files:** `core/BehaviorTests.cs` (modify)
- **What:** Remove `Frolic_PlayChaseGatesOnPlayRange` test (tests removed play-chase flavor). Keep `Frolic_InFlock_StaysTetheredToAnchor` but update its assertions if needed (it tested flavor-2 specifically, now the only path is darty+tether). Update `FlocklessFrolic_ReleasesWhenBoredomDrops` if it references specific flavors.
- **Why:** Tests must match the single-Frolic behavior.
- **Depends on:** 1

## 4. Change Frolic animation from vertical hop to side-to-side body rock
- **Files:** `scripts/CreatureVisual.cs` (modify)
- **What:** Replace the vertical pronk hop (`Abs(sin)` on Y) with a side-to-side body rock: apply a sinusoidal roll rotation (around forward/Z axis) to the creature's `Rotation` when `_frolic > 0`. Remove `HopFreq` and `HopHeight` constants. Add a `RockFreq` (~3 Hz) and `RockAngle` (~15°) for the lateral sway. Blend the rock angle with `_frolic` easing.
- **Why:** User wants side-to-side body tilt as the frolic visual tell instead of vertical bounce.
- **Depends on:** none (cosmetic only, independent of steering)

## 5. Verify
- **Files:** `dotnet build`, `dotnet test`, Godot launch
- **What:** Full build + test suite (236 pass, 2 skip), Godot headless launches clean.
- **Why:** Gate before claiming done.
- **Depends on:** 1, 2, 3, 4
