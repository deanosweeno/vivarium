using System.Numerics;
using Vivarium.Core;
using Xunit;

namespace Vivarium.Core.Tests;

/// <summary>
/// Tests for the pure steering primitives (<see cref="Steering"/>). All headless and
/// deterministic — no RNG, no Godot.
/// </summary>
public class SteeringTests
{
    [Fact]
    public void Cohesion_OutsideSlowRadius_SteersTowardCentroidAtFullSpeed()
    {
        var self = new Vector3(0, 0, 0);
        var centroid = new Vector3(10, 0, 0);   // far away
        var v = Steering.Cohesion(self, centroid, maxSpeed: 2f, slowRadius: 1f);

        Assert.Equal(2f, v.Length(), 4);                 // full speed outside the slow radius
        Assert.True(v.X > 0f, "should steer toward +X (the centroid)");
        Assert.Equal(0f, v.Z, 5);
    }

    [Fact]
    public void Cohesion_InsideSlowRadius_Decelerates()
    {
        var self = new Vector3(0, 0, 0);
        var centroid = new Vector3(0.5f, 0, 0);  // half a unit inside a 1-unit slow radius
        var v = Steering.Cohesion(self, centroid, maxSpeed: 2f, slowRadius: 1f);

        // Arrive scales speed by dist/slowRadius → 0.5 * 2 = 1.0, half of max.
        Assert.Equal(1f, v.Length(), 4);
        Assert.True(v.X > 0f);
    }

    [Fact]
    public void Cohesion_AtCentroid_IsZero()
    {
        var p = new Vector3(3, 0, 3);
        Assert.Equal(Vector3.Zero, Steering.Cohesion(p, p, maxSpeed: 2f, slowRadius: 1f));
    }
}
