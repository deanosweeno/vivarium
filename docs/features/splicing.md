# System: Genetics, Gene Harvest & Hybrid Splicing

**Status:** Design locked · supported by [art-and-animation.md](art-and-animation.md)
(systemic visuals) · **Design:** [../GAME-DESIGN.md](../GAME-DESIGN.md)

### Implementation progress

Built piece-by-piece in `core/genetics/`. ✅ done · 🚧 partial · ⬜ not started.

- ✅ **§1 Gene model** — `Gene`, `GeneKind`, `Rarity`, `Tier`/`Visible`, `StatPin`, `StatKey` +
  `StatRegistry` (clamp seam onto `CreatureTraits`/`Drives`), `Locus`, `Genome` (one-base +
  budget invariants). `BodyEnvelope` sidecar carries whole-body scale/palette (off `Gene` by design).
- 🚧 **§2 Expressor** — MVP linear path done: `BaseGene.From(CreatureDef)` derivation,
  `Expressor.Express(Genome, Random)` → `Phenotype`; base lay-down, single-pin override,
  first-specialty-replaces-at-locus, clamp, envelope-driven `BodyPlan`. **Deferred:** §4 multi-pin
  conflict (currently last-wins), §5/§6 stack & multiplicity (currently stack), §6 mutation/fusion,
  §2 seeded jitter, and non-scalar traits (CanFly/Diet/PreferredBiomes — see `BaseGene` TODO).
- ✅ **§3 gameplay loop** — `GeneCatalog` (+ `assets/genes.json`, seeded sprout/sheep genes),
  `HarvestTable.Roll` (seeded rarity-weighted drops, missing tiers redistribute weight),
  `GenePool` (collect/de-dupe by id, `HasFullSet`/`Missing` craft gate, JSON save/load),
  `Craft.CraftBase` (gated on the full Common set, still def-derived — see its `// TODO §3`),
  `Splicer.Splice` (named seam over `Genome.Create`), `GeneticsConfig` (drop odds, min/max
  drops, splice budget — all data, not scattered constants). Pure `core/`; the harness Genetics
  panel now spawns a spliced phenotype into the live sim (see §8).
- ⬜ **§4–6** collision resolution · ⬜ **§7** splice budget as player progression ·
  🚧 **§8** spawn integration — `Simulator.SpawnFromPhenotype(pos, Phenotype, Genome?)` drops an
  expressed genome into the live sim as a real `Blob` (auto-rendered by `CreatureVisual`), retained
  on `Creature.Genome`; `Genetics.Similarity` reads `Genome` when both creatures have one (shared
  base species + specialty overlap), else falls back to Body+Drives. Exposed via the devtools
  Genetics panel "Spawn into sim" button. **Pending:** `assets/creatures.json` gene metadata.
- ✅ **Player-facing splice UI** — `scenes/splice_ui.tscn` + `scripts/genetics_ui/` (`GeneSlotView`,
  `GeneTrayView`, `SpliceUi`, `ISpliceHost`): Tab opens a drag-and-drop board (1 base slot + 8
  specialty ring slots, locked past `GeneticsConfig.DefaultSpliceBudget`) fed by two scrollable
  trays, rarity color-coded. Pauses the sim while open (`ISpliceHost.Paused`). Splice button runs
  `Splicer.Splice` → `Expressor.Express` → `Sim.SpawnFromPhenotype`. `SpliceUi` depends only on
  `ISpliceHost` (`PlayerInput`/`Sim`/`Creatures`/`PlayerPosition`/`Paused`), so both the real game
  (`VivariumMain`) and the devtools harness (`HarnessSimHost`, Play mode) instantiate the *same*
  scene on Tab — devtools' old bespoke `SpliceOverlay` (dropdown/checkbox craft-and-save UI) has
  been retired. `core/genetics/GenePoolSeed.FillAll` auto-populates `PlayerInputMode.Pool` with
  every species' base gene + every catalog gene for both hosts (harvest/craft gating and pool
  persistence are still deferred to a separate craft screen — see §3; devtools' isolated Genetics
  mode still exercises the real harvest→craft→splice gate independently).

