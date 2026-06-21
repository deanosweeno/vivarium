# Implementation Plan: High-noon GI (rotated light + SDFGI + ambient)

## Goal
Rotate the DirectionalLight3D to shine straight down (12:00), add a
WorldEnvironment with SDFGI enabled and ambient light fill so shadows are
illuminated rather than pitch black.

## Current state
- DirectionalLight3D at (5,10,5), identity rotation → shines diagonal (-Z)
- Shadows on (bias/PSSM applied at runtime from VivariumMain.cs)
- No GI, no ambient, no WorldEnvironment → shadows go to black

## Strategy
Try MCP scene-editing tools first. If they work, bake the changes into
`vivarium.tscn`. If not, fall back to C# runtime config in `VivariumMain._Ready()`.

---

## 1. Rotate DirectionalLight3D to shine straight down + reposition
- **Files:** `scenes/vivarium.tscn` (modify, via godot_modify_scene_node)
- **What:**
  - Set `transform` so the light shines along world -Y:
    - In Godot, DirectionalLight3D shines along local -Z
    - Rotate 90° around X → local -Z = world -Y
    - Position: (0, 30, 0)
    - In scene terms: `Transform3D(1, 0, 0, 0, 0, 1, 0, -1, 0, 0, 30, 0)`
  - Also set `light_color = Color(1, 1, 0.98)` (noon white, slight warmth)
  - Set `light_energy = 1.0` (noon is brighter)
- **Why:** High noon lighting direction; GI + ambient will fill the flat shadows.
- **Depends on:** none

## 2. Add WorldEnvironment node with SDFGI + ambient
- **Files:** `scenes/vivarium.tscn` (modify, via godot_add_node)
- **What:** Add a `WorldEnvironment` node as child of VivariumMain, with an
  Environment resource configured:
  - `SdfgiEnabled = true`
  - `SdfgiUseOcclusion = false` (outdoor, avoids blotches)
  - `SdfgiReadSkyLight = true`
  - `SdfgiBounceFeedback = 0.5` (natural multi-bounce)
  - `SdfgiCascades = 4`
  - `SdfgiMinCellSize = 0.5` → auto-computed cascade distances
  - `AmbientLightSource = AmbientSource.Color`
  - `AmbientLightColor = Color(0.25f, 0.30f, 0.45f)` (cool sky fill)
  - `AmbientLightSkyContribution = 0.0` (use color only)
  - `AmbientLightEnergy = 0.4` (gentle fill)
- **Why:** SDFGI provides real-time bounce; ambient fills the deepest shadows.
- **Depends on:** none

## 3. Save the scene
- **Files:** `scenes/vivarium.tscn`
- **What:** `godot_save_scene`
- **Why:** Persist the changes.
- **Depends on:** 1, 2

## 4. Build + test
- **What:** `dotnet build`, launch game, orbit camera to top-down view, screenshot
- **Why:** Verify SDFGI is active and shadows are softly lit.
- **Depends on:** 3

## 5. (Fallback) If MCP tools fail: apply everything in VivariumMain.cs
- **Files:** `scripts/VivariumMain.cs` (modify)
- **What:** In `_Ready()`, after the shadow-bias loop, add:
  - `light.Rotation = new Vector3(Mathf.Pi / 2, 0, 0);`
  - `light.Position = new Vector3(0, 30, 0);`
  - `light.LightColor = new Color(1, 1, 0.98f);`
  - `light.LightEnergy = 1.0f;`
  - Create `Environment` resource, configure SDFGI+ambient
  - Create `WorldEnvironment` node, add as child
- **Depends on:** MCP tools unavailable
