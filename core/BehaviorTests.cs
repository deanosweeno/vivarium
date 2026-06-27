using System.Numerics;
using Vivarium.Core;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the Pillar-1 Utility AI: response curves, considerations, action scoring,
/// drive-driven selection, anti-dithering stickiness, determinism, and need dynamics.
/// All headless — crafted contexts, no Godot.
/// </summary>
public class BehaviorTests
{
    // ---------- response curves ----------

    [Fact]
    public void Linear_IsIdentityByDefault()
        => Assert.Equal(0.3f, new ResponseCurve { Type = CurveType.Linear }.Evaluate(0.3f), 5);

    [Fact]
    public void Inverse_FlipsInput()
        => Assert.Equal(0.8f, new ResponseCurve { Type = CurveType.Inverse }.Evaluate(0.2f), 5);

    [Fact]
    public void Power_SquaresInput()
        => Assert.Equal(0.25f, new ResponseCurve { Type = CurveType.Power, Exponent = 2f }.Evaluate(0.5f), 5);

    [Fact]
    public void Power_OffsetAddsFloor()
        => Assert.Equal(0.35f, new ResponseCurve { Type = CurveType.Power, Exponent = 2f, Offset = 0.1f }.Evaluate(0.5f), 5);

    [Fact]
    public void Logistic_IsHalfAtMidpoint()
        => Assert.Equal(0.5f, new ResponseCurve { Type = CurveType.Logistic, Midpoint = 0.5f }.Evaluate(0.5f), 5);

    [Fact]
    public void Curve_ClampsToUnitRange()
    {
        var steep = new ResponseCurve { Type = CurveType.Linear, Slope = 10f };
        Assert.Equal(1f, steep.Evaluate(0.5f), 5);
        Assert.Equal(0f, new ResponseCurve { Type = CurveType.Linear, Slope = 1f, Offset = -2f }.Evaluate(0.1f), 5);
    }

    // ---------- considerations ----------

    [Fact]
    public void Consideration_ReadsInputThroughCurve()
    {
        var c = new Consideration { Input = InputKind.Fatigue, Curve = ResponseCurve.Identity };
        var ctx = new SenseContext { Fatigue = 0.7f };
        Assert.Equal(0.7f, c.Evaluate(ctx, Drives.Default), 5);
    }

    [Fact]
    public void Consideration_InvertedDrive_UsesOneMinusDrive()
    {
        var c = new Consideration { Input = InputKind.Constant, Drive = DriveKind.Fear, InvertDrive = true };
        var drives = new Drives { Fear = 0.3f };
        Assert.Equal(0.7f, c.Evaluate(default, drives), 5);
    }

    [Fact]
    public void Action_ScoreIsMultiplicative_AndZeroConsiderationKillsIt()
    {
        var half = new Consideration { Input = InputKind.Fatigue, Curve = ResponseCurve.Identity };
        var action = new BehaviorAction
        {
            Name = "T", BaseWeight = 1f, Considerations = [half, half],
        };
        Assert.Equal(0.25f, action.Score(new SenseContext { Fatigue = 0.5f }, Drives.Default), 5);
        Assert.Equal(0f, action.Score(new SenseContext { Fatigue = 0f }, Drives.Default), 5);
    }

    // ---------- drive-driven selection (default action table) ----------

    private static Creature MakeCreature(Drives drives)
        => new(Vector3.Zero, new CreatureTraits { MaxSpeed = 1f }, new SteeringLocomotion(), drives);

