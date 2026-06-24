# Research: Soft-Body & Procedural Creature Animation

**Status:** Exploratory (not locked) · informs
[../features/art-and-animation.md](../features/art-and-animation.md) ·
**Question:** how to get *soft-body, procedural, but detailed* animals for any spliced
part combination, without hand-authoring per-combo animation.

This is a technique survey + a proposed direction. It does **not** override the locked
pipeline in `art-and-animation.md`; it expands the options behind it and flags one real
architectural decision (how far toward true soft-body physics to go).

---

## The reference

Anchor video: *"Simulating soft body animals"* (the frog). The technique is the
**pressure soft-body model** (Maciej Matyka):

- A **shell of point masses** around the creature outline.
- **Linear springs** (Hooke's law + damping) between neighbors = the membrane.
- One extra force: **internal pressure from the ideal gas law** (P ∝ 1/volume). Squash
  the shape → volume drops → pressure spikes → walls push back out. This is why it bounces
  like a balloon instead of collapsing like cloth.
- **Verlet** integration (position-based, stable, cheap); visible skin drawn over the
  points.

Same family Rain World uses. The gooey, weighty, alive quality = **physics-simulated
dynamics**, which is in tension with our determinism invariant (see Reconciliation).

---

## The landscape — four philosophies

Everything trades off on two axes: **physics-simulated ↔ kinematic** (controllable /
testable) and **squishy ↔ structured/detailed**. The goal is the top-right corner
(organic *and* detailed). No single technique gets there — they layer.

| Approach | What it is | Gives | Costs | Fit |
|---|---|---|---|---|
| **Pressure soft-body** (frog video, Rain World blobs) | Point masses + springs + gas pressure, Verlet | Max squish / weight / life | Non-deterministic; hard to splice parts onto a blob; expensive ×300 | Core body *feel*, not foundation |
| **Verlet chain + IK legs** (t3ssel8r) | Spine of points each following the last at fixed distance, angle-clamped; FABRIK legs that **replant when overstretched** | Organic follow/lag *and* real limbs; scales snake→fish→quadruped | Semi-physical spine | **Strong match** — maps to slots |
| **Kinematic FK/IK gait** (runevision) | Footstep planning, gait phase offsets, ray-cast ground, dynamic foot roll, hips follow feet | Max *detail* & control; fully deterministic | Stiff without a softness layer | **What `art-and-animation.md` locks** |
| **Chunk + spring ragdoll** (Rain World creatures) | A few physics "body chunks" (circle colliders) + spring connections; cosmetic mesh over them | Soft *and* art-directed; AI-state-driven | Physics = non-deterministic | The hybrid blueprint |

The t3ssel8r method is notable for two transferable tricks:
- Each spine point carries a **body radius** → defines the silhouette.
- Eyes / fins / spines are **placed parametrically around the spine** (circle equations),
  so features bank and bend with the body. This is how you get *detail* cheaply on a
  procedural body — and it maps directly onto our **parts-at-slots** model.

---

## Key finding: "soft body + detailed" = a layered rig

Every example that nails both does the same thing — **structure for detail, dynamics for
softness, layered on top of each other:**

- **Detail** comes from *structure*: a skeleton, IK limbs that actually step,
  anatomically-placed features, gene-scaled proportions.
- **Softness** comes from *dynamics* layered on: springs, squash & stretch,
  follow-through, jiggle.

Rain World = structured chunks (control) + spring connections + soft cosmetic mesh.
t3ssel8r = structured spine + IK legs (detail) + the chain's natural lag (softness). The
frog video is pure softness — you'd *add* detail by hanging placed eyes/legs off it.

The answer to "how do I get both" is therefore **not** picking one video. It is: a
**kinematic skeleton + IK as load-bearing structure, with a spring/squash dynamics layer
on top.**

---

## Reconciliation with our architecture

True soft-body physics as the *foundation* would break three locked invariants:

1. **Determinism** ("same inputs ⇒ same outputs"). Engine physics integration +
   collision is timestep/float/order-sensitive, not reproducible across runs/platforms.
2. **Core-is-pure-C# / scripts-are-thin.** Engine soft-body lives in the engine layer; to
   keep it deterministic *and* testable we'd reimplement mass-spring in `core/` (real
   work, but doable).
