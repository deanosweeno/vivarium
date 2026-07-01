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
        Assert.Equal(0.6f, traits.SprintSpeed);
        Assert.Equal(2.0f, traits.SprintAcceleration);
        Assert.Equal(1.0f, traits.GravityScale);
        Assert.False(traits.CanFly);
        Assert.Equal(float.MaxValue, traits.MaxFlyHeight);
        Assert.Equal(0.06f, traits.FatigueGainPerSec);
        Assert.Equal(0.4f, traits.FatigueRecoverPerSec);
        Assert.Null(traits.Diet);
        Assert.Equal(0.3f, traits.GrazeHungerThreshold);
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
        traits.SprintSpeed = 2.5f;
        traits.SprintAcceleration = 8.0f;
        traits.GravityScale = 0.5f;
        traits.CanFly = true;
        traits.MaxFlyHeight = 10f;
        traits.FatigueGainPerSec = 0.03f;
        traits.FatigueRecoverPerSec = 1.2f;

        Assert.Equal(1.5f, traits.MaxSpeed);
        Assert.Equal(3.0f, traits.JumpHeight);
        Assert.Equal(5.0f, traits.Acceleration);
        Assert.Equal(6.0f, traits.TurnRate);
        Assert.Equal(1.0f, traits.Radius);
        Assert.Equal(2.5f, traits.SprintSpeed);
        Assert.Equal(8.0f, traits.SprintAcceleration);
        Assert.Equal(0.5f, traits.GravityScale);
        Assert.True(traits.CanFly);
        Assert.Equal(10f, traits.MaxFlyHeight);
        Assert.Equal(0.03f, traits.FatigueGainPerSec);
        Assert.Equal(1.2f, traits.FatigueRecoverPerSec);
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
            SprintSpeed = 2.0f,
            SprintAcceleration = 5.0f,
            GravityScale = 2.0f,
            CanFly = true,
            MaxFlyHeight = 20f,
            FatigueGainPerSec = 0.05f,
            FatigueRecoverPerSec = 0.9f,
            Diet = new HashSet<string> { "berries", "acorns" },
            GrazeHungerThreshold = 0.7f,
        };

        var copy = new CreatureTraits(original);

        Assert.Equal(original.MaxSpeed, copy.MaxSpeed);
        Assert.Equal(original.JumpHeight, copy.JumpHeight);
        Assert.Equal(original.Acceleration, copy.Acceleration);
        Assert.Equal(original.TurnRate, copy.TurnRate);
        Assert.Equal(original.Radius, copy.Radius);
        Assert.Equal(original.SprintSpeed, copy.SprintSpeed);
        Assert.Equal(original.SprintAcceleration, copy.SprintAcceleration);
        Assert.Equal(original.GravityScale, copy.GravityScale);
        Assert.Equal(original.CanFly, copy.CanFly);
        Assert.Equal(original.MaxFlyHeight, copy.MaxFlyHeight);
        Assert.Equal(original.FatigueGainPerSec, copy.FatigueGainPerSec);
        Assert.Equal(original.FatigueRecoverPerSec, copy.FatigueRecoverPerSec);
        Assert.Equal(original.Diet, copy.Diet);
        Assert.Equal(original.GrazeHungerThreshold, copy.GrazeHungerThreshold);
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
