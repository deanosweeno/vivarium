# Plan: Fix Passive Grazing — Add Flock + Update Comment

The previous research note planned adding `SteeringKind.Flock` to the passive
graze gate, but the edit was never applied. The `ResolveGrazing` method still
checks only `Wander`. Sheep in Flock walk over food at 0.9 hunger without eating.

## 1. Add `Flock` to the passive graze gate + update comment
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Change:
    ```csharp
    bool isPassiveGraze = steering == SteeringKind.Wander
        && entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold;
    ```
    to:
    ```csharp
    bool isPassiveGraze = (steering == SteeringKind.Wander || steering == SteeringKind.Flock)
        && entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold;
    ```
  - Update the doc comment above the method: remove "Only creatures whose current
    action steers toward Forage eat" and replace with listing the gated actions.
- **Why:** Sheep spend most time in Flock — without it, passive grazing rarely fires.
- **Depends on:** none

## 2. Verify gate
- `dotnet build` — zero errors
- `dotnet test` — 243 total, 2 skipped, 0 failed
- `godot --headless --quit` — launches clean
- **Depends on:** 1

## Note: Forage vs Flock scoring

At 0.9 hunger, Flock (0.63) still out-scores Forage (0.583) because the Flock
base weight (0.7) plus Sociability (0.9) beats Forage's BaseWeight (0.9) ×
Hunger²Appetite (0.648). Sheep only switch to active Forage at hunger=1.0
(score 0.72). This is a separate tuning issue — the Flock gate fix makes
sheep eat *during* Flock, which is the right design (grazing is passive, not an
interruption). If the user wants sheep to also switch to dedicated Forage
earlier, that's a Flock-vs-Forage scoring change.