How the player **harvests genes** from animals and **splices** them into hybrids. A genome is
a composition-first flat list of genes; an `Expressor` turns a genome into the existing
`BodyPlan` / `CreatureTraits` / `Drives` phenotype. Design + architecture spec — implementation
is a later, separate effort.

> **Builds on:** the *proto-genome* that exists today — [`core/Drives.cs`](../../core/Drives.cs)
> (heritable personality), [`core/body/BodyPlan.cs`](../../core/body/BodyPlan.cs) /
> `BodyPart.cs` (parts seeded as the future `PartGene`), and
> [`core/Genetics.cs`](../../core/Genetics.cs) whose `Similarity()` doc already reserves
> itself as *"the one place that learns to read"* the genome when it lands.

---

## Context

Vivarium's core pillar is a creature ecosystem where the player harvests genes and splices
them to craft hybrids. Today only a proto-genome exists (Drives, BodyPlan/BodyPart, the
`Genetics.Similarity` kinship metric, `CreatureTraits`, data-driven `assets/creatures.json`).
There is **no** real genotype, gene pool, harvesting, crafting, splicing, Expressor, or
inheritance yet. This doc specifies that system end-to-end, composition-first, so creatures
are easy to author and modify from a genome perspective.

Architecture constraints (project `CLAUDE.md`): all logic lives in **`core/`** — pure C#, no
Godot, deterministic (seeded `Random`, no wall-clock/GUIDs). The `scripts/` layer is a thin
presentation/intents shell that owns no rules. Tunables live in data/config, not code.

---

## 1. Gene model (the core data type)

A genome is a **flat list of `Gene`** — composition over hierarchy. There is **one unified
`Gene` type** (no `PartGene`/`StatGene` subclasses); a gene carries optional visual and/or
stat payloads.

### `Gene`
| Field | Meaning |
|-------|---------|
| `Id` | Stable identifier, e.g. `sheep.wool`, `wolf.lone`. |
| `Kind` | `Base` or `Specialty`. Exactly one `Base` per genome. |
| `Rarity` | `Common` \| `Rare` \| `Legendary`. Drives drop odds + conflict weighting. |
| `Tier` | Integer rank. Reserved for future rarity/unlock/dominance; today only a conflict tiebreak input. |
| `Visible` | `bool`. True if the gene contributes a visual payload. |
| `Parts` | Optional list of `BodyPart` (reuse [`core/body/BodyPart.cs`](../../core/body/BodyPart.cs)), each at a `Locus`. Present when `Visible`. |
| `Pins` | Optional list of `StatPin` — the invisible payload (hard-set stats / drives). |
| `SourceSpecies` | Which creature this gene came from (attribution). |

A single gene can be **both** visual and invisible (e.g. `sheep.wool` = a Surface part **and**
a Warmth/Sociability pin). `Visible` is just a property — not a separate class.

### `Locus`
Identifies *where on the body* a part sits: `PartSlot` + a discriminator so a slot can hold
multiple distinct part-types (e.g. `Locomotion:legs`, `Head:main`). Drives the replace/stack
rules (§5) and part-multiplicity (§6).

### `StatPin`
| Field | Meaning |
|-------|---------|
| `Key` | A `StatKey` enum value (see below). |
| `Value` | The pinned value. |

### `StatKey` + registry
An enum spanning the tunable fields of `CreatureTraits` (MaxSpeed, JumpHeight, Acceleration,
TurnRate, Radius, GravityScale, MaxFlyHeight, FatigueGainPerSec, FatigueRecoverPerSec,
GrazeHungerThreshold) **and** `Drives` (Curiosity, Fear, Sociability, PlayCuddle, Appetite,
Aggression). A small **registry** maps each `StatKey` to a get/set accessor on a
`CreatureTraits`/`Drives` pair plus a valid `[min,max]` clamp range. This is the single seam
through which a gene touches a stat — keeps `CreatureTraits`/`Drives` unchanged.

### `BaseGene` (a `Gene` with `Kind=Base`)
The player-**crafted** bundle of *all common genes* of one creature: the common body parts
(default core/head/eyes/legs/tail) **and** the common baseline stat pins (the species' stock
MaxSpeed, Sociability, etc.). A base gene alone expresses a complete, plain "stock" animal of
that species. Authored/derived from `creatures.json` (the common parts/stats of each def).

