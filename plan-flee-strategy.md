# Implementation Plan: Composable flee-from-player strategy

**Status: ✅ IMPLEMENTED — 263 tests pass, 0 failures.**

Replace the current non-latched FleePlayer action with a latched panic-flee system
where all flee behavior is data-driven via an `IFleeStrategy` interface, injectable
per-creature. Isolated sheep gallop toward nearest flock (or away from player if none);
the flock itself flees as a group when any member detects a threat. Both suppressed
when the player holds food.

## 1. Create IFleeStrategy interface
- **Files:** `core/IFleeStrategy.cs` (new)
- **What:** Define the composable per-creature flee contract:
  ```csharp
  public interface IFleeStrategy
  {
      bool IsPlayerThreat(bool holdingFood, float affection);
      float FleeSpeedMultiplier { get; }          // 1.0 = full maxSpeed
      float SafeDistance { get; }                  // latch releases when player > this
      Vector3? GetFleeTarget(Vector3 self, Vector3 player, Vector3? nearestFlock);
      bool FlockFleesAsGroup { get; }
      float FlockFleeSpeedMultiplier { get; }     // anchor speed multiplier
  }
  ```
- **Why:** Single injection point; all flee tunables live here. Later creatures swap
  their own implementation without touching the brain or flock code.
- **Depends on:** none

## 2. Create SheepFleeStrategy (default implementation)
- **Files:** `core/SheepFleeStrategy.cs` (new)
- **What:** Default sheep flee:
  - `IsPlayerThreat` → `!holdingFood` (player is a threat unless offering food)
  - `FleeSpeedMultiplier` → `1.0f` (full gallop)
  - `SafeDistance` → `5f` (sense radius; can tune later)
  - `GetFleeTarget` → returns `nearestFlock` if non-null, else `null` (flee away)
  - `FlockFleesAsGroup` → `true`
  - `FlockFleeSpeedMultiplier` → `2.0f` (anchor moves at 2× FlockPace)
- **Why:** Concrete data for the sheep creature type. Serves as the template for
  future creature flee strategies (skittish deer, indifferent sloth, etc.).
- **Depends on:** 1 (IFleeStrategy)

## 3. Add PlayerThreat to InputKind and SenseContext
- **Files:** `core/Consideration.cs` (modify), `core/SenseContext.cs` (modify)
- **What:**
  - Add `PlayerThreat` to `InputKind` enum (alongside existing `PlayerProximity` etc.)
  - Add `IsPlayerThreat` field to `SenseContext` struct: `bool IsPlayerThreat`
  - Wire `InputKind.PlayerThreat` → `ctx.IsPlayerThreat ? 1f : 0f` in `Consideration.ReadInput`
- **Why:** The new FleePlayer action needs a consideration that gates on whether the
  player is actually threatening (not just nearby). Existing `PlayerProximity` +
  `PlayerHoldingFood` + `Affection` do this today as three separate considerations;
  collapsing them into one `PlayerThreat` × `Fear` × `Proximity` is cleaner and lets
  the strategy own the threat decision entirely.
- **Depends on:** 1 (IFleeStrategy exists so we know the field name)

## 4. Compute IsPlayerThreat in Simulator.BuildSenses
- **Files:** `core/Simulator.cs` (modify)
- **What:** In `BuildSenses`, after the existing player-awareness block (~line 480),
  add:
  ```csharp
  bool isPlayerThreat = hasPlayer
      && _fleeStrategy.IsPlayerThreat(playerHoldingFood, self.Needs.Affection);
  ```
  Set it on the `SenseContext` return value. Also store `LastSenses` on the creature
  for the flock loop to read later (add `internal SenseContext LastSenses` field to
  `Creature`).
- **Why:** Centralizes threat computation. The flock loop reuses this cached value
  rather than recomputing player position/distance checks.
- **Depends on:** 3 (IsPlayerThreat field exists on SenseContext)

