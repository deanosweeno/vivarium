using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class SimulatorTests
{
    [Fact]
    public void SpawnBlobReturnsBlobAtPosition()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        var pos = new Vector2(2, 3);
        var blob = sim.SpawnBlob(pos);

        Assert.NotNull(blob);
        Assert.Equal(2f, blob.Position.X, 3);
        Assert.Equal(3f, blob.Position.Y, 3);
    }

    [Fact]
    public void SpawnBlobOutsideArena_ClampedToBounds()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        // Position way outside the 10x10 arena
        var blob = sim.SpawnBlob(new Vector2(100, -100));

        Assert.True(blob.Position.X <= 5f);
        Assert.True(blob.Position.X >= -5f);
        Assert.True(blob.Position.Y <= 5f);
        Assert.True(blob.Position.Y >= -5f);
    }

    [Fact]
    public void SpawnedBlobInList()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        var blob = sim.SpawnBlob(Vector2.Zero);

        Assert.Single(sim.Blobs);
        Assert.Same(blob, sim.Blobs[0]);
        Assert.Equal(1, sim.BlobCount);
    }

    [Fact]
    public void TickAdvancesAllBlobs()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        var b1 = sim.SpawnBlob(Vector2.Zero);
        var b2 = sim.SpawnBlob(new Vector2(1, 1));
        var b3 = sim.SpawnBlob(new Vector2(-1, -1));

        // Force blobs to idle with tiny timer so they transition to sliding
        b1.State = WanderState.Idle;
        b1.StateTimer = 0.01;
        b2.State = WanderState.Idle;
        b2.StateTimer = 0.01;
        b3.State = WanderState.Idle;
        b3.StateTimer = 0.01;

        sim.Tick(0.1);

        // All should now be sliding (have velocity)
        Assert.NotEqual(Vector2.Zero, b1.Velocity);
        Assert.NotEqual(Vector2.Zero, b2.Velocity);
        Assert.NotEqual(Vector2.Zero, b3.Velocity);
    }

    [Fact]
    public void DeterministicSim_SameSeedProducesIdenticalState()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));

        var sim1 = new Simulator(arena, seed: 999);
        var sim2 = new Simulator(arena, seed: 999);

        // Spawn multiple blobs at the same positions
        sim1.SpawnBlob(Vector2.Zero);
        sim1.SpawnBlob(new Vector2(2, 2));
        sim1.SpawnBlob(new Vector2(-2, -2));

        sim2.SpawnBlob(Vector2.Zero);
        sim2.SpawnBlob(new Vector2(2, 2));
        sim2.SpawnBlob(new Vector2(-2, -2));

        // Tick both for many frames
        for (int i = 0; i < 50; i++)
        {
            sim1.Tick(0.1);
            sim2.Tick(0.1);
        }

        Assert.Equal(sim1.BlobCount, sim2.BlobCount);

        for (int i = 0; i < sim1.BlobCount; i++)
        {
            Assert.Equal(sim1.Blobs[i].Position, sim2.Blobs[i].Position);
            Assert.Equal(sim1.Blobs[i].State, sim2.Blobs[i].State);
            Assert.Equal(sim1.Blobs[i].Velocity, sim2.Blobs[i].Velocity);
        }
    }

    [Fact]
    public void PushApart_OverlappingBlobsSeparated()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        // Create overlapping blobs directly (bypass SpawnBlob overlap check)
        var b1 = new Blob(new Vector2(1.0f, 0f), 1f, 0f, 0f, sim.Rng);
        var b2 = new Blob(new Vector2(1.6f, 0f), 1f, 0f, 0f, sim.Rng);
        sim.Blobs.Add(b1);
        sim.Blobs.Add(b2);

        // Keep them idle so they don't wander on Tick
        b1.State = WanderState.Idle;
        b1.StateTimer = 999;
        b2.State = WanderState.Idle;
        b2.StateTimer = 999;

        float distBefore = (b1.Position - b2.Position).Length();
        Assert.True(distBefore < 0.9f, "blobs should start overlapping");

        sim.Tick(0.1);

        float distAfter = (b1.Position - b2.Position).Length();
        Assert.True(distAfter >= Blob.Radius * 1.9f, "blobs should be pushed apart");
    }

    [Fact]
    public void PushApart_NoOverlapPreserved()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        // Spawn two blobs far apart
        var b1 = sim.SpawnBlob(new Vector2(-3f, 0f));
        var b2 = sim.SpawnBlob(new Vector2(3f, 0f));

        b1.State = WanderState.Idle;
        b1.StateTimer = 999;
        b2.State = WanderState.Idle;
        b2.StateTimer = 999;

        var pos1Before = b1.Position;
        var pos2Before = b2.Position;

        sim.Tick(0.1);

        // Should be unchanged
        Assert.Equal(pos1Before, b1.Position);
        Assert.Equal(pos2Before, b2.Position);
    }

    [Fact]
    public void PushApart_DistanceZero_NoCrash()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var sim = new Simulator(arena, seed: 42);

        // Force two blobs to exactly the same position (bypass SpawnBlob overlap check)
        var b1 = new Blob(new Vector2(0f, 0f), 1f, 0f, 0f, sim.Rng);
        var b2 = new Blob(new Vector2(0f, 0f), 1f, 0f, 0f, sim.Rng);
        sim.Blobs.Add(b1);
        sim.Blobs.Add(b2);

        b1.State = WanderState.Idle;
        b1.StateTimer = 999;
        b2.State = WanderState.Idle;
        b2.StateTimer = 999;

        // Shouldn't throw or produce NaN
        sim.Tick(0.1);

        float dist = (b1.Position - b2.Position).Length();
        Assert.False(float.IsNaN(dist));
        Assert.False(float.IsInfinity(dist));
        Assert.True(dist > 0f, "blobs should be separated");
    }

    [Fact]
    public void DeterministicCollisions_SameSeedSameOutcome()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));

        var sim1 = new Simulator(arena, seed: 777);
        var sim2 = new Simulator(arena, seed: 777);

        // Spawn blobs close together so they collide
        sim1.SpawnBlob(new Vector2(0f, 0f));
        sim1.SpawnBlob(new Vector2(0.8f, 0f));
        sim1.SpawnBlob(new Vector2(0f, 0.8f));

        sim2.SpawnBlob(new Vector2(0f, 0f));
        sim2.SpawnBlob(new Vector2(0.8f, 0f));
        sim2.SpawnBlob(new Vector2(0f, 0.8f));

        // Let them move and collide
        for (int i = 0; i < 30; i++)
        {
            sim1.Tick(0.1);
            sim2.Tick(0.1);
        }

        Assert.Equal(sim1.BlobCount, sim2.BlobCount);
        for (int i = 0; i < sim1.BlobCount; i++)
        {
            Assert.Equal(sim1.Blobs[i].Position, sim2.Blobs[i].Position);
        }
    }
}
