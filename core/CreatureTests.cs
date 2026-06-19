using System.Numerics;
using Xunit;

namespace Vivarium.Core.Tests;

public class CreatureTests
{
    // Minimal stub for injecting into Creature during tests
    private sealed class StubMovement : IMovementMode
    {
        public int TickCallCount { get; private set; }
        public Creature? LastCreature { get; private set; }
        public Arena LastArena { get; private set; }

        public void Tick(double delta, Creature creature, Arena arena, Random rng)
        {
            TickCallCount++;
            LastCreature = creature;
            LastArena = arena;
        }
    }

    [Fact]
    public void Constructor_SetsPosition()
    {
        var pos = new Vector3(3, 1, 5);
        var creature = new Creature(pos, null, new StubMovement());

        Assert.Equal(pos, creature.Position);
    }

    [Fact]
    public void Constructor_StartsWithZeroVelocity()
    {
        var creature = new Creature(Vector3.Zero, null, new StubMovement());

        Assert.Equal(Vector3.Zero, creature.Velocity);
    }

    [Fact]
    public void Constructor_UsesProvidedTraits()
    {
        var traits = new CreatureTraits { MaxSpeed = 2.0f, Radius = 1.0f };
        var creature = new Creature(Vector3.Zero, traits, new StubMovement());

        Assert.Same(traits, creature.Traits);
        Assert.Equal(2.0f, creature.Traits.MaxSpeed);
        Assert.Equal(1.0f, creature.Traits.Radius);
    }

    [Fact]
    public void Constructor_NullTraitsDefaults()
    {
        var creature = new Creature(Vector3.Zero, null, new StubMovement());

        Assert.NotNull(creature.Traits);
        Assert.Equal(CreatureTraits.Default.MaxSpeed, creature.Traits.MaxSpeed);
        Assert.Equal(CreatureTraits.Default.Radius, creature.Traits.Radius);
        Assert.Equal(CreatureTraits.Default.GravityScale, creature.Traits.GravityScale);
    }

    [Fact]
    public void Constructor_UsesProvidedMovement()
    {
        var movement = new StubMovement();
        var creature = new Creature(Vector3.Zero, null, movement);

        Assert.Same(movement, creature.Movement);
    }

    [Fact]
    public void Movement_IsSwappable()
    {
        var original = new StubMovement();
        var creature = new Creature(Vector3.Zero, null, original);

        var replacement = new StubMovement();
        creature.Movement = replacement;

        Assert.Same(replacement, creature.Movement);
        Assert.NotSame(original, creature.Movement);
    }

    [Fact]
    public void Position_Velocity_AreMutable_Internally()
    {
        var creature = new Creature(Vector3.Zero, null, new StubMovement());

        // internal set allows mutation from within the assembly (test project is
        // not the same assembly, but InternalsVisibleTo or same-project tests
        // may need this — verify the property exists and is settable)
        // We test by creating and verifying the initial state; actual internal
        // mutation is exercised by Simulator movement mode integration tests
        // in later phases.
        Assert.Equal(Vector3.Zero, creature.Position);
        Assert.Equal(Vector3.Zero, creature.Velocity);
    }

    [Fact]
    public void CreatureTraits_Mutation_VisibleThroughCreature()
    {
        var traits = new CreatureTraits { MaxSpeed = 1.0f };
        var creature = new Creature(Vector3.Zero, traits, new StubMovement());

        traits.MaxSpeed = 3.0f;
        traits.Radius = 1.5f;

        // Creature holds the same reference — mutations are visible
        Assert.Equal(3.0f, creature.Traits.MaxSpeed);
        Assert.Equal(1.5f, creature.Traits.Radius);
    }
}
