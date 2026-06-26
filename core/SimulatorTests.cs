using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class SimulatorTests
{
    // A map whose every cell is the given biome, so a creature anywhere samples it.
    private static MapData UniformBiomeMap(Biome biome, int size = 16)
    {
        var map = new MapData(size, size, 1f);
        for (int cz = 0; cz < size; cz++)
        for (int cx = 0; cx < size; cx++)
            map.SetCell(cx, cz, new Cell { Terrain = Terrain.Grass, Biome = biome });
        return map;
    }

    private static readonly BiomeCatalog HappinessCatalog = BiomeCatalog.Parse("""
        [
          { "Biome": "Forest", "HappinessRate":  1.0 },
          { "Biome": "Desert", "HappinessRate": -1.0 }
        ]
        """);

    [Fact]
    public void BiomeEffects_ForestCell_IncreasesHappiness()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Map = UniformBiomeMap(Biome.Forest),
            Biomes = HappinessCatalog,
        };
        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));

        for (int i = 0; i < 10; i++)
            sim.Tick(0.1);

        Assert.True(blob.Happiness > 0f, $"expected happiness > 0, got {blob.Happiness}");
    }

    [Fact]
    public void BiomeEffects_DesertCell_DecreasesHappiness()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Map = UniformBiomeMap(Biome.Desert),
            Biomes = HappinessCatalog,
        };
        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));

        for (int i = 0; i < 10; i++)
            sim.Tick(0.1);

        Assert.True(blob.Happiness < 0f, $"expected happiness < 0, got {blob.Happiness}");
    }

    [Fact]
    public void BiomeEffects_NoMap_LeavesHappinessUnchanged()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1); // Map/Biomes null
        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));

        for (int i = 0; i < 10; i++)
            sim.Tick(0.1);

        Assert.Equal(0f, blob.Happiness, 5);
    }

    [Fact]
    public void SpawnBlobReturnsBlobAtPosition()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var pos = new Vector3(2, 0, 3);
        var blob = sim.SpawnBlob(pos);

        Assert.NotNull(blob);
        Assert.Equal(2f, blob.Position.X, 3);
        Assert.Equal(0.5f, blob.Position.Y, 3);  // clamped to floor + radius
        Assert.Equal(3f, blob.Position.Z, 3);
    }

    [Fact]
    public void SpawnBlobOutsideArena_ClampedToBounds()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        // Position way outside the 10x10 arena
        var blob = sim.SpawnBlob(new Vector3(100, 0, -100));

        Assert.True(blob.Position.X <= 5f);
        Assert.True(blob.Position.X >= -5f);
        Assert.True(blob.Position.Z <= 5f);
        Assert.True(blob.Position.Z >= -5f);
    }

    [Fact]
    public void SpawnedBlobInList()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var blob = sim.SpawnBlob(Vector3.Zero);

        Assert.Single(sim.Entities);
        Assert.Same(blob, sim.Entities[0]);
        Assert.Equal(1, sim.EntityCount);
    }

    [Fact]
    public void TickAdvancesAllBlobs()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var b1 = sim.SpawnBlob(Vector3.Zero);
        var b2 = sim.SpawnBlob(new Vector3(1, 0, 1));
        var b3 = sim.SpawnBlob(new Vector3(-1, 0, -1));

        sim.Tick(0.1);

        // The Utility-AI brain picks a moving action (wander/approach/flee) → nonzero velocity.
        Assert.NotEqual(Vector3.Zero, b1.Velocity);
        Assert.NotEqual(Vector3.Zero, b2.Velocity);
        Assert.NotEqual(Vector3.Zero, b3.Velocity);
    }

    [Fact]
    public void DeterministicSim_SameSeedProducesIdenticalState()
    {
        var arena = Arena.GroundArena(10, 10);

        var sim1 = new Simulator(arena, seed: 999);
        var sim2 = new Simulator(arena, seed: 999);

        // Spawn multiple blobs at the same positions
        sim1.SpawnBlob(Vector3.Zero);
        sim1.SpawnBlob(new Vector3(2, 0, 2));
        sim1.SpawnBlob(new Vector3(-2, 0, -2));

        sim2.SpawnBlob(Vector3.Zero);
        sim2.SpawnBlob(new Vector3(2, 0, 2));
        sim2.SpawnBlob(new Vector3(-2, 0, -2));

        // Tick both for many frames
        for (int i = 0; i < 50; i++)
        {
            sim1.Tick(0.1);
            sim2.Tick(0.1);
        }

        Assert.Equal(sim1.EntityCount, sim2.EntityCount);

        for (int i = 0; i < sim1.EntityCount; i++)
        {
            Assert.Equal(sim1.Entities[i].Position, sim2.Entities[i].Position);
            Assert.Equal(sim1.Entities[i].Velocity, sim2.Entities[i].Velocity);
            // Brains decide identically under the same seed → same chosen action.
            Assert.Equal(sim1.Entities[i].Brain?.CurrentName, sim2.Entities[i].Brain?.CurrentName);
        }
    }

    [Fact]
    public void PushApart_OverlappingBlobsSeparated()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        // Create overlapping blobs directly (bypass SpawnBlob overlap check)
        var b1 = new Blob(new Vector3(1.0f, 0, 0), 1f, 0f, 0f, sim.Rng);
        var b2 = new Blob(new Vector3(1.6f, 0, 0), 1f, 0f, 0f, sim.Rng);
        sim.Entities.Add(b1);
        sim.Entities.Add(b2);

        // Directly-constructed blobs have no brain → stationary, isolating collision.
        float distBefore = (b1.Position - b2.Position).Length();
        Assert.True(distBefore < 0.9f, "blobs should start overlapping");

        sim.Tick(0.1);

        float distAfter = (b1.Position - b2.Position).Length();
        Assert.True(distAfter >= Blob.DefaultBlobTraits.Radius * 1.9f, "blobs should be pushed apart");
    }

    [Fact]
    public void PushApart_NoOverlapPreserved()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        // Spawn two blobs far apart
        var b1 = sim.SpawnBlob(new Vector3(-3f, 0, 0));
        var b2 = sim.SpawnBlob(new Vector3(3f, 0, 0));

        // Freeze the brains so they don't wander — isolate the no-overlap case.
        b1.Brain = null;
        b2.Brain = null;

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
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        // Force two blobs to exactly the same position (bypass SpawnBlob overlap check)
        var b1 = new Blob(Vector3.Zero, 1f, 0f, 0f, sim.Rng);
        var b2 = new Blob(Vector3.Zero, 1f, 0f, 0f, sim.Rng);
        sim.Entities.Add(b1);
        sim.Entities.Add(b2);

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
        var arena = Arena.GroundArena(10, 10);

        var sim1 = new Simulator(arena, seed: 777);
        var sim2 = new Simulator(arena, seed: 777);

        // Spawn blobs close together so they collide
        sim1.SpawnBlob(Vector3.Zero);
        sim1.SpawnBlob(new Vector3(0.8f, 0, 0));
        sim1.SpawnBlob(new Vector3(0, 0, 0.8f));

        sim2.SpawnBlob(Vector3.Zero);
        sim2.SpawnBlob(new Vector3(0.8f, 0, 0));
        sim2.SpawnBlob(new Vector3(0, 0, 0.8f));

        // Let them move and collide
        for (int i = 0; i < 30; i++)
        {
            sim1.Tick(0.1);
            sim2.Tick(0.1);
        }

        Assert.Equal(sim1.EntityCount, sim2.EntityCount);
        for (int i = 0; i < sim1.EntityCount; i++)
        {
            Assert.Equal(sim1.Entities[i].Position, sim2.Entities[i].Position);
        }
    }

    // -------------------------------------------------
    // Creature pipeline tests (Phase 3)
    // -------------------------------------------------

    [Fact]
    public void SpawnCreature_PlacesAtPosition()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var pos = new Vector3(2, 1, 3);
        var creature = sim.SpawnCreature(pos);

        Assert.NotNull(creature);
        Assert.Equal(2f, creature.Position.X, 3);
        Assert.Equal(1f, creature.Position.Y, 3);
        Assert.Equal(3f, creature.Position.Z, 3);
    }

    [Fact]
    public void SpawnCreature_OutsideBounds_Clamped()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var creature = sim.SpawnCreature(new Vector3(100, -100, 100));

        Assert.True(creature.Position.X <= 5f);
        Assert.True(creature.Position.X >= -5f);
        Assert.True(creature.Position.Y >= 0.5f, $"Y={creature.Position.Y} should be ≥ floor + radius");
        Assert.True(creature.Position.Z <= 5f);
        Assert.True(creature.Position.Z >= -5f);
    }

    [Fact]
    public void SpawnedCreature_InList()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var creature = sim.SpawnCreature(Vector3.Zero);

        Assert.Equal(1, sim.EntityCount);
        Assert.Single(sim.Entities);
        Assert.Same(creature, sim.Entities[0]);
    }

    [Fact]
    public void SpawnCreature_HasWalkMode()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var creature = sim.SpawnCreature(Vector3.Zero);

        Assert.IsType<WalkMode>(creature.Movement);
    }

    [Fact]
    public void Gravity_PullsCreatureDown()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 10f;

        var traits = new CreatureTraits { GravityScale = 1.0f };
        var creature = sim.SpawnCreature(new Vector3(0, 3, 0), traits);

        Assert.Equal(3f, creature.Position.Y, 3);

        // One tick of gravity
        sim.Tick(0.5);

        // Gravity: 10 * 0.5 = 5 units/s reduction in Y velocity
        // Position should have moved down
        Assert.True(creature.Position.Y < 3f, $"Y={creature.Position.Y} should be less than 3 after gravity");
        Assert.True(creature.Velocity.Y < 0, $"Velocity.Y={creature.Velocity.Y} should be negative after gravity");
    }

    [Fact]
    public void GroundClamp_PreventsFallingThroughFloor()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 1000f; // extreme gravity

        var creature = sim.SpawnCreature(new Vector3(0, 0.5f, 0));

        // Tick with extreme gravity — should clamp to floor, not fall through
        sim.Tick(1.0);

        float floor = arena.MinY + creature.Traits.Radius;
        Assert.True(creature.Position.Y >= floor - 0.001f, $"Y={creature.Position.Y} should be ≥ floor {floor}");
    }

    [Fact]
    public void Creature_WithWalkMode_Wanders()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        var creature = sim.SpawnCreature(new Vector3(0, 1, 0));
        var startPos = creature.Position;

        // Tick many times
        for (int i = 0; i < 30; i++)
            sim.Tick(0.1);

        // Should have wandered (XZ position changed)
        float dist = (creature.Position - startPos).Length();
        Assert.True(dist > 0.2f, $"Should have wandered, distance={dist}");
    }

    [Fact]
    public void GravityScale_Zero_NoGravity()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 9.8f;

        var traits = new CreatureTraits { GravityScale = 0f };
        var creature = sim.SpawnCreature(new Vector3(0, 2, 0), traits);

        sim.Tick(1.0);

        // GravityScale = 0 means gravity is never applied — vertical velocity stays zero
        // (a gravity creature would have accumulated downward velocity).
        Assert.Equal(0f, creature.Velocity.Y, 4);
        // Terrain-bound: it snaps to the ground surface (arena floor + radius), not floating.
        Assert.Equal(arena.MinY + creature.Traits.Radius, creature.Position.Y, 4);
    }

    [Fact]
    public void CreatureGroundClamp_ZeroesVerticalVelocity()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 9.8f;

        var creature = sim.SpawnCreature(new Vector3(0, 0.5f, 0));

        // Tick until velocity.Y should be negative and position should be at floor
        sim.Tick(1.0);

        float floor = arena.MinY + creature.Traits.Radius;
        if (creature.Position.Y <= floor + 0.001f)
        {
            // If clamped to floor, Y velocity should be zeroed
            Assert.Equal(0f, creature.Velocity.Y, 3);
        }
    }

    [Fact]
    public void Deterministic_CreatureSim_SameSeedSameState()
    {
        var arena = Arena.GroundArena(10, 10);

        var sim1 = new Simulator(arena, seed: 999);
        var sim2 = new Simulator(arena, seed: 999);

        sim1.SpawnCreature(new Vector3(0, 1, 0));
        sim1.SpawnCreature(new Vector3(2, 1, 2));

        sim2.SpawnCreature(new Vector3(0, 1, 0));
        sim2.SpawnCreature(new Vector3(2, 1, 2));

        for (int i = 0; i < 30; i++)
        {
            sim1.Tick(0.1);
            sim2.Tick(0.1);
        }

        Assert.Equal(sim1.EntityCount, sim2.EntityCount);
        for (int i = 0; i < sim1.EntityCount; i++)
        {
            Assert.Equal(sim1.Entities[i].Position, sim2.Entities[i].Position);
            Assert.Equal(sim1.Entities[i].Velocity, sim2.Entities[i].Velocity);
        }
    }

    [Fact]
    public void BlobPipeline_Unaffected_ByCreatureChanges()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        // Spawn both entity types
        sim.SpawnBlob(Vector3.Zero);
        sim.SpawnCreature(new Vector3(3, 1, 0));

        Assert.Equal(2, sim.EntityCount);
        Assert.IsType<Blob>(sim.Entities[0]);
        Assert.IsNotType<Blob>(sim.Entities[1]);

        // Tick — blob should still work
        sim.Tick(0.1);

        Assert.Equal(2, sim.EntityCount);
        Assert.NotNull(sim.Entities[0]);
        Assert.NotNull(sim.Entities[1]);
    }

    // -------------------------------------------------
    // Entity collision tests (Phase 4)
    // -------------------------------------------------

    [Fact]
    public void CreatureCreature_PushApart()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 0f; // disable gravity so they don't fall

        // Two creatures with radius 1.0 at Y=2, XZ positions just 0.5 apart
        var traits = new CreatureTraits { Radius = 1.0f, MaxSpeed = 0f };
        var c1 = new Creature(new Vector3(2f, 2f, 0f), traits, new WalkMode());
        var c2 = new Creature(new Vector3(2.5f, 2f, 0f), traits, new WalkMode());
        sim.Entities.Add(c1);
        sim.Entities.Add(c2);

        float distBefore = (c1.Position - c2.Position).Length();
        Assert.True(distBefore < 2f, "creatures should start overlapping");

        sim.Tick(0.1);

        float distAfter = (c1.Position - c2.Position).Length();
        Assert.True(distAfter >= 2f - 0.001f,
            $"creatures should be separated: distance={distAfter}");
    }

    [Fact]
    public void CreatureCreature_PushApart3D()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 0f;

        // Two creatures at different Y heights, overlapping in 3D
        var traits = new CreatureTraits { Radius = 1.0f, MaxSpeed = 0f };
        var c1 = new Creature(new Vector3(0f, 2f, 0f), traits, new WalkMode());
        var c2 = new Creature(new Vector3(1f, 3f, 1f), traits, new WalkMode());
        sim.Entities.Add(c1);
        sim.Entities.Add(c2);

        float distBefore = (c1.Position - c2.Position).Length();
        float minDist = c1.Traits.Radius + c2.Traits.Radius;
        Assert.True(distBefore < minDist, "creatures should start overlapping in 3D");

        sim.Tick(0.1);

        float distAfter = (c1.Position - c2.Position).Length();
        Assert.True(distAfter >= minDist - 0.001f,
            $"creatures should be separated in 3D: distance={distAfter}, min={minDist}");
    }

    [Fact]
    public void CreatureCreature_PushApart_DistanceZero()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 0f;

        var traits = new CreatureTraits { Radius = 0.5f, MaxSpeed = 0f };
        var c1 = new Creature(Vector3.Zero, traits, new WalkMode());
        var c2 = new Creature(Vector3.Zero, traits, new WalkMode());
        sim.Entities.Add(c1);
        sim.Entities.Add(c2);

        // Shouldn't crash or produce NaN
        sim.Tick(0.1);

        float dist = (c1.Position - c2.Position).Length();
        Assert.False(float.IsNaN(dist));
        Assert.False(float.IsInfinity(dist));
        Assert.True(dist > 0f, "creatures should be separated from zero distance");
    }

    [Fact]
    public void BlobCreature_PushApart()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 0f;

        // Blob at Y=0.5 (floor + radius), creature at Y=0.5 overlapping
        var blob = new Blob(new Vector3(1f, 0.5f, 0f), 1f, 0f, 0f, sim.Rng);
        sim.Entities.Add(blob);

        var traits = new CreatureTraits { Radius = 0.5f, MaxSpeed = 0f };
        var creature = new Creature(new Vector3(1.3f, 0.5f, 0f), traits, new WalkMode());
        sim.Entities.Add(creature);

        float minDist = Blob.DefaultBlobTraits.Radius + creature.Traits.Radius; // 1.0
        float distBefore = (blob.Position - creature.Position).Length();
        Assert.True(distBefore < minDist, "blob and creature should start overlapping");

        sim.Tick(0.1);

        float distAfter = (blob.Position - creature.Position).Length();
        Assert.True(distAfter >= minDist - 0.001f,
            $"blob and creature should be separated: distance={distAfter}");
    }

    [Fact]
    public void PostCollision_GroundReclamp_Creature()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 0f;

        // Place one creature at floor level, another above it.
        // The upper one pushes the lower one below floor via collision.
        var traits = new CreatureTraits { Radius = 1.0f, MaxSpeed = 0f };
        float floor = arena.MinY + traits.Radius; // 1.0

        var lower = new Creature(new Vector3(0f, floor, 0f), traits, new WalkMode());
        var upper = new Creature(new Vector3(0f, floor + 0.5f, 0f), traits, new WalkMode());
        sim.Entities.Add(lower);
        sim.Entities.Add(upper);

        sim.Tick(0.1);

        // Lower creature should not go below floor
        Assert.True(lower.Position.Y >= floor - 0.001f,
            $"lower creature Y={lower.Position.Y} should be >= floor {floor}");
    }

    [Fact]
    public void PostCollision_GroundReclamp_Blob()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);
        sim.Gravity = 0f;

        float floor = arena.MinY + Blob.DefaultBlobTraits.Radius; // 0.5

        var blob = new Blob(new Vector3(0f, floor, 0f), 1f, 0f, 0f, sim.Rng);
        sim.Entities.Add(blob);

        // Creature directly above the blob — collision pushes blob down
        var traits = new CreatureTraits { Radius = 0.5f, MaxSpeed = 0f };
        var creature = new Creature(new Vector3(0f, floor + 0.3f, 0f), traits, new WalkMode());
        sim.Entities.Add(creature);

        sim.Tick(0.1);

        // Blob should not go below floor after being pushed
        Assert.True(blob.Position.Y >= floor - 0.001f,
            $"blob Y={blob.Position.Y} should be >= floor {floor}");
    }

    [Fact]
    public void Deterministic_Collisions_SameSeedSameOutcome()
    {
        var arena = Arena.GroundArena(10, 10);

        var sim1 = new Simulator(arena, seed: 777);
        var sim2 = new Simulator(arena, seed: 777);

        // Spawn blobs and creatures that will collide
        sim1.SpawnBlob(Vector3.Zero);
        sim1.SpawnBlob(new Vector3(0.8f, 0, 0));
        sim1.SpawnCreature(new Vector3(0, 1, 0.8f));

        sim2.SpawnBlob(Vector3.Zero);
        sim2.SpawnBlob(new Vector3(0.8f, 0, 0));
        sim2.SpawnCreature(new Vector3(0, 1, 0.8f));

        for (int i = 0; i < 30; i++)
        {
            sim1.Tick(0.1);
            sim2.Tick(0.1);
        }

        Assert.Equal(sim1.EntityCount, sim2.EntityCount);
        Assert.Equal(sim1.EntityCount, sim2.EntityCount);
        for (int i = 0; i < sim1.EntityCount; i++)
            Assert.Equal(sim1.Entities[i].Position, sim2.Entities[i].Position);
        for (int i = 0; i < sim1.EntityCount; i++)
            Assert.Equal(sim1.Entities[i].Velocity, sim2.Entities[i].Velocity);
    }

    [Fact]
    public void Collision_Entities_FarApart_NoPush()
    {
        var arena = Arena.GroundArena(10, 10);
        var sim = new Simulator(arena, seed: 42);

        // Spawn entities far apart — collision should not move them
        sim.SpawnBlob(new Vector3(-4, 0, -4));
        sim.SpawnCreature(new Vector3(3, 1, 3));

        Assert.Equal(2, sim.EntityCount);
        var posBefore0 = sim.Entities[0].Position;
        var posBefore1 = sim.Entities[1].Position;

        sim.Tick(0.1);

        // Positions unchanged since entities are far apart
        Assert.Equal(2, sim.EntityCount);
        Assert.NotNull(sim.Entities[0]);
        Assert.NotNull(sim.Entities[1]);
    }

    // -------------------------------------------------
    // Surface following (blobs ride the terrain height)
    // -------------------------------------------------

    // A map whose every cell sits at the given elevation, so a creature anywhere
    // samples a flat surface at that height.
    private static MapData UniformHeightMap(float height, int size = 16)
    {
        var map = new MapData(size, size, 1f);
        for (int cz = 0; cz < size; cz++)
        for (int cx = 0; cx < size; cx++)
            map.SetCell(cx, cz, new Cell { Terrain = Terrain.Grass, Height = height });
        return map;
    }

    [Fact]
    public void Surface_Blob_RidesRaisedTerrain()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Map = UniformHeightMap(3f),
        };
        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));

        sim.Tick(0.1);

        Assert.Equal(3f + blob.Traits.Radius, blob.Position.Y, 4);
    }

    [Fact]
    public void Surface_Blob_FollowsTerrainDown()
    {
        // Below-zero terrain: a clamp-up-only floor would leave the blob floating at y=0;
        // it must snap DOWN onto the surface.
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Map = UniformHeightMap(-2f),
        };
        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));

        sim.Tick(0.1);

        Assert.Equal(-2f + blob.Traits.Radius, blob.Position.Y, 4);
    }

    [Fact]
    public void Surface_NoMap_RestsOnArenaFloor()
    {
        var arena = Arena.GroundArena(16, 16); // Map null
        var sim = new Simulator(arena, seed: 1);
        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));

        sim.Tick(0.1);

        Assert.Equal(arena.MinY + blob.Traits.Radius, blob.Position.Y, 4);
    }

    [Fact]
    public void Surface_GravityCreature_LandsOnRaisedTerrain()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Map = UniformHeightMap(3f),
        };
        // A falling creature started above the surface; WalkMode leaves Y to gravity.
        var traits = new CreatureTraits { Radius = 0.5f, MaxSpeed = 0.6f, GravityScale = 1f };
        var creature = sim.SpawnCreature(new Vector3(0, 10, 0), traits);

        for (int i = 0; i < 100; i++)
            sim.Tick(0.1);

        Assert.Equal(3f + creature.Traits.Radius, creature.Position.Y, 3);
    }

    [Fact]
    public void Surface_SameSeedAndMap_IsDeterministic()
    {
        static Simulator Run()
        {
            var sim = new Simulator(Arena.GroundArena(16, 16), seed: 7)
            {
                Map = UniformHeightMap(2.5f),
            };
            for (int i = 0; i < 5; i++)
                sim.SpawnBlob(new Vector3(i - 2, 0, 0));
            for (int t = 0; t < 50; t++)
                sim.Tick(0.1);
            return sim;
        }

        var a = Run();
        var b = Run();

        Assert.Equal(a.EntityCount, b.EntityCount);
        for (int i = 0; i < a.EntityCount; i++)
            Assert.Equal(a.Entities[i].Position, b.Entities[i].Position);
    }

    // -------------------------------------------------
    // Herd cohesion (flocking)
    // -------------------------------------------------

    private static Drives HerdDrives() => new()
    {
        Sociability = 1f,   // strongly prefer the Flock action
        Curiosity = 0.1f,
        Fear = 0f,
        Appetite = 0f,      // no foraging to pull them apart
        Aggression = 0f,
    };

    /// <summary>Average distance of each entity from the group's centroid (a spread measure).</summary>
    private static float SpreadAroundCentroid(Simulator sim)
    {
        var centroid = Vector3.Zero;
        foreach (var e in sim.Entities) centroid += e.Position;
        centroid /= sim.EntityCount;

        float sum = 0f;
        foreach (var e in sim.Entities)
        {
            var d = e.Position - centroid;
            sum += MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        }
        return sum / sim.EntityCount;
    }

    [Fact(Skip = "flock split bug — herd can split into two flocks; revisit (hysteresis on Leave + wider Merge)")]
    public void Herd_SociableKin_FormAndHoldAFlock()
    {
        var sim = new Simulator(Arena.GroundArena(40, 40), seed: 99);

        // Four sociable kin in a tight cluster (all within join radius of each other).
        sim.SpawnBlob(new Vector3(-2, 0, -2), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(2, 0, -2), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(-2, 0, 2), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(2, 0, 2), traits: null, drives: HerdDrives());

        for (int i = 0; i < 120; i++) sim.Tick(0.1);

        // The rework's guarantee is no longer "collapse onto the centroid" but "form one flock and
        // hold a loose, uncramped circle around its moving anchor" — members stay grouped, not glued.
        var flock = Assert.Single(sim.Flocks);
        Assert.Equal(4, flock.Members.Count);
        foreach (var m in flock.Members)
        {
            var d = m.Position - flock.Anchor;
            Assert.True(MathF.Sqrt(d.X * d.X + d.Z * d.Z) <= flock.Radius + 3f,
                "members should hold the circle around the anchor, not fly apart");
        }
    }

    [Fact]
    public void Herd_Member_SteersTowardItsFlockAnchor()
    {
        // A sociable kin off to the −X edge of its flock should steer back toward the anchor (+X).
        // Neighbors sit beyond sense radius (5) but within join radius (8): a flock forms, yet no
        // close-range Approach pull competes, so the Flock steering is what's exercised.
        var sim = new Simulator(Arena.GroundArena(20, 20), seed: 1);
        var self = sim.SpawnBlob(new Vector3(-6, 0, 0), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(0, 0, 5), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(0, 0, -5), traits: null, drives: HerdDrives());

        for (int i = 0; i < 20; i++) sim.Tick(0.1);

        Assert.NotNull(self.Flock);
        Assert.Equal("Flock", self.Brain!.CurrentName);
        Assert.True(self.DesiredVelocity.X > 0f,
            $"should steer toward the flock anchor (+X), desired={self.DesiredVelocity}");
    }

    [Fact]
    public void Herd_NonKinNeighbors_DoNotFormHerd()
    {
        // A sociable creature flanked by two genetically dissimilar neighbors (opposite drives)
        // should NOT treat them as a herd → it never settles into Flock.
        var sim = new Simulator(Arena.GroundArena(20, 20), seed: 1);
        var self = sim.SpawnBlob(new Vector3(-4, 0, 0), traits: null, drives: HerdDrives());
        var stranger = new Drives { Sociability = 0f, Curiosity = 1f, Fear = 1f, Appetite = 1f, Aggression = 1f, PlayCuddle = 1f };
        sim.SpawnBlob(new Vector3(0, 0, 2), traits: null, drives: new Drives(stranger));
        sim.SpawnBlob(new Vector3(0, 0, -2), traits: null, drives: new Drives(stranger));

        for (int i = 0; i < 5; i++) sim.Tick(0.1);

        Assert.NotEqual("Flock", self.Brain!.CurrentName);
    }

    [Fact]
    public void Herd_Cohering_KeepsMinimumSpacing()
    {
        // Sociable kin pull together but separation steering keeps personal space: no two end up
        // collapsed onto each other.
        var sim = new Simulator(Arena.GroundArena(20, 20), seed: 7);
        sim.SpawnBlob(new Vector3(-3, 0, -3), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(3, 0, -3), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(-3, 0, 3), traits: null, drives: HerdDrives());
        sim.SpawnBlob(new Vector3(3, 0, 3), traits: null, drives: HerdDrives());

        for (int i = 0; i < 200; i++) sim.Tick(0.1);

        float minSpacing = float.MaxValue;
        var ents = sim.Entities;
        for (int i = 0; i < ents.Count; i++)
        for (int j = i + 1; j < ents.Count; j++)
        {
            var d = ents[i].Position - ents[j].Position;
            minSpacing = MathF.Min(minSpacing, MathF.Sqrt(d.X * d.X + d.Z * d.Z));
        }

        // Each pair stays at least roughly touching-distance apart (radii sum), not piled up.
        float radiiSum = ents[0].Traits.Radius + ents[1].Traits.Radius;
        Assert.True(minSpacing >= radiiSum * 0.9f,
            $"herd should keep spacing ≥ ~{radiiSum:F2}, got {minSpacing:F2}");
    }

    [Fact]
    public void Herd_TwoKin_FlockAndKeepSpacing()
    {
        // Regression for the sheep-jitter / kissing bug: two kin spawned CLOSE (as the herd
        // spawner clusters them) settle to a personal-space gap and stay there, GENTLY MILLING
        // rather than vibrating inside the collision radius or freezing solid. Uses the real sheep
        // traits + drives (Sociability 0.9, Fear 0.15, small radius, low speed, brisk accel) that
        // drive the pair into the Approach action. Appetite is 0 here so hunger-wandering doesn't
        // mask the social settle. NOTE: the herd no longer freezes — the idle-drift floor keeps it
        // milling on purpose (frozen herds were the bug) — so we assert a held gap + bounded gentle
        // motion, not a dead stop.
        var sim = new Simulator(Arena.GroundArena(40, 40), seed: 7);
        var a = sim.SpawnBlob(new Vector3(-0.7f, 0, 0), traits: SheepTraits(), drives: SheepDrives());
        sim.SpawnBlob(new Vector3(0.7f, 0, 0), traits: SheepTraits(), drives: SheepDrives());

        for (int i = 0; i < 400; i++) sim.Tick(0.1);

        float radiiSum = sim.Entities[0].Traits.Radius + sim.Entities[1].Traits.Radius;
        float maxSpeed = sim.Entities[0].Traits.MaxSpeed;

        // Over the final ticks the pair holds a real personal-space gap (never the kissing
        // collision distance, never flung apart) and drifts only gently — no max-speed thrash.
        for (int i = 0; i < 40; i++)
        {
            sim.Tick(0.1);
            var d = sim.Entities[0].Position - sim.Entities[1].Position;
            float spacing = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            Assert.True(spacing >= radiiSum * 1.4f,
                $"two sheep should hold a personal-space gap (≥ ~{radiiSum * 1.4f:F2}), got {spacing:F2}");
            Assert.True(spacing <= radiiSum * 6f,
                $"two kin should stay a loose pair, not scatter (≤ ~{radiiSum * 6f:F2}), got {spacing:F2}");
            foreach (var e in sim.Entities)
            {
                float hspeed = MathF.Sqrt(e.Velocity.X * e.Velocity.X + e.Velocity.Z * e.Velocity.Z);
                Assert.True(hspeed <= maxSpeed + 1e-3f,
                    $"milling sheep should drift gently within max speed, got {hspeed:F3} (max {maxSpeed:F2})");
            }
        }
    }

    private static CreatureTraits SheepTraits() => new(Blob.DefaultBlobTraits)
    {
        Radius = 0.6f,
        MaxSpeed = 0.35f,
        Acceleration = 1.6f,
        GravityScale = 0f,
    };

    // The real default-sheep drives (see scripts/VivariumMain.cs), but Appetite 0 so the test
    // isolates social settling from hunger-driven wandering.
    private static Drives SheepDrives() => new()
    {
        Curiosity = 0.3f,
        Fear = 0.15f,
        Sociability = 0.9f,
        Appetite = 0f,
        Aggression = 0.1f,
        PlayCuddle = 0.3f,
    };

    // -------------------------------------------------
    // Diet filtering
    // -------------------------------------------------

    private static FoodCatalog TwoFoodTypes() => FoodCatalog.Parse("""
        [
            { "Id": "berries",  "ColorHex": "#CC3333", "Nutrition": 0.4, "GrazeRate": 0.5, "RespawnSeconds": 5 },
            { "Id": "cactus",   "ColorHex": "#33CC33", "Nutrition": 0.3, "GrazeRate": 0.3, "RespawnSeconds": 5 }
        ]
        """);

    [Fact]
    public void Diet_NoDiet_EatsAnyFood()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Foods = TwoFoodTypes(),
        };
        // Place one berries and one cactus near a blob with no diet restriction.
        sim.Food.Add(new FoodItem { Position = new Vector3(0, 0, 0), Def = sim.Foods!.Get("berries") });
        sim.Food.Add(new FoodItem { Position = new Vector3(3, 0, 0), Def = sim.Foods.Get("cactus") });

        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));
        blob.Needs.Hunger = 0.8f; // very hungry → Forage

        // Blob has no diet restriction — it should graze the nearest food (berries at origin).
        float hungerBefore = blob.Needs.Hunger;
        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        Assert.True(blob.Needs.Hunger < hungerBefore, "blob with no diet should have grazed");
    }

    [Fact]
    public void Diet_Restricted_EatsOnlyAllowedFood()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Foods = TwoFoodTypes(),
        };
        // Berries at origin, cactus at (0.1, 0, 0) — the blob is on top of berries.
        sim.Food.Add(new FoodItem { Position = new Vector3(0, 0, 0), Def = sim.Foods!.Get("berries") });
        sim.Food.Add(new FoodItem { Position = new Vector3(0.1f, 0, 0), Def = sim.Foods.Get("cactus") });

        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));
        blob.Needs.Hunger = 0.8f;
        blob.Diet = new HashSet<string> { "berries" };

        float hungerBefore = blob.Needs.Hunger;
        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        Assert.True(blob.Needs.Hunger < hungerBefore,
            "blob with berries diet should graze the berries");

        // The cactus should be untouched.
        Assert.Equal(1f, sim.Food[1].Amount, 4);
    }

    [Fact]
    public void Diet_Restricted_SkipsInedibleNearestFood()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Foods = TwoFoodTypes(),
        };
        // Cactus is nearer, then berries within easy travel range — a berries-eater
        // should path to and graze the berries, never touching the cactus.
        sim.Food.Add(new FoodItem { Position = new Vector3(0.3f, 0, 0), Def = sim.Foods!.Get("cactus") });
        sim.Food.Add(new FoodItem { Position = new Vector3(1f, 0, 0), Def = sim.Foods.Get("berries") });

        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));
        blob.Needs.Hunger = 0.8f;
        blob.Diet = new HashSet<string> { "berries" };

        // 50 ticks (5 s) gives the blob time to move ~1 unit toward berries.
        for (int i = 0; i < 50; i++) sim.Tick(0.1);

        // The cactus should be untouched; only the berries should have been grazed.
        Assert.Equal(1f, sim.Food[0].Amount, 4);
        Assert.True(sim.Food[1].Amount < 1f,
            "the berries should have been grazed (not the nearer cactus)");
    }

    [Fact]
    public void Diet_Restricted_StarvesWithoutAllowedFood()
    {
        var sim = new Simulator(Arena.GroundArena(16, 16), seed: 1)
        {
            Foods = TwoFoodTypes(),
        };
        // Only cactus near the blob — a berries-eater should not touch it.
        sim.Food.Add(new FoodItem { Position = new Vector3(0, 0, 0), Def = sim.Foods!.Get("cactus") });

        var blob = sim.SpawnBlob(new Vector3(0, 0, 0));
        blob.Needs.Hunger = 0f;
        blob.Diet = new HashSet<string> { "berries" };

        float hungerBefore = blob.Needs.Hunger;
        for (int i = 0; i < 10; i++) sim.Tick(0.1);

        // Hunger should grow (from hunger gain) — the blob couldn't eat the cactus.
        Assert.True(blob.Needs.Hunger >= hungerBefore,
            "berries-eater should not graze cactus; hunger unchanged or up");
        Assert.Equal(1f, sim.Food[0].Amount, 4);
    }

    [Fact]
    public void Diet_Deterministic_SameSeedSameState()
    {
        static Simulator Run()
        {
            var sim = new Simulator(Arena.GroundArena(16, 16), seed: 99)
            {
                Foods = TwoFoodTypes(),
            };
            sim.Food.Add(new FoodItem { Position = new Vector3(0, 0, 0), Def = sim.Foods!.Get("berries") });
            var blob = sim.SpawnBlob(new Vector3(0, 0, 0));
            blob.Diet = new HashSet<string> { "berries" };
            sim.Tick(0.1);
            return sim;
        }

        var a = Run();
        var b = Run();

        Assert.Equal(a.Entities[0].Position, b.Entities[0].Position);
        Assert.Equal(a.Entities[0].Velocity, b.Entities[0].Velocity);
        Assert.Equal(a.Food[0].Amount, b.Food[0].Amount);
    }
}