## 5. Replace FleePlayer action in BehaviorConfig.DefaultActions
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Replace the current FleePlayer entry with:
  ```csharp
  new BehaviorAction
  {
      Name = "FleePlayer",
      Steering = SteeringKind.AvoidPlayer,
      BaseWeight = 1f,
      EmergencyCapable = true,
      EmergencyThreshold = 0.6f,
      Considerations =
      [
          // Threat gate: 1 if player is a threat, 0 otherwise → kills action when safe
          new Consideration { Input = InputKind.PlayerThreat, Drive = DriveKind.Fear },
          // Proximity: closer player = more urgency
          new Consideration { Input = InputKind.PlayerProximity, Drive = DriveKind.Fear,
              Curve = new ResponseCurve { Type = CurveType.Power, Exponent = 0.6f } },
          // Isolation gate: ×(1−HerdPresence) → kills action when in a flock,
          // because flock-level flee handles that case
          new Consideration { Input = InputKind.HerdPresence, InvertDrive = true,
              Curve = new ResponseCurve { Type = CurveType.Linear } },
      ],
  },
  ```
  Also remove `PlayerAvoidSpeedFrac` from the config class (dead field — speed is
  now strategy-driven).
- **Why:** Emergency + latched so a panicked creature isn't yanked back to Flock
  mid-flight. Threat gate collapses three old considerations into one strategy-owned
  decision. Isolation gate prevents doubled flee (individual + flock).
- **Depends on:** 3 (InputKind.PlayerThreat exists)

## 6. Inject IFleeStrategy into UtilityBrain
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:**
  - Add `private readonly IFleeStrategy _fleeStrategy;` field
  - Modify constructor: `public UtilityBrain(BehaviorConfig config, IFleeStrategy fleeStrategy)`
  - In `UtilityBrain` → internal setter or pass on construction so tests can inject
- **Why:** The brain needs the strategy for two things: (a) the flee latch uses
  `IsPlayerThreat` to decide hold duration, (b) the `AvoidPlayer` steering case reads
  `GetFleeTarget` and `FleeSpeedMultiplier`.
- **Depends on:** 1 (IFleeStrategy)

## 7. Add flee latch to UtilityBrain.Decide()
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `Decide()`, add a new latch condition alongside the existing
  Forage/Frolic/Rest latches:
  ```csharp
  // FleePlayer latch: hold while player is still a threat and creature is isolated.
  // Releases when player leaves SafeDistance OR creature rejoins a flock. The
  // Isolation gate (step 5) already scores this action 0 when HasFlock, so the
  // latch also releases automatically once a flock is joined.
  else if (Current.Steering == SteeringKind.AvoidPlayer
      && senses.IsPlayerThreat && senses.HasPlayer && !senses.HasFlock)
      hold = 1f;
  ```
- **Why:** Prevents dithering — a panicked sheep stays panicked until it reaches
  safety, rather than flipping back to Flock mid-flight because FearProximity drops
  slightly.
- **Depends on:** 6 (brain has strategy reference), 3 (IsPlayerThreat on SenseContext)

## 8. Modify AvoidPlayer steering to use strategy
- **Files:** `core/UtilityBrain.cs` (modify)
- **What:** In `ComputeSteering`, replace the `SteeringKind.AvoidPlayer` case:
  ```csharp
  case SteeringKind.AvoidPlayer:
  {
      if (!senses.HasPlayer)
          return Wander(delta, maxSpeed, rng);
      float speed = maxSpeed * _fleeStrategy.FleeSpeedMultiplier;
      var target = _fleeStrategy.GetFleeTarget(
          self.Position, senses.PlayerPosition,
          senses.HasNearbyFlock ? senses.NearestFlockAnchor : null);
      return target is Vector3 tgt
          ? Steering.Seek(self.Position, tgt, speed)        // flee toward flock
          : Steering.Flee(self.Position, senses.PlayerPosition, speed); // flee away
  }
  ```
- **Why:** Isolated sheep flees toward the nearest flock when one is in range,
  otherwise flees directly away. Speed comes from the strategy (data-driven).
  The old amble (0.5×) is gone.
- **Depends on:** 6 (brain has strategy reference)

## 9. Add LastSenses to Creature for flock-loop reuse
- **Files:** `core/Creature.cs` (modify)
- **What:** Add `internal SenseContext LastSenses;` field. Set it in the Simulator's
  main creature loop right after `BuildSenses` returns.
- **Why:** The flock-advancement loop runs after all creatures have been sensored.
  Instead of recomputing player-threat per member in the flock loop, we read the
  cached value. Internal-only — no public API impact.
- **Depends on:** 4 (BuildSenses computes IsPlayerThreat)

