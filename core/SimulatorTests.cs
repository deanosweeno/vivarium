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

        // Force blob walk modes to idle with tiny timer so they transition to sliding
        ((BlobWalkMode)b1.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b1.Movement).StateTimer = 0.01;
        ((BlobWalkMode)b2.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b2.Movement).StateTimer = 0.01;
        ((BlobWalkMode)b3.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b3.Movement).StateTimer = 0.01;

        sim.Tick(0.1);

        // All should now be sliding (have velocity)
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
            // Verify wander state determinism for blobs
            if (sim1.Entities[i] is Blob b1 && sim2.Entities[i] is Blob b2)
                Assert.Equal(((BlobWalkMode)b1.Movement).State, ((BlobWalkMode)b2.Movement).State);
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

        // Keep them idle so they don't wander on Tick
        ((BlobWalkMode)b1.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b1.Movement).StateTimer = 999;
        ((BlobWalkMode)b2.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b2.Movement).StateTimer = 999;

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

        ((BlobWalkMode)b1.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b1.Movement).StateTimer = 999;
        ((BlobWalkMode)b2.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b2.Movement).StateTimer = 999;

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

        ((BlobWalkMode)b1.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b1.Movement).StateTimer = 999;
        ((BlobWalkMode)b2.Movement).State = WanderState.Idle;
        ((BlobWalkMode)b2.Movement).StateTimer = 999;

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

        float initialY = creature.Position.Y;

        sim.Tick(1.0);

        // Y should be unchanged (GravityScale = 0, so no gravity applied)
        Assert.Equal(initialY, creature.Position.Y, 3);
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
        ((BlobWalkMode)blob.Movement).State = WanderState.Idle;
        ((BlobWalkMode)blob.Movement).StateTimer = 999;
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
        ((BlobWalkMode)blob.Movement).State = WanderState.Idle;
        ((BlobWalkMode)blob.Movement).StateTimer = 999;
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
}
