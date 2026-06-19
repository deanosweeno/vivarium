using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class BlobWalkModeTests
{
    [Fact]
    public void StartsIdle()
    {
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        Assert.Equal(WanderState.Idle, mode.State);
        Assert.True(mode.StateTimer > 0);
    }

    [Fact]
    public void IdleHasZeroVelocity()
    {
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        var arena = Arena.GroundArena(10, 10);
        var creature = new Creature(Vector3.Zero, Blob.DefaultBlobTraits, mode);
        mode.Tick(0.5, creature, arena, rng);
        Assert.Equal(Vector3.Zero, creature.Velocity);
    }

    [Fact]
    public void MovesWhenSliding()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        var creature = new Creature(new Vector3(2, 0.5f, 2), Blob.DefaultBlobTraits, mode);
        mode.ForceSlide(new Vector3(1f, 0f, 0f), 0.5f, 999);
        mode.Tick(0.5, creature, arena, rng);
        Assert.Equal(2.25f, creature.Position.X, 3);
        Assert.Equal(0.5f, creature.Position.Y, 3);
        Assert.Equal(2f, creature.Position.Z, 3);
        Assert.Equal(WanderState.Sliding, mode.State);
    }

    [Fact]
    public void TransitionsToIdleAfterSlideTimerExpires()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        var creature = new Creature(new Vector3(2, 0, 2), Blob.DefaultBlobTraits, mode);
        mode.ForceSlide(new Vector3(0.5f, 0f, 0f), 0.5f, 0.01);
        mode.Tick(0.1, creature, arena, rng);
        Assert.Equal(WanderState.Idle, mode.State);
        Assert.Equal(Vector3.Zero, creature.Velocity);
        Assert.True(mode.StateTimer > 0);
    }

    [Fact]
    public void TransitionsToSlidingAfterIdleExpires()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        var creature = new Creature(Vector3.Zero, Blob.DefaultBlobTraits, mode);
        mode.State = WanderState.Idle;
        mode.StateTimer = 0.01;
        mode.Tick(0.1, creature, arena, rng);
        Assert.Equal(WanderState.Sliding, mode.State);
        Assert.True(mode.StateTimer > 0);
    }

    [Fact]
    public void ClampedToArenaXZ()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        var creature = new Creature(new Vector3(20, 0, 20), Blob.DefaultBlobTraits, mode);
        mode.ForceSlide(new Vector3(1f, 0f, 1f), 0.5f, 999);
        mode.Tick(0.1, creature, arena, rng);
        Assert.True(creature.Position.X <= 4.5f);
        Assert.True(creature.Position.Z <= 4.5f);
        Assert.True(creature.Position.X >= -4.5f);
        Assert.True(creature.Position.Z >= -4.5f);
    }

    [Fact]
    public void BouncesOffWall()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng = new Random(42);
        var mode = new BlobWalkMode(rng);
        var creature = new Creature(new Vector3(4.4f, 0, 0), Blob.DefaultBlobTraits, mode);
        mode.ForceSlide(new Vector3(2f, 0f, 0f), 2f, 999);
        mode.Tick(0.5, creature, arena, rng);
        Assert.True(creature.Velocity.X < 0, "Velocity should reflect off right wall");
        Assert.True(creature.Position.X <= 4.5f);
    }

    [Fact]
    public void DeterministicWander_SameSeedProducesSamePath()
    {
        var arena = Arena.GroundArena(10, 10);
        var rng1 = new Random(123);
        var mode1 = new BlobWalkMode(rng1);
        var creature1 = new Creature(Vector3.Zero, Blob.DefaultBlobTraits, mode1);
        var rng2 = new Random(123);
        var mode2 = new BlobWalkMode(rng2);
        var creature2 = new Creature(Vector3.Zero, Blob.DefaultBlobTraits, mode2);
        for (int i = 0; i < 100; i++)
        {
            mode1.Tick(0.1, creature1, arena, rng1);
            mode2.Tick(0.1, creature2, arena, rng2);
        }
        Assert.Equal(creature1.Position, creature2.Position);
        Assert.Equal(mode1.State, mode2.State);
    }
}
