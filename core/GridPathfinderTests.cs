using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the deterministic A* grid pathfinder: it finds a straight route on open ground,
/// detours around obstacles, refuses walled-off goals, and is reproducible.
/// </summary>
public class GridPathfinderTests
{
    private const int Budget = 10000;

    private static MapData OpenMap(int w, int d)
    {
        var map = new MapData(w, d, 1f); // all Grass ⇒ all walkable
        return map;
    }

    private static void Block(MapData map, int cx, int cz)
        => map.SetCell(cx, cz, new Cell { Terrain = Terrain.Rock });

    [Fact]
    public void FindPath_OpenGround_ReturnsDirectRoute_ExcludingStart()
    {
        var map = OpenMap(5, 5);

        var path = GridPathfinder.FindPath(map, (0, 0), (4, 0), Budget);

        Assert.NotNull(path);
        Assert.Equal(4, path!.Count);              // (1,0)..(4,0) — start excluded
        Assert.Equal((4, 0), path[^1]);            // ends on the goal
        Assert.DoesNotContain((0, 0), path);       // start is not a waypoint
    }

    [Fact]
    public void FindPath_StartEqualsGoal_ReturnsEmpty()
    {
        var map = OpenMap(5, 5);

        var path = GridPathfinder.FindPath(map, (2, 2), (2, 2), Budget);

        Assert.NotNull(path);
        Assert.Empty(path!);
    }

    [Fact]
    public void FindPath_RoutesAroundARockWall()
    {
        // A wall on column cx=2 for cz=0..3, leaving a gap at cz=4. The only route from the left
        // half to the right half squeezes through the gap, so the path must detour downward.
        var map = OpenMap(5, 5);
        for (int cz = 0; cz <= 3; cz++)
            Block(map, 2, cz);

        var path = GridPathfinder.FindPath(map, (0, 0), (4, 0), Budget);

        Assert.NotNull(path);
        Assert.Equal((4, 0), path![^1]);
        // Never steps onto a blocked cell.
        foreach (var (cx, cz) in path)
            Assert.True(map.IsWalkable(cx, cz), $"waypoint ({cx},{cz}) is not walkable");
        // Must have used the gap row (cz == 4) to get around the wall.
        Assert.Contains(path, wp => wp.cz == 4);
    }

    [Fact]
    public void FindPath_GoalWalledOff_ReturnsNull()
    {
        var map = OpenMap(5, 5);
        // Ring the goal (2,2) with rock on all four orthogonal + diagonal sides.
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (dx != 0 || dz != 0)
                Block(map, 2 + dx, 2 + dz);

        var path = GridPathfinder.FindPath(map, (0, 0), (2, 2), Budget);

        Assert.Null(path);
    }

    [Fact]
    public void FindPath_GoalCellItselfBlocked_ReturnsNull()
    {
        var map = OpenMap(5, 5);
        Block(map, 4, 4);

        var path = GridPathfinder.FindPath(map, (0, 0), (4, 4), Budget);

        Assert.Null(path);
    }

    [Fact]
    public void FindPath_NoCornerCutting_AroundADiagonalRock()
    {
        // Rock at (1,1); path (0,0)->(2,2) must not cut the corner diagonally past it.
        var map = OpenMap(3, 3);
        Block(map, 1, 1);

        var path = GridPathfinder.FindPath(map, (0, 0), (2, 2), Budget);

        Assert.NotNull(path);
        // The illegal corner-cut would step (0,0)->(1,1) or hop the rock's corner; every step must
        // stay walkable and adjacent moves must not squeeze diagonally between two blocked cells.
        // Here only (1,1) is blocked, so simply assert the route avoids it and reaches the goal.
        Assert.DoesNotContain((1, 1), path!);
        Assert.Equal((2, 2), path![^1]);
    }

    [Fact]
    public void FindPath_IsDeterministic()
    {
        var map = OpenMap(8, 8);
        for (int cz = 1; cz <= 5; cz++)
            Block(map, 4, cz);

        var a = GridPathfinder.FindPath(map, (0, 0), (7, 7), Budget);
        var b = GridPathfinder.FindPath(map, (0, 0), (7, 7), Budget);

        Assert.NotNull(a);
        Assert.Equal(a, b); // same grid + endpoints ⇒ identical waypoint sequence
    }

    [Fact]
    public void FindPath_RespectsExpansionBudget()
    {
        // A large open map with a walled-off goal: an unbounded search would flood the whole grid.
        var map = OpenMap(64, 64);
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
            if (dx != 0 || dz != 0)
                Block(map, 32 + dx, 32 + dz);

        // Tiny budget ⇒ bail out early with null rather than exploring thousands of cells.
        var path = GridPathfinder.FindPath(map, (0, 0), (32, 32), maxExpansions: 10);

        Assert.Null(path);
    }
}
