# Implementation Plan: Fix terrain shadow acne (sporadic speckled shadows)

## Root cause
Single `DirectionalLight3D` with `shadow_enabled = true` but no shadow bias,
normal bias, or PSSM split settings. On a dense mesh (4 subdiv/cell, 128×128 =
~250k triangles) with smooth normals from `GenerateNormals()`, the shadow map
fights the geometry producing speckled self-shadowing artifacts ("shadow acne").

## 1. Add shadow quality settings to DirectionalLight3D
- **Files:** `scenes/vivarium.tscn` (modify)
- **What:** Add these properties to the `DirectionalLight3D` node:
  ```
  directional_shadow_mode = 1        # PSSM 2-split for large terrain
  directional_shadow_split_1 = 0.1   # near split distance (fraction of max)
  shadow_bias = 0.02                 # push shadow sample away from surface
  shadow_normal_bias = 0.5           # bias along surface normal
  ```
  Keeping: `light_energy = 0.8`, `shadow_enabled = true`, position/color as-is.
- **Why:** Shadow bias + normal bias eliminate acne by offsetting the depth test.
  PSSM splits give higher shadow resolution near the camera and lower far away,
  which suits the large terrain map. Roughness 0.95 (matte) means no specular
  to fight — just need clean contact shadows.
- **Depends on:** none

## 2. Visual verification
- **What:** Launch the game, orbit the camera around the terrain, verify no
  more speckled/random dark patches on the hills. Shadows should fall cleanly.
- **Depends on:** 1
