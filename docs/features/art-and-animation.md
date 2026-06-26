# System: Art & Procedural Animation

**Status:** Design locked · supports [splicing.md](splicing.md) (systemic visuals) &
[creatures.md](creatures.md) · **Design:** [../GAME-DESIGN.md](../GAME-DESIGN.md)

How a creature is visually assembled from one part per slot and animated for *any*
combination without hand-authoring per-combo animation. Cozy, pastel, toon-shaded.

> **Deeper dive:** [../research/procedural-animation.md](../research/procedural-animation.md)
> surveys the technique landscape (pressure soft-body, Verlet chains, IK gait, spring
> bones) and proposes the **layered rig** — kinematic skeleton+IK for *detail*, a cosmetic
> spring/squash layer for *soft-body feel* — that this pipeline builds toward. One open
> decision there: how far toward true soft-body physics to go.

## Pipeline

```
Blender: model creature -> split into parts -> rig/skin each part
  to the shared skeleton -> export (glTF)
Runtime: pick one part per slot -> bind to shared Skeleton3D
  -> procedural animation drives it -> unify tint + toon shader
```

## Rig: shared parameterized skeleton + IK

- One **superset `Skeleton3D`** (spine, head, limb chains, tail, wing/appendage bones,
  etc.). Each **slot maps to a bone group**; **genes set bone scale/length** (core size,
  limb proportions).
- Part meshes **skin** to the relevant bones; rigid accessories (horns, stinger) can use
  `BoneAttachment3D`.
- **IK** (`SkeletonIK3D` / FABRIK) drives feet/limb placement so any leg count/length
  walks correctly.

## Procedural animation (all code-driven)

- **IK gait** — procedural stepping for any locomotion part. "Any legs/fins just work."
- **Oscillators** — wing flap, tail sway, gill/fin pulse via sine + phase.
- **Spring / secondary motion** — jiggle & follow-through on soft parts.
- **Squash & stretch** — cozy bounce on moves/jumps.
- **Look-at, driven by AI focus** — the head/eyes track the creature's *current
  Utility-AI target*: player when curious, food when hungry, threat when fearful. **The
  animation layer reads AI state**, so creatures visibly "think."

## Authoring split

- **Start fully procedural from the rest pose** — author only the rest pose; all motion
  is code. Lowest barrier (no keyframing).
- **Optional 1–2 key poses per part later** (e.g. wing up/down) as polish, blended by
  code.
- **Skill note (first-time artist):** the real Blender skill needed is **rigging/skinning
  + weight-painting** parts to the shared skeleton — *not* keyframe animation. Learnable
  and well-documented.
- **Fallback if rigging is a wall:** socket-only **puppet** approach — rigid parts
  snapped to `Marker3D` sockets, animated by moving sockets (no skinning). Chunkier but
  cozy; keeps the project unblocked.

## Visual cohesion (avoid ugly Frankencreatures)

- **Global constrained pastel palette;** color genes pick within it.
- **Per-creature unify pass** — nudge all parts toward one primary/secondary scheme so
  mismatched parts read as one creature.
- **Shared toon/flat shader** unifies lighting and hides seams.

## Performance / LOD

At ~100–300 creatures, full IK on all is costly. **Near the player:** full IK + all
techniques. **Distant:** cheaper animation (oscillators only / reduced rate), matching
the [ecology](ecology.md) near/far LOD split.

## Ragdoll / physics-comedy moments

Beyond the structured procedural walk/look/fly, inject occasional **goofy ragdoll
beats** that give creatures personality and unpredictability:

- **Trip-and-faceplant** — when fleeing in fear at speed, creatures can stumble on
  terrain features (rocks, roots, ledges), triggering a brief ragdoll collapse + recovery.
  Makes a panicked escape feel clumsy and endearing.
- **Slip on slopes / wet ground** — weather + terrain can make surfaces "slick,"
  causing legs to lose traction mid-stride and the creature to slide or wipe out.
- **Startle-flail** — a sudden stimulus (player appears, loud noise) triggers a brief
  flinch / backwards stumble before the AI decides to flee or investigate.
- **Play-dead / dramatic collapse** — some personality templates might overreact to
  minor threats with an exaggerated flop, adding comedic variety.
- **Recovery wiggle** — after a fall, a short scramble-to-feet animation (legs kicking
  in air for upside-down creatures, shake-off jitter) before resuming normal gait.

**Implementation note:** these are triggered sparingly by the AI/behavior layer and
last 0.5–2.0 seconds. They interrupt normal procedural animation, hand control to a
temporary physics-driven ragdoll or blend-tree, then smoothly transition back to IK
gait. Frequency should be low enough to stay novel — a creature that trips every 5
seconds is broken, not charming.

**Tuning knobs:** chance-per-second when fleeing, personality modifier (skittish vs
steady), terrain-trip threshold.

## Cross-system hooks
- **Animation ↔ AI:** gaze/look-at and gait speed read Utility-AI state & velocity.
- **Genes ↔ rig:** PartGene `mesh`/`visualMods` + bone-scale come from the
  [part data model](part-data-model.md); organ parts have no mesh (skip assembly).
- **Lighting ↔ time/weather:** toon shader should respond to day/night & weather
  ([ecology](ecology.md); see lighting/GI work in [archive/](../archive/)).

## Implemented — v1 socket-puppet body plan

The first creature ("Sprout") ships as the **socket-only puppet** fallback (no Blender/glTF
yet), as a creature-first step toward the full rig. The body description is **data in `core/`**
(pure C#, deterministic, geneifiable), the animation is **cosmetic in `scripts/`**:

- `core/body/` — `BodyPlan` = ordered `BodyPart`s (`PartSlot`, `ShapePrimitive`, size, socket,
  tint hex, `AnimRole`, oscillator phase/freq) + `BaseScale` + pastel palette. `CreatureCatalog`
  loads `assets/creatures.json` (mirrors `FoodCatalog`/`BiomeCatalog`). This is the data shape a
  future `Expressor` (genotype→phenotype) will emit — `PartSlot`→`PartGene.slot`,
  shape/size/tint/socket→`mesh`/`visualMods`.
- `Creature.Body` holds the plan; `Creature.FocusPosition` (set by the `Simulator` from senses)
  exposes the AI's current focus target for look-at.
- `scripts/CreatureVisual.cs` assembles a `MeshInstance3D` per part at its socket (shaded
  StandardMaterial3D so parts read as 3D form; unify-lerp toward the primary tint), and drives
  **layer-2(a)** animation: face-heading turn, body squash/stretch + bob, limb/tail oscillators,
  head/eye look-at toward `FocusPosition`. Cosmetic only — never feeds back into the sim.

Next deepening (per [../research/procedural-animation.md](../research/procedural-animation.md)
open decision): keep tuning (a) jiggle+squash, then decide on (b) chunk-soft core / Blender rig.

## Open / for later
- Exact superset skeleton bone layout & slot→bone mapping.
- Retargeting for wildly different proportions (tiny vs huge, many limbs).
- The unify-tint pass implementation (shader vs per-material).
- How key poses (if added) are authored & blended.
