using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Phase B: per-creature-type variety. Verifies the new data-driven seams — flee strategy,
/// action set, and per-trait sense radii — actually change perception/decision output per
/// creature instead of every creature sharing one global brain. Headless — crafted contexts,
/// no Godot.
/// </summary>
public class CreatureVarietyTests
{
    private static Creature MakeSenseCreature(CreatureTraits? traits = null, Drives? drives = null)
        => new(Vector3.Zero, traits ?? new CreatureTraits { MaxSpeed = 1f }, new SteeringLocomotion(), drives);

    private static (FoodItem? Item, float Dist) NoFood(Vector3 from, HashSet<string>? diet) => (null, float.PositiveInfinity);

    // ---------- FleeStrategyRegistry ----------

    [Fact]
    public void Registry_ResolvesKnownNames_ToDistinctStrategies()
    {
        var sheep = FleeStrategyRegistry.Resolve("sheep", bondThreshold: 0.4f);
        var never = FleeStrategyRegistry.Resolve("never", bondThreshold: 0.4f);
        var always = FleeStrategyRegistry.Resolve("always", bondThreshold: 0.4f);

        Assert.IsType<SheepFleeStrategy>(sheep);
        Assert.IsType<NeverFleeStrategy>(never);
        Assert.IsType<AlwaysFleeStrategy>(always);
    }

    [Fact]
    public void Registry_UnknownOrNullName_ResolvesToNull()
    {
        Assert.Null(FleeStrategyRegistry.Resolve(null, 0.4f));
        Assert.Null(FleeStrategyRegistry.Resolve("dragon", 0.4f));
    }

    [Fact]
    public void NeverFleeStrategy_NeverTreatsPlayerAsThreat()
    {
        var strategy = new NeverFleeStrategy();
        Assert.False(strategy.IsPlayerThreat(holdingFood: false, affection: 0f));
    }

    [Fact]
    public void AlwaysFleeStrategy_AlwaysTreatsPlayerAsThreat()
    {
        var strategy = new AlwaysFleeStrategy();
        Assert.True(strategy.IsPlayerThreat(holdingFood: true, affection: 1f));
    }

    // ---------- per-creature FleeStrategy changes PerceptionBuilder output ----------

    [Fact]
    public void PerCreatureFleeStrategy_Overrides_IsPlayerThreat()
    {
        var self = MakeSenseCreature();
        var player = new Creature(new Vector3(1f, 0f, 0f), null, new SteeringLocomotion()) { IsPlayer = true };
        var entities = new List<Creature> { self, player };

        // Simulator-wide default says "never a threat"; this creature's own override says "always".
        self.FleeStrategy = new AlwaysFleeStrategy();
        var senses = PerceptionBuilder.Build(
            self, entities, [], player, null, null, new BehaviorConfig(), new NeverFleeStrategy(), NoFood);

        Assert.True(senses.IsPlayerThreat);
    }

    [Fact]
    public void NoPerCreatureOverride_FallsBackTo_SimulatorWideStrategy()
    {
        var self = MakeSenseCreature();
        var player = new Creature(new Vector3(1f, 0f, 0f), null, new SteeringLocomotion()) { IsPlayer = true };
        var entities = new List<Creature> { self, player };

        // self.FleeStrategy left null → falls back to the injected default.
        var senses = PerceptionBuilder.Build(
            self, entities, [], player, null, null, new BehaviorConfig(), new NeverFleeStrategy(), NoFood);

        Assert.False(senses.IsPlayerThreat);
    }

    // ---------- ActionSetCatalog ----------

    [Fact]
    public void ActionSetCatalog_Herbivore_MatchesDefaultActionCount()
        => Assert.Equal(BehaviorConfig.DefaultActions().Count, ActionSetCatalog.Herbivore.Count);

    [Fact]
    public void ActionSetCatalog_UnknownName_ResolvesToNull()
        => Assert.Null(ActionSetCatalog.Resolve("carnivore"));

    [Fact]
    public void DifferentActionSets_ProduceDifferentBrainDecisions()
    {
        // A single-action table that only ever picks "OnlyWander" vs. the full herbivore table
        // (which, given a starving creature, picks Forage instead). Same drives/senses, different
        // CreatureDef.ActionSet-equivalent config → different Brain.CurrentName.
        var wanderOnly = new BehaviorConfig
        {
            Actions = [new BehaviorAction { Name = "OnlyWander", Steering = SteeringKind.Wander, BaseWeight = 1f }],
        };
        var herbivore = new BehaviorConfig { Actions = ActionSetCatalog.Herbivore };

        var senses = new SenseContext { Hunger = 0.9f };
        var drives = new Drives { Appetite = 1f };
        var self = MakeSenseCreature(drives: drives);

        var wanderBrain = new UtilityBrain(wanderOnly);
        wanderBrain.Tick(1.0, self, senses, new Random(1));

        var herbivoreBrain = new UtilityBrain(herbivore);
        herbivoreBrain.Tick(1.0, self, senses, new Random(1));

        Assert.Equal("OnlyWander", wanderBrain.CurrentName);
        Assert.Equal("Forage", herbivoreBrain.CurrentName);
        Assert.NotEqual(wanderBrain.CurrentName, herbivoreBrain.CurrentName);
    }

