# Implementation Plan: Configure DirectionalLight3D shadow settings from VivariumMain.cs

## Problem
The `godot-mcp` extension failed to register its tools this session, blocking `.tscn`
edits. The DirectionalLight3D in `vivarium.tscn` needs shadow bias + PSSM settings to fix
shadow acne. Instead of editing the scene file, apply the settings at runtime from C#.

## Approach
In `VivariumMain._Ready()`, find the DirectionalLight3D sibling node and set:
- `DirectionalShadowMode = DirectionalShadowMode.Pssm2Splits`
- `DirectionalShadowSplit1 = 0.1f`
- `ShadowBias = 0.02f`
- `ShadowNormalBias = 0.5f`

This is temporary — when MCP tools are restored, remote the C# config and bake into the scene.

## 1. Add light configuration to VivariumMain.cs
- **Files:** `scripts/VivariumMain.cs` (modify)
- **What:** In `_Ready()`, after the camera find loop, locate the DirectionalLight3D and set shadow properties.
- **Why:** Godot MCP tools unavailable this session — runtime config achieves the same fix.
- **Depends on:** none

## 2. Verify
- **What:**
  - `dotnet build Vivarium.csproj` — zero warnings
  - Launch game, verify no shadow acne
- **Depends on:** 1
