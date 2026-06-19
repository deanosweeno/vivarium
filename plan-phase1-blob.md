# Implementation Plan: Phase 1 — Blob

## 0. Prerequisites — C# build infrastructure ✅
- `Vivarium.sln`, `vivarium.csproj`, `core/core.csproj` created
- `dotnet build` — 0 errors, 0 warnings

## 1. Arena.cs — world bounds ✅
- `core/Arena.cs` — struct with Clamp, Contains, Reflect
- Uses `System.Numerics.Vector2`

## 2. Blob.cs — entity state and behavior
- Enum `WanderState { Idle, Sliding }`
- Position/Velocity (Vector2), R/G/B (float 0-1), State, StateTimer
- Tick, StartIdle, StartSlide, RandomPastelColor
- Wander cycle: Idle(0.5-3s) → Slide(random dir, 0.2-0.6 u/s, 1-4s) → repeat
- Wall bounce reflects velocity

## 3. Simulator.cs — tick loop
- List<Blob>, Arena, seeded Random
- SpawnBlob(Vector2), Tick(double delta)

## 4. Unit tests for core
- xUnit: BlobTests.cs + SimulatorTests.cs
- Tests: idle/transition/bounce/determinism

## 5. Wire projects + verify
- ProjectReference core → vivarium
- `dotnet build` + `dotnet test` green

## 6. Update project.godot
- main_scene → `res://scenes/vivarium.tscn`

## 7. vivarium.tscn — main scene
- Floor + 4 walls + camera + light + VivariumMain script

## 8. Blob.tscn — blob prefab
- Cube mesh + BlobVisual script

## 9. VivariumMain.cs — sim controller
- Simulator owner, click→spawn, per-frame sync

## 10. BlobVisual.cs — visual sync
- Init/SyncFromModel, position + pastel material

## 11. Full verification gate
- Build, test, launch, click→spawn, wander+bounce, multi-blob
