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
    public void IdleCreature_WithNothingAround_Wanders()
    {
        var brain = new UtilityBrain(new BehaviorConfig());
        var self = MakeCreature(new Drives { Curiosity = 1f });
        brain.Tick(0.1, self, new SenseContext(), new Random(1));
        Assert.Equal("Wander", brain.CurrentName);
        Assert.NotEqual(Vector3.Zero, self.DesiredVelocity);
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
