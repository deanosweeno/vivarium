# Research: Passive Grazing — Add Flock Steering

## Decision
Add `SteeringKind.Flock` to the passive graze gate in `ResolveGrazing`.

## Current state
```csharp
bool isPassiveGraze = steering == SteeringKind.Wander
    && entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold;
```

## After
```csharp
bool isPassiveGraze = (steering == SteeringKind.Wander || steering == SteeringKind.Flock)
    && entity.Needs.Hunger >= entity.Traits.GrazeHungerThreshold;
```

## Why Flock, not more
- Sheep spend most time in Flock — without it, passive grazing rarely fires.
- `SeekFlock`, `Approach` are intentionally excluded: snacking mid-chase or
  mid-approach reads as distraction, not grazing.
- `Flee`, `Rest`, `Frolic`, `Forage` already excluded (Forage has its own path).

## Tasks
1. Edit `core/Simulator.cs:ResolveGrazing` — one `||` clause
2. Update test comment/name? Existing `PassiveGraze_WanderEatsFoodWhenHungry`
   tests still pass — the Wander path is unchanged. Could add a Flock-specific
   test but the gate change is trivial and the existing tests cover the
   mechanism. Skipping new test.
3. `dotnet build`, `dotnet test`, `godot --headless --quit`
