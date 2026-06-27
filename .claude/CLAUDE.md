# CLAUDE.md — Vivarium Project Guide

> Project: Vivarium — 3D simulation sandbox (creature ecosystem)
> Engine: Godot 4.7 / .NET 8.0 / C#
> Renderer: Forward Plus

## Architecture

```
core/          ← Pure C# simulation. NO Godot references. NO using Godot;.
                 Deterministic: seeded Random, no DateTime.Now, no GUIDs.
                 Unit-tested (184 tests, all green).

scripts/       ← Thin Godot presentation layer. Reads core state, draws it.
                 Owns NO rules — no AI logic, no map rules, no physics here.

scenes/        ← .tscn scene files (Blob, Food, vivarium main scene).
assets/        ← JSON data (biomes.json, foods.json), .tres resources, map files.
tools/         ← Separate console projects (e.g., MapGenTool).
docs/          ← Architecture specs and research docs.
```

## Rules (enforced by AGENTS.md harness)

- **GDScript ban**: All game code is C#. Edit `.cs` files with `edit`/`write` tools, never `create_script`/`attach_script`/`game_eval` for authoring logic.
- **Partial classes**: All Godot-derived classes must be `partial`.
- **Double delta**: `_Process(double delta)` / `_PhysicsProcess(double delta)` — `double`, not `float`.
- **No magic strings**: Use `[Export] NodePath` or `%UniqueName`, never `GetNode("...")`.
- **Nullable on, warnings-as-errors**: No null-blind code.
- **core/ purity**: `core/` never references Godot. No `using Godot;` in any core file.
- **Determinism in core/**: Same seed ⇒ same result. Use the seeded `Random` only, never `GD.Randi()`, `DateTime.Now`, `Guid.NewGuid()`, thread-local entropy.

## Verification Gate (run after every change)

1. `dotnet build` — zero errors. Use `godot_mcp` tool `dotnet_build` with `projectPath: "/home/dean/vivarium"`.
2. `dotnet test` — all 184 tests green. Use `godot_mcp` tool `dotnet_test` with `projectPath: "/home/dean/vivarium"`.
3. If scenes/resources changed: `run_project` → `game_get_errors` → `game_get_logs` → `game_screenshot`.
4. Never claim success without saying which gates ran and their output.

## MCP Tools Available

All standard Godot MCP tools are available. Vivarium-specific additions:

| Tool | Use |
|------|-----|
| `dotnet_build` | Build the C# project |
| `dotnet_test` | Run core unit tests (all 184) |
| `game_call_csharp` | Call methods on C# objects in the running game |
| `game_save_map` | Save current MapData to a file |
| `game_get_sim_state` | Inspect running simulation node tree |
| `run_project` | Launch the game with `projectPath: "/home/dean/vivarium"` |

## Godot Documentation — Local Knowledge Base

A complete offline knowledge base of Godot 4.7 docs lives at:

```
/home/dean/GodotDocs/
```

**68 files, 1.4MB** covering all major Godot 4 areas. Clean RST source from the
official docs (no sidebar pollution). Each file has a source URL header.

### When to consult it

Before writing or modifying any Godot-related code, search the knowledge base first:

```bash
# Search for a specific topic
grep -r "search term" /home/dean/GodotDocs/

# Find where a concept is documented
grep -rl "topic" /home/dean/GodotDocs/

# Read a specific reference page
cat /home/dean/GodotDocs/scripting/csharp/c_sharp_signals.md
```

### Key pages for this project

| Topic | File |
|-------|------|
| **C# API differences** (PascalCase mapping) | `scripting/csharp/c_sharp_differences.md` (43KB) |
| **C# signals** (`[Signal] delegate`) | `scripting/csharp/c_sharp_signals.md` (11KB) |
| **C# exports** (`[Export]` attribute) | `scripting/csharp/c_sharp_exports.md` (19KB) |
| **C# style guide** | `scripting/csharp/c_sharp_style_guide.md` (10KB) |
| **3D transforms** (quaternions, Basis) | `tutorials/3d/using_transforms.md` (17KB) |
| **Standard Material 3D** (PBR params) | `tutorials/3d/standard_material_3d.md` (35KB) |
| **3D lights & shadows** | `tutorials/3d/3d_lights_and_shadows.md` (31KB) |
| **Global illumination** | `tutorials/3d/global_illumination.md` (19KB) |
| **Procedural geometry** (ArrayMesh) | `tutorials/3d/procedural_geometry.md` (7KB) |
| **Custom 2D drawing** | `tutorials/2d/custom_drawing_2d.md` (35KB) |
| **Scene organization** (best practices) | `manual/best-practices/scene_organization.md` (18KB) |
| **Godot notifications** (_process etc.) | `manual/best-practices/godot_notifications.md` (14KB) |
| **Autoloads vs regular nodes** | `manual/best-practices/autoloads_vs_regular_nodes.md` (5KB) |
| **Editor CLI reference** | `editor/command_line_tutorial.md` (50KB) |
| **Project settings** | `editor/project_settings.md` (5KB) |
| **GDScript reference** (for understanding engine) | `scripting/gdscript/gdscript_basics.md` (115KB) |

### Adding new pages

```bash
BASE="https://raw.githubusercontent.com/godotengine/godot-docs/stable"
curl -sL "$BASE/tutorials/some/path.rst" -o /home/dean/GodotDocs/local_name.md
```

### Key live docs (not yet downloaded)

- **Node class reference**: https://docs.godotengine.org/en/stable/classes/class_node.html
- **Node3D**: https://docs.godotengine.org/en/stable/classes/class_node3d.html
- **CharacterBody2D**: https://docs.godotengine.org/en/stable/classes/class_characterbody2d.html
- **AnimationPlayer**: https://docs.godotengine.org/en/stable/classes/class_animationplayer.html
- **Viewport**: https://docs.godotengine.org/en/stable/classes/class_viewport.html
- **Shading Language**: https://docs.godotengine.org/en/stable/tutorials/shaders/shading_language.html

## Project-specific patterns

### Key types

```
core/
  Simulator.cs        — Tick orchestration (physics/collision) + seeded RNG; delegates
                        perception/flocking/grazing/interaction to the subsystems below
  PerceptionBuilder.cs— Builds each creature's SenseContext (pure fn of self+world)
  FlockManager.cs     — Flock membership reconciliation (form/join/leave/merge)
  GrazingSystem.cs    — Resolves graze-down + Hunger relief (pure fn)
  Vec.cs              — Shared X/Z helpers: HorizDist, generic NearestBy scan
  Arena.cs            — World bounds (X/Y/Z min/max)
  MapData.cs          — Grid-based terrain (128×128, Terrain enum, Cell struct)
  MapGenerator.cs     — Deterministic terrain generation (lakes, rocks)
  MapStorage.cs       — Save/load MapData to file
  Creature.cs         — Base entity (Position, Velocity, Radius, Traits, IMovementMode)
  CreatureTraits.cs   — Mutable config: maxSpeed, jumpHeight, canFly, etc.
  Blob.cs             — First concrete creature type
  WalkMode.cs         — Ground movement (gravity + jump)
  FlyMode.cs          — Free 3D movement
  UtilityBrain.cs     — AI: utility-based action selection (scored considerations)
  BehaviorConfig.cs   — All Utility-AI tunables + action table (data-over-code home)
  IFleeStrategy.cs    — Composable per-creature flee-from-player policy (SheepFleeStrategy)
  SenseContext.cs     — What a creature can perceive (nearby entities, terrain)
  BiomeCatalog.cs     — Biome definitions loaded from assets/biomes.json
  FoodCatalog.cs      — Food definitions loaded from assets/foods.json
  body/CreatureCatalog.cs — Creature defs (body plan + traits/drives/herd) from creatures.json
  body/CreatureDef.cs — Full per-type definition: BodyPlan + optional sim rules
  HerdSpawner.cs      — Spawns herds from a CreatureDef (data-driven)

scripts/
  VivariumMain.cs     — Game entry point: creates Simulator, wires up MapView
  MapView.cs          — Reads MapData, generates 3D mesh via ArrayMesh/SurfaceTool
  BlobVisual.cs       — Renders a Blob entity in 3D
  FoodVisual.cs       — Renders Food items
  CameraOrbit.cs      — 3D orbit camera controller
  PlayerVisual.cs     — Player-controlled entity visual

assets/
  biomes.json         — Biome definitions (height ranges, terrain ratios, colors)
  foods.json          — Food item catalog
  creatures.json      — Creature catalog: body plan + traits/drives/herd config per type
  default_env.tres    — DefaultEnvironment with sky, ambient light
```

### Color/material conventions
- Cozy, pastel-saturated, simple and vibrant art direction
- Colors defined in biomes.json per biome
- Materials use StandardMaterial3D with PBR params
