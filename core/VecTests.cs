using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the shared <see cref="Vec"/> helpers: horizontal (X/Z) distance and the
/// generic nearest-by scan that replaced the per-file duplicates.
/// </summary>
public class VecTests
{
    [Fact]
    public void HorizDist_IgnoresY()
    {
        var a = new Vector3(0f, 0f, 0f);
        var b = new Vector3(3f, 100f, 4f);  // Y differs wildly
        Assert.Equal(5f, Vec.HorizDist(a, b), 5);
    }

    private sealed record P(Vector3 Pos, bool Ok);

    [Fact]
    public void NearestBy_PicksClosestPassingFilter()
    {
        var items = new[]
        {
            new P(new Vector3(1f, 0f, 0f), false), // closer but filtered out
            new P(new Vector3(2f, 0f, 0f), true),
            new P(new Vector3(5f, 0f, 0f), true),
        };
        var (item, dist) = Vec.NearestBy(items, Vector3.Zero, p => p.Pos, p => p.Ok);
        Assert.Same(items[1], item);
        Assert.Equal(2f, dist, 5);
    }

    [Fact]
    public void NearestBy_RespectsMaxDist_ReturnsNullWhenNoneInRange()
    {
        var items = new[] { new P(new Vector3(10f, 0f, 0f), true) };
        var (item, dist) = Vec.NearestBy(items, Vector3.Zero, p => p.Pos, maxDist: 3f);
        Assert.Null(item);
        Assert.Equal(3f, dist, 5);
    }

    [Fact]
    public void NearestBy_EmptyInput_ReturnsNull()
    {
        var (item, _) = Vec.NearestBy(System.Array.Empty<P>(), Vector3.Zero, p => p.Pos);
        Assert.Null(item);
    }
}