## 10. Add FlockAction.FleePlayer and flock-level flee
- **Files:** `core/Flock.cs` (modify), `core/IFlockEnv` (modify)
- **What:**
  - Add `FleePlayer` to `FlockAction` enum
  - Add `(Vector3 playerPos, bool hasPlayer) GetPlayerInfo()` method to `IFlockEnv`
  - Modify `Flock.AdvanceAnchor` signature to accept `IFleeStrategy`:
    ```csharp
    public void AdvanceAnchor(double delta, Arena arena, Random rng, IFlockEnv env,
        BehaviorConfig cfg, IFleeStrategy strategy)
    ```
  - Add parameter `bool fleePlayer, Vector3 playerPos` (or compute inside):
  - At the top of `AdvanceAnchor`, if `fleePlayer`:
    - Set `Current = FlockAction.FleePlayer`
    - Steer anchor: `Steering.Flee(Anchor, playerPos, cfg.FlockPace * strategy.FlockFleeSpeedMultiplier)`
    - Clamp to arena, rest on ground
    - Return early (Wander/Graze decision is skipped while fleeing)
  - When NOT fleeing, existing Wander/Graze logic runs unchanged
- **Why:** The flock reacts as one creature. Anchor steers away from the player at
  an increased pace; members cohere to the fleeing anchor via their normal Flock
  steering — no individual override needed. The strategy owns the speed multiplier
  so different creature types can have different flock-flee intensity.
- **Depends on:** 1 (IFleeStrategy), 9 (LastSenses for threat detection)

## 11. Wire flock flee in Simulator.AdvanceFlocks
- **Files:** `core/Simulator.cs` (modify)
- **What:** Modify the flock-advancement loop (~line 308-310):
  ```csharp
  foreach (var flock in Flocks)
  {
      // Does any member of this flock see the player as a threat?
      bool flockFlee = false;
      Vector3 playerPos = Vector3.Zero;
      if (_fleeStrategy.FlockFleesAsGroup)
      {
          foreach (var m in flock.Members)
          {
              if (m.LastSenses.IsPlayerThreat && m.LastSenses.HasPlayer)
              {
                  flockFlee = true;
                  playerPos = m.LastSenses.PlayerPosition;
                  break;
              }
          }
      }
      flock.AdvanceAnchor(delta, Arena, Rng, this, Behavior, _fleeStrategy, flockFlee, playerPos);
  }
  ```
- **Why:** The flock-level flee trigger: any member sensing a threat puts the whole
  flock into flee mode. Strategy check (`FlockFleesAsGroup`) means future solitary
  creature types can opt out of group flee.
- **Depends on:** 10 (Flock.AdvanceAnchor takes flee params), 9 (LastSenses cached)

## 12. Implement IFlockEnv.GetPlayerInfo in Simulator
- **Files:** `core/Simulator.cs` (modify)
- **What:** Add explicit interface implementation for the new `IFlockEnv` method:
  ```csharp
  (Vector3 playerPos, bool hasPlayer) IFlockEnv.GetPlayerInfo()
      => Player is not null ? (Player.Position, true) : (Vector3.Zero, false);
  ```
- **Why:** IFlockEnv is the read-only world-access seam for flocks. Adding player
  info here keeps the Flock class decoupled from the Simulator's Player field.
- **Depends on:** 10 (IFlockEnv method added)

## 13. Update IFlockEnv.NearestFood signature if needed
- **Files:** `core/IFlockEnv` (modify), `core/Simulator.cs` (modify)
- **What:** Verify the existing IFlockEnv implementation still compiles after adding
  `GetPlayerInfo`. Update `GetPlayerInfo` return type if the tuple syntax conflicts
  with older C# version requirements (use explicit `ValueTuple` or a struct).
- **Why:** Flock.cs calls `env.NearestFood(…)` and now `env.GetPlayerInfo()`. Both
  must be implemented by Simulator.
- **Depends on:** 12

## 14. Create FleeStrategy in VivariumMain and inject
- **Files:** `scripts/VivariumMain.cs` (modify)
- **What:**
  - Create `_fleeStrategy = new SheepFleeStrategy();` in initialization
  - Pass it when constructing `UtilityBrain` instances (via `BaseCreatureFactory` or
    wherever brains are created)
  - Pass it to `Simulator` (new field `_fleeStrategy` on Simulator, set via
    constructor or property)
- **Why:** Wires the concrete strategy into the game. The Godot scripts layer owns
  creature-type selection; this is where sheep get their flee behavior.
- **Depends on:** 2 (SheepFleeStrategy exists), 6 (brain takes strategy), 11 (simulator
  uses strategy in flock loop)