### `Genome`
`Genome = { 1 Base gene } + { specialty Gene[] }`. Invariants:
- Exactly one `Base` gene. (A genome with no base is invalid and the Expressor refuses it.)
- Specialty count bounded by the player's current **splice budget** (§7, progression 2→8).

---

## 2. Expressor — genotype → phenotype

`Expressor.Express(Genome genome, Random rng) → Phenotype { BodyPlan, CreatureTraits, Drives }`

Pure and **deterministic** for a given seed. Pipeline:

1. **Lay down the base.** Instantiate the base gene's parts (by `Locus`) and baseline stat
   pins → seed `CreatureTraits` + `Drives` + the part set.
2. **Resolve visible loci** (§5 replace/stack) and **part multiplicity** (§6) across base +
   specialty genes → final part instances per locus, each tagged with its owning gene.
3. **Aggregate effects** (§6 scaling): group expressed instances by gene; same-gene
   multiplicity scales regressively (`0.75·k`), different genes are additive.
4. **Resolve stat pins** (§4): apply non-conflicting pins; for stats pinned by multiple
   genes, do the rarity-weighted random pick.
5. **Jitter + clamp.** Optional seeded jitter on non-pinned stats; clamp every stat to its
   registry range.
6. **Assemble `BodyPlan`** (palette/scale from base) → ready for existing `CreatureVisual`.

Output feeds the existing spawn/visual path unchanged (`BlobFactory`, `CreatureVisual`). This
is the concrete `Expressor` that `BodyPlan.cs` / [art-and-animation.md](art-and-animation.md)
already name.

---

## 3. Gameplay loop — harvest → pool → craft → splice

### Harvest (drop table)
Player harvests an animal → it **drops 1–3 genes**, each rolled against the species' rarity
table:

- Default odds: **Common 65% · Rare 30% · Legendary 5%**.
- If a species has **no legendary** gene: **Common 70% · Rare 30%**.
- General rule: odds are **normalized over the rarities that species actually owns**, so a
  species missing a tier redistributes that weight.

Each rolled drop yields a specific gene from that species' pool. Seeded RNG → deterministic.

