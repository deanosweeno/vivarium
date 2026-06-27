# Implementation Plan: Frolic Satiation & Flavor Visibility

## Findings

### Why boredom isn't satiating during play

The speed threshold `BoredomActiveSpeedFrac = 0.5` is exactly at the boundary of
what the frolic flavors produce:

| Flavor | Steering formula | Typical speedFrac | Boredom effect |
|---|---|---|---|
| 1. Play-chase | `chase + sep*push + darty*0.5 + anchor*0.3` | 0.3–0.9 (varies with chase strength) | **unreliable** — builds when chase is weak |
| 2. Frolic-in-herd | `anchorPull + darty*0.5` | ~0.5 when near anchor | **borderline** — slight dip builds boredom |
| 3. Solo zoomies | `darty` | 1.0 | ✅ relieves fine |

Flavor 1 and 2 can spend long stretches at or below the 0.5 cutoff, meaning
boredom *builds* during what should be relief. The sheep stays stuck in Frolic
because the boredom meter never drops enough for Flock (0.63) to retake.

### Why only one flavor is visible

The visual system (`CreatureVisual.cs:125`) has a single boolean `IsFrolicking`
→ one pronk hop animation. There is **zero visual distinction** between
play-chase, frolic-in-herd, and solo zoomies. You *cannot* see which flavor is
active — they all look like the same pronk bounce.

Additionally, `FrolicPlayRange = 4u` — neighbors must be within 4 arena units
to trigger play-chase. With `PersonalSpaceRadii = 4` (personal space ≈ 2.4u)
and flock spread ~1.3u (5 members), neighbors *are* typically within range.
But play-chase doesn't look different from frolic-in-herd visually, so the
player can't notice it firing.

---

## Plan

### 1. Fix boredom relief: lower `BoredomActiveSpeedFrac`
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Change `BoredomActiveSpeedFrac` from `0.5f` to `0.35f`.
- **Why:** Drop the threshold below the slowest Frolic blend (flavor 2 at
  darty*0.5 = 0.5, also below `FlockWanderFloor = 0.35` which is the herd
  milling speed). Any Frolic movement now relieves boredom reliably. Flock
  milling (speedFrac=0.35) stays at the threshold — it neither builds nor
  relieves, preserving the design intent that only active play drains boredom.
- **Depends on:** none

### 2. Double relief rate so play bursts are short and snappy
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Change `BoredomRelievePerSec` from `0.06f` to `0.12f`.
- **Why:** At 0.12/s, a full-boredom (1.0) sheep drains to the Flock recapture
  point (~0.72) in ~2.3s — about 3-4 pronk bounces — instead of the current
  ~4.7s. This makes play bursts feel like a brief "pop," not a prolonged state.
  Combined with step 1, relief is now reliable (never interrupted by sub-0.5
  speedFrac dips).
- **Depends on:** step 1 (or can be done together)

### 3. Update existing tests for new constants
- **Files:** `core/BehaviorTests.cs` (modify)
- **What:** Adjust any test that hardcodes boredom relief values or Frolic
  cadence expectations to match the new `BoredomRelievePerSec = 0.12`.
- **Why:** Regression tests must reflect the new tuning.
- **Depends on:** steps 1, 2

### 4. (Optional) Make flavors visually distinct
- **Files:** `scripts/CreatureVisual.cs` (modify), `core/Creature.cs` (modify)
- **What:** Expose a `SteeringKind?` or flavor enum on the Creature model so
  `CreatureVisual` can show different animations/tells for play-chase vs
  frolic-in-herd vs solo zoomies. E.g., play-chase could have a wider orbit
  bounce, frolic-in-herd a subtler head-bob, solo zoomies the full pronk.
- **Why:** Currently impossible to distinguish flavors visually — this makes
  the behavior readable to the player.
- **Depends on:** none (orthogonal to core tuning)
- **Note:** This is a visual-only change; core simulation is unaffected.
  Requires design decisions from user (what should each flavor look like?).

### 5. (Optional) Widen `FrolicPlayRange` so play-chase fires more often
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Increase `FrolicPlayRange` from `4f` to `6f`.
- **Why:** At 4u, only very tight flock neighbors trigger play-chase. At 6u
  (slightly beyond `SenseRadius` of 5u, but gated by the neighbor check anyway),
  more distant herd members can trigger the mutual-chase orbit.
- **Depends on:** none
- **Risk:** May make frolicking sheep dart toward distant neighbors, increasing
  herd spread. The universal anchor tether (already in place) mitigates this.

**User decision needed on steps 4 and 5 before implementing.**
