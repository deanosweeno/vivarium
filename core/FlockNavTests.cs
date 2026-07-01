using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// The flock anchor is the herd's nav agent: when grazing toward a patch with an obstacle in the
/// way it routes around it, and it never settles on a non-walkable cell (which would drag the whole
/// circle into a rock/lake). Members follow the anchor reactively — no per-member pathing here.
/// </summary>
public class FlockNavTests
{
    private static readonly Drives Kin = new()
    {
        Curiosity = 0.5f, Fear = 0.15f, Sociability = 0.9f,
        Appetite = 0.5f, Aggression = 0.1f, PlayCuddle = 0.3f,
    };

    private static float HorizDist(Vector3 a, Vector3 b)
    {
        float dx = a.X - b.X, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void GrazingAnchor_RoutesAroundRock_AndNeverRestsOnBlockedCell()
    {
        var sim = new Simulator(Arena.GroundArena(64, 64), seed: 1)
        {
            Map = new MapData(64, 64, 1f), // all grass ⇒ walkable, centered on origin
        };

        // A 3×3 rock cluster around the world origin, between the herd (west) and the food (east).
        for (int cz = 31; cz <= 33; cz++)
        for (int cx = 31; cx <= 33; cx++)
            sim.Map!.SetCell(cx, cz, new Cell { Terrain = Terrain.Rock });

        sim.SpawnBlob(new Vector3(-5, 0, 0), Blob.DefaultBlobTraits, Kin);
        sim.SpawnBlob(new Vector3(-4, 0, 0), Blob.DefaultBlobTraits, Kin);
        sim.SpawnBlob(new Vector3(-5, 0, 1), Blob.DefaultBlobTraits, Kin);
        sim.Tick(0.1); // form the flock

        var flock = Assert.Single(sim.Flocks);

        // Collectively hungry, with food just east of the rock cluster (within graze range).
        foreach (var m in flock.Members) m.Needs.Hunger = 0.9f;
        var foodPos = new Vector3(4, 0, 0);
        sim.Food.Add(new FoodItem { Position = foodPos, Def = FoodDef.Neutral("berries") });

        float startDist = HorizDist(flock.Anchor, foodPos);

        for (int i = 0; i < 400; i++)
        {
            sim.Tick(0.1);
            Assert.True(sim.Map!.IsWalkableWorld(flock.Anchor),
                $"anchor rested on a non-walkable cell at {flock.Anchor}");
        }

        Assert.Equal(FlockAction.Graze, flock.Current);
        Assert.True(HorizDist(flock.Anchor, foodPos) < startDist,
            "the grazing anchor should have made progress toward the food around the rock");
    }
}
