using Xunit;

namespace Vivarium.Core.Tests;

public class CreatureTraitsTests
{
    [Fact]
    public void Default_HasExpectedValues()
    {
        var traits = new CreatureTraits();

        Assert.Equal(0.6f, traits.MaxSpeed);
        Assert.Equal(1.0f, traits.JumpHeight);
        Assert.Equal(2.0f, traits.Acceleration);
        Assert.Equal(3.0f, traits.TurnRate);
        Assert.Equal(0.5f, traits.Radius);
        Assert.Equal(1.0f, traits.GravityScale);
        Assert.False(traits.CanFly);
        Assert.Equal(float.MaxValue, traits.MaxFlyHeight);
    }

    [Fact]
    public void Default_Property_ReturnsNewInstance()
    {
        var a = CreatureTraits.Default;
        var b = CreatureTraits.Default;

        Assert.NotSame(a, b);
        Assert.Equal(a.MaxSpeed, b.MaxSpeed);
        Assert.Equal(a.Radius, b.Radius);
    }

    [Fact]
    public void Property_Mutation_Persists()
    {
        var traits = new CreatureTraits();

        traits.MaxSpeed = 1.5f;
        traits.JumpHeight = 3.0f;
        traits.Acceleration = 5.0f;
        traits.TurnRate = 6.0f;
        traits.Radius = 1.0f;
        traits.GravityScale = 0.5f;
        traits.CanFly = true;
        traits.MaxFlyHeight = 10f;

        Assert.Equal(1.5f, traits.MaxSpeed);
        Assert.Equal(3.0f, traits.JumpHeight);
        Assert.Equal(5.0f, traits.Acceleration);
        Assert.Equal(6.0f, traits.TurnRate);
        Assert.Equal(1.0f, traits.Radius);
        Assert.Equal(0.5f, traits.GravityScale);
        Assert.True(traits.CanFly);
        Assert.Equal(10f, traits.MaxFlyHeight);
    }

    [Fact]
    public void CopyConstructor_ClonesAllValues()
    {
        var original = new CreatureTraits
        {
            MaxSpeed = 1.5f,
            JumpHeight = 2.0f,
            Acceleration = 3.0f,
            TurnRate = 4.0f,
            Radius = 0.75f,
            GravityScale = 2.0f,
            CanFly = true,
            MaxFlyHeight = 20f
        };

        var copy = new CreatureTraits(original);

        Assert.Equal(original.MaxSpeed, copy.MaxSpeed);
        Assert.Equal(original.JumpHeight, copy.JumpHeight);
        Assert.Equal(original.Acceleration, copy.Acceleration);
        Assert.Equal(original.TurnRate, copy.TurnRate);
        Assert.Equal(original.Radius, copy.Radius);
        Assert.Equal(original.GravityScale, copy.GravityScale);
        Assert.Equal(original.CanFly, copy.CanFly);
        Assert.Equal(original.MaxFlyHeight, copy.MaxFlyHeight);
    }

    [Fact]
    public void CopyConstructor_IndependentMutation()
    {
        var original = new CreatureTraits
        {
            MaxSpeed = 1.0f,
            CanFly = false
        };

        var copy = new CreatureTraits(original);

        // Mutate original — copy should be unaffected
        original.MaxSpeed = 5.0f;
        original.CanFly = true;

        Assert.Equal(1.0f, copy.MaxSpeed);
        Assert.False(copy.CanFly);
    }
}
