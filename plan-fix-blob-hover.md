# Implementation Plan: Fix blob ground placement (remove double lift)

## Root cause
`BlobVisual.SyncFromModel()` adds `+0.5f` to model Y, but `Simulator` already positions
entities at `floor + Radius` (default radius 0.5). The cube (1×1×1 BoxMesh) thus sits
with its bottom at `floor + 0.5` instead of `floor` — a consistent 0.5-unit hover.

## 1. Remove the +0.5f offset in BlobVisual
- **Files:** `scripts/BlobVisual.cs` (modify)
- **What:** Change line 48 from:
  ```csharp
  Position = new Vector3(_model.Position.X, _model.Position.Y + 0.5f, _model.Position.Z);
  ```
  to:
  ```csharp
  Position = new Vector3(_model.Position.X, _model.Position.Y, _model.Position.Z);
  ```
- **Why:** The Simulator already sets entities at `floor + Radius` so the visual
  node doesn't need an additional offset. For default radius 0.5, the cube bottom
  lands exactly on the terrain surface.
- **Depends on:** none

## 2. Verify
- **What:**
  - `dotnet build Vivarium.csproj` — zero warnings
  - `dotnet test core/core.csproj` — all 137 pass
  - Visual check: launch game, verify blobs rest on ground (not hovering)
- **Depends on:** 1