### Gene pool (collection + attribution)
`GenePool` — the player's collected genes, **attributed to source species/creature**.
Serializable to JSON (mirrors `CreatureCatalog` load/save). De-dupes by gene `Id` (duplicate
drops don't stack; future: track counts for trading/sacrifice). Tracks, per species, which of
its genes are still missing.

### Craft (build the base)
Once the player has collected a species' **full gene set (all common + rare + legendary)**,
they can **craft that species' base genome**. Crafting bundles the species' common genes into
a reusable `BaseGene`. Only then can that species be used as a base for splicing.

### Splice (make a hybrid)
`Splice(BaseGene base, IReadOnlyList<Gene> specialty) → Genome`
- Requires exactly **one** base gene.
- Up to **N specialty genes**, where N is the player's splice budget (§7).
- Any kinds, no slot restrictions (collisions handled by §5/§6).
- Validates against the budget and the one-base invariant; otherwise pure.

The hybrid **inherits all stats of the base creature**; spliced genes then override per §4.
(Override chance is **100% for testing**; exposed as a tunable to soften later.)

### Harvest-from-hybrid (recursive breeding)
Once a hybrid exists, the player can **harvest any selected gene from it** — including the new
**fused mega-genes** (§6 Fusion) — as a **non-destructive copy** into the `GenePool` (the
hybrid keeps its genes intact). Progression raises how many genes can be copied per harvest.
This closes a **recursive loop**: fused ability/visual combos become reusable single genes
that can be spliced onto further creatures → recursive mutation breeding + ability combining.

### Gene removal — revert / drop-slot
Editing a creature's genome by **removing a gene**:
- the affected spot **reverts to the base animal's part** for that locus, **if** the base has
  one;
- if the base animal **has no part at that locus** (a locus introduced by splicing/fusion),
  the **slot is removed** entirely.

Pure/deterministic genome edit; re-runs the Expressor.

---

## 4. Stat resolution & conflicts

Per stat `S`:

1. **Baseline** = the base gene's pin for `S` (the "stock species" value).
2. **Single specialty pin** on `S` → **overrides** baseline (100% for testing; tunable).
3. **Multiple specialty pins** on `S` (conflict) → apply all *non-conflicting* effects, then
   for the contested value do a **seeded random pick among the pins, weighted toward the
   rarer gene** (legendary > rare > common weight). One winner takes `S`.
4. **No pin** on `S` → baseline (+ optional seeded jitter), clamped to registry range.

Part-multiplicity effects (§6) feed into the same stat values *before* the pin-conflict step,
where a gene's stat contribution scales with how many times its part is expressed.

---

## 5. Visible locus rules — replace vs stack

For a given visual `Locus`:

- **Base part is the default**, present **only if no specialty gene occupies that locus**.
- **First specialty gene at a locus → replaces** the base part (e.g. rare legs replace the
  base legs; a wolf head replaces the sheep head).
- **Second+ specialty genes at the same locus** are resolved by **§6 multiplicity**, which
  depends on the base instance count `N` of that part:
  - **N = 1 (e.g. head): default = pick ONE** of the co-located genes (rarity-weighted) — one
    *or* the other, **not** both. Multiple heads appear *only* under a §6 mutation.
  - **N > 1 (e.g. 4 legs): the N instances split** across the co-located genes (uniform
    partition, §6). Specialty genes coexist; only the *base* part is displaced.

Emergent multi-part bodies (two heads, etc.) arise **via the §6 mutation path**, where the
co-located genes also **fuse into one gene** (§6 Fusion). A future "merge / pick-one"
flourish for the non-mutated N=1 case is **deferred**.

---

## 6. Part multiplicity & effect scaling

Let `M` = number of spliced genes targeting one part-type, and `N` = the base creature's
instance count for that part (legs N=4, eyes N=2, head N=1).

### Instance count `N'` and assignment
- **Default (no mutation):** `N' = N`. Distribute the `N` instances across the `M` genes by a
  **uniform-random partition** (each instance independently assigned to one of the `M` genes
  with equal probability). Mean ~`N/M` per gene; extremes allowed (all `N` to one gene).
  - `N = 1` collapses to "pick one gene" (weighted toward rarer per §4-style weighting).
- **Mutation (rare, seeded roll):** `N'` grows **past** the base count.
  - For `N = 1`: `N' ∈ [1, M]`; the instances are a random mixture of the `M` genes (repeats
    allowed). Four head genes → 1–4 heads, any blend up to 4× one gene.
  - For `N > 1`: `N'` grows by the mutation roll, then the same uniform-random partition
    distributes `N'` across the `M` genes (a share can be entirely one gene).

Mutation chance and the growth range are **tunables in config** (balancing = data).

### Effect scaling
Group expressed instances by owning gene; let gene `g` appear `k` times:

- `k == 1` → `×1.0`
- `k ≥ 2` → `×(0.75 · k)`  (2→1.5, 3→2.25, 4→3.0) — **regressive** for the *same* gene.

Across **different** genes → **additive** (sum each gene's scaled effect). So: stacking the
same gene = diminishing returns; combining different genes = full additive contribution. The
`0.75` factor is a config tunable.

Visually, `N'` part meshes render, each shaped/tinted per its assigned gene.

### Fusion (mutation-only) — co-located genes bake into one gene
Co-located genes **fuse into a single new gene** in the offspring **only when a mutation
fires**. Plain (non-mutated) same-spot resolution never fuses — N=1 picks one gene, N>1 keeps
its source genes per the split. Two mutation forms:

- **(A) Multiplicity mutation** — `N` grows past base (e.g. head 1→2..M), instances drawn from
  the co-located spliced genes. Those genes **bake into one fused gene** carrying the multiple
  meshes **and** their combined effects (scaled by the rule above: same gene `0.75·k`,
  different genes additive).
- **(B) Ability-merge mutation** — the visual stays a **single** part (one head), but the
  co-located genes' **pins/abilities combine** onto it → **one fused gene**, single mesh,
  merged effects.

In both forms the baby's genome records **one new combined gene**: new `Id`,
`SourceSpecies = <hybrid>`, merged `Parts` + `Pins`. It **counts as one gene** for the splice
budget (§7) and for harvesting (§3) — enabling the recursive loop below.

---

## 7. Splice budget — progression

The number of specialty genes a hybrid can hold is a **progression stat**, starting at **2**
and upgrading up to **8**. Any gene kinds, no per-slot restriction. The budget is player state
(not a creature trait); stored wherever player progression lives and read at splice
validation time.

---

## 8. Integration with existing systems

- **`core/Genetics.cs`** — rewire `Similarity()` to read the new `Genome` when present (its
  doc already reserves this as "the one place that learns to read it"). Body + Drives
  similarity become genome-derived; flock kin-threshold behaviour preserved.
- **Spawn path** (`HerdSpawner`, `ICreatureFactory`/`BlobFactory`, `creatures.json`) — build a
  `Genome` per spawned creature, run the Expressor, feed the resulting
  `BodyPlan`/`Traits`/`Drives` into the existing `Creature` constructor. Each `Creature`
  carries its `Genome` as source of truth (enables harvest).
- **`assets/creatures.json`** — extend each def's parts/stats with gene metadata: which are
  **common** (→ base) vs **specialty**, plus `Rarity`/`Tier`/`Locus`. Possibly a companion
  `assets/genes.json` catalog. Authoring stays data-only.
- **Presentation (`scripts/`)** — thin: a harvest interaction (`IPlayerInteraction`) collects
  drops into the `GenePool`; a splice/craft UI reads the pool and sends intents. No rules in
  the view layer.

---

## 9. Determinism & testing

Everything in `core/` is seeded and pure → unit-testable without Godot:

- Drop-table odds normalize correctly per species; seeded rolls reproducible.
- Expressor: same genome + seed ⇒ identical phenotype.
- Locus replace/stack: base displaced by first specialty; specialties coexist.
- Multiplicity: uniform partition distribution; mutation growth bounds (`N=1→[1,M]`);
  effect scaling `0.75·k` same-gene, additive across genes.
- Stat conflicts: single pin overrides; multi-pin rarity-weighted pick is seeded.
- Genome invariant: exactly one base; splice budget enforced.
- `Genetics.Similarity` continues to satisfy existing herd-threshold tests.

---

## 9.5 Interactive harness — Play mode

The loop is playable end-to-end in the dev harness (`devtools/harness/play/`), not just via
buttons: a WASD-controlled avatar with a real `harvest` verb and a pull-up splice UI.

- **`harvest` verb** (`core/player/interactions/HarvestInteraction.cs`) — a real
  `IPlayerInteraction`, same shape as `feed`/`soothe`/`play`. Targets the nearest creature in
  reach; species is read from the target's `Body.Id` (falls back to `Genome.Base.SourceSpecies`
  for already-spliced hybrids), then rolls `HarvestTable.Roll` and deposits drops into the
  player's `PlayerInputMode.Pool`. Non-lethal — sets a mild `Startled` tell, not a bond hit;
  sampling isn't play.
- **`PlayModePanel`** — camera-relative WASD (mirrors `VivariumMain.UpdatePlayerInput`), verb
  keys (F/G/1/2/3/H), **Tab** pulls up the real, in-game `SpliceUi` (see §9 above) reading/writing
  the *player's* `GenePool` (not a mode-local one) — the same scene the shipped game uses, not a
  devtools-only stand-in.

---

## 10. Deferred (doors left open)

- **Mutation of stat values** on inheritance (beyond part-count mutation).
- **Tier gameplay meaning** (rarity/unlock/dominance) — today only a tiebreak input.
- **Override chance < 100%** (testing value is 100%).
- **Same-locus specialty "merge / pick-one"** flourish.
- **Part socket de-overlap** for stacked parts (cosmetic; may visually overlap initially).
- **Duplicate-gene counts** in the pool (trading/sacrifice economy).

---

## 11. Proposed new/changed files (for the eventual build)

- New `core/genetics/`: `Gene.cs`, `Genome.cs`, `StatPin.cs`, `StatKey.cs` (+ registry),
  `Locus.cs`, `Expressor.cs`, `GenePool.cs`, `GeneCatalog.cs`, harvest/craft/splice fns.
- Changed: `core/Genetics.cs` (read genome), spawn path, `assets/creatures.json`
  (+ optional `assets/genes.json`).
- Update the docs hub + `.claude/CLAUDE.md` key-types table to point at this system.
