using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class WalkModeTests
{
    [Fact]
    public void Wander_ChangesDirectionOverTime()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var walkMode = new WalkMode();
        var creature = new Creature(new Vector3(0, 1, 0), null, walkMode);

        var startPos = creature.Position;

        // Tick many times — should wander around
        for (int i = 0; i < 100; i++)
        {
            walkMode.Tick(0.1, creature, arena, rng);
        }

        // Position should have changed from starting position
        Assert.NotEqual(startPos, creature.Position);

        // Direction should have changed at least once (position moved)
        float dist = (creature.Position - startPos).Length();
        Assert.True(dist > 0.5f, $"Should have wandered at least 0.5 units, got {dist}");
    }

    [Fact]
    public void Speed_ClampedToMaxSpeed()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var traits = new CreatureTraits { MaxSpeed = 1.0f };
        var walkMode = new WalkMode();
        var creature = new Creature(new Vector3(0, 1, 0), traits, walkMode);

        // Tick once to set direction and velocity
        walkMode.Tick(0.1, creature, arena, rng);

        // Horizontal speed should be ≤ MaxSpeed
        float horizSpeed = new Vector2(creature.Velocity.X, creature.Velocity.Z).Length();
        Assert.True(horizSpeed <= 1.01f, $"Horizontal speed {horizSpeed} exceeds MaxSpeed 1.0");
        Assert.True(horizSpeed >= 0.99f, $"Horizontal speed {horizSpeed} should be exactly MaxSpeed");
    }

    [Fact]
    public void WallBounce_StaysWithinBounds()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var traits = new CreatureTraits { MaxSpeed = 3.0f }; // fast wander
        var walkMode = new WalkMode();

        // Start near the right wall
        var creature = new Creature(new Vector3(4.4f, 1, 0), traits, walkMode);

        // Tick many times — the creature must never escape bounds
        for (int i = 0; i < 100; i++)
        {
            walkMode.Tick(0.1, creature, arena, rng);

            Assert.True(creature.Position.X >= -4.5f && creature.Position.X <= 4.5f,
                $"Tick {i}: X={creature.Position.X} out of bounds");
            Assert.True(creature.Position.Z >= -4.5f && creature.Position.Z <= 4.5f,
                $"Tick {i}: Z={creature.Position.Z} out of bounds");
        }
    }

    [Fact]
    public void Jump_SetsVerticalVelocity()
    {
        var walkMode = new WalkMode();
        var creature = new Creature(Vector3.Zero, null, walkMode);

        walkMode.Jump(creature, 5.0f);

        Assert.Equal(5.0f, creature.Velocity.Y);
        Assert.Equal(0f, creature.Velocity.X);
        Assert.Equal(0f, creature.Velocity.Z);
    }

    [Fact]
    public void Jump_PreservesHorizontalVelocity()
    {
        var walkMode = new WalkMode();
        var creature = new Creature(Vector3.Zero, null, walkMode);

        creature.Velocity = new Vector3(2f, 0f, 3f); // some horizontal movement
        walkMode.Jump(creature, 4.0f);

        Assert.Equal(4.0f, creature.Velocity.Y);
        Assert.Equal(2f, creature.Velocity.X);
        Assert.Equal(3f, creature.Velocity.Z);
    }

    [Fact]
    public void Deterministic_SameSeedSamePath()
    {
        var arena = Arena.GroundArena(10, 10);

        var w1 = new WalkMode();
        var c1 = new Creature(new Vector3(0, 1, 0), null, w1);
        var rng1 = new Random(123);

        var w2 = new WalkMode();
        var c2 = new Creature(new Vector3(0, 1, 0), null, w2);
        var rng2 = new Random(123);

        for (int i = 0; i < 50; i++)
        {
            w1.Tick(0.1, c1, arena, rng1);
            w2.Tick(0.1, c2, arena, rng2);
        }

        Assert.Equal(c1.Position, c2.Position);
        Assert.Equal(c1.Velocity, c2.Velocity);
    }

    [Fact]
    public void Y_Velocity_PreservedAcrossWalkTicks()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var walkMode = new WalkMode();
        var creature = new Creature(new Vector3(0, 2, 0), null, walkMode);

        // Simulate gravity setting a downward velocity
        creature.Velocity = new Vector3(0f, -3f, 0f);

        // Tick — WalkMode should change X/Z but NOT Y
        walkMode.Tick(0.1, creature, arena, rng);

        Assert.Equal(-3f, creature.Velocity.Y, 6);
    }

    [Fact]
    public void Direction_Timer_PicksNewDirection()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var walkMode = new WalkMode();
        var creature = new Creature(Vector3.Zero, null, walkMode);

        // After first tick, a direction is set
        walkMode.Tick(0.1, creature, arena, rng);
        var firstVelocity = creature.Velocity;

        // Tick many times to give direction change a chance
        for (int i = 0; i < 100; i++)
        {
            walkMode.Tick(0.1, creature, arena, rng);
        }

        // Velocity should have changed at some point
        Assert.NotEqual(firstVelocity, creature.Velocity);
    }

    [Fact]
    public void MaxSpeed_Respected_AfterDirectionChange()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var traits = new CreatureTraits { MaxSpeed = 0.3f };
        var walkMode = new WalkMode();
        var creature = new Creature(new Vector3(0, 1, 0), traits, walkMode);

        // Tick many times — speed should never exceed MaxSpeed
        for (int i = 0; i < 100; i++)
        {
            walkMode.Tick(0.1, creature, arena, rng);

            float horizSpeed = new Vector2(creature.Velocity.X, creature.Velocity.Z).Length();
            Assert.True(horizSpeed <= 0.31f, $"Tick {i}: speed {horizSpeed} exceeds MaxSpeed 0.3");
        }
    }
}
