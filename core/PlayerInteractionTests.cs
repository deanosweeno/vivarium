using System;
using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Player ↔ creature interaction & taming: feed/soothe/play verbs, the Affection bond, and the
/// FleePlayer/FollowPlayer reactions. Drives are set explicitly so selection is deterministic.
/// </summary>
public class PlayerInteractionTests
{
    private static Simulator NewSim() => new(Arena.GroundArena(32, 32), seed: 1);

    // A timid sheep (high Fear) so FleePlayer scoring is unambiguous.
    private static Drives Timid => new() { Fear = 0.9f, Curiosity = 0.5f, Sociability = 0.5f };

    // A stand-in food type for putting something in the player's hand.
    private static FoodDef Snack => new() { Id = "snack", Name = "Snack" };

    [Fact]
    public void Feed_ReducesHunger_AndRaisesAffection_OnNearestInReach()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Hunger = 0.8f;

        input.CarriedFood = Snack;
        input.FeedPressed = true;
        sim.Tick(0.1);

        Assert.True(sheep.Needs.Hunger < 0.5f, $"hunger not relieved: {sheep.Needs.Hunger}");
        Assert.True(sheep.Needs.Affection > 0.1f, $"affection not gained: {sheep.Needs.Affection}");
    }

    [Fact]
    public void Feed_RequiresFoodInHand()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Hunger = 0.8f;

        input.CarriedFood = null;
        input.FeedPressed = true;
        sim.Tick(0.1);

        Assert.Equal(0f, sheep.Needs.Affection, 3);
    }

    [Fact]
    public void Feed_OnlyAffectsNearestInReach()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var near = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        var far = sim.SpawnBlob(new Vector3(10f, 0, 0), traits: null, drives: Timid);

        input.CarriedFood = Snack;
        input.FeedPressed = true;
        sim.Tick(0.1);

        Assert.True(near.Needs.Affection > 0.1f);
        Assert.Equal(0f, far.Needs.Affection, 3);
    }

    [Fact]
    public void Soothe_IsNoOp_BelowPartialBond()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0.1f;        // below PartialBondThreshold (0.4)
        sheep.Needs.Fatigue = 0.8f;

        input.SoothePressed = true;
        sim.Tick(0.1);

        Assert.True(sheep.Needs.Fatigue > 0.7f, $"fatigue should be untouched: {sheep.Needs.Fatigue}");
    }

    [Fact]
    public void SootheAndPlay_AreEffective_AtOrAbovePartialBond()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0.5f;        // above PartialBondThreshold
        sheep.Needs.Fatigue = 0.8f;
        sheep.Needs.Boredom = 0.8f;

        input.SoothePressed = true;
        input.PlayPressed = true;
        sim.Tick(0.1);

        Assert.True(sheep.Needs.Fatigue < 0.5f, $"soothe should rest: {sheep.Needs.Fatigue}");
        Assert.True(sheep.Needs.Boredom < 0.5f, $"play should relieve boredom: {sheep.Needs.Boredom}");
    }

    [Fact]
    public void StrangerSheep_PicksFleePlayer_NearPlayer()
    {
        var sim = NewSim();
        sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(2f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0f;

        sim.Tick(0.1);

        Assert.Equal("FleePlayer", sheep.Brain!.CurrentName);
    }

    [Fact]
    public void BondedSheep_DoesNotFleePlayer()
    {
        var sim = NewSim();
        sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(2f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0.9f;

        sim.Tick(0.1);

        Assert.NotEqual("FleePlayer", sheep.Brain!.CurrentName);
    }

    [Fact]
    public void PlayerHoldingFood_FlipsSheepToFollow_RegardlessOfBond()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(2f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0f;          // still a stranger
        input.CarriedFood = Snack;

        sim.Tick(0.1);

        Assert.Equal("FollowPlayer", sheep.Brain!.CurrentName);
        // Must actually walk toward the player — Arrive toward player at (0,0) from (2,0)
        // should produce a negative-X velocity (heading left toward origin).
        Assert.True(sheep.DesiredVelocity.X < -1e-4f,
            $"FollowPlayer should steer toward player (-X), got X={sheep.DesiredVelocity.X}");
    }

    [Fact]
    public void Affection_ClampsToOne()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);

        for (int i = 0; i < 20; i++)
        {
            input.CarriedFood = Snack;   // feed consumes it, so refill each round
            input.FeedPressed = true;
            sim.Tick(0.1);
        }

        Assert.True(sheep.Needs.Affection <= 1f, $"affection exceeded 1: {sheep.Needs.Affection}");
        Assert.True(sheep.Needs.Affection > 0.9f, "affection should have ramped up");
    }

    // --- Compositional spine: intent routing + per-verb dispatch + player state ---

    [Fact]
    public void QueueIntent_RoutesToMatchingVerb_OnlyTheQueuedOneFires()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0.5f;   // above bond, so soothe/play would be allowed
        sheep.Needs.Fatigue = 0.8f;
        sheep.Needs.Boredom = 0.8f;

        input.QueueIntent("soothe");    // only soothe queued
        sim.Tick(0.1);

        Assert.True(sheep.Needs.Fatigue < 0.5f, "soothe should have fired");
        Assert.True(sheep.Needs.Boredom > 0.7f, "play must NOT fire when not queued");
    }

    [Fact]
    public void Intent_IsEdgeTriggered_OneKeypressOneApply()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Affection = 0.5f;
        sheep.Needs.Fatigue = 0.8f;

        input.QueueIntent("soothe");
        sim.Tick(0.1);
        float afterFirst = sheep.Needs.Fatigue;

        sim.Tick(0.1);   // no re-queue — intent already consumed
        // Soothe must NOT re-apply (no second ~0.5 relief). Natural fatigue drift may nudge it up
        // slightly, so assert it didn't drop again rather than exact equality.
        Assert.True(sheep.Needs.Fatigue >= afterFirst - 0.001f,
            $"soothe re-applied without a new keypress: {afterFirst} -> {sheep.Needs.Fatigue}");
    }

    [Fact]
    public void Feed_CanApplyGate_NoFoodInHand_NoOp()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var sheep = sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);
        sheep.Needs.Hunger = 0.8f;

        input.CarriedFood = null;
        input.QueueIntent("feed");
        sim.Tick(0.1);

        Assert.Equal(0.8f, sheep.Needs.Hunger, 3);
    }

    [Fact]
    public void PlayerState_IsWalking_WhenMoveInputPresent()
    {
        var sim = NewSim();
        var (player, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));

        input.MoveInput = new Vector2(1f, 0f);
        sim.Tick(0.1);

        Assert.Equal(PlayerStateKind.Walking, player.PlayerState.Kind);
    }

    [Fact]
    public void PlayerState_IsIdle_WhenStill()
    {
        var sim = NewSim();
        var (player, _) = sim.SpawnPlayer(new Vector3(0, 0, 0));

        sim.Tick(0.1);

        Assert.Equal(PlayerStateKind.Idle, player.PlayerState.Kind);
    }

    [Fact]
    public void PlayerState_IsInteracting_AndRecordsVerb_AfterFeed()
    {
        var sim = NewSim();
        var (player, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);

        input.CarriedFood = Snack;
        input.QueueIntent("feed");
        sim.Tick(0.05);   // shorter than the interact-display window

        Assert.Equal(PlayerStateKind.Interacting, player.PlayerState.Kind);
        Assert.Equal("feed", player.PlayerState.LastVerbId);
    }

    // --- Pick up / place / feed-consume: food as a pickable item ---

    // Drop a pickable food item into the world at a position and return it.
    private static FoodItem AddFood(Simulator sim, Vector3 pos, bool pickable = true)
    {
        var item = new FoodItem { Position = pos, Def = new FoodDef { Id = "snack", Name = "Snack", Pickable = pickable } };
        sim.Food.Add(item);
        return item;
    }

    [Fact]
    public void PickUp_TakesNearbyPickableFood_IntoHand_AndDepletesItem()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var item = AddFood(sim, new Vector3(1f, 0, 0));

        input.QueueIntent("pickup");
        sim.Tick(0.1);

        Assert.True(input.HoldingFood, "player should be holding food");
        Assert.Equal("snack", input.CarriedFood!.Id);
        Assert.False(item.Available, "picked item should be depleted");
        Assert.True(item.RespawnTimer > 0f, "picked item should be regrowing");
    }

    [Fact]
    public void PickUp_NoOp_WhenAlreadyHolding()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var item = AddFood(sim, new Vector3(1f, 0, 0));
        input.CarriedFood = Snack;

        input.QueueIntent("pickup");
        sim.Tick(0.1);

        Assert.True(item.Available, "item must be untouched when hands are full");
    }

    [Fact]
    public void PickUp_NoOp_WhenOutOfReach()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var item = AddFood(sim, new Vector3(10f, 0, 0));

        input.QueueIntent("pickup");
        sim.Tick(0.1);

        Assert.False(input.HoldingFood);
        Assert.True(item.Available);
    }

    [Fact]
    public void PickUp_NoOp_WhenItemNotPickable()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        var item = AddFood(sim, new Vector3(1f, 0, 0), pickable: false);

        input.QueueIntent("pickup");
        sim.Tick(0.1);

        Assert.False(input.HoldingFood);
        Assert.True(item.Available);
    }

    [Fact]
    public void Place_EmptiesTheHand()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        input.CarriedFood = Snack;

        input.QueueIntent("place");
        sim.Tick(0.1);

        Assert.False(input.HoldingFood, "drop should empty the hand");
    }

    [Fact]
    public void Feed_ConsumesHeldFood()
    {
        var sim = NewSim();
        var (_, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));
        sim.SpawnBlob(new Vector3(1f, 0, 0), traits: null, drives: Timid);

        input.CarriedFood = Snack;
        input.QueueIntent("feed");
        sim.Tick(0.1);

        Assert.False(input.HoldingFood, "feeding should consume the held item");
    }

    [Fact]
    public void PlayerState_Heading_FacesMoveDirection()
    {
        var sim = NewSim();
        var (player, input) = sim.SpawnPlayer(new Vector3(0, 0, 0));

        input.MoveInput = new Vector2(1f, 0f);   // move along +X
        sim.Tick(0.1);

        // Heading is atan2(vel.X, vel.Z); pure +X motion ⇒ ~+pi/2.
        Assert.Equal(MathF.PI / 2f, player.PlayerState.Heading, 2);
        Assert.True(player.PlayerState.Speed01 > 0f, "speed factor should be positive while moving");
    }
}