    // ---------- per-trait SenseRadius ----------

    [Fact]
    public void PerTraitSenseRadius_Overrides_HasNeighborAtFixedDistance()
    {
        // Neighbor sits 8 units away — beyond the default 5-unit SenseRadius but within a
        // keen-eyed creature's overridden 10-unit radius.
        var behavior = new BehaviorConfig();
        var keenTraits = new CreatureTraits { MaxSpeed = 1f, SenseRadius = 10f };
        var dullTraits = new CreatureTraits { MaxSpeed = 1f }; // SenseRadius unset → falls back to config default (5)

        var neighbor = new Creature(new Vector3(8f, 0f, 0f), null, new SteeringLocomotion());

        var keen = new Creature(Vector3.Zero, keenTraits, new SteeringLocomotion());
        var dull = new Creature(Vector3.Zero, dullTraits, new SteeringLocomotion());

        var keenSenses = PerceptionBuilder.Build(
            keen, [keen, neighbor], [], null, null, null, behavior, new NeverFleeStrategy(), NoFood);
        var dullSenses = PerceptionBuilder.Build(
            dull, [dull, neighbor], [], null, null, null, behavior, new NeverFleeStrategy(), NoFood);

        Assert.True(keenSenses.HasNeighbor);
        Assert.False(dullSenses.HasNeighbor);
    }

    [Fact]
    public void UnsetSenseRadius_ReproducesConfigDefault_Deterministically()
    {
        var behavior = new BehaviorConfig();
        var neighbor = new Creature(new Vector3(4f, 0f, 0f), null, new SteeringLocomotion());

        var unset = new Creature(Vector3.Zero, new CreatureTraits { MaxSpeed = 1f }, new SteeringLocomotion());
        var explicitDefault = new Creature(
            Vector3.Zero, new CreatureTraits { MaxSpeed = 1f, SenseRadius = behavior.SenseRadius }, new SteeringLocomotion());

        var unsetSenses = PerceptionBuilder.Build(
            unset, [unset, neighbor], [], null, null, null, behavior, new NeverFleeStrategy(), NoFood);
        var explicitSenses = PerceptionBuilder.Build(
            explicitDefault, [explicitDefault, neighbor], [], null, null, null, behavior, new NeverFleeStrategy(), NoFood);

        Assert.Equal(explicitSenses.HasNeighbor, unsetSenses.HasNeighbor);
        Assert.Equal(explicitSenses.NeighborProximity, unsetSenses.NeighborProximity, 5);
    }

    [Fact]
    public void PerTraitFoodSenseRadius_Overrides_HasFoodAtFixedDistance()
    {
        var behavior = new BehaviorConfig();
        var food = new FoodItem { Position = new Vector3(25f, 0f, 0f), Def = new FoodDef { Id = "berries" } };
        (FoodItem? Item, float Dist) NearestFood(Vector3 from, HashSet<string>? diet) => (food, 25f);

        var keenTraits = new CreatureTraits { MaxSpeed = 1f, FoodSenseRadius = 30f };
        var dullTraits = new CreatureTraits { MaxSpeed = 1f }; // falls back to config default (20)

        var keen = new Creature(Vector3.Zero, keenTraits, new SteeringLocomotion());
        var dull = new Creature(Vector3.Zero, dullTraits, new SteeringLocomotion());

        var keenSenses = PerceptionBuilder.Build(
            keen, [keen], [], null, null, null, behavior, new NeverFleeStrategy(), NearestFood);
        var dullSenses = PerceptionBuilder.Build(
            dull, [dull], [], null, null, null, behavior, new NeverFleeStrategy(), NearestFood);

        Assert.True(keenSenses.HasFood);
        Assert.False(dullSenses.HasFood);
    }

    // ---------- HoldWhile latch ----------

    [Fact]
    public void HoldWhile_Active_WhenInputAboveThreshold()
    {
        var latch = new HoldWhile(InputKind.Hunger, Threshold: 0.15f);
        Assert.True(latch.Active(new SenseContext { Hunger = 0.5f }));
        Assert.False(latch.Active(new SenseContext { Hunger = 0.1f }));
    }

    [Fact]
    public void HoldWhile_PlayerPanic_ReadsSenseContextProperty()
    {
        var latch = new HoldWhile(InputKind.PlayerPanic, Threshold: 0.5f);
        var panicking = new SenseContext { IsPlayerThreat = true, HasPlayer = true, HasFlock = false };
        var safe = new SenseContext { IsPlayerThreat = true, HasPlayer = true, HasFlock = true };

        Assert.True(latch.Active(panicking));
        Assert.False(latch.Active(safe));
    }
}
