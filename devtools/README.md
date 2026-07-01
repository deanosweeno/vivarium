# devtools/ — interactive dev/QA harness

An in-Godot manual test sandbox. **Not** automated tests (those live in `core/` as xUnit) and
**not** part of the shipped game — a scene you open to spawn, watch, tune, and poke each core
mechanic live.

## Run it

Open `devtools/harness/DevHarness.tscn` in the editor and press **Play Scene** (F6), or launch:

```bash
godot --path /home/dean/vivarium devtools/harness/DevHarness.tscn
```

## What's in it

One harness scene, one shared live `Simulator`, a mode dropdown, and an always-on control strip
(**Pause / Step / TimeScale / Reset world**). Left-click a creature to select it (Tab cycles).

**Camera:** `Q`/`E` rotate, middle-mouse-drag orbits, wheel zooms. Q/E are polled, so they work
even while a slider or spinbox holds keyboard focus. The view eases to follow the selected
creature (world center when nothing is selected).

| Mode | Do |
|------|-----|
| **Creature AI** | Spawn by species / herd, force any `UtilityBrain` action on the selection, live-tune the **global** `BehaviorConfig`, and edit the **selected creature's own** Needs/Traits (they reload per selection and persist on that creature) — plus a CanFly toggle and Randomize-needs. Inspect action + drive-need bars. |
| **Player** | Spawn the avatar, warp it to the selection, Feed/Soothe/Play through the real intent path, tune `InteractionConfig`. |
| **Map / Terrain** | Regenerate terrain from a seed + `MapGenConfig` sliders (rebuilds the mesh), tally terrain/biome cells. |
| **Genetics** | Harvest → pool → craft base → splice (`§3`), phenotype readout. Runs isolated: splicing does **not** spawn into the live sim (that's the open `§8` integration — see `docs/features/splicing.md`). |
| **Play** | Actually play it: WASD-controlled avatar (camera-relative, camera follows the player), verb keys **F** pickup · **G** place · **1** feed · **2** soothe · **3** play · **H** harvest, **Tab** pulls up the splice overlay. Harvest deposits real gene drops into the player's `GenePool`; the overlay crafts/splices against that same pool, pauses the sim while open, and can spawn the spliced hybrid live next to the player. Save/Load persist the pool to `user://genepool.json`. |

## Keeping it out of the shipped game

The harness compiles into the main assembly (it needs live Godot APIs), but is reachable **only**
by opening `DevHarness.tscn` directly — it is never the project's `run/main_scene` and is not in any
autoload. **Any future `export_presets.cfg` must exclude `devtools/*`** so it never ships.

## Core touch-points

The only non-presentation code this feature adds to `core/` is `UtilityBrain.ForceAction(name)`
(the force-action seam, covered by a test in `core/BehaviorTests.cs`). Everything else consumes
existing public seams. `scripts/MapView.cs` gained a public `Rebuild(MapData)` (pure refactor).