    [Fact]
    public void FearfulCreature_WithCloseNeighbor_Flees()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Fear = 1f, Curiosity = 0f });
        var senses = new SenseContext
        {
            HasNeighbor = true, NeighborPosition = new Vector3(1, 0, 0), NeighborProximity = 0.9f,
        };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Flee", brain.CurrentName);
    }

    [Fact]
    public void SociableUnafraidCreature_WithNeighbor_Approaches()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Fear = 0f, Curiosity = 0f });
        var senses = new SenseContext
        {
            HasNeighbor = true, NeighborPosition = new Vector3(2, 0, 0), NeighborProximity = 0.5f,
        };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Approach", brain.CurrentName);
    }

    [Fact]
    public void TiredCreature_Rests()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Curiosity = 1f });
        brain.Tick(0.1, self, new SenseContext { Fatigue = 1f }, new Random(1));
        Assert.Equal("Rest", brain.CurrentName);
        Assert.Equal(Vector3.Zero, self.DesiredVelocity);
    }

    [Fact]
    public void ApproachingCreature_AtStandoffEquilibrium_StillDrifts_NotFrozen()
    {
        // Frozen-herd regression guard: a sheep settled exactly at the Standoff distance has a
        // zero settle vector and no crowders, so without the idle-drift floor it would freeze.
        // The drift must keep it gently milling. Radius 0.5 ⇒ standoff = Radius×PersonalSpaceRadii(4) = 2.
        var self = new Creature(Vector3.Zero, new CreatureTraits { MaxSpeed = 1f, Radius = 0.5f },
            new SteeringLocomotion(), new Drives { Sociability = 1f, Fear = 0f, Curiosity = 0f });
        var brain = new UtilityBrain(new BehaviorConfig());
        var senses = new SenseContext
        {
            HasNeighbor = true, NeighborPosition = new Vector3(2, 0, 0), NeighborProximity = 0.5f,
        };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Approach", brain.CurrentName);
        Assert.True(self.DesiredVelocity.LengthSquared() > 1e-4f, "settled approacher should drift, not freeze");
    }

    [Fact]
    public void IdleCreature_WithNothingAround_Wanders()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Curiosity = 1f });
        brain.Tick(0.1, self, new SenseContext(), new Random(1));
        Assert.Equal("Wander", brain.CurrentName);
        Assert.NotEqual(Vector3.Zero, self.DesiredVelocity);
    }

    [Fact]
    public void SociableCreature_WithHerdInRange_Flocks()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        // Sociable but not curious/hungry, with a herd present but no single neighbor crowding it.
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 0f, Fear = 0f, Appetite = 0f });
        var senses = new SenseContext { HasFlock = true, FlockAnchor = new Vector3(3, 0, 0) };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Flock", brain.CurrentName);
    }

    [Fact]
    public void LoneCreature_DoesNotFlock()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        // Same sociable temperament, but curious and alone → no flock to cohere to.
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 1f });
        brain.Tick(0.1, self, new SenseContext { HasFlock = false }, new Random(1));
        Assert.NotEqual("Flock", brain.CurrentName);
    }

    [Fact]
    public void BoredCreature_RousesToFrolic_BreakingAFrozenEquilibrium()
    {
        // A sociable creature parked with its herd would otherwise sit in the Flock equilibrium
        // forever. Once Boredom maxes out, the high-midpoint Frolic gate spikes past Flock and
        // rouses it to play — the anti-freeze loop. (Frolic then relieves Boredom, so it resettles.)
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 1f, Fear = 0f, Appetite = 0f });
        var senses = new SenseContext { HasFlock = true, FlockAnchor = new Vector3(3, 0, 0), Boredom = 1f };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Frolic", brain.CurrentName);
    }

    // ---------- frolic (boredom play) ----------

    [Fact]
    public void HighBoredom_BeatsFlockHold_AndFrolics()
    {
        // The boredom-play gate (logistic midpoint 0.7) stays near-zero until genuinely bored,
        // then out-scores the 0.7 Flock hold so a settled-but-bored member breaks into play.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 0.5f, Fear = 0f, Appetite = 0f });
        var senses = new SenseContext { HasFlock = true, FlockAnchor = new Vector3(3, 0, 0), Boredom = 1f };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Frolic", brain.CurrentName);
    }

    [Fact]
    public void LowBoredom_DoesNotFrolic()
    {
        // Below the high midpoint the gate is ~0, so a content member keeps flocking — play only
        // arrives in bursts once boredom builds, never as a constant buzz.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 1f, Fear = 0f, Appetite = 0f });
        var senses = new SenseContext { HasFlock = true, FlockAnchor = new Vector3(3, 0, 0), Boredom = 0.3f };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.NotEqual("Frolic", brain.CurrentName);
    }

    [Fact]
    public void Frolic_WithNearNeighbor_PlayChasesTowardIt()
    {
        // Flavor 1: a neighbor within play range pulls the frolic steering toward it (play-chase),
        // standing off at one body-diameter rather than colliding. Neighbor on +X ⇒ net steer +X.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Curiosity = 1f, Sociability = 1f, Fear = 0f, Appetite = 0f });
        var senses = new SenseContext
        {
            Boredom = 1f,
            HasNeighbor = true, NeighborPosition = new Vector3(3, 0, 0), NeighborProximity = 0.5f,
        };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Frolic", brain.CurrentName);
        Assert.True(self.DesiredVelocity.X > 0f, "play-chase should steer toward the neighbor");
    }

    [Fact]
    public void Frolic_Alone_ProducesDartyMotion_NotFrozen()
    {
        // Flavor 3: solo zoomies — no neighbor, no flock ⇒ a non-zero darty steer at near-max speed.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Curiosity = 1f, Sociability = 0f, Fear = 0f, Appetite = 0f });
        brain.Tick(0.1, self, new SenseContext { Boredom = 1f }, new Random(1));
        Assert.Equal("Frolic", brain.CurrentName);
        Assert.True(self.DesiredVelocity.LengthSquared() > 1e-4f, "solo frolic should dart, not freeze");
    }

    [Fact]
    public void SettledHerd_KeepsDrifting_InsteadOfFreezing()
    {
        // A sheep centered on its herd centroid with no crowders sees cohere==0 and separate==0:
        // the old Flock blend was a dead zero-velocity freeze until Boredom slowly rescued it. The
        // FlockWanderFloor drift keeps it milling. Still picks Flock (drift doesn't change selection).
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 1f, Fear = 0f, Appetite = 0f });
        // Anchor on the creature (origin) ⇒ cohesion zero; no SeparationPush ⇒ separation zero.
        var senses = new SenseContext { HasFlock = true, FlockAnchor = Vector3.Zero, Boredom = 0f };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Flock", brain.CurrentName);
        Assert.True(self.DesiredVelocity.LengthSquared() > 1e-4f, "settled herd should still drift, not freeze");
    }

    [Fact]
    public void Flock_CapsSeparation_SoCohesionStillWins()
    {
        // The clamped flock separation (≤0.5×maxSpeed) can no longer overpower cohesion. With the
        // centroid far on +X (cohesion full-speed +X) and a huge raw SeparationPush on −X, the old
        // uncapped blend flung the sheep away (−X) and exploded the herd. Capped, cohesion wins: net
        // velocity still points toward the herd (+X), and never exceeds max speed.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Curiosity = 0f, Fear = 0f, Appetite = 0f });
        var senses = new SenseContext
        {
            HasFlock = true,
            FlockAnchor = new Vector3(10, 0, 0),        // far +X → cohesion at full speed toward +X
            SeparationPush = new Vector3(-5f, 0, 0),    // enormous raw push toward −X (would dominate uncapped)
        };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Flock", brain.CurrentName);
        Assert.True(self.DesiredVelocity.X > 0f, "capped separation must not overpower cohesion");
        Assert.True(self.DesiredVelocity.Length() <= self.Traits.MaxSpeed + 1e-4f, "never exceeds max speed");
    }

    [Fact]
    public void HungrySheep_WithHerdInRange_ForagesInsteadOfFlocking()
    {
        // Individual-movement guarantee: a genuine need (Forage at full hunger ≈ 0.9) out-scores the
        // 0.7 Flock hold, so a hungry, sociable sheep with its flock in range still picks Forage and
        // peels away to eat rather than staying glued to the group.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 1f, Appetite = 1f, Curiosity = 0f, Fear = 0f });
        var senses = new SenseContext { Hunger = 1f, HasFlock = true, FlockAnchor = new Vector3(3, 0, 0) };
        brain.Tick(0.1, self, senses, new Random(1));
        Assert.Equal("Forage", brain.CurrentName);
    }

    [Fact]
    public void Forager_StaysLatchedUntilSated_ThenReleases()
    {
        // Re-decide every tick so we can drive the latch directly.
        var brain = new UtilityBrain(new BehaviorConfig { DecisionInterval = 0f });
        var self = MakeCreature(new Drives { Appetite = 1f, Sociability = 1f, Curiosity = 0f, Fear = 0f });
        var rng = new Random(1);

        // Commit to Forage on sensed food while hungry.
        brain.Tick(0.1, self, new SenseContext { Hunger = 0.8f, HasFood = true, FoodPosition = new Vector3(1, 0, 0) }, rng);
        Assert.Equal("Forage", brain.CurrentName);

        // Food now gone and a herd is in range. Without the satiation latch Flock would beat the
        // hunger-only Forage score and yank the creature off its meal. Still hungry → latch holds Forage.
        brain.Tick(0.1, self, new SenseContext { Hunger = 0.7f, HasFood = false, HasFlock = true, FlockAnchor = new Vector3(3, 0, 0) }, rng);
        Assert.Equal("Forage", brain.CurrentName);

        // Eaten down past the satiation threshold → latch releases and Flock can reclaim it.
        brain.Tick(0.1, self, new SenseContext { Hunger = 0.1f, HasFood = false, HasFlock = true, FlockAnchor = new Vector3(3, 0, 0) }, rng);
        Assert.Equal("Flock", brain.CurrentName);
    }

    // ---------- stickiness (controlled two-action config) ----------

    private static BehaviorConfig StickyConfig() => new()
    {
        DecisionInterval = 0f,      // re-decide every tick
        SwitchMargin = 0.15f,
        CommitmentBonus = 0.25f,
        CommitmentDecayPerSec = 0f, // no decay, isolate the margin behavior
        DecisionNoise = 0f,
        Actions =
        [
            new BehaviorAction { Name = "Stay", Steering = SteeringKind.Rest,
                Considerations = [new Consideration { Input = InputKind.Fatigue, Curve = ResponseCurve.Identity }] },
            new BehaviorAction { Name = "Go", Steering = SteeringKind.Wander, EmergencyCapable = true, EmergencyThreshold = 0.7f,
                Considerations = [new Consideration { Input = InputKind.Boredom, Curve = ResponseCurve.Identity }] },
        ],
    };

    [Fact]
    public void Committed_DoesNotAbandonForMarginallyHigherChallenger()
    {
        var brain = new UtilityBrain(StickyConfig());
        var self = MakeCreature(Drives.Default);
        var rng = new Random(1);

        // Commit to "Stay" (fatigue 0.6 > boredom 0.4).
        brain.Tick(0.1, self, new SenseContext { Fatigue = 0.6f, Boredom = 0.4f }, rng);
        Assert.Equal("Stay", brain.CurrentName);

        // "Go" now leads slightly (0.6 vs 0.5) but not past margin+commitment → stays.
        brain.Tick(0.1, self, new SenseContext { Fatigue = 0.5f, Boredom = 0.6f }, rng);
        Assert.Equal("Stay", brain.CurrentName);
    }

    [Fact]
    public void EmergencyAction_InterruptsCommitment()
    {
        var brain = new UtilityBrain(StickyConfig());
        var self = MakeCreature(Drives.Default);
        var rng = new Random(1);

        brain.Tick(0.1, self, new SenseContext { Fatigue = 0.6f, Boredom = 0.4f }, rng);
        Assert.Equal("Stay", brain.CurrentName);

        // "Go" crosses its emergency threshold (0.95 ≥ 0.7) → interrupts despite stickiness.
        brain.Tick(0.1, self, new SenseContext { Fatigue = 0.6f, Boredom = 0.95f }, rng);
        Assert.Equal("Go", brain.CurrentName);
    }

    // ---------- frolic regression: stuck-loop + tether + play-range gate ----------

    [Fact]
    public void Forage_AlwaysPathsToNearestFood_NotWanderFallthrough()
    {
        // Regression: Forage steering must always Arrive at the nearest food, even when
        // that food is beyond the general SenseRadius. No Wander fallthrough — a foraging
        // creature targets what it can eat, it doesn't drift.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Appetite = 1f, Sociability = 0f,
            Curiosity = 0f, Fear = 0f });
        var rng = new Random(42);

        // Food is 15u away — beyond SenseRadius (5) but within FoodSenseRadius (20).
        // Forage steering should produce an Arrive toward it, which means DesiredVelocity.X
        // should be positive (food is at +X).
        brain.Tick(0.1, self, new SenseContext
        {
            Hunger = 1f,            // high hunger → Forage should score highest
            HasFood = false,         // beyond SenseRadius, but food position is known
            FoodPosition = new Vector3(15, 0, 0),
            FoodDistance = 15f,
        }, rng);
        Assert.Equal("Forage", brain.CurrentName);
        // Arrive at far-away target: steer toward +X at near-max speed.
        Assert.True(self.DesiredVelocity.X > 0.5f,
            $"Forage should path +X toward food, got X={self.DesiredVelocity.X}");
        // Should NOT be random Wander — with seed=42, Wander would go to 0.248+X.
        // An Arrive at 15+X should dominate strongly toward +X.
    }

    [Fact]
    public void FlocklessFrolic_ReleasesWhenBoredomDrops()
    {
        // Regression: a flockless sheep that frolicked away from its herd must be able to stop
        // frolicking once boredom is satiated — Wander's floor must beat Frolic+SwitchMargin.
        // Without this fix (old SwitchMargin 0.15), the sheep stayed in Frolic forever.
        // Use zero decision interval + instant commitment decay so the scoring math is isolated.
        var brain = new UtilityBrain(new BehaviorConfig
        {
            DecisionInterval = 0f,
            CommitmentBonus = 0f,
        });
        var self = MakeCreature(new Drives { Sociability = 0.9f, Curiosity = 0.5f,
            Fear = 0f, Appetite = 0f });
        var rng = new Random(42);

        // High boredom — should commit to Frolic.
        brain.Tick(0.1, self, new SenseContext { Boredom = 1f }, rng);
        Assert.Equal("Frolic", brain.CurrentName);

        // Second tick: zero boredom, no commitment stickiness.
        // Wander floor (0.0675) > Frolic floor (0.0009) + SwitchMargin (0.06) = 0.0609  ✓
        brain.Tick(0.1, self, new SenseContext { Boredom = 0f }, rng);
        Assert.NotEqual("Frolic", brain.CurrentName);
        Assert.Equal("Wander", brain.CurrentName);
    }

    [Fact]
    public void Frolic_Latch_HoldsUntilBoredomZero()
    {
        // Frolic's latch must prevent other actions from breaking in before boredom
        // is fully drained to 0. Without the latch, Wander would retake at ~0.4 boredom
        // when commitment decays.
        var brain = new UtilityBrain(new BehaviorConfig
        {
            DecisionInterval = 0f,
            CommitmentBonus = 0f,  // zero commitment to isolate the latch
        });
        var self = MakeCreature(new Drives { Sociability = 0.9f, Curiosity = 0.5f,
            Fear = 0f, Appetite = 0f });
        var rng = new Random(42);

        // Commit to Frolic at max boredom.
        brain.Tick(0.1, self, new SenseContext { Boredom = 1f, HasFlock = true,
            FlockAnchor = Vector3.Zero }, rng);
        Assert.Equal("Frolic", brain.CurrentName);

        // Boredom 0.5 — Frolic score ≈ 0.5¹⁰ ≈ 0.001, Wander would beat it easily.
        // But the latch should hold Frolic anyway.
        brain.Tick(0.1, self, new SenseContext { Boredom = 0.5f, HasFlock = true,
            FlockAnchor = Vector3.Zero }, rng);
        Assert.Equal("Frolic", brain.CurrentName);

        // Boredom 0 — latch releases.
        brain.Tick(0.1, self, new SenseContext { Boredom = 0f }, rng);
        Assert.Equal("Wander", brain.CurrentName);
    }

    [Fact]
    public void Rest_Latch_HoldsAtHalfFatigue()
    {
        var brain = new UtilityBrain(new BehaviorConfig { DecisionInterval = 0f, CommitmentBonus = 0f });
        var self = MakeCreature(new Drives { Sociability = 0.5f, Curiosity = 0.2f, Fear = 0f, Appetite = 0f });
        var rng = new Random(42);

        brain.Tick(0.1, self, new SenseContext { Fatigue = 1f }, rng);
        Assert.Equal("Rest", brain.CurrentName);

        brain.Tick(0.1, self, new SenseContext { Fatigue = 0.5f }, rng);
        Assert.Equal("Rest", brain.CurrentName);
    }

    [Fact]
    public void Rest_Latch_ReleasesAtZeroFatigue()
    {
        var brain = new UtilityBrain(new BehaviorConfig { DecisionInterval = 0f, CommitmentBonus = 0f });
        var self = MakeCreature(new Drives { Sociability = 0.5f, Curiosity = 1f, Fear = 0f, Appetite = 0f });
        var rng = new Random(42);

        // Commit to Rest at fatigue=1, then release at fatigue=0
        brain.Tick(0.1, self, new SenseContext { Fatigue = 1f }, rng);
        brain.Tick(0.1, self, new SenseContext { Fatigue = 0f }, rng);
        Assert.NotEqual("Rest", brain.CurrentName);
    }

    [Fact]
    public void Frolic_IgnoresNeighbor_NoPlayChase()
    {
        // Regression: with single Frolic (no play-chase flavor), a neighbor should NOT
        // pull the steering — Frolic is pure darty zig-zag + optional flock tether.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Curiosity = 1f, Sociability = 1f,
            Fear = 0f, Appetite = 0f });
        var rng = new Random(42);

        brain.Tick(0.1, self, new SenseContext
        {
            Boredom = 1f,
            HasNeighbor = true,
            NeighborPosition = new Vector3(3f, 0, 0),
            NeighborProximity = 0.4f,
            HasFlock = true,
            FlockAnchor = Vector3.Zero,
        }, rng);
        Assert.Equal("Frolic", brain.CurrentName);
        Assert.True(
            self.DesiredVelocity.LengthSquared() > 0.01f,
            "Frolic should produce movement");
    }

    [Fact]
    public void Frolic_InFlock_StaysTetheredToAnchor()
    {
        // Regression: a frolicking sheep in a flock must have a component of its desired
        // velocity pointing toward the anchor. The single Frolic steering always includes
        // a soft anchor tether when HasFlock, so play never strands a member.
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Sociability = 0.9f, Curiosity = 0.5f,
            Fear = 0f, Appetite = 0f });
        var rng = new Random(42);

        // Sheep in flock, anchor far to the +X, Frolic steers darty + anchor pull.
        // The anchor pull should dominate toward +X.
        brain.Tick(0.1, self, new SenseContext
        {
            Boredom = 1f,
            HasFlock = true,
            FlockAnchor = new Vector3(10, 0, 0), // far +X
        }, rng);
        Assert.Equal("Frolic", brain.CurrentName);
        Assert.True(self.DesiredVelocity.X > 0f,
            $"frolic must pull toward anchor (+X), got X={self.DesiredVelocity.X}");
    }

    // ---------- determinism ----------

    [Fact]
    public void SameSeedAndInputs_ProduceSameDecisions()
    {
        var brainA = new UtilityBrain(new BehaviorConfig { DecisionNoise = 0.3f });
        var brainB = new UtilityBrain(new BehaviorConfig { DecisionNoise = 0.3f });
        var a = MakeCreature(new Drives { Curiosity = 0.6f, Sociability = 0.7f });
        var b = MakeCreature(new Drives { Curiosity = 0.6f, Sociability = 0.7f });
        var rngA = new Random(123);
        var rngB = new Random(123);

        for (int i = 0; i < 20; i++)
        {
            var senses = new SenseContext { HasNeighbor = true, NeighborProximity = 0.4f, Fatigue = i / 20f };
            brainA.Tick(0.1, a, senses, rngA);
            brainB.Tick(0.1, b, senses, rngB);
            Assert.Equal(brainA.CurrentName, brainB.CurrentName);
        }
    }

    // ---------- need dynamics (via Simulator) ----------

    [Fact]
    public void Fatigue_RecoversWhileResting()
    {
        var sim = new Simulator(Arena.GroundArena(10, 10), seed: 5);
        var blob = sim.SpawnBlob(Vector3.Zero);   // alone → no neighbor
        blob.Needs.Fatigue = 1f;                  // exhausted → brain should Rest

        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        Assert.True(blob.Needs.Fatigue < 1f, $"fatigue should recover at rest, was {blob.Needs.Fatigue}");
        Assert.Equal("Rest", blob.Brain!.CurrentName);
    }

    [Fact]
    public void Fatigue_RisesWhileMoving()
    {
        var sim = new Simulator(Arena.GroundArena(10, 10), seed: 5);
        var blob = sim.SpawnBlob(Vector3.Zero);
        blob.Needs.Fatigue = 0f;                  // fresh → brain wanders/moves

        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        Assert.True(blob.Needs.Fatigue > 0f, $"fatigue should accrue while moving, was {blob.Needs.Fatigue}");
    }
}
