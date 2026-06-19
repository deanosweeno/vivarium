using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class ArenaTests
{
    [Fact]
    public void GroundArena_HasCorrectBounds()
    {
        var arena = Arena.GroundArena(10, 20);

        Assert.Equal(-5f, arena.MinX);
        Assert.Equal(5f, arena.MaxX);
        Assert.Equal(0f, arena.MinY);
        Assert.Equal(float.MaxValue, arena.MaxY);
        Assert.Equal(-10f, arena.MinZ);
        Assert.Equal(10f, arena.MaxZ);
    }

    [Fact]
    public void Constructor_YieldsCorrectBounds()
    {
        var arena = new Arena(new Vector3(5, 2, 10), new Vector3(10, 4, 20));

        Assert.Equal(0f, arena.MinX);
        Assert.Equal(10f, arena.MaxX);
        Assert.Equal(0f, arena.MinY);
        Assert.Equal(4f, arena.MaxY);
        Assert.Equal(0f, arena.MinZ);
        Assert.Equal(20f, arena.MaxZ);
    }

    [Fact]
    public void Contains_PointWithinBounds()
    {
        var arena = Arena.GroundArena(10, 10);
        Assert.True(arena.Contains(new Vector3(0, 1, 0)));
    }

    [Fact]
    public void Contains_SphereAtFloor_WithRadius()
    {
        var arena = new Arena(new Vector3(0, 2, 0), new Vector3(10, 4, 10)); // MinY=0, MaxY=4

        // Sphere radius 0.5, center at Y=0.5 — bottom edge at Y=0 (exactly on floor)
        Assert.True(arena.Contains(new Vector3(0, 0.5f, 0), 0.5f));

        // Sphere radius 0.5, center at Y=0.4 — bottom edge at Y=-0.1 (below floor)
        Assert.False(arena.Contains(new Vector3(0, 0.4f, 0), 0.5f));
    }

    [Fact]
    public void Contains_SphereAtCeiling_WithRadius()
    {
        var arena = new Arena(new Vector3(0, 2, 0), new Vector3(10, 4, 10)); // MinY=0, MaxY=4

        // Sphere radius 0.5, center at Y=3.5 — top edge at Y=4 (exactly at ceiling)
        Assert.True(arena.Contains(new Vector3(0, 3.5f, 0), 0.5f));

        // Sphere radius 0.5, center at Y=3.6 — top edge at Y=4.1 (above ceiling)
        Assert.False(arena.Contains(new Vector3(0, 3.6f, 0), 0.5f));
    }

    [Fact]
    public void Contains_XZ_Wall_WithRadius()
    {
        var arena = Arena.GroundArena(10, 10);

        Assert.True(arena.Contains(new Vector3(4.5f, 1, 0), 0.5f));
        Assert.False(arena.Contains(new Vector3(4.6f, 1, 0), 0.5f));
    }

    [Fact]
    public void Clamp_ToFloor()
    {
        var arena = Arena.GroundArena(10, 10);

        var result = arena.Clamp(new Vector3(1, -2, 3), 0.5f);

        Assert.Equal(1f, result.X);
        Assert.Equal(0.5f, result.Y); // MinY(0) + radius(0.5)
        Assert.Equal(3f, result.Z);
    }

    [Fact]
    public void Clamp_ToSides()
    {
        var arena = Arena.GroundArena(10, 10);

        var result = arena.Clamp(new Vector3(10, 3, -10), 0.5f);

        Assert.Equal(4.5f, result.X);  // MaxX(5) - radius(0.5)
        Assert.Equal(3f, result.Y);
        Assert.Equal(-4.5f, result.Z); // MinZ(-5) + radius(0.5)
    }

    [Fact]
    public void Reflect_FloorBounce()
    {
        var arena = Arena.GroundArena(10, 10);
        // Position at floor, velocity moving down
        var (pos, vel) = arena.Reflect(
            new Vector3(0, 0.3f, 0),
            new Vector3(1, -5, 2),
            0.5f);

        Assert.Equal(0.5f, pos.Y, 3); // clamped to floor + radius
        Assert.Equal(5f, vel.Y);      // Y velocity reflected
        Assert.Equal(1f, vel.X);      // X/Z unchanged
        Assert.Equal(2f, vel.Z);
    }

    [Fact]
    public void Reflect_XZ_WallBounce()
    {
        var arena = Arena.GroundArena(10, 10);

        var (pos, vel) = arena.Reflect(
            new Vector3(4.6f, 1, 0),
            new Vector3(2, 0, 0),
            0.5f);

        Assert.True(pos.X <= 4.5f);   // clamped to MaxX - radius
        Assert.True(vel.X < 0);       // reflected
    }

    [Fact]
    public void Reflect_NoBounceWhenInside()
    {
        var arena = Arena.GroundArena(10, 10);

        var (pos, vel) = arena.Reflect(
            new Vector3(1, 2, 1),
            new Vector3(1, 0, 1),
            0.5f);

        Assert.Equal(new Vector3(1, 2, 1), pos);
        Assert.Equal(new Vector3(1, 0, 1), vel); // velocity unchanged
    }

    [Fact]
    public void Deterministic_SameArenaSameResult()
    {
        var arena1 = Arena.GroundArena(10, 10);
        var arena2 = Arena.GroundArena(10, 10);

        var pos = new Vector3(4.6f, -1, 3);
        var vel = new Vector3(3, -5, 1);

        var (pos1, vel1) = arena1.Reflect(pos, vel, 0.5f);
        var (pos2, vel2) = arena2.Reflect(pos, vel, 0.5f);

        Assert.Equal(pos1, pos2);
        Assert.Equal(vel1, vel2);
    }
}
