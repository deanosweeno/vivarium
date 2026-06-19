using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class BlobTests
{
    [Fact]
    public void NewBlobStartsIdle()
    {
        var rng = new Random(42);
        var blob = new Blob(Vector2.Zero, 1f, 0f, 0f, rng);

        Assert.Equal(WanderState.Idle, blob.State);
        Assert.True(blob.StateTimer > 0);
    }

    [Fact]
    public void BlobStartsWithZeroVelocity()
    {
        var rng = new Random(42);
        var blob = new Blob(Vector2.Zero, 1f, 0f, 0f, rng);

        Assert.Equal(Vector2.Zero, blob.Velocity);
    }

    [Fact]
    public void BlobMovesWhenSliding()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var rng = new Random(42);
        var blob = new Blob(new Vector2(2, 2), 1f, 0f, 0f, rng);

        // Force into Sliding with known velocity
        blob.State = WanderState.Sliding;
        blob.Velocity = new Vector2(0.5f, 0f);
        blob.StateTimer = 999; // won't expire during tick

        blob.Tick(0.5, arena, rng);

        // moved 0.5 units/sec * 0.5 sec = 0.25 in X
        Assert.Equal(2.25f, blob.Position.X, 3);
        Assert.Equal(2f, blob.Position.Y, 3);
        Assert.Equal(WanderState.Sliding, blob.State);
    }

    [Fact]
    public void BlobBecomesIdleAfterSlideTimerExpires()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var rng = new Random(42);
        var blob = new Blob(new Vector2(2, 2), 1f, 0f, 0f, rng);

        // Force into Sliding with timer about to expire
        blob.State = WanderState.Sliding;
        blob.Velocity = new Vector2(0.5f, 0f);
        blob.StateTimer = 0.01; // almost expired

        blob.Tick(0.1, arena, rng);

        Assert.Equal(WanderState.Idle, blob.State);
        Assert.Equal(Vector2.Zero, blob.Velocity);
        Assert.True(blob.StateTimer > 0); // new idle timer set
    }

    [Fact]
    public void BlobBecomesSlidingAfterIdleExpires()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var rng = new Random(42);
        var blob = new Blob(Vector2.Zero, 1f, 0f, 0f, rng);

        // Force into Idle with timer about to expire
        blob.State = WanderState.Idle;
        blob.Velocity = Vector2.Zero;
        blob.StateTimer = 0.01;

        blob.Tick(0.1, arena, rng);

        Assert.Equal(WanderState.Sliding, blob.State);
        Assert.NotEqual(Vector2.Zero, blob.Velocity);
        Assert.True(blob.StateTimer > 0);
    }

    [Fact]
    public void BlobClampedToArena()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var rng = new Random(42);

        // Place blob outside the arena bounds
        var blob = new Blob(new Vector2(20, 20), 1f, 0f, 0f, rng);
        blob.State = WanderState.Sliding;
        blob.Velocity = new Vector2(1f, 1f);
        blob.StateTimer = 999;

        blob.Tick(0.1, arena, rng);

        // Should be clamped to edge minus radius (5 - 0.5 = 4.5)
        Assert.True(blob.Position.X <= 4.5f);
        Assert.True(blob.Position.Y <= 4.5f);
        Assert.True(blob.Position.X >= -4.5f);
        Assert.True(blob.Position.Y >= -4.5f);
    }

    [Fact]
    public void BlobBouncesOffWall()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));
        var rng = new Random(42);

        // Place blob just inside right wall (radius margin), heading right
        var blob = new Blob(new Vector2(4.4f, 0), 1f, 0f, 0f, rng);
        blob.State = WanderState.Sliding;
        blob.Velocity = new Vector2(2f, 0f); // fast enough to hit wall in one tick
        blob.StateTimer = 999;

        blob.Tick(0.5, arena, rng);

        // Should have bounced: velocity reflected, position clamped to edge minus radius
        Assert.True(blob.Velocity.X < 0, "Velocity should reflect off right wall");
        Assert.True(blob.Position.X <= 4.5f);
    }

    [Fact]
    public void DeterministicWander_SameSeedProducesSamePath()
    {
        var arena = new Arena(Vector2.Zero, new Vector2(10, 10));

        var rng1 = new Random(123);
        var blob1 = new Blob(Vector2.Zero, 1f, 0f, 0f, rng1);

        var rng2 = new Random(123);
        var blob2 = new Blob(Vector2.Zero, 1f, 0f, 0f, rng2);

        // Tick both through many cycles
        for (int i = 0; i < 100; i++)
        {
            blob1.Tick(0.1, arena, rng1);
            blob2.Tick(0.1, arena, rng2);
        }

        Assert.Equal(blob1.Position, blob2.Position);
        Assert.Equal(blob1.State, blob2.State);
    }
}