3. **Splice-ability.** A pressurized blob has no clean slots; a skeleton/chain does, and
   the whole game is parts-at-slots.
4. (Also) at **100–300 creatures**, full per-creature soft-body is the most expensive
   option → forces aggressive LOD.

**Clean reconciliation — separate by purpose:**

- **Gameplay state stays deterministic in `core/`:** position, locomotion intent, AI,
  slot contents. Pure C#, xUnit-tested. The spine + footstep planning is *math here*.
- **Softness is a cosmetic layer in the engine:** spring-bone jiggle, squash & stretch,
  follow-through. It **never feeds back into gameplay**, so it can be non-deterministic
  without violating anything. Creatures look gooey; the sim stays reproducible.

That single distinction buys the squishy aesthetic while keeping every invariant. (If we
later need deterministic softness — replays, netcode — implement Verlet in `core/` with a
fixed timestep.)

---

## Godot 4.7 ships most of this

We are on **Godot 4.7 / C# / Forward+**, so much of what these videos hand-roll is
built-in via the `SkeletonModifier3D` family:

- **`LookAtModifier3D`** (4.4) → "look-at driven by AI focus" gaze, for free.
- **`SpringBoneSimulator3D`** + `BoneConstraint3D` (4.5) → jiggle/spring bones for tails,
  ears, jowls, belly, wings. **This is the lightweight soft-body layer** — and being
  cosmetic, it is determinism-safe.
- **IK modifiers** returned in 4.6 (`IKModifier3D` / chain IK), plus `SkeletonIK3D`
  (FABRIK) → procedural leg stepping.

So the layered rig is mostly *configuring built-in modifiers*, with custom
`SkeletonModifier3D` only where bespoke gait logic is needed — not a large custom build.

Note: Godot's `SoftBody3D` (true mesh soft-body via physics) is cloth-oriented,
non-deterministic, and doesn't map to parts → **not** the route for creatures.

---

## Proposed direction for Vivarium

A three-layer rig, in priority order:

1. **Structure (core, deterministic):** spine chain + IK limbs with footstep planning.
   Slots → bone groups; genes → bone scale (already locked). Pure C#, tested. → *detail.*
2. **Softness (scripts, cosmetic):** `SpringBoneSimulator3D` on secondary parts + code
   squash-and-stretch on the core + chain follow-through. → *soft-body feel*, zero
   determinism cost.
3. **Reach goal — true soft-body core (optional):** a *small* pressurized chunk-and-spring
   lattice (Rain-World-style, ~5–8 chunks) for the torso only, IK limbs hung off it, gated
   to near-LOD. Deterministic → implement Verlet in `core/`; cosmetic-only → let the
   engine do it.

This delivers *soft body, procedural, but detailed* while respecting every locked
invariant. The v0.1 POC's squash/flap/look-at is already step 1 of layer 2 — this is a
deepening, not a rewrite.

---

## Open decision: how soft?

Build in this order; decide the next level only after seeing the previous one on real
spliced creatures:

- **(a) Jiggle + squash only** — spring bones + squash/stretch on the kinematic rig.
  *Recommended first.* Cheapest, determinism-safe, big perceived payoff.
- **(b) + chunk-soft core body** — a few physics/Verlet chunks for the torso so it truly
  squishes; limbs still IK.
- **(c) Full pressure soft-body** — the frog model as the body foundation. Highest cost,
  weakest fit with slots/determinism; reserve for a deliberate stylistic bet.

---

## Sources

- Pressure soft-body model (Matyka): <https://arxiv.org/pdf/physics/0407003> ·
  implementation guide <http://panoramx.ift.uni.wroc.pl/~maq/soft2d/howtosoftbody.pdf>
- t3ssel8r, *A simple procedural animation technique*:
  <https://www.youtube.com/watch?v=qlfh_rv6khY>
- runevision, procedural creature progress 2021–2024:
  <https://blog.runevision.com/2025/01/procedural-creature-progress-2021-2024.html>
- Rain World code structure / PhysicalObject:
  <https://rainworldmodding.miraheze.org/wiki/Rain_World_Code_Structure/PhysicalObject>
- Godot IK returns (4.6): <https://godotengine.org/article/inverse-kinematics-returns-to-godot-4-6/>
- Godot `SpringBoneSimulator3D`:
  <https://docs.godotengine.org/en/stable/classes/class_springbonesimulator3d.html>
