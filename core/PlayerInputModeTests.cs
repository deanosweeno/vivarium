using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class PlayerInputModeTests
{
    private static Creature MakePlayer(Vector3 pos)
    {
        var traits = new CreatureTraits(Blob.DefaultBlobTraits) { MaxSpeed = 2.0f, Acceleration = 100f };
        return new Creature(pos, traits, new PlayerInputMode());
    }

    [Fact]
    public void MoveInputSetsDesiredVelocityTowardInput()
    {
        var arena = Arena.GroundArena(20, 20);
        var rng = new Random(1);
        var creature = MakePlayer(Vector3.Zero);
        var mode = (PlayerInputMode)creature.Movement;

        mode.MoveInput = new Vector2(1f, 0f);
        mode.Tick(0.1, creature, arena, rng);

        Assert.Equal(creature.Traits.MaxSpeed, creature.DesiredVelocity.X, 3);
        Assert.True(creature.Position.X > 0f);
        Assert.Equal(0f, creature.Position.Z, 3);
    }

    [Fact]
    public void ZeroInputStopsTheCreature()
    {
        var arena = Arena.GroundArena(20, 20);
        var rng = new Random(1);
        var creature = MakePlayer(Vector3.Zero);
        var mode = (PlayerInputMode)creature.Movement;

        mode.MoveInput = new Vector2(0f, 1f);
        mode.Tick(0.1, creature, arena, rng);
        Assert.True(creature.Velocity.Length() > 0f);

        mode.MoveInput = Vector2.Zero;
        mode.Tick(0.1, creature, arena, rng);
        Assert.Equal(Vector3.Zero, creature.DesiredVelocity);
        Assert.Equal(0f, creature.Velocity.Length(), 3);
    }

    [Fact]
    public void DiagonalInputIsNormalized()
    {
        var arena = Arena.GroundArena(20, 20);
        var rng = new Random(1);
        var creature = MakePlayer(Vector3.Zero);
        var mode = (PlayerInputMode)creature.Movement;

        // Full-throttle diagonal: magnitude > 1, must be clamped to MaxSpeed (no diagonal boost).
        mode.MoveInput = new Vector2(1f, 1f);
        mode.Tick(0.1, creature, arena, rng);

        Assert.Equal(creature.Traits.MaxSpeed, creature.DesiredVelocity.Length(), 3);
    }

    [Fact]
    public void ReflectsOffArenaWall()
    {
        var arena = Arena.GroundArena(4, 4); // walls at ±2
        var rng = new Random(1);
        var creature = MakePlayer(new Vector3(1.5f, 0f, 0f));
        var mode = (PlayerInputMode)creature.Movement;

        mode.MoveInput = new Vector2(1f, 0f); // drive into the +X wall
        for (int i = 0; i < 30; i++)
            mode.Tick(0.1, creature, arena, rng);

        Assert.True(creature.Position.X <= arena.MaxX - creature.Traits.Radius + 1e-3f);
    }
}

public class SpawnPlayerTests
{
    [Fact]
    public void SpawnPlayerPlacesBrainlessAvatarInBounds()
    {
        var sim = new Simulator(Arena.GroundArena(20, 20), seed: 7);
        var (player, input) = sim.SpawnPlayer(new Vector3(0f, 0f, 0f));

        Assert.Null(player.Brain);
        Assert.Same(input, player.Movement);
        Assert.Contains(player, sim.Entities);
        Assert.True(sim.Arena.Contains(player.Position));
    }

    [Fact]
    public void TickWithInputAdvancesPlayerWhileBlobsRun()
    {
        var sim = new Simulator(Arena.GroundArena(40, 40), seed: 7);
        var (player, input) = sim.SpawnPlayer(Vector3.Zero);
        sim.SpawnBlob(new Vector3(10f, 0f, 10f));

        var start = player.Position;
        input.MoveInput = new Vector2(1f, 0f);
        for (int i = 0; i < 20; i++)
            sim.Tick(0.1);

        Assert.True(player.Position.X > start.X);
        Assert.Equal(2, sim.EntityCount);
    }
}
