using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the Blob class — now a thin Creature subclass that adds
/// pastel color and uses BlobWalkMode for wander behavior.
/// Physics and state machine tests live in BlobWalkModeTests.
/// </summary>
public class BlobTests
{
    [Fact]
    public void SetsColorOnConstruction()
    {
        var rng = new Random(42);
        var blob = new Blob(Vector3.Zero, 0.2f, 0.5f, 0.8f, rng);

        Assert.Equal(0.2f, blob.R);
        Assert.Equal(0.5f, blob.G);
        Assert.Equal(0.8f, blob.B);
    }

    [Fact]
    public void InheritsFromCreature()
    {
        var rng = new Random(42);
        var blob = new Blob(Vector3.Zero, 1f, 0f, 0f, rng);

        Assert.IsAssignableFrom<Creature>(blob);
    }

    [Fact]
    public void DefaultTraits_HasZeroGravityScale()
    {
        var traits = Blob.DefaultBlobTraits;
        Assert.Equal(0f, traits.GravityScale);
    }

    [Fact]
    public void DefaultTraits_HasDefaultRadius()
    {
        var traits = Blob.DefaultBlobTraits;
        Assert.Equal(0.5f, traits.Radius);
    }

    [Fact]
    public void StartsWithZeroVelocity()
    {
        var rng = new Random(42);
        var blob = new Blob(Vector3.Zero, 1f, 0f, 0f, rng);
        Assert.Equal(Vector3.Zero, blob.Velocity);
    }

    [Fact]
    public void UsesBlobWalkMode()
    {
        var rng = new Random(42);
        var blob = new Blob(Vector3.Zero, 1f, 0f, 0f, rng);
        Assert.IsType<BlobWalkMode>(blob.Movement);
    }

    [Fact]
    public void RandomPastelColor_ProducesValidRgb()
    {
        var rng = new Random(42);
        var (r, g, b) = Blob.RandomPastelColor(rng);

        Assert.InRange(r, 0f, 1f);
        Assert.InRange(g, 0f, 1f);
        Assert.InRange(b, 0f, 1f);
    }
}