## 15. Update Simulator constructor/pipeline to accept IFleeStrategy
- **Files:** `core/Simulator.cs` (modify)
- **What:**
  - Add `private readonly IFleeStrategy _fleeStrategy;` field
  - Accept it in the constructor (or a property set post-construction)
  - Use it in `BuildSenses` (step 4) and the flock loop (step 11)
  - When a brain is assigned to a creature via the factory, inject the strategy
- **Why:** The Simulator is the orchestrator that calls BuildSenses and
  AdvanceFlocks. It needs the strategy to compute IsPlayerThreat and to pass
  to flocks.
- **Depends on:** 4, 11

## 16. Update BaseCreatureFactory to inject IFleeStrategy into brains
- **Files:** `core/BaseCreatureFactory.cs` (modify)
- **What:** Accept `IFleeStrategy` in the factory constructor. When creating a
  `UtilityBrain`, pass the strategy: `new UtilityBrain(_behaviorConfig, _fleeStrategy)`.
- **Why:** The factory creates creatures and their brains. This is the injection
  point — all creatures from this factory share the same strategy (sheep for the
  sheep factory, deer for a future deer factory).
- **Depends on:** 6 (brain constructor changed), 15 (Simulator holds strategy)

## 17. Remove PlayerAvoidSpeedFrac from BehaviorConfig
- **Files:** `core/BehaviorConfig.cs` (modify)
- **What:** Delete the `PlayerAvoidSpeedFrac` property and its default value (0.5f).
  Verify no other code references it.
- **Why:** Speed is now owned by `IFleeStrategy.FleeSpeedMultiplier`. The old config
  field is dead.
- **Depends on:** 8 (AvoidPlayer steering no longer reads PlayerAvoidSpeedFrac)

## 18. Update existing PlayerInteractionTests for new flee semantics
- **Files:** `core/PlayerInteractionTests.cs` (modify)
- **What:**
  - `StrangerSheep_PicksFleePlayer_NearPlayer` — update to verify the new latched
    behavior (sheep stays in FleePlayer across multiple ticks until player moves
    beyond SafeDistance)
  - `BondedSheep_DoesNotFleePlayer` — still valid (high Affection → IsPlayerThreat
    returns false). Ensure test still passes.
  - `PlayerHoldingFood_FlipsSheepToFollow_RegardlessOfBond` — still valid (holding
    food → IsPlayerThreat returns false). Ensure test still passes.
  - Add `IsPlayerThreat` to sense context in tests that build senses manually, or
    use the strategy to compute it.
- **Why:** Existing tests must reflect the new latched, strategy-driven behavior.
- **Depends on:** 2, 3, 4, 7

## 19. New tests: FleeStrategyTests (isolated + flock flee)
- **Files:** `core/FleeStrategyTests.cs` (new)
- **What:** Comprehensive tests for the new flee behavior:
  - **Isolated sheep flees toward flock:** spawn sheep, player, and a distant kin
    flock; sheep picks FleePlayer; verify desired velocity points toward flock anchor,
    not away from player.
  - **Isolated sheep flees away when no flock:** no flocks in world; verify desired
    velocity points directly away from player.
  - **Flock flee triggered by member:** spawn a flock, place player near one member;
    after tick, verify flock anchor moves away from player and `Flock.Current` is
    `FleePlayer`.
  - **Flee latch:** sheep in FleePlayer latches through multiple ticks while player
    is within SafeDistance; releases when player moves beyond SafeDistance.
  - **Flee suppressed when player holds food:** player has CarriedFood; verify
    IsPlayerThreat is false, FleePlayer scores zero, sheep does not flee.
  - **Speed respect:** verify desired velocity magnitude matches
    `maxSpeed * FleeSpeedMultiplier`.
  - **Flock flee speed:** verify anchor velocity magnitude matches
    `FlockPace * FlockFleeSpeedMultiplier`.
  - **Bonded sheep doesn't flee but flock might:** a bonded sheep in a flock with
    a timid unbonded neighbor — the flock flees from the unbonded member's threat
    detection; the bonded sheep follows the fleeing anchor.
- **Why:** The new flee mechanics are non-trivial and need dedicated coverage.
  Currently player-flee tests are mixed into PlayerInteractionTests; a dedicated
  file keeps concerns separated.
- **Depends on:** 2, 7, 8, 10, 11

## 20. Verify full gate: build + test + run
- **Files:** none
- **What:** Run `dotnet build`, `dotnet test`, launch the project, verify no errors
  in game logs, take screenshots of sheep fleeing from the player.
- **Why:** Standard verification gate per project rules.
- **Depends on:** 1–19
