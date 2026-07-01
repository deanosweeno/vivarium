using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Need-dynamics resolution: fatigue drains while moving and recovers at rest, boredom is
/// relieved only by Frolic, hunger creeps up steadily. Pure function — no sim, no RNG.
/// </summary>
public class NeedSystemTests
{
    private sealed class StubMovement : IMovementMode
    {
        public void Tick(double delta, Creature creature, Arena arena, Random rng) { }
    }

    private static Creature MakeCreature(Vector3 velocity, CreatureTraits? traits = null)
    {
        var creature = new Creature(Vector3.Zero, traits, new StubMovement());
        creature.Velocity = velocity;
        return creature;
    }

    [Fact]
    public void Stopped_Creature_RecoversFatigue()
    {
        var traits = new CreatureTraits { MaxSpeed = 2f, FatigueRecoverPerSec = 0.4f };
        var creature = MakeCreature(Vector3.Zero, traits);
        creature.Needs.Fatigue = 0.5f;

        NeedSystem.Resolve(1.0, creature, new NeedConfig());

        Assert.Equal(0.1f, creature.Needs.Fatigue, 3);
    }

    [Fact]
    public void MovingFast_Creature_AccruesFatigue()
    {
        var traits = new CreatureTraits { MaxSpeed = 2f, FatigueGainPerSec = 0.06f };
        var creature = MakeCreature(new Vector3(2f, 0f, 0f), traits);
        creature.Needs.Fatigue = 0f;

        NeedSystem.Resolve(1.0, creature, new NeedConfig());

        Assert.Equal(0.06f, creature.Needs.Fatigue, 3);
    }

    [Fact]
    public void SpeedBelowThreshold_StillRecovers()
    {
        var traits = new CreatureTraits { MaxSpeed = 10f, FatigueRecoverPerSec = 0.4f };
        // speedFrac = 0.5/10 = 0.05, below the default 0.1 threshold → recovers, doesn't accrue.
        var creature = MakeCreature(new Vector3(0.5f, 0f, 0f), traits);
        creature.Needs.Fatigue = 0.5f;

        NeedSystem.Resolve(1.0, creature, new NeedConfig());

        Assert.Equal(0.1f, creature.Needs.Fatigue, 3);
    }

    [Fact]
    public void Hunger_AccruesAtConfiguredRate()
    {
        var creature = MakeCreature(Vector3.Zero);
        var cfg = new NeedConfig { HungerGainPerSec = 0.01f };

        NeedSystem.Resolve(2.0, creature, cfg);

        Assert.Equal(0.02f, creature.Needs.Hunger, 3);
    }

    [Fact]
    public void Boredom_AccruesWhenNotFrolicking()
    {
        var creature = MakeCreature(Vector3.Zero);
        var cfg = new NeedConfig { BoredomGainPerSec = 0.02f };

        NeedSystem.Resolve(1.0, creature, cfg);

        Assert.Equal(0.02f, creature.Needs.Boredom, 3);
    }

    [Fact]
    public void Needs_StayClamped_ToUnitRange()
    {
        var creature = MakeCreature(Vector3.Zero);
        creature.Needs.Hunger = 0.999f;

        NeedSystem.Resolve(100.0, creature, new NeedConfig());

        Assert.InRange(creature.Needs.Hunger, 0f, 1f);
        Assert.InRange(creature.Needs.Fatigue, 0f, 1f);
        Assert.InRange(creature.Needs.Boredom, 0f, 1f);
    }

    [Fact]
    public void FixedSeed_Trajectory_MatchesPreExtractionBehavior()
    {
        // Regression guard: same inputs the old inline Simulator.UpdateNeeds computed against,
        // asserting identical output now that the logic lives in NeedSystem.
        var traits = new CreatureTraits
        {
            MaxSpeed = 3f,
            FatigueGainPerSec = 0.06f,
            FatigueRecoverPerSec = 0.4f,
        };
        var creature = MakeCreature(new Vector3(1.5f, 0f, 0f), traits);
        creature.Needs.Fatigue = 0.2f;
        creature.Needs.Hunger = 0.1f;
        creature.Needs.Boredom = 0.3f;

        var cfg = new NeedConfig
        {
            HungerGainPerSec = 0.003f,
            BoredomGainPerSec = 0.015f,
            BoredomRelievePerSec = 0.4f,
        };

        NeedSystem.Resolve(0.5, creature, cfg);

        float speedFrac = 1.5f / 3f; // 0.5
        float expectedFatigue = 0.2f + traits.FatigueGainPerSec * speedFrac * 0.5f;
        float expectedHunger = 0.1f + cfg.HungerGainPerSec * 0.5f;
        float expectedBoredom = 0.3f + cfg.BoredomGainPerSec * 0.5f;

        Assert.Equal(expectedFatigue, creature.Needs.Fatigue, 5);
        Assert.Equal(expectedHunger, creature.Needs.Hunger, 5);
        Assert.Equal(expectedBoredom, creature.Needs.Boredom, 5);
    }
}
