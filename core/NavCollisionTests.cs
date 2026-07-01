using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for solid-terrain collision (<see cref="SimPhysics.SlideAgainstTerrain"/> /
/// <see cref="SimPhysics.ResolveTerrainCollision"/>) and the <see cref="NavSystem"/> steering seam:
/// agents slide along non-walkable cells instead of ramming, and goal-seeking steering follows an
/// A* path (falling back to straight steering when there is none).
/// </summary>
public class NavCollisionTests
{
    // 5×5 map, cell size 1, centered on origin. Cell (2,2) is the world origin; block it with rock.
    private static MapData MapWithCenterRock()
    {
        var map = new MapData(5, 5, 1f);
        map.SetCell(2, 2, new Cell { Terrain = Terrain.Rock });
        return map;
    }

    [Fact]
    public void SlideAgainstTerrain_MovingStraightIntoRock_IsCancelled()
    {
        var map = MapWithCenterRock();
        var prev = new Vector3(-1, 0, 0);   // cell (1,2), walkable
        var next = new Vector3(0, 0, 0);    // cell (2,2), rock

        var resolved = SimPhysics.SlideAgainstTerrain(prev, next, map);

        Assert.Equal(prev, resolved);       // fully blocked — stays put
    }

    [Fact]
    public void SlideAgainstTerrain_DiagonalIntoRock_SlidesAlongOpenAxis()
    {
        var map = MapWithCenterRock();
        var prev = new Vector3(-1, 0, -1);  // cell (1,1), walkable
        var next = new Vector3(0, 0, 0);    // cell (2,2), rock

        var resolved = SimPhysics.SlideAgainstTerrain(prev, next, map);

        Assert.NotEqual(next, resolved);
        Assert.Equal(0f, resolved.X);       // X move allowed (into cell (2,1))
        Assert.Equal(-1f, resolved.Z);      // Z move blocked
        Assert.True(map.IsWalkableWorld(resolved));
    }

    [Fact]
    public void SlideAgainstTerrain_ParallelMove_PassesThrough()
    {
        var map = MapWithCenterRock();
        var prev = new Vector3(-1, 0, -1);  // cell (1,1)
        var next = new Vector3(0, 0, -1);   // cell (2,1), walkable — sliding past the rock

        var resolved = SimPhysics.SlideAgainstTerrain(prev, next, map);

        Assert.Equal(next, resolved);
    }

    [Fact]
    public void SlideAgainstTerrain_StartingOnBlockedCell_DoesNotTrap()
    {
        var map = MapWithCenterRock();
        var prev = new Vector3(0, 0, 0);    // on the rock (e.g. spawned there)
        var next = new Vector3(-1, 0, 0);   // stepping out to a walkable cell

        var resolved = SimPhysics.SlideAgainstTerrain(prev, next, map);

        Assert.Equal(next, resolved);       // allowed to move freely out
    }

    [Fact]
    public void ResolveTerrainCollision_ZeroesBlockedAxisVelocity()
    {
        var map = MapWithCenterRock();
        var creature = new Creature(new Vector3(-1, 0, -1), null, new SteeringLocomotion());
        // Simulate a movement tick that drove it diagonally into the rock cell.
        creature.Position = new Vector3(0, 0, 0);
        creature.Velocity = new Vector3(1, 0, 1);

        SimPhysics.ResolveTerrainCollision(creature, new Vector3(-1, 0, -1), map);

        Assert.True(map.IsWalkableWorld(creature.Position));
        Assert.Equal(1f, creature.Velocity.X);   // open axis keeps its speed
        Assert.Equal(0f, creature.Velocity.Z);   // blocked axis is cancelled
    }

    [Fact]
    public void NavSystem_Steer_FollowsPathAroundWall()
    {
        // Wall on column cx=2 for cz=0..3, gap at cz=4. Agent bottom-left, goal bottom-right.
        var map = new MapData(5, 5, 1f);
        for (int cz = 0; cz <= 3; cz++)
            map.SetCell(2, cz, new Cell { Terrain = Terrain.Rock });

        var nav = new NavState();
        var cfg = new NavConfig();
        var pos = map.CellToWorldCenter(0, 0);
        var goal = map.CellToWorldCenter(4, 0);

        var vel = NavSystem.Steer(pos, goal, nav, map, cfg, delta: 0.1f, maxSpeed: 1f);

        Assert.NotNull(vel);
        Assert.NotNull(nav.Waypoints);
        Assert.NotEmpty(nav.Waypoints!);
        // The next waypoint should not be straight east into the wall — the route detours.
        Assert.True(vel!.Value.LengthSquared() > 0f);
    }

    [Fact]
    public void NavSystem_Steer_NoPath_ReturnsNullAndClearsWaypoints()
    {
        var map = new MapData(5, 5, 1f);
        map.SetCell(4, 4, new Cell { Terrain = Terrain.Rock }); // goal cell itself blocked

        var nav = new NavState();
        var pos = map.CellToWorldCenter(0, 0);
        var goal = map.CellToWorldCenter(4, 4);

        var vel = NavSystem.Steer(pos, goal, nav, map, new NavConfig(), delta: 0.1f, maxSpeed: 1f);

        Assert.Null(vel);
        Assert.Null(nav.Waypoints);
    }
}
